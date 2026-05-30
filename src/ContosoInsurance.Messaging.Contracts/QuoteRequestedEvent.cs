namespace ContosoInsurance.Messaging.Contracts;

public record QuoteRequestedEvent(
    Guid PublicQuoteId,
    Guid WorkflowCorrelationId,
    string QuoteNumber,
    Guid CustomerId,
    string PolicyType,
    decimal CoverageAmount,
    decimal EstimatedPremium,
    DateTime RequestedAtUtc);
