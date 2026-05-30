using ContosoInsurance.BackendApi.DTOs;
using ContosoInsurance.BackendApi.Messaging;
using ContosoInsurance.Data;
using ContosoInsurance.Data.Enums;
using ContosoInsurance.Data.Models;
using ContosoInsurance.Messaging.Contracts;
using Microsoft.EntityFrameworkCore;

namespace ContosoInsurance.BackendApi.Services;

public sealed class ClaimWorkflowService(InsuranceDbContext db, IOutboxDispatcher outboxDispatcher)
{
    public async Task<PagedResult<ClaimCaseSummaryResponse>> GetClaimsAsync(ClaimStatus? status, string? assignedToUserId, Guid? publicClaimId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = db.PrivateClaimCases.AsNoTracking().AsQueryable();

        if (status.HasValue)
        {
            query = query.Where(claimCase => claimCase.Status == status.Value);
        }

        if (!string.IsNullOrWhiteSpace(assignedToUserId))
        {
            query = query.Where(claimCase => claimCase.AssignedToUserId == assignedToUserId);
        }

        if (publicClaimId.HasValue)
        {
            query = query.Where(claimCase => claimCase.PublicClaimId == publicClaimId.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var claimCases = await query.OrderByDescending(claimCase => claimCase.UpdatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var projections = await db.ClaimProjections.AsNoTracking()
            .Where(projection => claimCases.Select(claimCase => claimCase.PublicClaimId).Contains(projection.PublicClaimId))
            .ToDictionaryAsync(projection => projection.PublicClaimId, cancellationToken);

        return new PagedResult<ClaimCaseSummaryResponse>(
            claimCases.Select(claimCase => MapSummary(claimCase, projections.GetValueOrDefault(claimCase.PublicClaimId))).ToArray(),
            totalCount,
            page,
            pageSize);
    }

    public async Task<ClaimCaseDetailResponse?> GetClaimByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var claimCase = await db.PrivateClaimCases.AsNoTracking()
            .Include(item => item.Notes)
            .Include(item => item.AuditTrail)
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (claimCase is null)
        {
            return null;
        }

        var projection = await db.ClaimProjections.AsNoTracking().FirstOrDefaultAsync(item => item.PublicClaimId == claimCase.PublicClaimId, cancellationToken);
        return MapDetail(claimCase, projection);
    }

    public async Task<ClaimCaseDetailResponse?> UpdateStatusAsync(Guid id, UpdateClaimCaseStatusRequest request, string performedBy, CancellationToken cancellationToken)
    {
        var claimCase = await db.PrivateClaimCases
            .Include(item => item.Notes)
            .Include(item => item.AuditTrail)
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (claimCase is null)
        {
            return null;
        }

        ValidateClaimTransition(claimCase.Status, request.Status);

        var previousStatus = claimCase.Status;
        claimCase.Status = request.Status;
        claimCase.ValidationSummary = string.IsNullOrWhiteSpace(request.ValidationSummary) ? claimCase.ValidationSummary : request.ValidationSummary;
        claimCase.UpdatedAtUtc = DateTime.UtcNow;
        claimCase.ResolvedAtUtc = request.Status is ClaimStatus.Approved or ClaimStatus.Denied or ClaimStatus.Paid or ClaimStatus.Closed ? DateTime.UtcNow : null;
        claimCase.AuditTrail.Add(new ClaimCaseAuditEntry
        {
            Id = Guid.NewGuid(),
            Action = "claim.status.changed",
            FromStatus = previousStatus,
            ToStatus = request.Status,
            PerformedBy = performedBy,
            Details = request.ValidationSummary,
            OccurredAtUtc = DateTime.UtcNow
        });

        if (!string.IsNullOrWhiteSpace(request.Note))
        {
            claimCase.Notes.Add(new ClaimCaseNote
            {
                Id = Guid.NewGuid(),
                Note = request.Note,
                CreatedBy = performedBy,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        AddStatusEvents(claimCase, previousStatus, request.ValidationSummary);
        await db.SaveChangesAsync(cancellationToken);
        await outboxDispatcher.DispatchPendingAsync(cancellationToken);
        return await GetClaimByIdAsync(id, cancellationToken);
    }

    public async Task<ClaimCaseDetailResponse?> AssignAsync(Guid id, AssignCaseRequest request, string performedBy, CancellationToken cancellationToken)
    {
        var claimCase = await db.PrivateClaimCases
            .Include(item => item.AuditTrail)
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (claimCase is null)
        {
            return null;
        }

        claimCase.AssignedToUserId = request.AssignedToUserId;
        claimCase.AssignedToDisplayName = request.AssignedToDisplayName;
        claimCase.UpdatedAtUtc = DateTime.UtcNow;
        claimCase.AuditTrail.Add(new ClaimCaseAuditEntry
        {
            Id = Guid.NewGuid(),
            Action = "claim.assignment.updated",
            PerformedBy = performedBy,
            Details = $"Assigned to {request.AssignedToDisplayName} ({request.AssignedToUserId}).",
            OccurredAtUtc = DateTime.UtcNow
        });

        await db.SaveChangesAsync(cancellationToken);
        return await GetClaimByIdAsync(id, cancellationToken);
    }

    public async Task<ClaimCaseDetailResponse?> AddNoteAsync(Guid id, AddCaseNoteRequest request, string performedBy, CancellationToken cancellationToken)
    {
        var claimCase = await db.PrivateClaimCases
            .Include(item => item.Notes)
            .Include(item => item.AuditTrail)
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (claimCase is null)
        {
            return null;
        }

        claimCase.UpdatedAtUtc = DateTime.UtcNow;
        claimCase.Notes.Add(new ClaimCaseNote
        {
            Id = Guid.NewGuid(),
            Note = request.Note,
            CreatedBy = performedBy,
            CreatedAtUtc = DateTime.UtcNow
        });
        claimCase.AuditTrail.Add(new ClaimCaseAuditEntry
        {
            Id = Guid.NewGuid(),
            Action = "claim.note.added",
            PerformedBy = performedBy,
            Details = request.Note,
            OccurredAtUtc = DateTime.UtcNow
        });

        await db.SaveChangesAsync(cancellationToken);
        return await GetClaimByIdAsync(id, cancellationToken);
    }

    private void AddStatusEvents(PrivateClaimCase claimCase, ClaimStatus previousStatus, string? validationSummary)
    {
        var changedEvent = new ClaimStatusChangedEvent(
            claimCase.PublicClaimId,
            claimCase.Id,
            claimCase.WorkflowCorrelationId,
            claimCase.ClaimNumber,
            claimCase.Status.ToString(),
            validationSummary ?? claimCase.ValidationSummary,
            claimCase.AssignedToDisplayName,
            DateTime.UtcNow);

        var projectionEvent = new ClaimProjectionUpdatedEvent(
            claimCase.PublicClaimId,
            claimCase.WorkflowCorrelationId,
            claimCase.ClaimNumber,
            claimCase.Status.ToString(),
            $"Claim moved from {previousStatus} to {claimCase.Status}.",
            DateTime.UtcNow);

        db.OutboxMessages.Add(OutboxMessageFactory.Create(
            MessagingTopology.EventsExchange,
            RoutingKeys.ClaimStatusChangedV1,
            MessageTypes.ClaimStatusChanged,
            "backend-api",
            "private",
            "claim",
            claimCase.PublicClaimId.ToString(),
            claimCase.WorkflowCorrelationId,
            null,
            changedEvent));

        db.OutboxMessages.Add(OutboxMessageFactory.Create(
            MessagingTopology.EventsExchange,
            RoutingKeys.ClaimProjectionUpdatedV1,
            MessageTypes.ClaimProjectionUpdated,
            "backend-api",
            "public",
            "claim",
            claimCase.PublicClaimId.ToString(),
            claimCase.WorkflowCorrelationId,
            null,
            projectionEvent));
    }

    private static void ValidateClaimTransition(ClaimStatus currentStatus, ClaimStatus targetStatus)
    {
        var isValid = currentStatus switch
        {
            ClaimStatus.Submitted => targetStatus is ClaimStatus.UnderReview or ClaimStatus.Denied,
            ClaimStatus.UnderReview => targetStatus is ClaimStatus.Approved or ClaimStatus.Denied,
            ClaimStatus.Approved => targetStatus is ClaimStatus.Paid or ClaimStatus.Closed,
            ClaimStatus.Paid => targetStatus is ClaimStatus.Closed,
            _ => false
        };

        if (!isValid)
        {
            throw new InvalidOperationException($"Cannot transition claim from {currentStatus} to {targetStatus}.");
        }
    }

    private static ClaimCaseSummaryResponse MapSummary(PrivateClaimCase claimCase, ClaimProjection? projection)
        => new(
            claimCase.Id,
            claimCase.PublicClaimId,
            claimCase.WorkflowCorrelationId,
            claimCase.ClaimNumber,
            claimCase.Status,
            claimCase.Amount,
            claimCase.Description,
            claimCase.AssignedToDisplayName,
            claimCase.ValidationSummary,
            claimCase.CreatedAtUtc,
            claimCase.UpdatedAtUtc,
            projection?.PublicStatus,
            projection?.LastUpdatedAtUtc);

    private static ClaimCaseDetailResponse MapDetail(PrivateClaimCase claimCase, ClaimProjection? projection)
        => new(
            claimCase.Id,
            claimCase.PublicClaimId,
            claimCase.WorkflowCorrelationId,
            claimCase.ClaimNumber,
            claimCase.Status,
            claimCase.Description,
            claimCase.Amount,
            claimCase.IncidentDate,
            claimCase.PolicyId,
            claimCase.AssignedToUserId,
            claimCase.AssignedToDisplayName,
            claimCase.ValidationSummary,
            claimCase.CreatedAtUtc,
            claimCase.UpdatedAtUtc,
            claimCase.ResolvedAtUtc,
            projection?.PublicStatus,
            projection?.LastUpdatedAtUtc,
            claimCase.Notes.OrderByDescending(note => note.CreatedAtUtc).Select(note => new ClaimCaseNoteResponse(note.Id, note.Note, note.CreatedBy, note.CreatedAtUtc)).ToArray(),
            claimCase.AuditTrail.OrderByDescending(entry => entry.OccurredAtUtc).Select(entry => new ClaimCaseAuditResponse(entry.Id, entry.Action, entry.FromStatus, entry.ToStatus, entry.PerformedBy, entry.Details, entry.OccurredAtUtc)).ToArray());
}
