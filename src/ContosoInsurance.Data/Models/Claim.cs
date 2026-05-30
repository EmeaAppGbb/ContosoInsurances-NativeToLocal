using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ContosoInsurance.Data.Enums;

namespace ContosoInsurance.Data.Models;

public class Claim
{
    public Guid Id { get; set; }

    [Required, MaxLength(50)]
    public string ClaimNumber { get; set; } = string.Empty;

    public ClaimStatus Status { get; set; } = ClaimStatus.Submitted;

    [Required, MaxLength(1000)]
    public string Description { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }

    public DateTime IncidentDate { get; set; }
    public DateTime FiledDate { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedDate { get; set; }
    public Guid WorkflowCorrelationId { get; set; } = Guid.NewGuid();

    // Foreign keys
    public Guid PolicyId { get; set; }
    public Policy Policy { get; set; } = null!;
}
