using System.ComponentModel.DataAnnotations;
using ContosoInsurance.Data.Enums;

namespace ContosoInsurance.Api.DTOs;

public record CreateQuoteRequest(
    [Required] Guid CustomerId,
    PolicyType Type,
    [Range(0.01, double.MaxValue)] decimal CoverageAmount);

public record QuoteResponse(
    Guid Id,
    string QuoteNumber,
    PolicyType Type,
    decimal EstimatedPremium,
    decimal CoverageAmount,
    bool IsAccepted,
    DateTime CreatedAt,
    DateTime ExpiresAt,
    Guid CustomerId,
    string CustomerName,
    Guid WorkflowCorrelationId,
    string? PublicStatus = null,
    DateTime? ProjectionUpdatedAtUtc = null);
