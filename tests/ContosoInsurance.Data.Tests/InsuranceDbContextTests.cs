using ContosoInsurance.Data;
using ContosoInsurance.Data.Enums;
using ContosoInsurance.Data.Models;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ContosoInsurance.Data.Tests;

public class InsuranceDbContextTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly InsuranceDbContext _db;

    public InsuranceDbContextTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<InsuranceDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new InsuranceDbContext(options);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private Customer CreateCustomer(string email = "test@example.com") => new()
    {
        Id = Guid.NewGuid(),
        FirstName = "John",
        LastName = "Doe",
        Email = email,
        Phone = "555-0100",
        Address = "123 Main St"
    };

    private Policy CreatePolicy(Guid customerId) => new()
    {
        Id = Guid.NewGuid(),
        PolicyNumber = $"POL-{Guid.NewGuid().ToString()[..8]}",
        Type = PolicyType.Auto,
        Status = PolicyStatus.Active,
        PremiumAmount = 500m,
        CoverageAmount = 50000m,
        StartDate = DateTime.UtcNow,
        EndDate = DateTime.UtcNow.AddYears(1),
        CustomerId = customerId
    };

    // --- DbSet registration ---

    [Fact]
    public void DbContext_HasCustomersDbSet()
    {
        _db.Customers.Should().NotBeNull();
    }

    [Fact]
    public void DbContext_HasPoliciesDbSet()
    {
        _db.Policies.Should().NotBeNull();
    }

    [Fact]
    public void DbContext_HasClaimsDbSet()
    {
        _db.Claims.Should().NotBeNull();
    }

    [Fact]
    public void DbContext_HasQuotesDbSet()
    {
        _db.Quotes.Should().NotBeNull();
    }

    // --- Relationships ---

    [Fact]
    public async Task Customer_HasMany_Policies()
    {
        var customer = CreateCustomer();
        _db.Customers.Add(customer);

        var policy = CreatePolicy(customer.Id);
        _db.Policies.Add(policy);
        await _db.SaveChangesAsync();

        var loaded = await _db.Customers
            .Include(c => c.Policies)
            .FirstAsync(c => c.Id == customer.Id);

        loaded.Policies.Should().HaveCount(1);
        loaded.Policies.First().PolicyNumber.Should().Be(policy.PolicyNumber);
    }

    [Fact]
    public async Task Customer_HasMany_Quotes()
    {
        var customer = CreateCustomer();
        _db.Customers.Add(customer);

        var quote = new Quote
        {
            Id = Guid.NewGuid(),
            QuoteNumber = "QTE-001",
            Type = PolicyType.Home,
            EstimatedPremium = 300m,
            CoverageAmount = 100000m,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(30),
            CustomerId = customer.Id
        };
        _db.Quotes.Add(quote);
        await _db.SaveChangesAsync();

        var loaded = await _db.Customers
            .Include(c => c.Quotes)
            .FirstAsync(c => c.Id == customer.Id);

        loaded.Quotes.Should().HaveCount(1);
    }

    [Fact]
    public async Task Policy_HasMany_Claims()
    {
        var customer = CreateCustomer();
        _db.Customers.Add(customer);

        var policy = CreatePolicy(customer.Id);
        _db.Policies.Add(policy);

        var claim = new Claim
        {
            Id = Guid.NewGuid(),
            ClaimNumber = "CLM-001",
            Description = "Fender bender",
            Amount = 2500m,
            IncidentDate = DateTime.UtcNow.AddDays(-1),
            PolicyId = policy.Id
        };
        _db.Claims.Add(claim);
        await _db.SaveChangesAsync();

        var loaded = await _db.Policies
            .Include(p => p.Claims)
            .FirstAsync(p => p.Id == policy.Id);

        loaded.Claims.Should().HaveCount(1);
        loaded.Claims.First().ClaimNumber.Should().Be("CLM-001");
    }

    // --- Unique index enforcement ---

    [Fact]
    public async Task Customer_Email_MustBeUnique()
    {
        _db.Customers.Add(CreateCustomer("dup@test.com"));
        await _db.SaveChangesAsync();

        _db.Customers.Add(CreateCustomer("dup@test.com"));

        var act = () => _db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Policy_PolicyNumber_MustBeUnique()
    {
        var customer = CreateCustomer();
        _db.Customers.Add(customer);

        var p1 = CreatePolicy(customer.Id);
        p1.PolicyNumber = "POL-UNIQUE";
        _db.Policies.Add(p1);
        await _db.SaveChangesAsync();

        var p2 = CreatePolicy(customer.Id);
        p2.PolicyNumber = "POL-UNIQUE";
        _db.Policies.Add(p2);

        var act = () => _db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Claim_ClaimNumber_MustBeUnique()
    {
        var customer = CreateCustomer();
        _db.Customers.Add(customer);
        var policy = CreatePolicy(customer.Id);
        _db.Policies.Add(policy);
        await _db.SaveChangesAsync();

        _db.Claims.Add(new Claim { Id = Guid.NewGuid(), ClaimNumber = "CLM-DUP", Description = "A", Amount = 100, IncidentDate = DateTime.UtcNow, PolicyId = policy.Id });
        await _db.SaveChangesAsync();

        _db.Claims.Add(new Claim { Id = Guid.NewGuid(), ClaimNumber = "CLM-DUP", Description = "B", Amount = 200, IncidentDate = DateTime.UtcNow, PolicyId = policy.Id });

        var act = () => _db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Quote_QuoteNumber_MustBeUnique()
    {
        var customer = CreateCustomer();
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();

        _db.Quotes.Add(new Quote { Id = Guid.NewGuid(), QuoteNumber = "QTE-DUP", Type = PolicyType.Auto, EstimatedPremium = 100, CoverageAmount = 10000, CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(30), CustomerId = customer.Id });
        await _db.SaveChangesAsync();

        _db.Quotes.Add(new Quote { Id = Guid.NewGuid(), QuoteNumber = "QTE-DUP", Type = PolicyType.Auto, EstimatedPremium = 200, CoverageAmount = 20000, CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(30), CustomerId = customer.Id });

        var act = () => _db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    // --- CRUD operations ---

    [Fact]
    public async Task CanAddAndRetrieveCustomer()
    {
        var customer = CreateCustomer("crud@test.com");
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();

        var loaded = await _db.Customers.FindAsync(customer.Id);
        loaded.Should().NotBeNull();
        loaded!.Email.Should().Be("crud@test.com");
    }

    [Fact]
    public async Task CanAddAndRetrievePolicy()
    {
        var customer = CreateCustomer("policy@test.com");
        _db.Customers.Add(customer);
        var policy = CreatePolicy(customer.Id);
        _db.Policies.Add(policy);
        await _db.SaveChangesAsync();

        var loaded = await _db.Policies
            .Include(p => p.Customer)
            .FirstAsync(p => p.Id == policy.Id);

        loaded.Customer.Email.Should().Be("policy@test.com");
    }
}
