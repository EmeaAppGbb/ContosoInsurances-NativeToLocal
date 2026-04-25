using ContosoInsurance.Data.Enums;
using ContosoInsurance.Data.Models;
using FluentAssertions;

namespace ContosoInsurance.Data.Tests;

public class ModelTests
{
    [Fact]
    public void Customer_CreatedAt_DefaultsToUtcNow()
    {
        var before = DateTime.UtcNow;
        var customer = new Customer();
        var after = DateTime.UtcNow;

        customer.CreatedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    [Fact]
    public void Customer_Collections_InitializeEmpty()
    {
        var customer = new Customer();
        customer.Policies.Should().BeEmpty();
        customer.Quotes.Should().BeEmpty();
    }

    [Fact]
    public void Policy_Status_DefaultsToDraft()
    {
        var policy = new Policy();
        policy.Status.Should().Be(PolicyStatus.Draft);
    }

    [Fact]
    public void Policy_CreatedAt_DefaultsToUtcNow()
    {
        var before = DateTime.UtcNow;
        var policy = new Policy();
        var after = DateTime.UtcNow;

        policy.CreatedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    [Fact]
    public void Policy_Claims_InitializeEmpty()
    {
        var policy = new Policy();
        policy.Claims.Should().BeEmpty();
    }

    [Fact]
    public void Claim_Status_DefaultsToSubmitted()
    {
        var claim = new Claim();
        claim.Status.Should().Be(ClaimStatus.Submitted);
    }

    [Fact]
    public void Claim_FiledDate_DefaultsToUtcNow()
    {
        var before = DateTime.UtcNow;
        var claim = new Claim();
        var after = DateTime.UtcNow;

        claim.FiledDate.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    [Fact]
    public void Claim_ResolvedDate_DefaultsToNull()
    {
        var claim = new Claim();
        claim.ResolvedDate.Should().BeNull();
    }

    [Fact]
    public void Quote_IsAccepted_DefaultsToFalse()
    {
        var quote = new Quote();
        quote.IsAccepted.Should().BeFalse();
    }

    [Fact]
    public void Quote_CreatedAt_DefaultsToUtcNow()
    {
        var before = DateTime.UtcNow;
        var quote = new Quote();
        var after = DateTime.UtcNow;

        quote.CreatedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    // --- Enum coverage ---

    [Theory]
    [InlineData(PolicyStatus.Draft)]
    [InlineData(PolicyStatus.Active)]
    [InlineData(PolicyStatus.Expired)]
    [InlineData(PolicyStatus.Cancelled)]
    [InlineData(PolicyStatus.Suspended)]
    public void PolicyStatus_HasExpectedValues(PolicyStatus status)
    {
        Enum.IsDefined(status).Should().BeTrue();
    }

    [Theory]
    [InlineData(ClaimStatus.Submitted)]
    [InlineData(ClaimStatus.UnderReview)]
    [InlineData(ClaimStatus.Approved)]
    [InlineData(ClaimStatus.Denied)]
    [InlineData(ClaimStatus.Paid)]
    [InlineData(ClaimStatus.Closed)]
    public void ClaimStatus_HasExpectedValues(ClaimStatus status)
    {
        Enum.IsDefined(status).Should().BeTrue();
    }

    [Theory]
    [InlineData(PolicyType.Auto)]
    [InlineData(PolicyType.Home)]
    [InlineData(PolicyType.Life)]
    [InlineData(PolicyType.Health)]
    [InlineData(PolicyType.Travel)]
    [InlineData(PolicyType.Business)]
    public void PolicyType_HasExpectedValues(PolicyType type)
    {
        Enum.IsDefined(type).Should().BeTrue();
    }
}
