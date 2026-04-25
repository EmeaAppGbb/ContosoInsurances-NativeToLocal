using ContosoInsurance.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace ContosoInsurance.Data;

public class InsuranceDbContext(DbContextOptions<InsuranceDbContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Policy> Policies => Set<Policy>();
    public DbSet<Claim> Claims => Set<Claim>();
    public DbSet<Quote> Quotes => Set<Quote>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Customer>(entity =>
        {
            entity.HasIndex(e => e.Email).IsUnique();
            entity.HasMany(e => e.Policies).WithOne(p => p.Customer).HasForeignKey(p => p.CustomerId);
            entity.HasMany(e => e.Quotes).WithOne(q => q.Customer).HasForeignKey(q => q.CustomerId);
        });

        modelBuilder.Entity<Policy>(entity =>
        {
            entity.HasIndex(e => e.PolicyNumber).IsUnique();
            entity.HasMany(e => e.Claims).WithOne(c => c.Policy).HasForeignKey(c => c.PolicyId);
        });

        modelBuilder.Entity<Claim>(entity =>
        {
            entity.HasIndex(e => e.ClaimNumber).IsUnique();
        });

        modelBuilder.Entity<Quote>(entity =>
        {
            entity.HasIndex(e => e.QuoteNumber).IsUnique();
        });
    }
}
