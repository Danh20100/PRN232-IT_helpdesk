using HelpdeskWorker;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddDbContext<WorkerDb>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(builder.Configuration["Redis"] ?? "localhost:6379,abortConnect=false"));
builder.Services.AddHostedService<NotificationWorker>();
builder.Build().Run();
