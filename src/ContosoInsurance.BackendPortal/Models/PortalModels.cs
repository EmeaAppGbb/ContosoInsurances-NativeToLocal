using ContosoInsurance.Data.Enums;
using ContosoInsurance.Data.Models;

namespace ContosoInsurance.BackendPortal.Models;

public sealed class DashboardSnapshot
{
    public int ActiveClaimsCount { get; set; }
    public int PendingQuotesCount { get; set; }
    public int ReadyForPayoutCount { get; set; }
    public int TotalQueueDepth { get; set; }
    public string BackendApiMode { get; set; } = "Fallback sample data";
    public List<ProcessingStat> ProcessingStats { get; set; } = [];
    public List<RecentActivityItem> RecentActivity { get; set; } = [];
    public List<QueueSnapshot> Queues { get; set; } = [];
}

public sealed class ProcessingStat
{
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Trend { get; set; } = string.Empty;
}

public sealed class RecentActivityItem
{
    public string Category { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Actor { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
}

public sealed class PortalNote
{
    public string Author { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class AuditEntry
{
    public string Actor { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
}

public sealed class TimelineStep
{
    public string Label { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public DateTime TimestampUtc { get; set; }
}

public sealed class ClaimCase
{
    public Claim Claim { get; set; } = new();
    public Policy Policy { get; set; } = new();
    public Customer Customer { get; set; } = new();
    public string Assignee { get; set; } = string.Empty;
    public string Severity { get; set; } = "Medium";
    public string QueueName { get; set; } = "private.claim-intake";
    public List<PortalNote> Notes { get; set; } = [];
    public List<AuditEntry> AuditTrail { get; set; } = [];
    public List<TimelineStep> Timeline { get; set; } = [];
}

public enum PortalQuoteStatus
{
    Review,
    Approved,
    Declined,
    Bound
}

public sealed class PremiumBreakdown
{
    public decimal BasePremium { get; set; }
    public decimal RiskAdjustment { get; set; }
    public decimal TaxesAndFees { get; set; }
    public decimal Discount { get; set; }
    public decimal FinalPremium => BasePremium + RiskAdjustment + TaxesAndFees - Discount;
}

public sealed class QuoteCase
{
    public Quote Quote { get; set; } = new();
    public Customer Customer { get; set; } = new();
    public PortalQuoteStatus Status { get; set; } = PortalQuoteStatus.Review;
    public string Underwriter { get; set; } = string.Empty;
    public string RiskSummary { get; set; } = string.Empty;
    public string? GeneratedPolicyNumber { get; set; }
    public PremiumBreakdown PremiumBreakdown { get; set; } = new();
    public List<PortalNote> Notes { get; set; } = [];
    public List<AuditEntry> AuditTrail { get; set; } = [];
    public List<TimelineStep> Timeline { get; set; } = [];
}

public sealed class QueueSnapshot
{
    public string Name { get; set; } = string.Empty;
    public int ReadyCount { get; set; }
    public int InFlightCount { get; set; }
    public DateTime LastUpdatedUtc { get; set; }
    public List<QueueMessageEntry> DeadLetterEntries { get; set; } = [];
    public int DeadLetterCount => DeadLetterEntries.Count(entry => entry.State == QueueMessageState.DeadLetter);
    public int TotalDepth => ReadyCount + InFlightCount + DeadLetterCount;
}

public enum QueueMessageState
{
    DeadLetter,
    Retried,
    Reprocessed
}

public sealed class QueueMessageEntry
{
    public string Id { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public int Attempts { get; set; }
    public DateTime FailedAtUtc { get; set; }
    public QueueMessageState State { get; set; } = QueueMessageState.DeadLetter;
}
