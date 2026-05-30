namespace ContosoInsurance.Messaging.Contracts;

public record QuoteStatusChangedEvent(
    Guid PublicQuoteId,
    Guid PrivateCaseId,
    Guid WorkflowCorrelationId,
    string QuoteNumber,
    string Status,
    decimal EstimatedPremium,
    string? UnderwritingSummary,
    string? AssignedTo,
    DateTime ChangedAtUtc);
