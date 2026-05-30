using System.Security.Claims;
using ContosoInsurance.BackendApi.DTOs;
using ContosoInsurance.BackendApi.Services;
using ContosoInsurance.Data;
using ContosoInsurance.Data.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace ContosoInsurance.BackendApi.Endpoints;

public static class WorkflowEndpoints
{
    public static RouteGroupBuilder MapClaimWorkflowEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/backend-api/claims").WithTags("Backend Claims");
        group.MapGet("/", ClaimHandlers.GetClaimsAsync).WithSummary("List private claim cases for operations users.");
        group.MapGet("/{id:guid}", ClaimHandlers.GetClaimByIdAsync).WithSummary("Get a private claim case by id.");
        group.MapPut("/{id:guid}/status", ClaimHandlers.UpdateClaimStatusAsync).WithSummary("Transition a private claim case.");
        group.MapPut("/{id:guid}/assignment", ClaimHandlers.AssignClaimAsync).WithSummary("Assign a private claim case.");
        group.MapPost("/{id:guid}/notes", ClaimHandlers.AddClaimNoteAsync).WithSummary("Add an internal note to a claim case.");
        return group;
    }

    public static RouteGroupBuilder MapQuoteWorkflowEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/backend-api/quotes").WithTags("Backend Quotes");
        group.MapGet("/", QuoteHandlers.GetQuotesAsync).WithSummary("List private quote cases for operations users.");
        group.MapGet("/{id:guid}", QuoteHandlers.GetQuoteByIdAsync).WithSummary("Get a private quote case by id.");
        group.MapPut("/{id:guid}/status", QuoteHandlers.UpdateQuoteStatusAsync).WithSummary("Transition a private quote case.");
        group.MapPut("/{id:guid}/assignment", QuoteHandlers.AssignQuoteAsync).WithSummary("Assign a private quote case.");
        group.MapPost("/{id:guid}/notes", QuoteHandlers.AddQuoteNoteAsync).WithSummary("Add an internal note to a quote case.");
        return group;
    }

    public static RouteGroupBuilder MapDashboardEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/backend-api/dashboard").WithTags("Operations Dashboard");
        group.MapGet("/", DashboardHandlers.GetDashboardAsync).WithSummary("Get dashboard counts and recent work items.");
        return group;
    }
}

file static class ClaimHandlers
{
    [Authorize]
    public static async Task<IResult> GetClaimsAsync(ClaimWorkflowService service, ClaimStatus? status, string? assignedToUserId, Guid? publicClaimId, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default)
        => Results.Ok(await service.GetClaimsAsync(status, assignedToUserId, publicClaimId, page, pageSize, cancellationToken));

    [Authorize]
    public static async Task<IResult> GetClaimByIdAsync(Guid id, ClaimWorkflowService service, CancellationToken cancellationToken)
        => await service.GetClaimByIdAsync(id, cancellationToken) is { } claimCase ? Results.Ok(claimCase) : Results.NotFound();

    [Authorize]
    public static async Task<IResult> UpdateClaimStatusAsync(Guid id, UpdateClaimCaseStatusRequest request, ClaimWorkflowService service, HttpContext httpContext, CancellationToken cancellationToken)
        => await service.UpdateStatusAsync(id, request, EndpointActors.GetActor(httpContext.User), cancellationToken) is { } claimCase ? Results.Ok(claimCase) : Results.NotFound();

    [Authorize]
    public static async Task<IResult> AssignClaimAsync(Guid id, AssignCaseRequest request, ClaimWorkflowService service, HttpContext httpContext, CancellationToken cancellationToken)
        => await service.AssignAsync(id, request, EndpointActors.GetActor(httpContext.User), cancellationToken) is { } claimCase ? Results.Ok(claimCase) : Results.NotFound();

    [Authorize]
    public static async Task<IResult> AddClaimNoteAsync(Guid id, AddCaseNoteRequest request, ClaimWorkflowService service, HttpContext httpContext, CancellationToken cancellationToken)
        => await service.AddNoteAsync(id, request, EndpointActors.GetActor(httpContext.User), cancellationToken) is { } claimCase ? Results.Ok(claimCase) : Results.NotFound();
}

