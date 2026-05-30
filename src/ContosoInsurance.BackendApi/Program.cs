using ContosoInsurance.BackendApi.Authentication;
using ContosoInsurance.BackendApi.Endpoints;
using ContosoInsurance.BackendApi.Messaging;
using ContosoInsurance.BackendApi.Middleware;
using ContosoInsurance.BackendApi.Services;
using ContosoInsurance.Data;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddSqlServerDbContext<InsuranceDbContext>("insurancedb");
builder.AddRabbitMQClient("messaging");

builder.Services.AddScoped<ClaimWorkflowService>();
builder.Services.AddScoped<QuoteWorkflowService>();
builder.Services.AddScoped<IOutboxDispatcher, RabbitMqOutboxDispatcher>();
builder.Services.AddTransient<GlobalExceptionHandler>();
builder.Services.AddBackendApiAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddHostedService<RabbitMqTopologyInitializer>();
builder.Services.AddHostedService<OutboxPublisherService>();
builder.Services.AddHealthChecks().AddCheck<RabbitMqConnectionHealthCheck>("rabbitmq", tags: ["ready"]);
builder.Services.AddOpenApi();

var app = builder.Build();

app.MapDefaultEndpoints();
app.UseMiddleware<GlobalExceptionHandler>();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<InsuranceDbContext>();
    // DB already created by public API — just ensure tables exist
    try { await db.Database.EnsureCreatedAsync(); } catch { /* table already exists is fine */ }
}

app.UseHttpsRedirection();
app.MapClaimWorkflowEndpoints();
app.MapQuoteWorkflowEndpoints();
app.MapDashboardEndpoints();

app.Run();

public partial class Program;
