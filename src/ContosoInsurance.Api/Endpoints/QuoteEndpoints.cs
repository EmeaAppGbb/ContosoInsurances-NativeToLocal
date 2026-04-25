using ContosoInsurance.Api.DTOs;
using ContosoInsurance.Api.Services;

namespace ContosoInsurance.Api.Endpoints;

public static class QuoteEndpoints
{
    public static RouteGroupBuilder MapQuoteEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/quotes").WithTags("Quotes");

        group.MapGet("/", async (IQuoteService svc, Guid? customerId, int page = 1, int pageSize = 20) =>
            Results.Ok(await svc.GetQuotesAsync(customerId, page, pageSize)))
            .WithName("GetQuotes")
            .WithSummary("List quotes with optional customer filter and pagination");

        group.MapGet("/{id:guid}", async (Guid id, IQuoteService svc) =>
            await svc.GetQuoteByIdAsync(id) is { } quote
                ? Results.Ok(quote)
                : Results.NotFound())
            .WithName("GetQuote")
            .WithSummary("Get a quote by ID");

        group.MapPost("/", async (CreateQuoteRequest request, IQuoteService svc) =>
        {
            var quote = await svc.CreateQuoteAsync(request);
            return Results.Created($"/api/quotes/{quote.Id}", quote);
        })
        .WithName("CreateQuote")
        .WithSummary("Generate a new insurance quote with premium calculation");

        group.MapPost("/{id:guid}/accept", async (Guid id, IQuoteService svc) =>
            await svc.AcceptQuoteAsync(id) is { } quote
                ? Results.Ok(quote)
                : Results.NotFound())
            .WithName("AcceptQuote")
            .WithSummary("Accept a quote (must not be expired)");

        return group;
    }
}
