namespace ContosoInsurance.Messaging.Contracts;

public record QuoteProjectionUpdatedEvent(
    Guid PublicQuoteId,
    Guid WorkflowCorrelationId,
    string QuoteNumber,
    string PublicStatus,
    decimal EstimatedPremium,
    string StatusSummary,
    DateTime UpdatedAtUtc);
