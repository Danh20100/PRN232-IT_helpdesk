using Grpc.Core;

namespace Helpdesk.Priority.Services;

public class PriorityGrpc(ILogger<PriorityGrpc> log) : Priority.PriorityBase
{
    public override Task<SuggestReply> Suggest(SuggestRequest request, ServerCallContext context)
    {
        var (priority, hours) = PriorityRules.Suggest(request.CategoryName);
        log.LogInformation("Suggest {Category} -> {Priority} {Hours}h", request.CategoryName, priority, hours);
        return Task.FromResult(new SuggestReply { Priority = priority, SlaHours = hours });
    }
}
