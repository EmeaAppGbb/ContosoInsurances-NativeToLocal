using ContosoInsurance.Api.Endpoints;
using ContosoInsurance.Api.Middleware;
using ContosoInsurance.Api.Services;
using ContosoInsurance.Data;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add Aspire-managed SQL Server (EF Core)
builder.AddSqlServerDbContext<InsuranceDbContext>("insurancedb");

// Add Aspire-managed RabbitMQ
builder.AddRabbitMQClient("messaging");

// Services layer
builder.Services.AddScoped<ICustomerService, CustomerService>();
builder.Services.AddScoped<IPolicyService, PolicyService>();
builder.Services.AddScoped<IClaimService, ClaimService>();
builder.Services.AddScoped<IQuoteService, QuoteService>();

// Middleware
builder.Services.AddTransient<GlobalExceptionHandler>();

// CORS — allow the Web frontend
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader()
              .WithExposedHeaders("X-Api-Version");
    });
});

builder.Services.AddOpenApi();

var app = builder.Build();

app.MapDefaultEndpoints();

// Global exception handling
app.UseMiddleware<GlobalExceptionHandler>();

// CORS
app.UseCors();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    // Auto-migrate and seed database in development
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<InsuranceDbContext>();
    await db.Database.EnsureCreatedAsync();
    await SeedData.SeedAsync(db);
}

app.UseHttpsRedirection();

// API versioning header
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Api-Version"] = "1.0";
    await next();
});

// Map API endpoints
app.MapCustomerEndpoints();
app.MapPolicyEndpoints();
app.MapClaimEndpoints();
app.MapQuoteEndpoints();

app.Run();

// Make the implicit Program class accessible to integration tests
public partial class Program { }
