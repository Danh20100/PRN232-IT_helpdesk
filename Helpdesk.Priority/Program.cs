using Helpdesk.Priority;
using Helpdesk.Priority.Services;

if (args.Contains("--self-check"))
{
    var (p, h) = PriorityRules.Suggest("Network");
    if (p != "High" || h != 8) throw new Exception("network rule");
    if (PriorityRules.Suggest("Other").Priority != "Low") throw new Exception("default rule");
    return;
}

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddGrpc();
var app = builder.Build();
app.MapGrpcService<PriorityGrpc>();
app.MapGet("/", () => "Helpdesk priority gRPC");
app.Run();
