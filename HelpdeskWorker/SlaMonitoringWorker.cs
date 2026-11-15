using System.Data;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace HelpdeskWorker;

/// <summary>
/// BackgroundService chuyên trách kiểm tra SLA định kỳ mỗi 1 phút cho các Ticket đang hoạt động.
/// - Mốc 20h (Warning): Gửi cho Support Staff phụ trách (gửi bù cả khi Worker bị dừng qua mốc 24h).
/// - Mốc 24h (Overdue): Gửi cho Support Staff phụ trách và Admin.
/// - Mốc 48h (Overdue Reminder): Gửi nhắc lại cho Support Staff phụ trách và Admin.
/// - Chống gửi trùng bền vững qua DB Transaction (Serializable) + kiểm tra theo chu kỳ SlaStartAt hiện tại.
/// </summary>
public class SlaMonitoringWorker(IConnectionMultiplexer redis, IServiceProvider sp, ILogger<SlaMonitoringWorker> log) : BackgroundService
{
    public const string WarningTitle = "Ticket sắp quá hạn SLA";
    public const string OverdueTitle = "Ticket đã quá hạn SLA";
    public const string ReminderTitle = "Cảnh báo nhắc lại: Ticket quá hạn SLA 48 giờ";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        log.LogInformation("SlaMonitoringWorker started (interval: 60s)");
        // Chờ ngắn lúc khởi động để SQL Server sẵn sàng
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await ScanSlaMilestonesAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    public async Task ScanSlaMilestonesAsync(CancellationToken ct)
    {
        try
        {
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<WorkerDb>();
            var now = DateTime.UtcNow;

            // Chỉ truy vấn các Ticket chưa hoàn thành: 0 = Open, 1 = InProgress, 2 = Pending
            var activeTickets = await db.Tickets
                .AsNoTracking()
                .Where(t => t.Status < 3)
                .ToListAsync(ct);

            if (activeTickets.Count == 0) return;

            var ticketIds = activeTickets.Select(t => t.Id).ToList();

            // Lấy lần Reopen gần nhất (OldStatus = 3 Resolved -> NewStatus = 1 InProgress) để tính chu kỳ SLA mới
            var reopenMap = await db.StatusHistories
                .AsNoTracking()
                .Where(h => ticketIds.Contains(h.TicketId) && h.OldStatus == 3 && h.NewStatus == 1)
                .GroupBy(h => h.TicketId)
                .Select(g => new { TicketId = g.Key, LastReopenedAt = g.Max(x => x.ChangedAt) })
                .ToDictionaryAsync(x => x.TicketId, x => x.LastReopenedAt, ct);

            var adminIds = await db.Users
                .AsNoTracking()
                .Where(u => u.Role == "Admin" && u.IsActive)
                .Select(u => u.Id)
                .ToListAsync(ct);

            var sub = redis.GetSubscriber();
            var notifiedUsers = new HashSet<int>();

            foreach (var t in activeTickets)
            {
                var slaStartAt = reopenMap.TryGetValue(t.Id, out var reopenedAt) && reopenedAt > t.CreatedAt
                    ? reopenedAt
                    : t.CreatedAt;

                var elapsedHours = (now - slaStartAt).TotalHours;
                if (elapsedHours < 20.0) continue;

                // 1. Mốc 20h Warning (Elapsed >= 20h, có gửi bù nếu Worker dừng qua mốc 24h)
                if (elapsedHours >= 20.0 && t.AssignedToId is int staffId)
                {
                    var msg = $"Ticket {t.Code} đã được xử lý trong 20 giờ và chỉ còn 4 giờ trước khi vượt quá SLA 24 giờ.";
                    if (await TrySaveMilestoneNotificationAsync(db, staffId, t.Id, WarningTitle, msg, slaStartAt, now, ct))
                        notifiedUsers.Add(staffId);
                }

                // 2. Mốc 24h Overdue (Elapsed >= 24h -> Staff phụ trách + Admin)
                if (elapsedHours >= 24.0)
                {
                    var msg = $"Ticket {t.Code} đã vượt quá thời gian xử lý 24 giờ. Vui lòng kiểm tra và cập nhật tiến độ xử lý.";
                    var recipients = new List<int>(adminIds);
                    if (t.AssignedToId is int sId && !recipients.Contains(sId))
                        recipients.Add(sId);

                    foreach (var uid in recipients.Distinct())
                    {
                        if (await TrySaveMilestoneNotificationAsync(db, uid, t.Id, OverdueTitle, msg, slaStartAt, now, ct))
                            notifiedUsers.Add(uid);
                    }
                }

                // 3. Mốc 48h Overdue Reminder (Elapsed >= 48h -> Staff phụ trách + Admin)
                if (elapsedHours >= 48.0)
                {
                    var msg = $"Ticket {t.Code} đã vượt quá 48 giờ xử lý mà chưa được giải quyết. Vui lòng kiểm tra và cập nhật tiến độ xử lý.";
                    var recipients = new List<int>(adminIds);
                    if (t.AssignedToId is int sId && !recipients.Contains(sId))
                        recipients.Add(sId);

                    foreach (var uid in recipients.Distinct())
                    {
                        if (await TrySaveMilestoneNotificationAsync(db, uid, t.Id, ReminderTitle, msg, slaStartAt, now, ct))
                            notifiedUsers.Add(uid);
                    }
                }
            }

            if (notifiedUsers.Count > 0)
            {
                foreach (var uid in notifiedUsers)
                {
                    try
                    {
                        await sub.PublishAsync(
                            RedisChannel.Literal(NotificationWorker.RealtimeChannel),
                            uid.ToString());
                    }
                    catch (Exception rex)
                    {
                        log.LogWarning(rex, "Failed to publish realtime SLA signal for user {UserId}", uid);
                    }
                }
                log.LogInformation("SlaMonitoringWorker dispatched SLA notifications to {Count} user(s)", notifiedUsers.Count);
            }
        }
        catch (Exception ex)
        {
            log.LogError(ex, "SlaMonitoringWorker iteration failed");
        }
    }

    private static async Task<bool> TrySaveMilestoneNotificationAsync(
        WorkerDb db,
        int userId,
        int ticketId,
        string title,
        string message,
        DateTime slaStartAt,
        DateTime now,
        CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var exists = await db.Notifications.AnyAsync(n =>
            n.UserId == userId &&
            n.TicketId == ticketId &&
            n.Title == title &&
            n.CreatedAt >= slaStartAt, ct);

        if (exists)
        {
            await tx.RollbackAsync(ct);
            return false;
        }

        db.Notifications.Add(new Notification
        {
            UserId = userId,
            TicketId = ticketId,
            Title = title,
            Message = message,
            IsRead = false,
            CreatedAt = now
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }
}
