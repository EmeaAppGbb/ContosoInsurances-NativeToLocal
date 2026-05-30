using ContosoInsurance.BackendApi.DTOs;
using ContosoInsurance.BackendApi.Messaging;
using ContosoInsurance.Data;
using ContosoInsurance.Data.Enums;
using ContosoInsurance.Data.Models;
using ContosoInsurance.Messaging.Contracts;
using Microsoft.EntityFrameworkCore;

namespace ContosoInsurance.BackendApi.Services;

public sealed class QuoteWorkflowService(InsuranceDbContext db, IOutboxDispatcher outboxDispatcher)
{
    public async Task<PagedResult<QuoteCaseSummaryResponse>> GetQuotesAsync(QuoteStatus? status, string? assignedToUserId, Guid? publicQuoteId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = db.PrivateQuoteCases.AsNoTracking().AsQueryable();

        if (status.HasValue)
        {
            query = query.Where(quoteCase => quoteCase.Status == status.Value);
        }

        if (!string.IsNullOrWhiteSpace(assignedToUserId))
        {
            query = query.Where(quoteCase => quoteCase.AssignedToUserId == assignedToUserId);
        }

        if (publicQuoteId.HasValue)
        {
            query = query.Where(quoteCase => quoteCase.PublicQuoteId == publicQuoteId.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var quoteCases = await query.OrderByDescending(quoteCase => quoteCase.UpdatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var projections = await db.QuoteProjections.AsNoTracking()
            .Where(projection => quoteCases.Select(quoteCase => quoteCase.PublicQuoteId).Contains(projection.PublicQuoteId))
            .ToDictionaryAsync(projection => projection.PublicQuoteId, cancellationToken);

        return new PagedResult<QuoteCaseSummaryResponse>(
            quoteCases.Select(quoteCase => MapSummary(quoteCase, projections.GetValueOrDefault(quoteCase.PublicQuoteId))).ToArray(),
            totalCount,
            page,
            pageSize);
    }

    public async Task<QuoteCaseDetailResponse?> GetQuoteByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var quoteCase = await db.PrivateQuoteCases.AsNoTracking()
            .Include(item => item.Notes)
            .Include(item => item.AuditTrail)
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (quoteCase is null)
        {
            return null;
        }

        var projection = await db.QuoteProjections.AsNoTracking().FirstOrDefaultAsync(item => item.PublicQuoteId == quoteCase.PublicQuoteId, cancellationToken);
        return MapDetail(quoteCase, projection);
    }

    public async Task<QuoteCaseDetailResponse?> UpdateStatusAsync(Guid id, UpdateQuoteCaseStatusRequest request, string performedBy, CancellationToken cancellationToken)
    {
        var quoteCase = await db.PrivateQuoteCases
            .Include(item => item.Notes)
            .Include(item => item.AuditTrail)
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (quoteCase is null)
        {
            return null;
        }

        ValidateQuoteTransition(quoteCase.Status, request.Status);

        var previousStatus = quoteCase.Status;
        quoteCase.Status = request.Status;
        quoteCase.UnderwritingSummary = string.IsNullOrWhiteSpace(request.UnderwritingSummary) ? quoteCase.UnderwritingSummary : request.UnderwritingSummary;
        if (request.EstimatedPremium.HasValue)
        {
            quoteCase.EstimatedPremium = request.EstimatedPremium.Value;
        }

        quoteCase.UpdatedAtUtc = DateTime.UtcNow;
        quoteCase.CompletedAtUtc = request.Status is QuoteStatus.Approved or QuoteStatus.Declined ? DateTime.UtcNow : null;
        quoteCase.AuditTrail.Add(new QuoteCaseAuditEntry
        {
            Id = Guid.NewGuid(),
            Action = "quote.status.changed",
            FromStatus = previousStatus,
            ToStatus = request.Status,
            PerformedBy = performedBy,
            Details = request.UnderwritingSummary,
            OccurredAtUtc = DateTime.UtcNow
        });

        if (!string.IsNullOrWhiteSpace(request.Note))
        {
            quoteCase.Notes.Add(new QuoteCaseNote
            {
                Id = Guid.NewGuid(),
                Note = request.Note,
                CreatedBy = performedBy,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        AddStatusEvents(quoteCase, previousStatus, quoteCase.UnderwritingSummary);
        await db.SaveChangesAsync(cancellationToken);
        await outboxDispatcher.DispatchPendingAsync(cancellationToken);
        return await GetQuoteByIdAsync(id, cancellationToken);
    }

    public async Task<QuoteCaseDetailResponse?> AssignAsync(Guid id, AssignCaseRequest request, string performedBy, CancellationToken cancellationToken)
    {
        var quoteCase = await db.PrivateQuoteCases
            .Include(item => item.AuditTrail)
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (quoteCase is null)
        {
            return null;
        }

        quoteCase.AssignedToUserId = request.AssignedToUserId;
        quoteCase.AssignedToDisplayName = request.AssignedToDisplayName;
        quoteCase.UpdatedAtUtc = DateTime.UtcNow;
        quoteCase.AuditTrail.Add(new QuoteCaseAuditEntry
        {
            Id = Guid.NewGuid(),
            Action = "quote.assignment.updated",
            PerformedBy = performedBy,
            Details = $"Assigned to {request.AssignedToDisplayName} ({request.AssignedToUserId}).",
            OccurredAtUtc = DateTime.UtcNow
        });

        await db.SaveChangesAsync(cancellationToken);
        return await GetQuoteByIdAsync(id, cancellationToken);
    }

    public async Task<QuoteCaseDetailResponse?> AddNoteAsync(Guid id, AddCaseNoteRequest request, string performedBy, CancellationToken cancellationToken)
    {
        var quoteCase = await db.PrivateQuoteCases
            .Include(item => item.Notes)
            .Include(item => item.AuditTrail)
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (quoteCase is null)
        {
            return null;
        }

        quoteCase.UpdatedAtUtc = DateTime.UtcNow;
        quoteCase.Notes.Add(new QuoteCaseNote
        {
            Id = Guid.NewGuid(),
            Note = request.Note,
            CreatedBy = performedBy,
            CreatedAtUtc = DateTime.UtcNow
        });
        quoteCase.AuditTrail.Add(new QuoteCaseAuditEntry
        {
            Id = Guid.NewGuid(),
            Action = "quote.note.added",
            PerformedBy = performedBy,
            Details = request.Note,
            OccurredAtUtc = DateTime.UtcNow
        });

        await db.SaveChangesAsync(cancellationToken);
        return await GetQuoteByIdAsync(id, cancellationToken);
    }

    private void AddStatusEvents(PrivateQuoteCase quoteCase, QuoteStatus previousStatus, string? underwritingSummary)
    {
        var changedEvent = new QuoteStatusChangedEvent(
            quoteCase.PublicQuoteId,
            quoteCase.Id,
            quoteCase.WorkflowCorrelationId,
            quoteCase.QuoteNumber,
            quoteCase.Status.ToString(),
            quoteCase.EstimatedPremium,
            underwritingSummary,
            quoteCase.AssignedToDisplayName,
            DateTime.UtcNow);

        var projectionEvent = new QuoteProjectionUpdatedEvent(
            quoteCase.PublicQuoteId,
            quoteCase.WorkflowCorrelationId,
            quoteCase.QuoteNumber,
            quoteCase.Status.ToString(),
            quoteCase.EstimatedPremium,
            $"Quote moved from {previousStatus} to {quoteCase.Status}.",
            DateTime.UtcNow);

        db.OutboxMessages.Add(OutboxMessageFactory.Create(
            MessagingTopology.EventsExchange,
            RoutingKeys.QuoteStatusChangedV1,
            MessageTypes.QuoteStatusChanged,
            "backend-api",
            "private",
            "quote",
            quoteCase.PublicQuoteId.ToString(),
            quoteCase.WorkflowCorrelationId,
            null,
            changedEvent));

        db.OutboxMessages.Add(OutboxMessageFactory.Create(
            MessagingTopology.EventsExchange,
            RoutingKeys.QuoteProjectionUpdatedV1,
            MessageTypes.QuoteProjectionUpdated,
            "backend-api",
            "public",
            "quote",
            quoteCase.PublicQuoteId.ToString(),
            quoteCase.WorkflowCorrelationId,
            null,
            projectionEvent));
    }

    private static void ValidateQuoteTransition(QuoteStatus currentStatus, QuoteStatus targetStatus)
    {
        var isValid = currentStatus switch
        {
            QuoteStatus.Requested => targetStatus is QuoteStatus.Underwriting or QuoteStatus.Declined,
            QuoteStatus.Underwriting => targetStatus is QuoteStatus.Approved or QuoteStatus.Declined,
            _ => false
        };

        if (!isValid)
        {
            throw new InvalidOperationException($"Cannot transition quote from {currentStatus} to {targetStatus}.");
        }
    }

    private static QuoteCaseSummaryResponse MapSummary(PrivateQuoteCase quoteCase, QuoteProjection? projection)
        => new(
            quoteCase.Id,
            quoteCase.PublicQuoteId,
            quoteCase.WorkflowCorrelationId,
            quoteCase.QuoteNumber,
            quoteCase.Status,
            quoteCase.Type,
            quoteCase.CoverageAmount,
            quoteCase.EstimatedPremium,
            quoteCase.AssignedToDisplayName,
            quoteCase.UnderwritingSummary,
            quoteCase.CreatedAtUtc,
            quoteCase.UpdatedAtUtc,
            projection?.PublicStatus,
            projection?.LastUpdatedAtUtc);

    private static QuoteCaseDetailResponse MapDetail(PrivateQuoteCase quoteCase, QuoteProjection? projection)
        => new(
            quoteCase.Id,
            quoteCase.PublicQuoteId,
            quoteCase.WorkflowCorrelationId,
            quoteCase.QuoteNumber,
            quoteCase.Status,
            quoteCase.Type,
            quoteCase.CoverageAmount,
            quoteCase.EstimatedPremium,
            quoteCase.CustomerId,
            quoteCase.AssignedToUserId,
            quoteCase.AssignedToDisplayName,
            quoteCase.UnderwritingSummary,
            quoteCase.CreatedAtUtc,
            quoteCase.UpdatedAtUtc,
            quoteCase.CompletedAtUtc,
            projection?.PublicStatus,
            projection?.LastUpdatedAtUtc,
            quoteCase.Notes.OrderByDescending(note => note.CreatedAtUtc).Select(note => new QuoteCaseNoteResponse(note.Id, note.Note, note.CreatedBy, note.CreatedAtUtc)).ToArray(),
            quoteCase.AuditTrail.OrderByDescending(entry => entry.OccurredAtUtc).Select(entry => new QuoteCaseAuditResponse(entry.Id, entry.Action, entry.FromStatus, entry.ToStatus, entry.PerformedBy, entry.Details, entry.OccurredAtUtc)).ToArray());
}
