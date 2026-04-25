using System.ComponentModel.DataAnnotations;

namespace ContosoInsurance.Data.Models;

public class Customer
{
    public Guid Id { get; set; }

    [Required, MaxLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string LastName { get; set; } = string.Empty;

    [Required, MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [MaxLength(20)]
    public string? Phone { get; set; }

    [MaxLength(500)]
    public string? Address { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public ICollection<Policy> Policies { get; set; } = [];
    public ICollection<Quote> Quotes { get; set; } = [];
}
