namespace ContosoInsurance.Messaging.Contracts;

public static class MessageTypes
{
    public const string ClaimSubmitted = "claim.submitted";
    public const string ClaimStatusChanged = "claim.status-changed";
    public const string QuoteRequested = "quote.requested";
    public const string QuoteStatusChanged = "quote.status-changed";
    public const string ClaimProjectionUpdated = "projection.claim.updated";
    public const string QuoteProjectionUpdated = "projection.quote.updated";
}
