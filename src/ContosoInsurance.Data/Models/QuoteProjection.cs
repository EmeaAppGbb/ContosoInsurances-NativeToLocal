using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ContosoInsurance.Data.Models;

public class QuoteProjection
{
    [Key]
    public Guid PublicQuoteId { get; set; }

    [Required, MaxLength(50)]
    public string QuoteNumber { get; set; } = string.Empty;

    public Guid WorkflowCorrelationId { get; set; }

    [Required, MaxLength(100)]
    public string PublicStatus { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,2)")]
    public decimal EstimatedPremium { get; set; }

    [MaxLength(250)]
    public string? StatusSummary { get; set; }

    public DateTime LastUpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid LastMessageId { get; set; }
}
