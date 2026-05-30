namespace ContosoInsurance.Messaging.Contracts;

public record ClaimProjectionUpdatedEvent(
    Guid PublicClaimId,
    Guid WorkflowCorrelationId,
    string ClaimNumber,
    string PublicStatus,
    string StatusSummary,
    DateTime UpdatedAtUtc);
