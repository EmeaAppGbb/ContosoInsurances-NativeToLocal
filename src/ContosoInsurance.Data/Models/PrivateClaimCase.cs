using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ContosoInsurance.Data.Enums;

namespace ContosoInsurance.Data.Models;

public class PrivateClaimCase
{
    public Guid Id { get; set; }
    public Guid PublicClaimId { get; set; }
    public Guid WorkflowCorrelationId { get; set; }

    [Required, MaxLength(50)]
    public string ClaimNumber { get; set; } = string.Empty;

    public ClaimStatus Status { get; set; } = ClaimStatus.Submitted;

    [Required, MaxLength(1000)]
    public string Description { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }

    public DateTime IncidentDate { get; set; }
    public Guid PolicyId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAtUtc { get; set; }

    [MaxLength(100)]
    public string? AssignedToUserId { get; set; }

    [MaxLength(200)]
    public string? AssignedToDisplayName { get; set; }

    [MaxLength(500)]
    public string? ValidationSummary { get; set; }

    public ICollection<ClaimCaseNote> Notes { get; set; } = new List<ClaimCaseNote>();
    public ICollection<ClaimCaseAuditEntry> AuditTrail { get; set; } = new List<ClaimCaseAuditEntry>();
}
