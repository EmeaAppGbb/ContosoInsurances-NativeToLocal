using System.ComponentModel.DataAnnotations;
using ContosoInsurance.Data.Enums;

namespace ContosoInsurance.Api.DTOs;

public record SubmitClaimRequest(
    [Required] Guid PolicyId,
    [Required, MaxLength(1000)] string Description,
    [Range(0.01, double.MaxValue)] decimal Amount,
    DateTime IncidentDate);

public record UpdateClaimRequest(
    ClaimStatus Status,
    string? ResolutionNotes);

public record ClaimResponse(
    Guid Id,
    string ClaimNumber,
    ClaimStatus Status,
    string Description,
    decimal Amount,
    DateTime IncidentDate,
    DateTime FiledDate,
    DateTime? ResolvedDate,
    Guid PolicyId,
    string PolicyNumber);
