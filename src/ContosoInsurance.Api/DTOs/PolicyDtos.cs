using System.ComponentModel.DataAnnotations;
using ContosoInsurance.Data.Enums;

namespace ContosoInsurance.Api.DTOs;

public record CreatePolicyRequest(
    [Required] Guid CustomerId,
    PolicyType Type,
    [Range(0.01, double.MaxValue)] decimal CoverageAmount,
    DateTime StartDate,
    DateTime EndDate);

public record UpdatePolicyRequest(
    PolicyStatus Status,
    [Range(0.01, double.MaxValue)] decimal? CoverageAmount,
    DateTime? EndDate);

public record PolicyResponse(
    Guid Id,
    string PolicyNumber,
    PolicyType Type,
    PolicyStatus Status,
    decimal PremiumAmount,
    decimal CoverageAmount,
    DateTime StartDate,
    DateTime EndDate,
    DateTime CreatedAt,
    Guid CustomerId,
    string CustomerName,
    int ClaimCount);
