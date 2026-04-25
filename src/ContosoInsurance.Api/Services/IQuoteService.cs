using ContosoInsurance.Api.DTOs;

namespace ContosoInsurance.Api.Services;

public interface IQuoteService
{
    Task<PaginatedResponse<QuoteResponse>> GetQuotesAsync(Guid? customerId, int page, int pageSize);
    Task<QuoteResponse?> GetQuoteByIdAsync(Guid id);
    Task<QuoteResponse> CreateQuoteAsync(CreateQuoteRequest request);
    Task<QuoteResponse?> AcceptQuoteAsync(Guid id);
}
