using System.ComponentModel.DataAnnotations;

namespace ContosoInsurance.Data.Models;

public class OutboxMessage
{
    public Guid Id { get; set; }
    public Guid MessageId { get; set; }
    public Guid CorrelationId { get; set; }
    public Guid? CausationId { get; set; }

    [Required, MaxLength(200)]
    public string Exchange { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string RoutingKey { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string MessageType { get; set; } = string.Empty;

    public int SchemaVersion { get; set; } = 1;

    [Required, MaxLength(100)]
    public string SourceSystem { get; set; } = string.Empty;

    [Required, MaxLength(50)]
    public string Classification { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string SubjectType { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string SubjectId { get; set; } = string.Empty;

    [Required]
    public string PayloadJson { get; set; } = string.Empty;

    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? PublishedAtUtc { get; set; }
    public int PublishAttempts { get; set; }
    public string? LastError { get; set; }
}
