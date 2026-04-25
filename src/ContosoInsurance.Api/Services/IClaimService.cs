using ContosoInsurance.Api.DTOs;
using ContosoInsurance.Data.Enums;

namespace ContosoInsurance.Api.Services;

public interface IClaimService
{
    Task<PaginatedResponse<ClaimResponse>> GetClaimsAsync(ClaimStatus? status, Guid? policyId, int page, int pageSize);
    Task<ClaimResponse?> GetClaimByIdAsync(Guid id);
    Task<ClaimResponse> SubmitClaimAsync(SubmitClaimRequest request);
    Task<ClaimResponse?> UpdateClaimStatusAsync(Guid id, UpdateClaimRequest request);
}
