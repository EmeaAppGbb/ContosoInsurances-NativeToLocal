using ContosoInsurance.Api.DTOs;
using ContosoInsurance.Data;
using ContosoInsurance.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace ContosoInsurance.Api.Services;

public class QuoteService(InsuranceDbContext db) : IQuoteService
{
    public async Task<PaginatedResponse<QuoteResponse>> GetQuotesAsync(Guid? customerId, int page, int pageSize)
    {
        var query = db.Quotes.AsNoTracking().AsQueryable();

        if (customerId.HasValue) query = query.Where(q => q.CustomerId == customerId.Value);

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderByDescending(q => q.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(q => new QuoteResponse(
                q.Id, q.QuoteNumber, q.Type,
                q.EstimatedPremium, q.CoverageAmount,
                q.IsAccepted, q.CreatedAt, q.ExpiresAt,
                q.CustomerId, q.Customer.FirstName + " " + q.Customer.LastName))
            .ToListAsync();

        return new PaginatedResponse<QuoteResponse>(items, totalCount, page, pageSize);
    }

    public async Task<QuoteResponse?> GetQuoteByIdAsync(Guid id)
    {
        return await db.Quotes.AsNoTracking()
            .Where(q => q.Id == id)
            .Select(q => new QuoteResponse(
                q.Id, q.QuoteNumber, q.Type,
                q.EstimatedPremium, q.CoverageAmount,
                q.IsAccepted, q.CreatedAt, q.ExpiresAt,
                q.CustomerId, q.Customer.FirstName + " " + q.Customer.LastName))
            .FirstOrDefaultAsync();
    }

    public async Task<QuoteResponse> CreateQuoteAsync(CreateQuoteRequest request)
    {
        var customerExists = await db.Customers.AnyAsync(c => c.Id == request.CustomerId);
        if (!customerExists)
            throw new KeyNotFoundException($"Customer '{request.CustomerId}' not found.");

        var premium = PolicyService.CalculatePremium(request.Type, request.CoverageAmount);

        var quote = new Quote
        {
            Id = Guid.NewGuid(),
            QuoteNumber = $"QTE-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString()[..8].ToUpperInvariant()}",
            Type = request.Type,
            EstimatedPremium = premium,
            CoverageAmount = request.CoverageAmount,
            IsAccepted = false,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(30),
            CustomerId = request.CustomerId
        };

        db.Quotes.Add(quote);
        await db.SaveChangesAsync();

        var customerName = await db.Customers
            .Where(c => c.Id == request.CustomerId)
            .Select(c => c.FirstName + " " + c.LastName)
            .FirstAsync();

        return new QuoteResponse(
            quote.Id, quote.QuoteNumber, quote.Type,
            quote.EstimatedPremium, quote.CoverageAmount,
            quote.IsAccepted, quote.CreatedAt, quote.ExpiresAt,
            quote.CustomerId, customerName);
    }

    public async Task<QuoteResponse?> AcceptQuoteAsync(Guid id)
    {
        var quote = await db.Quotes.FindAsync(id);
        if (quote is null) return null;

        if (quote.IsAccepted)
            throw new InvalidOperationException("Quote has already been accepted.");

        if (quote.ExpiresAt < DateTime.UtcNow)
            throw new InvalidOperationException("Quote has expired and cannot be accepted.");

        quote.IsAccepted = true;
        await db.SaveChangesAsync();

        return await GetQuoteByIdAsync(id);
    }
}
