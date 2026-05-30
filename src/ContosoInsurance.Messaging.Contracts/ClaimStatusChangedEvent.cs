namespace ContosoInsurance.Messaging.Contracts;

public record ClaimStatusChangedEvent(
    Guid PublicClaimId,
    Guid PrivateCaseId,
    Guid WorkflowCorrelationId,
    string ClaimNumber,
    string Status,
    string? ValidationSummary,
    string? AssignedTo,
    DateTime ChangedAtUtc);
