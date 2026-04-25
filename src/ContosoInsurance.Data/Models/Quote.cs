using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ContosoInsurance.Data.Enums;

namespace ContosoInsurance.Data.Models;

public class Quote
{
    public Guid Id { get; set; }

    [Required, MaxLength(50)]
    public string QuoteNumber { get; set; } = string.Empty;

    public PolicyType Type { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal EstimatedPremium { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal CoverageAmount { get; set; }

    public bool IsAccepted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }

    // Foreign keys
    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
}
