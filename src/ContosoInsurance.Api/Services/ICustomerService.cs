using ContosoInsurance.Api.DTOs;

namespace ContosoInsurance.Api.Services;

public interface ICustomerService
{
    Task<PaginatedResponse<CustomerResponse>> GetCustomersAsync(string? search, int page, int pageSize);
    Task<CustomerResponse?> GetCustomerByIdAsync(Guid id);
    Task<CustomerResponse> CreateCustomerAsync(CreateCustomerRequest request);
    Task<CustomerResponse?> UpdateCustomerAsync(Guid id, UpdateCustomerRequest request);
    Task<bool> DeleteCustomerAsync(Guid id);
}
