using ContosoInsurance.Api.DTOs;
using ContosoInsurance.Api.Services;
using ContosoInsurance.Data.Enums;

namespace ContosoInsurance.Api.Endpoints;

public static class ClaimEndpoints
{
    public static RouteGroupBuilder MapClaimEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/claims").WithTags("Claims");

        group.MapGet("/", async (IClaimService svc, ClaimStatus? status, Guid? policyId, int page = 1, int pageSize = 20) =>
            Results.Ok(await svc.GetClaimsAsync(status, policyId, page, pageSize)))
            .WithName("GetClaims")
            .WithSummary("List claims with optional filters and pagination");

        group.MapGet("/{id:guid}", async (Guid id, IClaimService svc) =>
            await svc.GetClaimByIdAsync(id) is { } claim
                ? Results.Ok(claim)
                : Results.NotFound())
            .WithName("GetClaim")
            .WithSummary("Get a claim by ID");

        group.MapPost("/", async (SubmitClaimRequest request, IClaimService svc) =>
        {
            var claim = await svc.SubmitClaimAsync(request);
            return Results.Created($"/api/claims/{claim.Id}", claim);
        })
        .WithName("SubmitClaim")
        .WithSummary("Submit a new claim against an active policy");

        group.MapPut("/{id:guid}/status", async (Guid id, UpdateClaimRequest request, IClaimService svc) =>
            await svc.UpdateClaimStatusAsync(id, request) is { } claim
                ? Results.Ok(claim)
                : Results.NotFound())
            .WithName("UpdateClaimStatus")
            .WithSummary("Update claim status (follows valid transitions)");

        return group;
    }
}
