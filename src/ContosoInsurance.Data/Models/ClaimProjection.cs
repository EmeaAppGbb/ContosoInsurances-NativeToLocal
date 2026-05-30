using System.ComponentModel.DataAnnotations;

namespace ContosoInsurance.Data.Models;

public class ClaimProjection
{
    [Key]
    public Guid PublicClaimId { get; set; }

    [Required, MaxLength(50)]
    public string ClaimNumber { get; set; } = string.Empty;

    public Guid WorkflowCorrelationId { get; set; }

    [Required, MaxLength(100)]
    public string PublicStatus { get; set; } = string.Empty;

    [MaxLength(250)]
    public string? StatusSummary { get; set; }

    public DateTime LastUpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid LastMessageId { get; set; }
}
