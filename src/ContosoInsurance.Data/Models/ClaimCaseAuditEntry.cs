using System.ComponentModel.DataAnnotations;
using ContosoInsurance.Data.Enums;

namespace ContosoInsurance.Data.Models;

public class ClaimCaseAuditEntry
{
    public Guid Id { get; set; }
    public Guid PrivateClaimCaseId { get; set; }
    public PrivateClaimCase ClaimCase { get; set; } = null!;

    [Required, MaxLength(200)]
    public string Action { get; set; } = string.Empty;

    public ClaimStatus? FromStatus { get; set; }
    public ClaimStatus? ToStatus { get; set; }

    [Required, MaxLength(200)]
    public string PerformedBy { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Details { get; set; }

    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
}
