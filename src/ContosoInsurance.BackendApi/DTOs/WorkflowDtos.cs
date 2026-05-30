using ContosoInsurance.Data.Enums;

namespace ContosoInsurance.BackendApi.DTOs;

public record PagedResult<T>(IReadOnlyCollection<T> Items, int TotalCount, int Page, int PageSize);

public record ClaimCaseNoteResponse(Guid Id, string Note, string CreatedBy, DateTime CreatedAtUtc);
public record ClaimCaseAuditResponse(Guid Id, string Action, ClaimStatus? FromStatus, ClaimStatus? ToStatus, string PerformedBy, string? Details, DateTime OccurredAtUtc);
public record ClaimCaseSummaryResponse(Guid Id, Guid PublicClaimId, Guid WorkflowCorrelationId, string ClaimNumber, ClaimStatus Status, decimal Amount, string Description, string? AssignedToDisplayName, string? ValidationSummary, DateTime CreatedAtUtc, DateTime UpdatedAtUtc, string? PublicStatus, DateTime? ProjectionUpdatedAtUtc);
public record ClaimCaseDetailResponse(Guid Id, Guid PublicClaimId, Guid WorkflowCorrelationId, string ClaimNumber, ClaimStatus Status, string Description, decimal Amount, DateTime IncidentDate, Guid PolicyId, string? AssignedToUserId, string? AssignedToDisplayName, string? ValidationSummary, DateTime CreatedAtUtc, DateTime UpdatedAtUtc, DateTime? ResolvedAtUtc, string? PublicStatus, DateTime? ProjectionUpdatedAtUtc, IReadOnlyCollection<ClaimCaseNoteResponse> Notes, IReadOnlyCollection<ClaimCaseAuditResponse> AuditTrail);
public record UpdateClaimCaseStatusRequest(ClaimStatus Status, string? ValidationSummary, string? Note);
public record AssignCaseRequest(string AssignedToUserId, string AssignedToDisplayName);
public record AddCaseNoteRequest(string Note);

public record QuoteCaseNoteResponse(Guid Id, string Note, string CreatedBy, DateTime CreatedAtUtc);
public record QuoteCaseAuditResponse(Guid Id, string Action, QuoteStatus? FromStatus, QuoteStatus? ToStatus, string PerformedBy, string? Details, DateTime OccurredAtUtc);
public record QuoteCaseSummaryResponse(Guid Id, Guid PublicQuoteId, Guid WorkflowCorrelationId, string QuoteNumber, QuoteStatus Status, PolicyType Type, decimal CoverageAmount, decimal EstimatedPremium, string? AssignedToDisplayName, string? UnderwritingSummary, DateTime CreatedAtUtc, DateTime UpdatedAtUtc, string? PublicStatus, DateTime? ProjectionUpdatedAtUtc);
public record QuoteCaseDetailResponse(Guid Id, Guid PublicQuoteId, Guid WorkflowCorrelationId, string QuoteNumber, QuoteStatus Status, PolicyType Type, decimal CoverageAmount, decimal EstimatedPremium, Guid CustomerId, string? AssignedToUserId, string? AssignedToDisplayName, string? UnderwritingSummary, DateTime CreatedAtUtc, DateTime UpdatedAtUtc, DateTime? CompletedAtUtc, string? PublicStatus, DateTime? ProjectionUpdatedAtUtc, IReadOnlyCollection<QuoteCaseNoteResponse> Notes, IReadOnlyCollection<QuoteCaseAuditResponse> AuditTrail);
public record UpdateQuoteCaseStatusRequest(QuoteStatus Status, decimal? EstimatedPremium, string? UnderwritingSummary, string? Note);

public record DashboardCountResponse(string Key, int Count);
public record RecentWorkItemResponse(string Type, Guid Id, string ReferenceNumber, string Status, string? Assignee, DateTime UpdatedAtUtc);
public record DashboardResponse(IReadOnlyCollection<DashboardCountResponse> ClaimCounts, IReadOnlyCollection<DashboardCountResponse> QuoteCounts, IReadOnlyCollection<RecentWorkItemResponse> RecentWorkItems);
