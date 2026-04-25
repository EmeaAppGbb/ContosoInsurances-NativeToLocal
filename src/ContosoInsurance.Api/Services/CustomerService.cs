using ContosoInsurance.Api.DTOs;
using ContosoInsurance.Data;
using ContosoInsurance.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace ContosoInsurance.Api.Services;

public class CustomerService(InsuranceDbContext db) : ICustomerService
{
    public async Task<PaginatedResponse<CustomerResponse>> GetCustomersAsync(string? search, int page, int pageSize)
    {
        var query = db.Customers.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.ToLower();
            query = query.Where(c =>
                c.FirstName.ToLower().Contains(term) ||
                c.LastName.ToLower().Contains(term) ||
                c.Email.ToLower().Contains(term));
        }

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderBy(c => c.LastName).ThenBy(c => c.FirstName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new CustomerResponse(
                c.Id, c.FirstName, c.LastName, c.Email, c.Phone, c.Address,
                c.CreatedAt, c.Policies.Count, c.Quotes.Count))
            .ToListAsync();

        return new PaginatedResponse<CustomerResponse>(items, totalCount, page, pageSize);
    }

    public async Task<CustomerResponse?> GetCustomerByIdAsync(Guid id)
    {
        return await db.Customers.AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new CustomerResponse(
                c.Id, c.FirstName, c.LastName, c.Email, c.Phone, c.Address,
                c.CreatedAt, c.Policies.Count, c.Quotes.Count))
            .FirstOrDefaultAsync();
    }

    public async Task<CustomerResponse> CreateCustomerAsync(CreateCustomerRequest request)
    {
        var existingEmail = await db.Customers.AnyAsync(c => c.Email == request.Email);
        if (existingEmail)
            throw new InvalidOperationException($"A customer with email '{request.Email}' already exists.");

        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            FirstName = request.FirstName,
            LastName = request.LastName,
            Email = request.Email,
            Phone = request.Phone,
            Address = request.Address,
            CreatedAt = DateTime.UtcNow
        };

        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        return new CustomerResponse(
            customer.Id, customer.FirstName, customer.LastName, customer.Email,
            customer.Phone, customer.Address, customer.CreatedAt, 0, 0);
    }

    public async Task<CustomerResponse?> UpdateCustomerAsync(Guid id, UpdateCustomerRequest request)
    {
        var customer = await db.Customers.FindAsync(id);
        if (customer is null) return null;

        // Check email uniqueness if changed
        if (!string.Equals(customer.Email, request.Email, StringComparison.OrdinalIgnoreCase))
        {
            var emailExists = await db.Customers.AnyAsync(c => c.Email == request.Email && c.Id != id);
            if (emailExists)
                throw new InvalidOperationException($"A customer with email '{request.Email}' already exists.");
        }

        customer.FirstName = request.FirstName;
        customer.LastName = request.LastName;
        customer.Email = request.Email;
        customer.Phone = request.Phone;
        customer.Address = request.Address;

        await db.SaveChangesAsync();

        var policyCount = await db.Policies.CountAsync(p => p.CustomerId == id);
        var quoteCount = await db.Quotes.CountAsync(q => q.CustomerId == id);

        return new CustomerResponse(
            customer.Id, customer.FirstName, customer.LastName, customer.Email,
            customer.Phone, customer.Address, customer.CreatedAt, policyCount, quoteCount);
    }

    public async Task<bool> DeleteCustomerAsync(Guid id)
    {
        var customer = await db.Customers
            .Include(c => c.Policies)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (customer is null) return false;

        if (customer.Policies.Any(p => p.Status == Data.Enums.PolicyStatus.Active))
            throw new InvalidOperationException("Cannot delete a customer with active policies.");

        db.Customers.Remove(customer);
        await db.SaveChangesAsync();
        return true;
    }
}
