using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace HelpdeskWorker;

public record TicketStatusEvent(
    int TicketId,
    string Code,
    string NewStatus,
    int CreatedById,
    int? AssignedToId,
    int? ActorId = null,
    string? OldStatus = null,
    string? TicketTitle = null,
    string? Note = null,
    string? EventType = "StatusChanged",
    string? ActorName = null,
    int? OldAssignedToId = null,
    List<int>? AdminIds = null);

public class NotificationWorker(IConnectionMultiplexer redis, IServiceProvider sp, ILogger<NotificationWorker> log) : BackgroundService
{
    public const string Channel = "ticket.status.changed";
    public const string RealtimeChannel = "ticket.notification.realtime";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var sub = redis.GetSubscriber();
        await sub.SubscribeAsync(RedisChannel.Literal(Channel), async (_, msg) =>
        {
            try
            {
                var e = JsonSerializer.Deserialize<TicketStatusEvent>(msg.ToString());
                if (e is null) return;

                var notifications = BuildNotifications(e);
                if (notifications.Count == 0) return;

                using var scope = sp.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<WorkerDb>();
                var cutoff = DateTime.UtcNow.AddSeconds(-10);
                var notifiedUsers = new List<int>();

                foreach (var (userId, title, message) in notifications)
                {
                    var isDuplicate = await db.Notifications.AnyAsync(n =>
                        n.UserId == userId &&
                        n.TicketId == e.TicketId &&
                        n.Title == title &&
                        n.Message == message &&
                        n.CreatedAt >= cutoff, stoppingToken);

                    if (isDuplicate) continue;

                    db.Notifications.Add(new Notification
                    {
                        UserId = userId,
                        TicketId = e.TicketId,
                        Title = title,
                        Message = message,
                        IsRead = false,
                        CreatedAt = DateTime.UtcNow
                    });
                    notifiedUsers.Add(userId);
                }

                if (notifiedUsers.Count > 0)
                {
                    await db.SaveChangesAsync(stoppingToken);
                    foreach (var uid in notifiedUsers)
                    {
                        await sub.PublishAsync(
                            RedisChannel.Literal(RealtimeChannel),
                            uid.ToString());
                    }
                    log.LogInformation("Notifications saved for {Code} ({EventType}) -> {Count} recipient(s)",
                        e.Code, e.EventType, notifiedUsers.Count);
                }
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to consume {Channel}", Channel);
            }
        });
        log.LogInformation("Listening on {Channel}", Channel);

