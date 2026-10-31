using System.Text.Json;
using StackExchange.Redis;

namespace HelpdeskWorker;

public record TicketStatusEvent(int TicketId, string Code, string NewStatus, int CreatedById, int? AssignedToId);

public class NotificationWorker(IConnectionMultiplexer redis, IServiceProvider sp, ILogger<NotificationWorker> log) : BackgroundService
{
    public const string Channel = "ticket.status.changed";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var sub = redis.GetSubscriber();
        await sub.SubscribeAsync(RedisChannel.Literal(Channel), async (_, msg) =>
        {
            try
            {
                var e = JsonSerializer.Deserialize<TicketStatusEvent>(msg.ToString());
                if (e is null) return;
                using var scope = sp.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<WorkerDb>();
                foreach (var userId in new int?[] { e.CreatedById, e.AssignedToId }.OfType<int>().Distinct())
                {
                    db.Notifications.Add(new Notification
                    {
                        UserId = userId,
                        TicketId = e.TicketId,
                        Title = $"Ticket {e.Code}",
                        Message = $"Trạng thái mới: {e.NewStatus}"
                    });
                }
                await db.SaveChangesAsync(stoppingToken);
                log.LogInformation("Notifications saved for {Code} -> {Status}", e.Code, e.NewStatus);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to consume {Channel}", Channel);
            }
        });
        log.LogInformation("Listening on {Channel}", Channel);
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}
