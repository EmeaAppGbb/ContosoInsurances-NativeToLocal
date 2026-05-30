using ContosoInsurance.Data;
using ContosoInsurance.Worker.Quotes;
using ContosoInsurance.Worker.Quotes.Messaging;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddSqlServerDbContext<InsuranceDbContext>("insurancedb");
builder.AddRabbitMQClient("messaging");

builder.Services.AddScoped<IOutboxDispatcher, RabbitMqOutboxDispatcher>();
builder.Services.AddHostedService<RabbitMqTopologyInitializer>();
builder.Services.AddHostedService<QuotesWorker>();
builder.Services.AddHostedService<OutboxPublisherService>();
builder.Services.AddHealthChecks().AddCheck<RabbitMqConnectionHealthCheck>("rabbitmq", tags: ["ready"]);

var host = builder.Build();

using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<InsuranceDbContext>();
    await db.Database.EnsureCreatedAsync();
    await SeedData.SeedAsync(db);
}

host.Run();
