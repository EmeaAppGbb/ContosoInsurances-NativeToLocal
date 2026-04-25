using ContosoInsurance.Api.DTOs;
using ContosoInsurance.Api.Services;
using ContosoInsurance.Data.Enums;

namespace ContosoInsurance.Api.Endpoints;

public static class PolicyEndpoints
{
    public static RouteGroupBuilder MapPolicyEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/policies").WithTags("Policies");

        group.MapGet("/", async (IPolicyService svc, PolicyType? type, PolicyStatus? status, Guid? customerId, int page = 1, int pageSize = 20) =>
            Results.Ok(await svc.GetPoliciesAsync(type, status, customerId, page, pageSize)))
            .WithName("GetPolicies")
            .WithSummary("List policies with optional filters and pagination");

        group.MapGet("/{id:guid}", async (Guid id, IPolicyService svc) =>
            await svc.GetPolicyByIdAsync(id) is { } policy
                ? Results.Ok(policy)
                : Results.NotFound())
            .WithName("GetPolicy")
            .WithSummary("Get a policy by ID");

        group.MapPost("/", async (CreatePolicyRequest request, IPolicyService svc) =>
        {
            var policy = await svc.CreatePolicyAsync(request);
            return Results.Created($"/api/policies/{policy.Id}", policy);
        })
        .WithName("CreatePolicy")
        .WithSummary("Create a new policy");

        group.MapPut("/{id:guid}", async (Guid id, UpdatePolicyRequest request, IPolicyService svc) =>
            await svc.UpdatePolicyAsync(id, request) is { } policy
                ? Results.Ok(policy)
                : Results.NotFound())
            .WithName("UpdatePolicy")
            .WithSummary("Update a policy");

        group.MapPost("/{id:guid}/activate", async (Guid id, IPolicyService svc) =>
            await svc.ActivatePolicyAsync(id) is { } policy
                ? Results.Ok(policy)
                : Results.NotFound())
            .WithName("ActivatePolicy")
            .WithSummary("Activate a draft policy");

        group.MapPost("/{id:guid}/cancel", async (Guid id, IPolicyService svc) =>
            await svc.CancelPolicyAsync(id) is { } policy
                ? Results.Ok(policy)
                : Results.NotFound())
            .WithName("CancelPolicy")
            .WithSummary("Cancel a policy");

        group.MapPost("/{id:guid}/renew", async (Guid id, IPolicyService svc, int months = 12) =>
            await svc.RenewPolicyAsync(id, months) is { } policy
                ? Results.Ok(policy)
                : Results.NotFound())
            .WithName("RenewPolicy")
            .WithSummary("Renew an active or expired policy");

        return group;
    }
}
