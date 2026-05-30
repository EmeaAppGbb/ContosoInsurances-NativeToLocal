using System.ComponentModel.DataAnnotations;

namespace ContosoInsurance.Data.Models;

public class ClaimCaseNote
{
    public Guid Id { get; set; }
    public Guid PrivateClaimCaseId { get; set; }
    public PrivateClaimCase ClaimCase { get; set; } = null!;

    [Required, MaxLength(2000)]
    public string Note { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string CreatedBy { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
