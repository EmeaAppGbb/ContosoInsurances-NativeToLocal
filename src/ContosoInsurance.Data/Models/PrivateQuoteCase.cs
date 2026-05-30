using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ContosoInsurance.Data.Enums;

namespace ContosoInsurance.Data.Models;

public class PrivateQuoteCase
{
    public Guid Id { get; set; }
    public Guid PublicQuoteId { get; set; }
    public Guid WorkflowCorrelationId { get; set; }

    [Required, MaxLength(50)]
    public string QuoteNumber { get; set; } = string.Empty;

    public QuoteStatus Status { get; set; } = QuoteStatus.Requested;
    public PolicyType Type { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal EstimatedPremium { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal CoverageAmount { get; set; }

    public Guid CustomerId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }

    [MaxLength(100)]
    public string? AssignedToUserId { get; set; }

    [MaxLength(200)]
    public string? AssignedToDisplayName { get; set; }

    [MaxLength(500)]
    public string? UnderwritingSummary { get; set; }

    public ICollection<QuoteCaseNote> Notes { get; set; } = new List<QuoteCaseNote>();
    public ICollection<QuoteCaseAuditEntry> AuditTrail { get; set; } = new List<QuoteCaseAuditEntry>();
}
