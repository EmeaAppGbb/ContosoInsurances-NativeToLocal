namespace ContosoInsurance.Messaging.Contracts;

public static class RoutingKeys
{
    public const string ClaimSubmittedV1 = "claim.submitted.v1";
    public const string ClaimStatusChangedV1 = "claim.status-changed.v1";
    public const string QuoteRequestedV1 = "quote.requested.v1";
    public const string QuoteStatusChangedV1 = "quote.status-changed.v1";
    public const string ClaimProjectionUpdatedV1 = "projection.claim.updated.v1";
    public const string QuoteProjectionUpdatedV1 = "projection.quote.updated.v1";
}
