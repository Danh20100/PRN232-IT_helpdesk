using Grpc.Net.Client;
using HelpdeskApi.Grpc;

namespace HelpdeskApi.Services;

public class PriorityClient(IConfiguration cfg, ILogger<PriorityClient> log)
{
    public async Task<(TicketPriority Priority, int SlaHours)?> SuggestAsync(string categoryName)
    {
        try
        {
            using var channel = GrpcChannel.ForAddress(cfg["Grpc:Priority"] ?? "http://localhost:5001");
            var reply = await new Priority.PriorityClient(channel).SuggestAsync(new SuggestRequest { CategoryName = categoryName });
            log.LogInformation("gRPC suggest {Category} -> {Priority} {Hours}h", categoryName, reply.Priority, reply.SlaHours);
            return (Enum.Parse<TicketPriority>(reply.Priority), reply.SlaHours);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "gRPC priority unavailable for {Category}", categoryName);
            return null;
        }
    }
}
