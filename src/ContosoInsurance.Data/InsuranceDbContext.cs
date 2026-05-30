using ContosoInsurance.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace ContosoInsurance.Data;

public class InsuranceDbContext(DbContextOptions<InsuranceDbContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Policy> Policies => Set<Policy>();
    public DbSet<Claim> Claims => Set<Claim>();
    public DbSet<Quote> Quotes => Set<Quote>();
    public DbSet<PrivateClaimCase> PrivateClaimCases => Set<PrivateClaimCase>();
    public DbSet<ClaimCaseNote> ClaimCaseNotes => Set<ClaimCaseNote>();
    public DbSet<ClaimCaseAuditEntry> ClaimCaseAuditEntries => Set<ClaimCaseAuditEntry>();
    public DbSet<PrivateQuoteCase> PrivateQuoteCases => Set<PrivateQuoteCase>();
    public DbSet<QuoteCaseNote> QuoteCaseNotes => Set<QuoteCaseNote>();
    public DbSet<QuoteCaseAuditEntry> QuoteCaseAuditEntries => Set<QuoteCaseAuditEntry>();
    public DbSet<ClaimProjection> ClaimProjections => Set<ClaimProjection>();
    public DbSet<QuoteProjection> QuoteProjections => Set<QuoteProjection>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();

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
            entity.HasIndex(e => e.WorkflowCorrelationId).IsUnique();
        });

        modelBuilder.Entity<Quote>(entity =>
        {
            entity.HasIndex(e => e.QuoteNumber).IsUnique();
            entity.HasIndex(e => e.WorkflowCorrelationId).IsUnique();
        });

        modelBuilder.Entity<PrivateClaimCase>(entity =>
        {
            entity.HasIndex(e => e.PublicClaimId).IsUnique();
            entity.HasIndex(e => e.WorkflowCorrelationId).IsUnique();
            entity.HasIndex(e => e.ClaimNumber).IsUnique();
            entity.HasMany(e => e.Notes).WithOne(n => n.ClaimCase).HasForeignKey(n => n.PrivateClaimCaseId);
            entity.HasMany(e => e.AuditTrail).WithOne(a => a.ClaimCase).HasForeignKey(a => a.PrivateClaimCaseId);
        });

        modelBuilder.Entity<PrivateQuoteCase>(entity =>
        {
            entity.HasIndex(e => e.PublicQuoteId).IsUnique();
            entity.HasIndex(e => e.WorkflowCorrelationId).IsUnique();
            entity.HasIndex(e => e.QuoteNumber).IsUnique();
            entity.HasMany(e => e.Notes).WithOne(n => n.QuoteCase).HasForeignKey(n => n.PrivateQuoteCaseId);
            entity.HasMany(e => e.AuditTrail).WithOne(a => a.QuoteCase).HasForeignKey(a => a.PrivateQuoteCaseId);
        });

        modelBuilder.Entity<ClaimProjection>(entity =>
        {
            entity.HasIndex(e => e.WorkflowCorrelationId);
        });

        modelBuilder.Entity<QuoteProjection>(entity =>
        {
            entity.HasIndex(e => e.WorkflowCorrelationId);
        });

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.HasIndex(e => e.MessageId).IsUnique();
            entity.HasIndex(e => new { e.PublishedAtUtc, e.OccurredAtUtc });
        });

        modelBuilder.Entity<ProcessedMessage>(entity =>
        {
            entity.HasKey(e => new { e.MessageId, e.ConsumerName });
        });
    }
}