file static class QuoteHandlers
{
    [Authorize]
    public static async Task<IResult> GetQuotesAsync(QuoteWorkflowService service, QuoteStatus? status, string? assignedToUserId, Guid? publicQuoteId, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default)
        => Results.Ok(await service.GetQuotesAsync(status, assignedToUserId, publicQuoteId, page, pageSize, cancellationToken));

    [Authorize]
    public static async Task<IResult> GetQuoteByIdAsync(Guid id, QuoteWorkflowService service, CancellationToken cancellationToken)
        => await service.GetQuoteByIdAsync(id, cancellationToken) is { } quoteCase ? Results.Ok(quoteCase) : Results.NotFound();

    [Authorize]
    public static async Task<IResult> UpdateQuoteStatusAsync(Guid id, UpdateQuoteCaseStatusRequest request, QuoteWorkflowService service, HttpContext httpContext, CancellationToken cancellationToken)
        => await service.UpdateStatusAsync(id, request, EndpointActors.GetActor(httpContext.User), cancellationToken) is { } quoteCase ? Results.Ok(quoteCase) : Results.NotFound();

    [Authorize]
    public static async Task<IResult> AssignQuoteAsync(Guid id, AssignCaseRequest request, QuoteWorkflowService service, HttpContext httpContext, CancellationToken cancellationToken)
        => await service.AssignAsync(id, request, EndpointActors.GetActor(httpContext.User), cancellationToken) is { } quoteCase ? Results.Ok(quoteCase) : Results.NotFound();

    [Authorize]
    public static async Task<IResult> AddQuoteNoteAsync(Guid id, AddCaseNoteRequest request, QuoteWorkflowService service, HttpContext httpContext, CancellationToken cancellationToken)
        => await service.AddNoteAsync(id, request, EndpointActors.GetActor(httpContext.User), cancellationToken) is { } quoteCase ? Results.Ok(quoteCase) : Results.NotFound();
}

file static class DashboardHandlers
{
    [Authorize]
    public static async Task<IResult> GetDashboardAsync(InsuranceDbContext db, CancellationToken cancellationToken)
    {
        var claimCounts = await db.PrivateClaimCases.AsNoTracking()
            .GroupBy(item => item.Status)
            .Select(group => new DashboardCountResponse(group.Key.ToString(), group.Count()))
            .OrderBy(item => item.Key)
            .ToListAsync(cancellationToken);

        var quoteCounts = await db.PrivateQuoteCases.AsNoTracking()
            .GroupBy(item => item.Status)
            .Select(group => new DashboardCountResponse(group.Key.ToString(), group.Count()))
            .OrderBy(item => item.Key)
            .ToListAsync(cancellationToken);

        var recentClaims = await db.PrivateClaimCases.AsNoTracking()
            .OrderByDescending(item => item.UpdatedAtUtc)
            .Take(5)
            .Select(item => new RecentWorkItemResponse("claim", item.Id, item.ClaimNumber, item.Status.ToString(), item.AssignedToDisplayName, item.UpdatedAtUtc))
            .ToListAsync(cancellationToken);

        var recentQuotes = await db.PrivateQuoteCases.AsNoTracking()
            .OrderByDescending(item => item.UpdatedAtUtc)
            .Take(5)
            .Select(item => new RecentWorkItemResponse("quote", item.Id, item.QuoteNumber, item.Status.ToString(), item.AssignedToDisplayName, item.UpdatedAtUtc))
            .ToListAsync(cancellationToken);

        var recentWorkItems = recentClaims.Concat(recentQuotes)
            .OrderByDescending(item => item.UpdatedAtUtc)
            .Take(10)
            .ToArray();

        return Results.Ok(new DashboardResponse(claimCounts, quoteCounts, recentWorkItems));
    }
}

file static class EndpointActors
{
    public static string GetActor(ClaimsPrincipal principal)
        => principal.Identity?.Name
            ?? principal.FindFirstValue(ClaimTypes.Email)
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? "system";
}
