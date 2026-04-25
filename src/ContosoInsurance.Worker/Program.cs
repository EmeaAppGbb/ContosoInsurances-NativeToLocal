using ContosoInsurance.Data;
using ContosoInsurance.Worker.Consumers;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

// Add Aspire-managed SQL Server (EF Core)
builder.AddSqlServerDbContext<InsuranceDbContext>("insurancedb");

// Add Aspire-managed RabbitMQ
builder.AddRabbitMQClient("messaging");

builder.Services.AddHostedService<ClaimEventConsumer>();
builder.Services.AddHostedService<NotificationConsumer>();

// RabbitMQ health check
builder.Services.AddHealthChecks()
    .AddCheck<RabbitMqHealthCheck>("rabbitmq", tags: ["ready"]);

var host = builder.Build();
host.Run();
