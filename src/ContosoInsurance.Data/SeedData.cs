using ContosoInsurance.Data.Enums;
using ContosoInsurance.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace ContosoInsurance.Data;

public static class SeedData
{
    public static async Task SeedAsync(InsuranceDbContext db)
    {
        if (await db.Customers.AnyAsync())
            return;

        // Customers
        var customers = new List<Customer>
        {
            new() { Id = Guid.NewGuid(), FirstName = "Maria", LastName = "Santos", Email = "maria.santos@example.com", Phone = "555-0101", Address = "123 Oak Street, Seattle, WA 98101", CreatedAt = DateTime.UtcNow.AddMonths(-8) },
            new() { Id = Guid.NewGuid(), FirstName = "James", LastName = "Chen", Email = "james.chen@example.com", Phone = "555-0102", Address = "456 Pine Avenue, Portland, OR 97201", CreatedAt = DateTime.UtcNow.AddMonths(-6) },
            new() { Id = Guid.NewGuid(), FirstName = "Fatima", LastName = "Al-Rashid", Email = "fatima.alrashid@example.com", Phone = "555-0103", Address = "789 Elm Drive, San Francisco, CA 94102", CreatedAt = DateTime.UtcNow.AddMonths(-5) },
            new() { Id = Guid.NewGuid(), FirstName = "Robert", LastName = "Johnson", Email = "robert.johnson@example.com", Phone = "555-0104", Address = "321 Maple Lane, Denver, CO 80202", CreatedAt = DateTime.UtcNow.AddMonths(-4) },
            new() { Id = Guid.NewGuid(), FirstName = "Yuki", LastName = "Tanaka", Email = "yuki.tanaka@example.com", Phone = "555-0105", Address = "654 Cedar Road, Austin, TX 73301", CreatedAt = DateTime.UtcNow.AddMonths(-3) },
            new() { Id = Guid.NewGuid(), FirstName = "Elena", LastName = "Volkov", Email = "elena.volkov@example.com", Phone = "555-0106", Address = "987 Birch Way, Chicago, IL 60601", CreatedAt = DateTime.UtcNow.AddMonths(-2) },
            new() { Id = Guid.NewGuid(), FirstName = "David", LastName = "Okafor", Email = "david.okafor@example.com", Phone = "555-0107", Address = "147 Walnut Blvd, Miami, FL 33101", CreatedAt = DateTime.UtcNow.AddMonths(-1) },
        };

        db.Customers.AddRange(customers);
        await db.SaveChangesAsync();

        // Policies
        var policies = new List<Policy>
        {
            new() { Id = Guid.NewGuid(), PolicyNumber = "POL-20250101-AUTO0001", Type = PolicyType.Auto, Status = PolicyStatus.Active, PremiumAmount = 145.83m, CoverageAmount = 50000m, StartDate = DateTime.UtcNow.AddMonths(-6), EndDate = DateTime.UtcNow.AddMonths(6), CreatedAt = DateTime.UtcNow.AddMonths(-6), CustomerId = customers[0].Id },
            new() { Id = Guid.NewGuid(), PolicyNumber = "POL-20250102-HOME0001", Type = PolicyType.Home, Status = PolicyStatus.Active, PremiumAmount = 208.33m, CoverageAmount = 100000m, StartDate = DateTime.UtcNow.AddMonths(-5), EndDate = DateTime.UtcNow.AddMonths(7), CreatedAt = DateTime.UtcNow.AddMonths(-5), CustomerId = customers[0].Id },
            new() { Id = Guid.NewGuid(), PolicyNumber = "POL-20250103-LIFE0001", Type = PolicyType.Life, Status = PolicyStatus.Active, PremiumAmount = 62.50m, CoverageAmount = 50000m, StartDate = DateTime.UtcNow.AddMonths(-4), EndDate = DateTime.UtcNow.AddMonths(8), CreatedAt = DateTime.UtcNow.AddMonths(-4), CustomerId = customers[1].Id },
            new() { Id = Guid.NewGuid(), PolicyNumber = "POL-20250104-HLTH0001", Type = PolicyType.Health, Status = PolicyStatus.Active, PremiumAmount = 375.00m, CoverageAmount = 100000m, StartDate = DateTime.UtcNow.AddMonths(-3), EndDate = DateTime.UtcNow.AddMonths(9), CreatedAt = DateTime.UtcNow.AddMonths(-3), CustomerId = customers[2].Id },
            new() { Id = Guid.NewGuid(), PolicyNumber = "POL-20250105-AUTO0002", Type = PolicyType.Auto, Status = PolicyStatus.Draft, PremiumAmount = 87.50m, CoverageAmount = 30000m, StartDate = DateTime.UtcNow.AddDays(-10), EndDate = DateTime.UtcNow.AddMonths(12), CreatedAt = DateTime.UtcNow.AddDays(-10), CustomerId = customers[3].Id },
            new() { Id = Guid.NewGuid(), PolicyNumber = "POL-20250106-TRVL0001", Type = PolicyType.Travel, Status = PolicyStatus.Active, PremiumAmount = 16.67m, CoverageAmount = 10000m, StartDate = DateTime.UtcNow.AddMonths(-1), EndDate = DateTime.UtcNow.AddMonths(2), CreatedAt = DateTime.UtcNow.AddMonths(-1), CustomerId = customers[4].Id },
            new() { Id = Guid.NewGuid(), PolicyNumber = "POL-20250107-BUSI0001", Type = PolicyType.Business, Status = PolicyStatus.Active, PremiumAmount = 833.33m, CoverageAmount = 250000m, StartDate = DateTime.UtcNow.AddMonths(-2), EndDate = DateTime.UtcNow.AddMonths(10), CreatedAt = DateTime.UtcNow.AddMonths(-2), CustomerId = customers[5].Id },
            new() { Id = Guid.NewGuid(), PolicyNumber = "POL-20250108-HOME0002", Type = PolicyType.Home, Status = PolicyStatus.Expired, PremiumAmount = 104.17m, CoverageAmount = 50000m, StartDate = DateTime.UtcNow.AddMonths(-14), EndDate = DateTime.UtcNow.AddMonths(-2), CreatedAt = DateTime.UtcNow.AddMonths(-14), CustomerId = customers[6].Id },
            new() { Id = Guid.NewGuid(), PolicyNumber = "POL-20250109-HLTH0002", Type = PolicyType.Health, Status = PolicyStatus.Cancelled, PremiumAmount = 187.50m, CoverageAmount = 50000m, StartDate = DateTime.UtcNow.AddMonths(-6), EndDate = DateTime.UtcNow.AddMonths(6), CreatedAt = DateTime.UtcNow.AddMonths(-6), CustomerId = customers[1].Id },
        };

        db.Policies.AddRange(policies);
        await db.SaveChangesAsync();

        // Claims
        var claims = new List<Claim>
        {
            new() { Id = Guid.NewGuid(), ClaimNumber = "CLM-20250201-FENDER01", Status = ClaimStatus.Approved, Description = "Fender bender in parking lot. Minor damage to front bumper.", Amount = 3500m, IncidentDate = DateTime.UtcNow.AddDays(-30), FiledDate = DateTime.UtcNow.AddDays(-28), ResolvedDate = DateTime.UtcNow.AddDays(-14), PolicyId = policies[0].Id },
            new() { Id = Guid.NewGuid(), ClaimNumber = "CLM-20250202-WATER001", Status = ClaimStatus.UnderReview, Description = "Water damage from burst pipe in basement.", Amount = 15000m, IncidentDate = DateTime.UtcNow.AddDays(-14), FiledDate = DateTime.UtcNow.AddDays(-12), PolicyId = policies[1].Id },
            new() { Id = Guid.NewGuid(), ClaimNumber = "CLM-20250203-MEDIC001", Status = ClaimStatus.Submitted, Description = "Emergency room visit for broken arm.", Amount = 8500m, IncidentDate = DateTime.UtcNow.AddDays(-5), FiledDate = DateTime.UtcNow.AddDays(-3), PolicyId = policies[3].Id },
            new() { Id = Guid.NewGuid(), ClaimNumber = "CLM-20250204-THEFT001", Status = ClaimStatus.Denied, Description = "Laptop stolen from unlocked car.", Amount = 2000m, IncidentDate = DateTime.UtcNow.AddDays(-45), FiledDate = DateTime.UtcNow.AddDays(-40), ResolvedDate = DateTime.UtcNow.AddDays(-20), PolicyId = policies[0].Id },
            new() { Id = Guid.NewGuid(), ClaimNumber = "CLM-20250205-EQUIP001", Status = ClaimStatus.Paid, Description = "Server room equipment failure due to power surge.", Amount = 45000m, IncidentDate = DateTime.UtcNow.AddDays(-60), FiledDate = DateTime.UtcNow.AddDays(-58), ResolvedDate = DateTime.UtcNow.AddDays(-30), PolicyId = policies[6].Id },
        };

        db.Claims.AddRange(claims);
        await db.SaveChangesAsync();

        // Quotes
        var quotes = new List<Quote>
        {
            new() { Id = Guid.NewGuid(), QuoteNumber = "QTE-20250301-AUTO0001", Type = PolicyType.Auto, Status = QuoteStatus.Underwriting, EstimatedPremium = 116.67m, CoverageAmount = 40000m, IsAccepted = false, CreatedAt = DateTime.UtcNow.AddDays(-5), ExpiresAt = DateTime.UtcNow.AddDays(25), CustomerId = customers[3].Id },
            new() { Id = Guid.NewGuid(), QuoteNumber = "QTE-20250302-HOME0001", Type = PolicyType.Home, Status = QuoteStatus.Approved, EstimatedPremium = 312.50m, CoverageAmount = 150000m, IsAccepted = true, CreatedAt = DateTime.UtcNow.AddDays(-20), ExpiresAt = DateTime.UtcNow.AddDays(10), CustomerId = customers[4].Id },
            new() { Id = Guid.NewGuid(), QuoteNumber = "QTE-20250303-LIFE0001", Type = PolicyType.Life, Status = QuoteStatus.Requested, EstimatedPremium = 125.00m, CoverageAmount = 100000m, IsAccepted = false, CreatedAt = DateTime.UtcNow.AddDays(-2), ExpiresAt = DateTime.UtcNow.AddDays(28), CustomerId = customers[6].Id },
        };

        db.Quotes.AddRange(quotes);
        await db.SaveChangesAsync();

        var claimProjections = claims.Select(claim => new ClaimProjection
        {
            PublicClaimId = claim.Id,
            ClaimNumber = claim.ClaimNumber,
            WorkflowCorrelationId = claim.WorkflowCorrelationId,
            PublicStatus = claim.Status.ToString(),
            StatusSummary = $"Claim is {claim.Status}.",
            LastUpdatedAtUtc = claim.ResolvedDate ?? claim.FiledDate,
            LastMessageId = Guid.NewGuid()
        });

        var quoteProjections = quotes.Select(quote => new QuoteProjection
        {
            PublicQuoteId = quote.Id,
            QuoteNumber = quote.QuoteNumber,
            WorkflowCorrelationId = quote.WorkflowCorrelationId,
            PublicStatus = quote.Status.ToString(),
            EstimatedPremium = quote.EstimatedPremium,
            StatusSummary = $"Quote is {quote.Status}.",
            LastUpdatedAtUtc = quote.CreatedAt,
            LastMessageId = Guid.NewGuid()
        });

        db.ClaimProjections.AddRange(claimProjections);
        db.QuoteProjections.AddRange(quoteProjections);
        await db.SaveChangesAsync();
    }
}
