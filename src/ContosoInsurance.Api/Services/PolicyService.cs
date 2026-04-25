using ContosoInsurance.Api.DTOs;
using ContosoInsurance.Data;
using ContosoInsurance.Data.Enums;
using ContosoInsurance.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace ContosoInsurance.Api.Services;

public class PolicyService(InsuranceDbContext db) : IPolicyService
{
    public async Task<PaginatedResponse<PolicyResponse>> GetPoliciesAsync(
        PolicyType? type, PolicyStatus? status, Guid? customerId, int page, int pageSize)
    {
        var query = db.Policies.AsNoTracking().AsQueryable();

        if (type.HasValue) query = query.Where(p => p.Type == type.Value);
        if (status.HasValue) query = query.Where(p => p.Status == status.Value);
        if (customerId.HasValue) query = query.Where(p => p.CustomerId == customerId.Value);

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new PolicyResponse(
                p.Id, p.PolicyNumber, p.Type, p.Status,
                p.PremiumAmount, p.CoverageAmount,
                p.StartDate, p.EndDate, p.CreatedAt,
                p.CustomerId, p.Customer.FirstName + " " + p.Customer.LastName,
                p.Claims.Count))
            .ToListAsync();

        return new PaginatedResponse<PolicyResponse>(items, totalCount, page, pageSize);
    }

    public async Task<PolicyResponse?> GetPolicyByIdAsync(Guid id)
    {
        return await db.Policies.AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new PolicyResponse(
                p.Id, p.PolicyNumber, p.Type, p.Status,
                p.PremiumAmount, p.CoverageAmount,
                p.StartDate, p.EndDate, p.CreatedAt,
                p.CustomerId, p.Customer.FirstName + " " + p.Customer.LastName,
                p.Claims.Count))
            .FirstOrDefaultAsync();
    }

    public async Task<PolicyResponse> CreatePolicyAsync(CreatePolicyRequest request)
    {
        var customerExists = await db.Customers.AnyAsync(c => c.Id == request.CustomerId);
        if (!customerExists)
            throw new KeyNotFoundException($"Customer '{request.CustomerId}' not found.");

        if (request.EndDate <= request.StartDate)
            throw new ArgumentException("End date must be after start date.");

        var premium = CalculatePremium(request.Type, request.CoverageAmount);

        var policy = new Policy
        {
            Id = Guid.NewGuid(),
            PolicyNumber = $"POL-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString()[..8].ToUpperInvariant()}",
            Type = request.Type,
            Status = PolicyStatus.Draft,
            PremiumAmount = premium,
            CoverageAmount = request.CoverageAmount,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            CreatedAt = DateTime.UtcNow,
            CustomerId = request.CustomerId
        };

        db.Policies.Add(policy);
        await db.SaveChangesAsync();

        var customerName = await db.Customers
            .Where(c => c.Id == request.CustomerId)
            .Select(c => c.FirstName + " " + c.LastName)
            .FirstAsync();

        return new PolicyResponse(
            policy.Id, policy.PolicyNumber, policy.Type, policy.Status,
            policy.PremiumAmount, policy.CoverageAmount,
            policy.StartDate, policy.EndDate, policy.CreatedAt,
            policy.CustomerId, customerName, 0);
    }

    public async Task<PolicyResponse?> UpdatePolicyAsync(Guid id, UpdatePolicyRequest request)
    {
        var policy = await db.Policies.FindAsync(id);
        if (policy is null) return null;

        if (request.CoverageAmount.HasValue)
        {
            policy.CoverageAmount = request.CoverageAmount.Value;
            policy.PremiumAmount = CalculatePremium(policy.Type, policy.CoverageAmount);
        }

        if (request.EndDate.HasValue)
        {
            if (request.EndDate.Value <= policy.StartDate)
                throw new ArgumentException("End date must be after start date.");
            policy.EndDate = request.EndDate.Value;
        }

        policy.Status = request.Status;
        await db.SaveChangesAsync();

        return await GetPolicyByIdAsync(id);
    }

    public async Task<PolicyResponse?> ActivatePolicyAsync(Guid id)
    {
        var policy = await db.Policies.FindAsync(id);
        if (policy is null) return null;

        if (policy.Status != PolicyStatus.Draft)
            throw new InvalidOperationException($"Only Draft policies can be activated. Current status: {policy.Status}");

        policy.Status = PolicyStatus.Active;
        await db.SaveChangesAsync();

        return await GetPolicyByIdAsync(id);
    }

    public async Task<PolicyResponse?> CancelPolicyAsync(Guid id)
    {
        var policy = await db.Policies.FindAsync(id);
        if (policy is null) return null;

        if (policy.Status is PolicyStatus.Cancelled or PolicyStatus.Expired)
            throw new InvalidOperationException($"Cannot cancel a policy with status: {policy.Status}");

        policy.Status = PolicyStatus.Cancelled;
        await db.SaveChangesAsync();

        return await GetPolicyByIdAsync(id);
    }

    public async Task<PolicyResponse?> RenewPolicyAsync(Guid id, int months = 12)
    {
        var policy = await db.Policies.FindAsync(id);
        if (policy is null) return null;

        if (policy.Status is not (PolicyStatus.Active or PolicyStatus.Expired))
            throw new InvalidOperationException($"Only Active or Expired policies can be renewed. Current status: {policy.Status}");

        policy.StartDate = DateTime.UtcNow;
        policy.EndDate = DateTime.UtcNow.AddMonths(months);
        policy.Status = PolicyStatus.Active;
        await db.SaveChangesAsync();

        return await GetPolicyByIdAsync(id);
    }

    internal static decimal CalculatePremium(PolicyType type, decimal coverageAmount)
    {
        var rateMultiplier = type switch
        {
            PolicyType.Auto => 0.035m,
            PolicyType.Home => 0.025m,
            PolicyType.Life => 0.015m,
            PolicyType.Health => 0.045m,
            PolicyType.Travel => 0.02m,
            PolicyType.Business => 0.04m,
            _ => 0.03m
        };

        return Math.Round(coverageAmount * rateMultiplier / 12, 2);
    }
}
