using ContosoInsurance.Api.DTOs;
using ContosoInsurance.Data.Enums;

namespace ContosoInsurance.Api.Services;

public interface IPolicyService
{
    Task<PaginatedResponse<PolicyResponse>> GetPoliciesAsync(PolicyType? type, PolicyStatus? status, Guid? customerId, int page, int pageSize);
    Task<PolicyResponse?> GetPolicyByIdAsync(Guid id);
    Task<PolicyResponse> CreatePolicyAsync(CreatePolicyRequest request);
    Task<PolicyResponse?> UpdatePolicyAsync(Guid id, UpdatePolicyRequest request);
    Task<PolicyResponse?> ActivatePolicyAsync(Guid id);
    Task<PolicyResponse?> CancelPolicyAsync(Guid id);
    Task<PolicyResponse?> RenewPolicyAsync(Guid id, int months = 12);
}
