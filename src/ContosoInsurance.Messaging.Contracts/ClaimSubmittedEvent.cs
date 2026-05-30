namespace ContosoInsurance.Messaging.Contracts;

public record ClaimSubmittedEvent(
    Guid PublicClaimId,
    Guid WorkflowCorrelationId,
    string ClaimNumber,
    Guid PolicyId,
    decimal Amount,
    string Description,
    DateTime IncidentDate,
    DateTime FiledAtUtc);
