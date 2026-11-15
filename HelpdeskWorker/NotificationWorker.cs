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
    string? ActorName = null);

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
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private static List<(int UserId, string Title, string Message)> BuildNotifications(TicketStatusEvent e)
    {
        var list = new List<(int UserId, string Title, string Message)>();
        var titleText = Truncate(e.TicketTitle, 120);
        var hasTitle = !string.IsNullOrWhiteSpace(titleText);
        var ticketRef = hasTitle ? $"Ticket {e.Code} (\"{titleText}\")" : $"Ticket {e.Code}";

        if (string.Equals(e.EventType, "Assigned", StringComparison.OrdinalIgnoreCase))
        {
            if (e.AssignedToId is int staffId && staffId != e.ActorId)
            {
                list.Add((
                    staffId,
                    $"Bạn được phân công Ticket {e.Code}",
                    $"{ticketRef} đã được phân công cho bạn xử lý."
                ));
            }
            if (e.CreatedById != e.ActorId && e.CreatedById != e.AssignedToId)
            {
                var staffInfo = string.IsNullOrWhiteSpace(e.ActorName) ? "nhân viên IT" : $"nhân viên IT ({e.ActorName})";
                list.Add((
                    e.CreatedById,
                    $"Ticket {e.Code} đã được tiếp nhận",
                    $"{ticketRef} của bạn đã được phân công cho {staffInfo} xử lý."
                ));
            }
            return DeduplicateRecipients(list);
        }

        if (string.Equals(e.EventType, "CommentAdded", StringComparison.OrdinalIgnoreCase))
        {
            var actor = string.IsNullOrWhiteSpace(e.ActorName) ? "Người dùng" : e.ActorName;
            var comment = Truncate(e.Note, 300);
            foreach (var uid in new int?[] { e.CreatedById, e.AssignedToId }.OfType<int>().Distinct())
            {
                if (uid == e.ActorId) continue;
                list.Add((
                    uid,
                    $"Bình luận mới trên Ticket {e.Code}",
                    $"{actor}: \"{comment}\""
                ));
            }
            return DeduplicateRecipients(list);
        }

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
                    $"{ticketRef} đã được nhân viên IT xử lý xong.{notePart} Vui lòng kiểm tra và xác nhận đóng ticket hoặc phản hồi nếu vẫn còn lỗi."
                ));
            }
            return DeduplicateRecipients(list);
        }

        if (string.Equals(e.NewStatus, "InProgress", StringComparison.OrdinalIgnoreCase))
        {
            var isReopen = string.Equals(e.OldStatus, "Resolved", StringComparison.OrdinalIgnoreCase);
            if (isReopen)
            {
                if (e.AssignedToId is int staffId && staffId != e.ActorId)
                {
                    var notePart = string.IsNullOrWhiteSpace(e.Note)
                        ? ""
                        : $" Ghi chú: \"{Truncate(e.Note, 300)}\".";
                    list.Add((
                        staffId,
                        $"Ticket {e.Code} được yêu cầu xử lý lại",
                        $"Người tạo phản hồi {ticketRef} vẫn còn lỗi và yêu cầu tiếp tục xử lý.{notePart}"
                    ));
                }
            }
            else if (e.CreatedById != e.ActorId)
            {
                var resumed = string.Equals(e.OldStatus, "Pending", StringComparison.OrdinalIgnoreCase);
                list.Add((
                    e.CreatedById,
                    $"Cập nhật tiến độ Ticket {e.Code}",
                    resumed
                        ? $"{ticketRef} đã được nhân viên IT tiếp tục xử lý."
                        : $"{ticketRef} đã bắt đầu được nhân viên IT xử lý."
                ));
            }
            return DeduplicateRecipients(list);
        }

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
                    $"{ticketRef} đã chuyển sang trạng thái Chờ phản hồi.{notePart}"
                ));
            }
            return DeduplicateRecipients(list);
        }

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

        if (string.Equals(e.NewStatus, "Cancelled", StringComparison.OrdinalIgnoreCase))
        {
            if (e.AssignedToId is int staffId && staffId != e.ActorId)
            {
                list.Add((
                    staffId,
                    $"Ticket {e.Code} đã bị hủy",
                    $"Người tạo đã hủy {ticketRef}."
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
