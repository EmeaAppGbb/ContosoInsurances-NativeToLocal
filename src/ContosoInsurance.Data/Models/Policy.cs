using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ContosoInsurance.Data.Enums;

namespace ContosoInsurance.Data.Models;

public class Policy
{
    public Guid Id { get; set; }

    [Required, MaxLength(50)]
    public string PolicyNumber { get; set; } = string.Empty;

    public PolicyType Type { get; set; }
    public PolicyStatus Status { get; set; } = PolicyStatus.Draft;

    [Column(TypeName = "decimal(18,2)")]
    public decimal PremiumAmount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal CoverageAmount { get; set; }

    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Foreign keys
    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    // Navigation properties
    public ICollection<Claim> Claims { get; set; } = [];
}
