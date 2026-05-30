using System.ComponentModel.DataAnnotations;
using ContosoInsurance.Data.Enums;

namespace ContosoInsurance.Data.Models;

public class QuoteCaseAuditEntry
{
    public Guid Id { get; set; }
    public Guid PrivateQuoteCaseId { get; set; }
    public PrivateQuoteCase QuoteCase { get; set; } = null!;

    [Required, MaxLength(200)]
    public string Action { get; set; } = string.Empty;

    public QuoteStatus? FromStatus { get; set; }
    public QuoteStatus? ToStatus { get; set; }

    [Required, MaxLength(200)]
    public string PerformedBy { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Details { get; set; }

    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
}