        // Định kỳ kiểm tra Ticket quá hạn (Overdue) để thông báo cho Staff phụ trách và Admin
        while (!stoppingToken.IsCancellationRequested)
        {
            await CheckOverdueTicketsAsync(sub, stoppingToken);
            await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
        }
    }

    private async Task CheckOverdueTicketsAsync(ISubscriber sub, CancellationToken ct)
    {
        try
        {
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<WorkerDb>();
            var now = DateTime.UtcNow;

            // Status: 0=Open, 1=InProgress, 2=Pending, 3=Resolved, 4=Closed, 5=Cancelled
            var overdueTickets = await db.Tickets
                .Where(t => t.DueDate != null && t.DueDate < now && t.Status < 3)
                .ToListAsync(ct);

            if (overdueTickets.Count == 0) return;

            // Role: 2 = Admin
            var adminIds = await db.Users
                .Where(u => u.Role == 2 && u.IsActive)
                .Select(u => u.Id)
                .ToListAsync(ct);

            var notifiedUsers = new List<int>();

            foreach (var t in overdueTickets)
            {
                var dueStr = t.DueDate!.Value.ToString("dd/MM/yyyy HH:mm 'UTC'");
                var e = new TicketStatusEvent(
                    t.Id, t.Code, "Overdue", t.CreatedById, t.AssignedToId,
                    TicketTitle: t.Title, Note: dueStr, EventType: "Overdue", AdminIds: adminIds);

                var items = BuildNotifications(e);
                foreach (var (userId, title, message) in items)
                {
                    var alreadyNotified = await db.Notifications.AnyAsync(n =>
                        n.UserId == userId &&
                        n.TicketId == t.Id &&
                        n.Title == title, ct);
                    if (alreadyNotified) continue;

                    db.Notifications.Add(new Notification
                    {
                        UserId = userId,
                        TicketId = t.Id,
                        Title = title,
                        Message = message,
                        IsRead = false,
                        CreatedAt = now
                    });
                    notifiedUsers.Add(userId);
                }
            }

            if (notifiedUsers.Count > 0)
            {
                await db.SaveChangesAsync(ct);
                foreach (var uid in notifiedUsers.Distinct())
                {
                    await sub.PublishAsync(RedisChannel.Literal(RealtimeChannel), uid.ToString());
                }
            }
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Overdue check failed in NotificationWorker");
        }
    }

    private static List<(int UserId, string Title, string Message)> BuildNotifications(TicketStatusEvent e)
    {
        var list = new List<(int UserId, string Title, string Message)>();
        var titleText = Truncate(e.TicketTitle, 120);
        var hasTitle = !string.IsNullOrWhiteSpace(titleText);
        var ticketRef = hasTitle ? $"Ticket {e.Code} (\"{titleText}\")" : $"Ticket {e.Code}";
        var admins = e.AdminIds ?? [];

        // 1. User tạo Ticket mới → thông báo Admin
        if (string.Equals(e.EventType, "Created", StringComparison.OrdinalIgnoreCase))
        {
            var creator = string.IsNullOrWhiteSpace(e.ActorName) ? "Người dùng" : e.ActorName;
            foreach (var adminId in admins)
            {
                if (adminId == e.ActorId) continue;
                list.Add((
                    adminId,
                    $"Ticket mới {e.Code} cần phân công",
                    $"{creator} vừa tạo {ticketRef}. Vui lòng kiểm tra và phân công nhân viên IT xử lý."
                ));
            }
            return DeduplicateRecipients(list);
        }

        // 2. Admin phân công Ticket → thông báo Staff được giao
        // 3. Admin đổi người phụ trách → thông báo Staff mới
        if (string.Equals(e.EventType, "Assigned", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(e.EventType, "Reassigned", StringComparison.OrdinalIgnoreCase))
        {
            var isReassign = string.Equals(e.EventType, "Reassigned", StringComparison.OrdinalIgnoreCase)
                             || (e.OldAssignedToId.HasValue && e.OldAssignedToId != e.AssignedToId);
            if (e.AssignedToId is int staffId && staffId != e.ActorId)
            {
                list.Add((
                    staffId,
                    isReassign
                        ? $"Bạn được chuyển giao phụ trách Ticket {e.Code}"
                        : $"Bạn được phân công Ticket {e.Code}",
                    isReassign
                        ? $"Admin đã đổi người phụ trách và giao {ticketRef} cho bạn xử lý."
                        : $"{ticketRef} đã được Admin phân công cho bạn xử lý."
                ));
            }
            return DeduplicateRecipients(list);
        }

        // 6. User thêm Comment → thông báo Staff phụ trách
        // 7. Staff thêm Comment → thông báo User tạo Ticket
        if (string.Equals(e.EventType, "CommentAdded", StringComparison.OrdinalIgnoreCase))
        {
            var actor = string.IsNullOrWhiteSpace(e.ActorName) ? "Người dùng" : e.ActorName;
            var comment = Truncate(e.Note, 300);

            if (e.ActorId == e.CreatedById)
            {
                if (e.AssignedToId is int staffId && staffId != e.ActorId)
                {
                    list.Add((
                        staffId,
                        $"Người tạo đã bình luận trên Ticket {e.Code}",
                        $"{actor}: \"{comment}\""
                    ));
                }
            }
            else if (e.AssignedToId.HasValue && e.ActorId == e.AssignedToId.Value)
            {
                if (e.CreatedById != e.ActorId)
                {
                    list.Add((
                        e.CreatedById,
                        $"Nhân viên IT đã phản hồi trên Ticket {e.Code}",
                        $"{actor}: \"{comment}\""
                    ));
                }
            }
            else
            {
                foreach (var uid in new int?[] { e.CreatedById, e.AssignedToId }.OfType<int>().Distinct())
                {
                    if (uid == e.ActorId) continue;
                    list.Add((
                        uid,
                        $"Bình luận mới trên Ticket {e.Code}",
                        $"{actor}: \"{comment}\""
                    ));
                }
            }
            return DeduplicateRecipients(list);
        }

        // 12. Ticket quá hạn → thông báo Staff phụ trách và Admin
        if (string.Equals(e.EventType, "Overdue", StringComparison.OrdinalIgnoreCase))
        {
            var dueNote = string.IsNullOrWhiteSpace(e.Note) ? "" : $" (Hạn xử lý: {e.Note})";
            if (e.AssignedToId is int staffId)
            {
                list.Add((
                    staffId,
                    $"Cảnh báo quá hạn: Ticket {e.Code}",
                    $"{ticketRef} bạn đang phụ trách đã quá hạn xử lý{dueNote}. Vui lòng kiểm tra và xử lý gấp."
                ));
            }
            foreach (var adminId in admins)
            {
                list.Add((
                    adminId,
                    $"Cảnh báo quá hạn: Ticket {e.Code}",
                    $"{ticketRef} đã quá hạn xử lý{dueNote}. Vui lòng kiểm tra tiến độ hoặc điều phối nhân sự."
                ));
            }
            return DeduplicateRecipients(list);
        }

        // 8. Staff chuyển Ticket sang Resolved → thông báo User tạo Ticket
        if (string.Equals(e.NewStatus, "Resolved", StringComparison.OrdinalIgnoreCase))
        {
            if (e.CreatedById != e.ActorId)
            {
                var notePart = string.IsNullOrWhiteSpace(e.Note)
                    ? ""
                    : $" Ghi chú xử lý: \"{Truncate(e.Note, 300)}\".";
                list.Add((
                    e.CreatedById,
                    $"Ticket {e.Code} đã được giải quyết",
                    $"{ticketRef} đã được nhân viên IT xử lý xong.{notePart} Vui lòng kiểm tra và xác nhận đóng ticket hoặc mở lại nếu vẫn còn lỗi."
                ));
            }
            return DeduplicateRecipients(list);
        }

        // 4. Staff bắt đầu xử lý Ticket → thông báo User tạo Ticket
        // 10. Ticket được Reopen → thông báo Staff phụ trách và Admin
        if (string.Equals(e.NewStatus, "InProgress", StringComparison.OrdinalIgnoreCase))
        {
            var isReopen = string.Equals(e.OldStatus, "Resolved", StringComparison.OrdinalIgnoreCase);
            if (isReopen)
            {
                var notePart = string.IsNullOrWhiteSpace(e.Note)
                    ? ""
                    : $" Lý do: \"{Truncate(e.Note, 300)}\".";
                if (e.AssignedToId is int staffId && staffId != e.ActorId)
                {
                    list.Add((
                        staffId,
                        $"Ticket {e.Code} đã được mở lại (Reopen)",
                        $"Người tạo đã mở lại {ticketRef} và yêu cầu tiếp tục xử lý.{notePart}"
                    ));
                }
                foreach (var adminId in admins)
                {
                    if (adminId == e.ActorId) continue;
                    list.Add((
                        adminId,
                        $"Ticket {e.Code} đã được mở lại (Reopen)",
                        $"{ticketRef} đã được mở lại từ trạng thái Đã giải quyết.{notePart}"
                    ));
                }
            }
            else if (e.CreatedById != e.ActorId)
            {
                var resumed = string.Equals(e.OldStatus, "Pending", StringComparison.OrdinalIgnoreCase);
                list.Add((
                    e.CreatedById,
                    resumed
                        ? $"Nhân viên IT tiếp tục xử lý Ticket {e.Code}"
                        : $"Nhân viên IT bắt đầu xử lý Ticket {e.Code}",
                    resumed
                        ? $"{ticketRef} đã được nhân viên IT tiếp tục xử lý."
                        : $"{ticketRef} đã bắt đầu được nhân viên IT xử lý."
                ));
            }
            return DeduplicateRecipients(list);
        }

        // 5. Staff chuyển Ticket sang Pending → thông báo User tạo Ticket
        if (string.Equals(e.NewStatus, "Pending", StringComparison.OrdinalIgnoreCase))
        {
            if (e.CreatedById != e.ActorId)
            {
                var notePart = string.IsNullOrWhiteSpace(e.Note)
                    ? ""
                    : $" Ghi chú: \"{Truncate(e.Note, 300)}\".";
                list.Add((
                    e.CreatedById,
                    $"Ticket {e.Code} đang chờ phản hồi",
                    $"Nhân viên IT đã chuyển {ticketRef} sang trạng thái Chờ phản hồi.{notePart}"
                ));
            }
            return DeduplicateRecipients(list);
        }

        // 9. User chuyển Ticket sang Closed → thông báo Staff phụ trách
        if (string.Equals(e.NewStatus, "Closed", StringComparison.OrdinalIgnoreCase))
        {
            if (e.AssignedToId is int staffId && staffId != e.ActorId)
            {
                list.Add((
                    staffId,
                    $"Ticket {e.Code} đã được xác nhận đóng",
                    $"Người tạo đã xác nhận hoàn tất và đóng {ticketRef}."
                ));
            }
            return DeduplicateRecipients(list);
        }

        // 11. Ticket bị Cancelled → thông báo các bên liên quan theo quyền truy cập (User tạo, Staff được giao, Admin)
        if (string.Equals(e.NewStatus, "Cancelled", StringComparison.OrdinalIgnoreCase))
        {
            var notePart = string.IsNullOrWhiteSpace(e.Note)
                ? ""
                : $" Ghi chú: \"{Truncate(e.Note, 300)}\".";
            if (e.CreatedById != e.ActorId)
            {
                list.Add((
                    e.CreatedById,
                    $"Ticket {e.Code} đã bị hủy",
                    $"{ticketRef} đã bị hủy.{notePart}"
                ));
            }
            if (e.AssignedToId is int staffId && staffId != e.ActorId)
            {
                list.Add((
                    staffId,
                    $"Ticket {e.Code} đã bị hủy",
                    $"{ticketRef} mà bạn được phân công đã bị hủy.{notePart}"
                ));
            }
            foreach (var adminId in admins)
            {
                if (adminId == e.ActorId) continue;
                list.Add((
                    adminId,
                    $"Ticket {e.Code} đã bị hủy",
                    $"Người tạo đã hủy {ticketRef}.{notePart}"
                ));
            }
            return DeduplicateRecipients(list);
        }

        foreach (var uid in new int?[] { e.CreatedById, e.AssignedToId }.OfType<int>().Distinct())
        {
            if (uid == e.ActorId) continue;
            list.Add((uid, $"Ticket {e.Code}", $"Trạng thái mới: {e.NewStatus}"));
        }
        return DeduplicateRecipients(list);
    }

    private static List<(int UserId, string Title, string Message)> DeduplicateRecipients(
        List<(int UserId, string Title, string Message)> items)
        => items
            .GroupBy(x => x.UserId)
            .Select(g => g.First())
            .ToList();

    private static string Truncate(string? value, int maxLen)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var trimmed = value.Trim();
        return trimmed.Length <= maxLen ? trimmed : trimmed[..(maxLen - 1)] + "…";
    }
}
