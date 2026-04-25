using System.ComponentModel.DataAnnotations;

namespace ContosoInsurance.Api.DTOs;

public record CreateCustomerRequest(
    [Required, MaxLength(100)] string FirstName,
    [Required, MaxLength(100)] string LastName,
    [Required, MaxLength(256), EmailAddress] string Email,
    [MaxLength(20)] string? Phone,
    [MaxLength(500)] string? Address);

public record UpdateCustomerRequest(
    [Required, MaxLength(100)] string FirstName,
    [Required, MaxLength(100)] string LastName,
    [Required, MaxLength(256), EmailAddress] string Email,
    [MaxLength(20)] string? Phone,
    [MaxLength(500)] string? Address);

public record CustomerResponse(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    string? Address,
    DateTime CreatedAt,
    int PolicyCount,
    int QuoteCount);
