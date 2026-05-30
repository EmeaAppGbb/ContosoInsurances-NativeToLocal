namespace ContosoInsurance.Messaging.Contracts;

public static class MessagingTopology
{
    public const string CommandsExchange = "contoso.workflow.commands";
    public const string EventsExchange = "contoso.workflow.events";
    public const string DeadLetterExchange = "contoso.workflow.dlx";

    public const string PrivateClaimIntakeQueue = "private.claim-intake";
    public const string PrivateQuoteIntakeQueue = "private.quote-intake";
    public const string PublicClaimProjectionQueue = "public.claim-projection";
    public const string PublicQuoteProjectionQueue = "public.quote-projection";

    public static string DlqFor(string queueName) => $"{queueName}.dlq";
}
