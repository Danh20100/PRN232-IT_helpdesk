using System.Text.Json;
using StackExchange.Redis;

namespace HelpdeskApi.Services;

public record TicketStatusEvent(int TicketId, string Code, string NewStatus, int CreatedById, int? AssignedToId);

public class TicketPublisher(IConnectionMultiplexer redis, ILogger<TicketPublisher> log)
{
    public const string Channel = "ticket.status.changed";

    public async Task PublishStatusAsync(TicketStatusEvent e)
    {
        try
        {
            await redis.GetSubscriber().PublishAsync(RedisChannel.Literal(Channel), JsonSerializer.Serialize(e));
            log.LogInformation("Published {Channel} {Code} -> {Status}", Channel, e.Code, e.NewStatus);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Redis publish failed for {Code}", e.Code);
        }
    }
}
