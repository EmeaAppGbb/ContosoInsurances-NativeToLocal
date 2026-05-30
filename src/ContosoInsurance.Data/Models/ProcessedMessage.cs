using System.ComponentModel.DataAnnotations;

namespace ContosoInsurance.Data.Models;

public class ProcessedMessage
{
    public Guid MessageId { get; set; }

    [Required, MaxLength(200)]
    public string ConsumerName { get; set; } = string.Empty;

    public Guid CorrelationId { get; set; }
    public DateTime ProcessedAtUtc { get; set; } = DateTime.UtcNow;
    public string? SubjectId { get; set; }
}
