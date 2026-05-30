using System.Net.Http.Json;
using ContosoInsurance.BackendPortal.Models;
using ContosoInsurance.Data.Enums;
using ContosoInsurance.Data.Models;

namespace ContosoInsurance.BackendPortal.Services;

public interface IPortalOperationsService
{
    Task<DashboardSnapshot> GetDashboardAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ClaimCase>> GetClaimsAsync(CancellationToken cancellationToken = default);
    Task<ClaimCase?> GetClaimAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetAdjustersAsync(CancellationToken cancellationToken = default);
    Task TransitionClaimAsync(Guid id, ClaimStatus nextStatus, string actor, CancellationToken cancellationToken = default);
    Task AssignClaimAsync(Guid id, string assignee, string actor, CancellationToken cancellationToken = default);
    Task AddClaimNoteAsync(Guid id, string note, string actor, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<QuoteCase>> GetQuotesAsync(CancellationToken cancellationToken = default);
    Task<QuoteCase?> GetQuoteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetUnderwritersAsync(CancellationToken cancellationToken = default);
    Task SetQuoteStatusAsync(Guid id, PortalQuoteStatus status, string actor, CancellationToken cancellationToken = default);
    Task OverridePremiumAsync(Guid id, decimal premium, string reason, string actor, CancellationToken cancellationToken = default);
    Task ConvertQuoteToPolicyAsync(Guid id, string actor, CancellationToken cancellationToken = default);
    Task AddQuoteNoteAsync(Guid id, string note, string actor, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<QueueSnapshot>> GetQueuesAsync(CancellationToken cancellationToken = default);
    Task RetryQueueMessageAsync(string queueName, string messageId, string actor, CancellationToken cancellationToken = default);
    Task ReprocessQueueMessageAsync(string queueName, string messageId, string actor, CancellationToken cancellationToken = default);
}

public sealed class PortalOperationsService(IHttpClientFactory httpClientFactory, ILogger<PortalOperationsService> logger) : IPortalOperationsService
{
    private readonly object syncRoot = new();
    private readonly List<ClaimCase> claims = SeedClaims();
    private readonly List<QuoteCase> quotes = SeedQuotes();
    private readonly List<QueueSnapshot> queues = SeedQueues();
    private string backendApiMode = "Fallback sample data";

    public async Task<DashboardSnapshot> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        await TryRefreshQueuesFromBackendAsync(cancellationToken);

        lock (syncRoot)
        {
            var claimActivity = claims.SelectMany(claim => claim.AuditTrail.Select(entry => new RecentActivityItem
            {
                Category = "Claim",
                Title = entry.Action,
                Description = $"{claim.Claim.ClaimNumber}: {entry.Detail}",
                Actor = entry.Actor,
                OccurredAtUtc = entry.OccurredAtUtc
            }));

            var quoteActivity = quotes.SelectMany(quote => quote.AuditTrail.Select(entry => new RecentActivityItem
            {
                Category = "Quote",
                Title = entry.Action,
                Description = $"{quote.Quote.QuoteNumber}: {entry.Detail}",
                Actor = entry.Actor,
                OccurredAtUtc = entry.OccurredAtUtc
            }));

            var queueActivity = queues.SelectMany(queue => queue.DeadLetterEntries
                .Where(entry => entry.State != QueueMessageState.DeadLetter)
                .Select(entry => new RecentActivityItem
                {
                    Category = "Queue",
                    Title = entry.State.ToString(),
                    Description = $"{queue.Name}: {entry.Subject}",
                    Actor = "Operations automation",
                    OccurredAtUtc = entry.FailedAtUtc.AddMinutes(entry.State == QueueMessageState.Retried ? 5 : 10)
                }));

            return new DashboardSnapshot
            {
                ActiveClaimsCount = claims.Count(claim => claim.Claim.Status is ClaimStatus.Submitted or ClaimStatus.UnderReview or ClaimStatus.Approved),
                PendingQuotesCount = quotes.Count(quote => quote.Status == PortalQuoteStatus.Review),
                ReadyForPayoutCount = claims.Count(claim => claim.Claim.Status == ClaimStatus.Approved),
                TotalQueueDepth = queues.Sum(queue => queue.TotalDepth),
                BackendApiMode = backendApiMode,
                Queues = queues.OrderByDescending(queue => queue.TotalDepth).ToList(),
                ProcessingStats =
                [
                    new() { Label = "Claim SLA", Value = "93%", Trend = "+4% vs. yesterday" },
                    new() { Label = "Quote conversion", Value = "38%", Trend = "+6 approved this shift" },
                    new() { Label = "Average cycle time", Value = "2.6 days", Trend = "0.4 days faster" }
                ],
                RecentActivity = claimActivity
                    .Concat(quoteActivity)
                    .Concat(queueActivity)
                    .OrderByDescending(item => item.OccurredAtUtc)
                    .Take(8)
                    .ToList()
            };
        }
    }

    public Task<IReadOnlyList<ClaimCase>> GetClaimsAsync(CancellationToken cancellationToken = default)
    {
        lock (syncRoot)
        {
            return Task.FromResult<IReadOnlyList<ClaimCase>>(claims.OrderByDescending(claim => claim.Claim.FiledDate).ToList());
        }
    }

    public Task<ClaimCase?> GetClaimAsync(Guid id, CancellationToken cancellationToken = default)
    {
        lock (syncRoot)
        {
            return Task.FromResult(claims.SingleOrDefault(claim => claim.Claim.Id == id));
        }
    }

    public Task<IReadOnlyList<string>> GetAdjustersAsync(CancellationToken cancellationToken = default)
    {
        lock (syncRoot)
        {
            return Task.FromResult<IReadOnlyList<string>>(claims.Select(claim => claim.Assignee).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToList());
        }
    }

    public Task TransitionClaimAsync(Guid id, ClaimStatus nextStatus, string actor, CancellationToken cancellationToken = default)
    {
        lock (syncRoot)
        {
            var claim = RequireClaim(id);
            claim.Claim.Status = nextStatus;
            if (nextStatus is ClaimStatus.Denied or ClaimStatus.Paid or ClaimStatus.Closed)
            {
                claim.Claim.ResolvedDate = DateTime.UtcNow;
            }

            claim.AuditTrail.Insert(0, CreateAudit(actor, "Claim status updated", $"Moved to {FormatEnum(nextStatus)}"));
            claim.Timeline.Insert(0, CreateTimeline(FormatEnum(nextStatus), $"{actor} moved the claim to {FormatEnum(nextStatus)}", TimelineState(nextStatus), DateTime.UtcNow));
        }

        return Task.CompletedTask;
    }

    public Task AssignClaimAsync(Guid id, string assignee, string actor, CancellationToken cancellationToken = default)
    {
        lock (syncRoot)
        {
            var claim = RequireClaim(id);
            claim.Assignee = assignee;
            claim.AuditTrail.Insert(0, CreateAudit(actor, "Claim reassigned", $"Assigned to {assignee}"));
            claim.Timeline.Insert(0, CreateTimeline("Assigned", $"{actor} routed the claim to {assignee}", "info", DateTime.UtcNow));
        }

        return Task.CompletedTask;
    }

    public Task AddClaimNoteAsync(Guid id, string note, string actor, CancellationToken cancellationToken = default)
    {
        lock (syncRoot)
        {
            var claim = RequireClaim(id);
            claim.Notes.Insert(0, new PortalNote { Author = actor, Body = note.Trim(), CreatedAtUtc = DateTime.UtcNow });
            claim.AuditTrail.Insert(0, CreateAudit(actor, "Internal note added", note.Trim()));
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<QuoteCase>> GetQuotesAsync(CancellationToken cancellationToken = default)
    {
        lock (syncRoot)
        {
            return Task.FromResult<IReadOnlyList<QuoteCase>>(quotes.OrderByDescending(quote => quote.Quote.CreatedAt).ToList());
        }
    }

    public Task<QuoteCase?> GetQuoteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        lock (syncRoot)
        {
            return Task.FromResult(quotes.SingleOrDefault(quote => quote.Quote.Id == id));
        }
    }

    public Task<IReadOnlyList<string>> GetUnderwritersAsync(CancellationToken cancellationToken = default)
    {
        lock (syncRoot)
        {
            return Task.FromResult<IReadOnlyList<string>>(quotes.Select(quote => quote.Underwriter).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToList());
        }
    }

    public Task SetQuoteStatusAsync(Guid id, PortalQuoteStatus status, string actor, CancellationToken cancellationToken = default)
    {
        lock (syncRoot)
        {
            var quote = RequireQuote(id);
            quote.Status = status;
            quote.Quote.IsAccepted = status is PortalQuoteStatus.Approved or PortalQuoteStatus.Bound;
            quote.AuditTrail.Insert(0, CreateAudit(actor, "Quote workflow updated", $"Moved to {FormatEnum(status)}"));
            quote.Timeline.Insert(0, CreateTimeline(FormatEnum(status), $"{actor} moved the quote to {FormatEnum(status)}", status switch
            {
                PortalQuoteStatus.Approved or PortalQuoteStatus.Bound => "success",
                PortalQuoteStatus.Declined => "danger",
                _ => "info"
            }, DateTime.UtcNow));
        }

        return Task.CompletedTask;
    }

    public Task OverridePremiumAsync(Guid id, decimal premium, string reason, string actor, CancellationToken cancellationToken = default)
    {
        lock (syncRoot)
        {
            var quote = RequireQuote(id);
            quote.Quote.EstimatedPremium = premium;
            quote.PremiumBreakdown.BasePremium = premium - quote.PremiumBreakdown.RiskAdjustment - quote.PremiumBreakdown.TaxesAndFees + quote.PremiumBreakdown.Discount;
            quote.Notes.Insert(0, new PortalNote { Author = actor, Body = $"Premium override applied: {reason}", CreatedAtUtc = DateTime.UtcNow });
            quote.AuditTrail.Insert(0, CreateAudit(actor, "Premium overridden", $"Set premium to {premium:C}. {reason}"));
        }

        return Task.CompletedTask;
    }

    public Task ConvertQuoteToPolicyAsync(Guid id, string actor, CancellationToken cancellationToken = default)
    {
        lock (syncRoot)
        {
            var quote = RequireQuote(id);
            quote.Status = PortalQuoteStatus.Bound;
            quote.Quote.IsAccepted = true;
            quote.GeneratedPolicyNumber ??= $"POL-{DateTime.UtcNow:yyyyMMdd}-OPS{Random.Shared.Next(1000, 9999)}";
            quote.AuditTrail.Insert(0, CreateAudit(actor, "Quote converted", $"Bound as policy {quote.GeneratedPolicyNumber}"));
            quote.Timeline.Insert(0, CreateTimeline("Bound", $"{actor} converted the approved quote into policy {quote.GeneratedPolicyNumber}", "success", DateTime.UtcNow));
        }

        return Task.CompletedTask;
    }

    public Task AddQuoteNoteAsync(Guid id, string note, string actor, CancellationToken cancellationToken = default)
    {
        lock (syncRoot)
        {
            var quote = RequireQuote(id);
            quote.Notes.Insert(0, new PortalNote { Author = actor, Body = note.Trim(), CreatedAtUtc = DateTime.UtcNow });
            quote.AuditTrail.Insert(0, CreateAudit(actor, "Underwriting note added", note.Trim()));
        }

        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<QueueSnapshot>> GetQueuesAsync(CancellationToken cancellationToken = default)
    {
        await TryRefreshQueuesFromBackendAsync(cancellationToken);
        lock (syncRoot)
        {
            return queues.OrderByDescending(queue => queue.TotalDepth).ToList();
        }
    }

    public Task RetryQueueMessageAsync(string queueName, string messageId, string actor, CancellationToken cancellationToken = default)
    {
        lock (syncRoot)
        {
            var queue = RequireQueue(queueName);
            var entry = queue.DeadLetterEntries.Single(item => item.Id == messageId);
            entry.State = QueueMessageState.Retried;
            queue.ReadyCount += 1;
            queue.LastUpdatedUtc = DateTime.UtcNow;
            logger.LogInformation("{Actor} retried {MessageId} from {QueueName}", actor, messageId, queueName);
        }

        return Task.CompletedTask;
    }

    public Task ReprocessQueueMessageAsync(string queueName, string messageId, string actor, CancellationToken cancellationToken = default)
    {
        lock (syncRoot)
        {
            var queue = RequireQueue(queueName);
            var entry = queue.DeadLetterEntries.Single(item => item.Id == messageId);
            entry.State = QueueMessageState.Reprocessed;
            queue.InFlightCount += 1;
            queue.LastUpdatedUtc = DateTime.UtcNow;
            logger.LogInformation("{Actor} reprocessed {MessageId} from {QueueName}", actor, messageId, queueName);
        }

        return Task.CompletedTask;
    }

    private async Task TryRefreshQueuesFromBackendAsync(CancellationToken cancellationToken)
    {
        try
        {
            var client = httpClientFactory.CreateClient("backendapi");
            using var response = await client.GetAsync("operations/queues", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                backendApiMode = "Fallback sample data";
                return;
            }

            var apiQueues = await response.Content.ReadFromJsonAsync<List<QueueSnapshot>>(cancellationToken: cancellationToken);
            if (apiQueues is null || apiQueues.Count == 0)
            {
                backendApiMode = "Fallback sample data";
                return;
            }

            lock (syncRoot)
            {
                queues.Clear();
                queues.AddRange(apiQueues);
                backendApiMode = "Live backend API";
            }
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Backend API queue snapshot unavailable.");
            backendApiMode = "Fallback sample data";
        }
    }

    private ClaimCase RequireClaim(Guid id) => claims.Single(claim => claim.Claim.Id == id);
    private QuoteCase RequireQuote(Guid id) => quotes.Single(quote => quote.Quote.Id == id);
    private QueueSnapshot RequireQueue(string queueName) => queues.Single(queue => string.Equals(queue.Name, queueName, StringComparison.OrdinalIgnoreCase));

    private static AuditEntry CreateAudit(string actor, string action, string detail) =>
        new() { Actor = actor, Action = action, Detail = detail, OccurredAtUtc = DateTime.UtcNow };

    private static TimelineStep CreateTimeline(string label, string description, string state, DateTime timestampUtc) =>
        new() { Label = label, Description = description, State = state, TimestampUtc = timestampUtc };

    private static string TimelineState(ClaimStatus status) => status switch
    {
        ClaimStatus.Approved or ClaimStatus.Paid or ClaimStatus.Closed => "success",
        ClaimStatus.Denied => "danger",
        _ => "info"
    };

    private static string FormatEnum<TEnum>(TEnum value) where TEnum : struct, Enum => string.Concat(value.ToString().Select((character, index) => index > 0 && char.IsUpper(character) ? $" {character}" : character.ToString()));

    private static List<ClaimCase> SeedClaims()
    {
        var now = DateTime.UtcNow;
        var customer1 = new Customer { Id = Guid.Parse("11111111-aaaa-aaaa-aaaa-aaaaaaaaaaa1"), FirstName = "Maria", LastName = "Santos", Email = "maria.santos@example.com", Phone = "555-0101", Address = "123 Oak Street, Seattle, WA 98101", CreatedAt = now.AddMonths(-8) };
        var customer2 = new Customer { Id = Guid.Parse("11111111-aaaa-aaaa-aaaa-aaaaaaaaaaa2"), FirstName = "James", LastName = "Chen", Email = "james.chen@example.com", Phone = "555-0102", Address = "456 Pine Avenue, Portland, OR 97201", CreatedAt = now.AddMonths(-6) };
        var customer3 = new Customer { Id = Guid.Parse("11111111-aaaa-aaaa-aaaa-aaaaaaaaaaa3"), FirstName = "Fatima", LastName = "Al-Rashid", Email = "fatima.alrashid@example.com", Phone = "555-0103", Address = "789 Elm Drive, San Francisco, CA 94102", CreatedAt = now.AddMonths(-5) };
        var customer4 = new Customer { Id = Guid.Parse("11111111-aaaa-aaaa-aaaa-aaaaaaaaaaa4"), FirstName = "Elena", LastName = "Volkov", Email = "elena.volkov@example.com", Phone = "555-0106", Address = "987 Birch Way, Chicago, IL 60601", CreatedAt = now.AddMonths(-2) };

        var policy1 = new Policy { Id = Guid.Parse("22222222-bbbb-bbbb-bbbb-bbbbbbbbbbb1"), PolicyNumber = "POL-20250101-AUTO0001", Type = PolicyType.Auto, Status = PolicyStatus.Active, PremiumAmount = 145.83m, CoverageAmount = 50000m, StartDate = now.AddMonths(-6), EndDate = now.AddMonths(6), CreatedAt = now.AddMonths(-6), Customer = customer1, CustomerId = customer1.Id };
        var policy2 = new Policy { Id = Guid.Parse("22222222-bbbb-bbbb-bbbb-bbbbbbbbbbb2"), PolicyNumber = "POL-20250102-HOME0001", Type = PolicyType.Home, Status = PolicyStatus.Active, PremiumAmount = 208.33m, CoverageAmount = 100000m, StartDate = now.AddMonths(-5), EndDate = now.AddMonths(7), CreatedAt = now.AddMonths(-5), Customer = customer2, CustomerId = customer2.Id };
        var policy3 = new Policy { Id = Guid.Parse("22222222-bbbb-bbbb-bbbb-bbbbbbbbbbb3"), PolicyNumber = "POL-20250103-HLTH0001", Type = PolicyType.Health, Status = PolicyStatus.Active, PremiumAmount = 375m, CoverageAmount = 100000m, StartDate = now.AddMonths(-3), EndDate = now.AddMonths(9), CreatedAt = now.AddMonths(-3), Customer = customer3, CustomerId = customer3.Id };
        var policy4 = new Policy { Id = Guid.Parse("22222222-bbbb-bbbb-bbbb-bbbbbbbbbbb4"), PolicyNumber = "POL-20250104-BUSI0001", Type = PolicyType.Business, Status = PolicyStatus.Active, PremiumAmount = 833.33m, CoverageAmount = 250000m, StartDate = now.AddMonths(-2), EndDate = now.AddMonths(10), CreatedAt = now.AddMonths(-2), Customer = customer4, CustomerId = customer4.Id };

        return
        [
            new ClaimCase
            {
                Customer = customer1,
                Policy = policy1,
                Assignee = "Alicia Gomez",
                Severity = "Low",
                Claim = new Claim { Id = Guid.Parse("33333333-cccc-cccc-cccc-ccccccccccc1"), ClaimNumber = "CLM-20250201-FENDER01", Status = ClaimStatus.Approved, Description = "Parking-lot collision with minor front bumper damage.", Amount = 3500m, IncidentDate = now.AddDays(-30), FiledDate = now.AddDays(-28), Policy = policy1, PolicyId = policy1.Id },
                Notes = [new() { Author = "Alicia Gomez", Body = "Repair estimate validated against partner body shop.", CreatedAtUtc = now.AddDays(-2) }],
                AuditTrail = [CreateAudit("Alicia Gomez", "Claim status updated", "Moved to Approved"), new() { Actor = "System", Action = "Claim received", Detail = "FNOL entered via public intake.", OccurredAtUtc = now.AddDays(-28) }],
                Timeline = [CreateTimeline("Approved", "Coverage confirmed and payout prepared.", "success", now.AddDays(-2)), CreateTimeline("Under Review", "Adjuster validated photo set and police report.", "info", now.AddDays(-10)), CreateTimeline("Submitted", "Claim was submitted by the customer.", "info", now.AddDays(-28))]
            },
            new ClaimCase
            {
                Customer = customer2,
                Policy = policy2,
                Assignee = "Mina Patel",
                Severity = "High",
                Claim = new Claim { Id = Guid.Parse("33333333-cccc-cccc-cccc-ccccccccccc2"), ClaimNumber = "CLM-20250202-WATER001", Status = ClaimStatus.UnderReview, Description = "Water damage from a burst pipe in the finished basement.", Amount = 15000m, IncidentDate = now.AddDays(-14), FiledDate = now.AddDays(-12), Policy = policy2, PolicyId = policy2.Id },
                Notes = [new() { Author = "Mina Patel", Body = "Waiting on remediation invoice and contractor statement.", CreatedAtUtc = now.AddHours(-8) }],
                AuditTrail = [CreateAudit("Mina Patel", "Claim reassigned", "Assigned to catastrophic property queue"), new() { Actor = "System", Action = "Claim received", Detail = "Claim shell created from intake broker.", OccurredAtUtc = now.AddDays(-12) }],
                Timeline = [CreateTimeline("Under Review", "Property adjuster requested follow-up documents.", "info", now.AddDays(-6)), CreateTimeline("Submitted", "Claim was submitted by the customer.", "info", now.AddDays(-12))]
            },
            new ClaimCase
            {
                Customer = customer3,
                Policy = policy3,
                Assignee = "Jordan Lee",
                Severity = "Medium",
                Claim = new Claim { Id = Guid.Parse("33333333-cccc-cccc-cccc-ccccccccccc3"), ClaimNumber = "CLM-20250203-MEDIC001", Status = ClaimStatus.Submitted, Description = "Emergency room visit for a broken arm after a fall.", Amount = 8500m, IncidentDate = now.AddDays(-5), FiledDate = now.AddDays(-3), Policy = policy3, PolicyId = policy3.Id },
                Notes = [new() { Author = "Jordan Lee", Body = "Medical coding validation queued for tonight.", CreatedAtUtc = now.AddHours(-4) }],
                AuditTrail = [CreateAudit("Jordan Lee", "Claim status updated", "Awaiting nurse review"), new() { Actor = "System", Action = "Claim received", Detail = "Health intake workflow accepted the submission.", OccurredAtUtc = now.AddDays(-3) }],
                Timeline = [CreateTimeline("Submitted", "Claim entered the health review queue.", "info", now.AddDays(-3))]
            },
            new ClaimCase
            {
                Customer = customer4,
                Policy = policy4,
                Assignee = "Alicia Gomez",
                Severity = "Critical",
                Claim = new Claim { Id = Guid.Parse("33333333-cccc-cccc-cccc-ccccccccccc4"), ClaimNumber = "CLM-20250205-EQUIP001", Status = ClaimStatus.Paid, Description = "Server room equipment failure caused by a power surge.", Amount = 45000m, IncidentDate = now.AddDays(-60), FiledDate = now.AddDays(-58), ResolvedDate = now.AddDays(-30), Policy = policy4, PolicyId = policy4.Id },
                Notes = [new() { Author = "Alicia Gomez", Body = "Finance confirmed ACH disbursement to vendor replacement account.", CreatedAtUtc = now.AddDays(-1) }],
                AuditTrail = [CreateAudit("Finance Ops", "Payout completed", "ACH settlement released"), new() { Actor = "System", Action = "Claim received", Detail = "Business interruption claim opened.", OccurredAtUtc = now.AddDays(-58) }],
                Timeline = [CreateTimeline("Paid", "Settlement released to claimant.", "success", now.AddDays(-1)), CreateTimeline("Approved", "Supervisor approved high-value claim.", "success", now.AddDays(-8)), CreateTimeline("Under Review", "Forensics report attached.", "info", now.AddDays(-20))]
            }
        ];
    }

    private static List<QuoteCase> SeedQuotes()
    {
        var now = DateTime.UtcNow;
        var customer1 = new Customer { Id = Guid.Parse("44444444-dddd-dddd-dddd-ddddddddddd1"), FirstName = "Robert", LastName = "Johnson", Email = "robert.johnson@example.com", Phone = "555-0104", Address = "321 Maple Lane, Denver, CO 80202", CreatedAt = now.AddMonths(-4) };
        var customer2 = new Customer { Id = Guid.Parse("44444444-dddd-dddd-dddd-ddddddddddd2"), FirstName = "Yuki", LastName = "Tanaka", Email = "yuki.tanaka@example.com", Phone = "555-0105", Address = "654 Cedar Road, Austin, TX 73301", CreatedAt = now.AddMonths(-3) };
        var customer3 = new Customer { Id = Guid.Parse("44444444-dddd-dddd-dddd-ddddddddddd3"), FirstName = "David", LastName = "Okafor", Email = "david.okafor@example.com", Phone = "555-0107", Address = "147 Walnut Blvd, Miami, FL 33101", CreatedAt = now.AddMonths(-1) };

        return
        [
            new QuoteCase
            {
                Customer = customer1,
                Underwriter = "Priya Nair",
                Status = PortalQuoteStatus.Review,
                RiskSummary = "Low-mileage driver with prior comprehensive coverage and clean MVR.",
                Quote = new Quote { Id = Guid.Parse("55555555-eeee-eeee-eeee-eeeeeeeeeee1"), QuoteNumber = "QTE-20250301-AUTO0001", Type = PolicyType.Auto, EstimatedPremium = 116.67m, CoverageAmount = 40000m, Customer = customer1, CustomerId = customer1.Id, CreatedAt = now.AddDays(-5), ExpiresAt = now.AddDays(25) },
                PremiumBreakdown = new PremiumBreakdown { BasePremium = 92m, RiskAdjustment = 12m, TaxesAndFees = 18m, Discount = 5.33m },
                Notes = [new() { Author = "Priya Nair", Body = "Pulled telematics scorecard; no adverse signals.", CreatedAtUtc = now.AddHours(-7) }],
                AuditTrail = [CreateAudit("Priya Nair", "Quote workflow updated", "Moved to Review"), new() { Actor = "System", Action = "Quote received", Detail = "Quote entered underwriting review.", OccurredAtUtc = now.AddDays(-5) }],
                Timeline = [CreateTimeline("Review", "Queued for underwriting review.", "info", now.AddDays(-5))]
            },
            new QuoteCase
            {
                Customer = customer2,
                Underwriter = "Marcus Bell",
                Status = PortalQuoteStatus.Approved,
                RiskSummary = "High-value home with monitored alarm and recent roof replacement.",
                Quote = new Quote { Id = Guid.Parse("55555555-eeee-eeee-eeee-eeeeeeeeeee2"), QuoteNumber = "QTE-20250302-HOME0001", Type = PolicyType.Home, EstimatedPremium = 312.50m, CoverageAmount = 150000m, Customer = customer2, CustomerId = customer2.Id, CreatedAt = now.AddDays(-20), ExpiresAt = now.AddDays(10), IsAccepted = true },
                PremiumBreakdown = new PremiumBreakdown { BasePremium = 255m, RiskAdjustment = 32m, TaxesAndFees = 28m, Discount = 2.5m },
                Notes = [new() { Author = "Marcus Bell", Body = "Replacement cost estimator reviewed; ready to bind.", CreatedAtUtc = now.AddDays(-1) }],
                AuditTrail = [CreateAudit("Marcus Bell", "Quote workflow updated", "Moved to Approved"), new() { Actor = "System", Action = "Quote received", Detail = "Property data enrichment completed.", OccurredAtUtc = now.AddDays(-20) }],
                Timeline = [CreateTimeline("Approved", "Underwriter approved the quote.", "success", now.AddDays(-1)), CreateTimeline("Review", "Property quote entered the review queue.", "info", now.AddDays(-20))]
            },
            new QuoteCase
            {
                Customer = customer3,
                Underwriter = "Priya Nair",
                Status = PortalQuoteStatus.Declined,
                RiskSummary = "Commercial auto submission exceeded acceptable loss history threshold.",
                Quote = new Quote { Id = Guid.Parse("55555555-eeee-eeee-eeee-eeeeeeeeeee3"), QuoteNumber = "QTE-20250303-BUSI0001", Type = PolicyType.Business, EstimatedPremium = 540.00m, CoverageAmount = 200000m, Customer = customer3, CustomerId = customer3.Id, CreatedAt = now.AddDays(-2), ExpiresAt = now.AddDays(28) },
                PremiumBreakdown = new PremiumBreakdown { BasePremium = 430m, RiskAdjustment = 88m, TaxesAndFees = 36m, Discount = 14m },
                Notes = [new() { Author = "Priya Nair", Body = "Declined after loss-run review and underwriting manager approval.", CreatedAtUtc = now.AddHours(-12) }],
                AuditTrail = [CreateAudit("Priya Nair", "Quote workflow updated", "Moved to Declined"), new() { Actor = "System", Action = "Quote received", Detail = "Commercial review started.", OccurredAtUtc = now.AddDays(-2) }],
                Timeline = [CreateTimeline("Declined", "Loss history exceeded threshold.", "danger", now.AddHours(-12)), CreateTimeline("Review", "Quote entered commercial review.", "info", now.AddDays(-2))]
            }
        ];
    }

    private static List<QueueSnapshot> SeedQueues()
    {
        var now = DateTime.UtcNow;
        return
        [
            new QueueSnapshot
            {
                Name = "private.claim-intake",
                ReadyCount = 14,
                InFlightCount = 4,
                LastUpdatedUtc = now.AddMinutes(-3),
                DeadLetterEntries =
                [
                    new() { Id = "clm-dlq-001", Subject = "claim.submitted.v1", Reason = "Missing contractor attachment metadata", Attempts = 3, FailedAtUtc = now.AddMinutes(-35) },
                    new() { Id = "clm-dlq-002", Subject = "claim.status-changed.v1", Reason = "Optimistic concurrency conflict", Attempts = 2, FailedAtUtc = now.AddMinutes(-18) }
                ]
            },
            new QueueSnapshot
            {
                Name = "private.quote-intake",
                ReadyCount = 9,
                InFlightCount = 2,
                LastUpdatedUtc = now.AddMinutes(-2),
                DeadLetterEntries =
                [
                    new() { Id = "qte-dlq-001", Subject = "quote.requested.v1", Reason = "Rate table cache unavailable", Attempts = 4, FailedAtUtc = now.AddMinutes(-22) }
                ]
            },
            new QueueSnapshot
            {
                Name = "ops.notifications",
                ReadyCount = 3,
                InFlightCount = 1,
                LastUpdatedUtc = now.AddMinutes(-1),
                DeadLetterEntries = []
            }
        ];
    }
}
