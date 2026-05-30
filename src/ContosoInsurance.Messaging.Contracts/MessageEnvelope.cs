namespace ContosoInsurance.Messaging.Contracts;

public class MessageEnvelope<TPayload>
{
    public Guid MessageId { get; set; } = Guid.NewGuid();
    public Guid CorrelationId { get; set; }
    public Guid? CausationId { get; set; }
    public string MessageType { get; set; } = string.Empty;
    public int SchemaVersion { get; set; } = 1;
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
    public string SourceSystem { get; set; } = string.Empty;
    public string Classification { get; set; } = string.Empty;
    public string SubjectType { get; set; } = string.Empty;
    public string SubjectId { get; set; } = string.Empty;
    public TPayload Payload { get; set; } = default!;
}
