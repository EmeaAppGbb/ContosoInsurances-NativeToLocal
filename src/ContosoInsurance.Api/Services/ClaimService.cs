using ContosoInsurance.Api.DTOs;
using ContosoInsurance.Api.Messaging;
using ContosoInsurance.Data;
using ContosoInsurance.Data.Enums;
using ContosoInsurance.Data.Models;
using ContosoInsurance.Messaging.Contracts;
using Microsoft.EntityFrameworkCore;

namespace ContosoInsurance.Api.Services;

public class ClaimService(InsuranceDbContext db, IOutboxDispatcher outboxDispatcher) : IClaimService
{
    public async Task<PaginatedResponse<ClaimResponse>> GetClaimsAsync(ClaimStatus? status, Guid? policyId, int page, int pageSize)
    {
        var query = db.Claims.AsNoTracking().AsQueryable();

        if (status.HasValue) query = query.Where(c => c.Status == status.Value);
        if (policyId.HasValue) query = query.Where(c => c.PolicyId == policyId.Value);

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderByDescending(c => c.FiledDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new ClaimResponse(
                c.Id,
                c.ClaimNumber,
                c.Status,
                c.Description,
                c.Amount,
                c.IncidentDate,
                c.FiledDate,
                c.ResolvedDate,
                c.PolicyId,
                c.Policy.PolicyNumber,
                c.WorkflowCorrelationId,
                db.ClaimProjections.Where(p => p.PublicClaimId == c.Id).Select(p => p.PublicStatus).FirstOrDefault(),
                db.ClaimProjections.Where(p => p.PublicClaimId == c.Id).Select(p => (DateTime?)p.LastUpdatedAtUtc).FirstOrDefault()))
            .ToListAsync();

        return new PaginatedResponse<ClaimResponse>(items, totalCount, page, pageSize);
    }

    public async Task<ClaimResponse?> GetClaimByIdAsync(Guid id)
    {
        return await db.Claims.AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new ClaimResponse(
                c.Id,
                c.ClaimNumber,
                c.Status,
                c.Description,
                c.Amount,
                c.IncidentDate,
                c.FiledDate,
                c.ResolvedDate,
                c.PolicyId,
                c.Policy.PolicyNumber,
                c.WorkflowCorrelationId,
                db.ClaimProjections.Where(p => p.PublicClaimId == c.Id).Select(p => p.PublicStatus).FirstOrDefault(),
                db.ClaimProjections.Where(p => p.PublicClaimId == c.Id).Select(p => (DateTime?)p.LastUpdatedAtUtc).FirstOrDefault()))
            .FirstOrDefaultAsync();
    }

    public async Task<ClaimResponse> SubmitClaimAsync(SubmitClaimRequest request)
    {
        var policy = await db.Policies.FindAsync(request.PolicyId);
        if (policy is null)
            throw new KeyNotFoundException($"Policy '{request.PolicyId}' not found.");

        if (policy.Status != PolicyStatus.Active)
            throw new InvalidOperationException("Claims can only be filed against active policies.");

        if (request.Amount > policy.CoverageAmount)
            throw new ArgumentException("Claim amount exceeds policy coverage.");

        var claim = new Claim
        {
            Id = Guid.NewGuid(),
            WorkflowCorrelationId = Guid.NewGuid(),
            ClaimNumber = $"CLM-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString()[..8].ToUpperInvariant()}",
            PolicyId = request.PolicyId,
            Description = request.Description,
            Amount = request.Amount,
            IncidentDate = request.IncidentDate,
            FiledDate = DateTime.UtcNow,
            Status = ClaimStatus.Submitted
        };

        db.Claims.Add(claim);
        db.OutboxMessages.Add(OutboxMessageFactory.Create(
            MessagingTopology.CommandsExchange,
            RoutingKeys.ClaimSubmittedV1,
            MessageTypes.ClaimSubmitted,
            "public-api",
            "private",
            "claim",
            claim.Id.ToString(),
            claim.WorkflowCorrelationId,
            null,
            new ClaimSubmittedEvent(claim.Id, claim.WorkflowCorrelationId, claim.ClaimNumber, claim.PolicyId, claim.Amount, claim.Description, claim.IncidentDate, claim.FiledDate)));

        await db.SaveChangesAsync();
        await outboxDispatcher.DispatchPendingAsync();

        return new ClaimResponse(
            claim.Id,
            claim.ClaimNumber,
            claim.Status,
            claim.Description,
            claim.Amount,
            claim.IncidentDate,
            claim.FiledDate,
            claim.ResolvedDate,
            claim.PolicyId,
            policy.PolicyNumber,
            claim.WorkflowCorrelationId);
    }

    public async Task<ClaimResponse?> UpdateClaimStatusAsync(Guid id, UpdateClaimRequest request)
    {
        var claim = await db.Claims.FindAsync(id);
        if (claim is null) return null;

        ValidateStatusTransition(claim.Status, request.Status);

        claim.Status = request.Status;
        if (request.Status is ClaimStatus.Approved or ClaimStatus.Denied or ClaimStatus.Closed)
            claim.ResolvedDate = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return await GetClaimByIdAsync(id);
    }

    private static void ValidateStatusTransition(ClaimStatus current, ClaimStatus target)
    {
        var valid = current switch
        {
            ClaimStatus.Submitted => target is ClaimStatus.UnderReview or ClaimStatus.Denied,
            ClaimStatus.UnderReview => target is ClaimStatus.Approved or ClaimStatus.Denied,
            ClaimStatus.Approved => target is ClaimStatus.Paid or ClaimStatus.Closed,
            ClaimStatus.Paid => target is ClaimStatus.Closed,
            _ => false
        };

        if (!valid)
            throw new InvalidOperationException($"Cannot transition claim from {current} to {target}.");
    }
}
