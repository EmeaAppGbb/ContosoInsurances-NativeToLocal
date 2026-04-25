namespace ContosoInsurance.Api.Events;

/// <summary>
/// Event published when a new claim is submitted.
/// </summary>
public record ClaimSubmittedEvent(
    Guid ClaimId,
    string ClaimNumber,
    Guid PolicyId,
    decimal Amount,
    string Description,
    DateTime SubmittedAt);
