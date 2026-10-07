using FluentAssertions;
using PharmacyERP.Domain.Entities;
using Xunit;

namespace PharmacyERP.Domain.Tests;

/// <summary>Covers InsurancePolicy.IsCurrentlyValid(), which decides whether a policy may back a new reimbursement claim.</summary>
public class InsurancePolicyTests
{
    private static InsurancePolicy PolicyFor(DateTime start, DateTime? end, bool isActive = true) =>
        new() { StartDate = start, EndDate = end, IsActive = isActive };

    [Fact]
    public void IsCurrentlyValid_ReturnsTrue_WithinDateRange()
    {
        var today = new DateTime(2026, 6, 15);
        var policy = PolicyFor(today.AddMonths(-1), today.AddMonths(1));

        policy.IsCurrentlyValid(today).Should().BeTrue();
    }

    [Fact]
    public void IsCurrentlyValid_ReturnsTrue_WhenOpenEnded()
    {
        var today = new DateTime(2026, 6, 15);
        var policy = PolicyFor(today.AddYears(-2), end: null);

        policy.IsCurrentlyValid(today).Should().BeTrue();
    }

    [Fact]
    public void IsCurrentlyValid_ReturnsTrue_OnExactStartAndEndDates()
    {
        var today = new DateTime(2026, 6, 15);

        PolicyFor(today, today.AddMonths(1)).IsCurrentlyValid(today).Should().BeTrue();
        PolicyFor(today.AddMonths(-1), today).IsCurrentlyValid(today).Should().BeTrue();
    }

    [Fact]
    public void IsCurrentlyValid_ReturnsFalse_BeforeStartDate()
    {
        var today = new DateTime(2026, 6, 15);
        var policy = PolicyFor(today.AddDays(1), today.AddMonths(1));

        policy.IsCurrentlyValid(today).Should().BeFalse();
    }

    [Fact]
    public void IsCurrentlyValid_ReturnsFalse_AfterEndDate()
    {
        var today = new DateTime(2026, 6, 15);
        var policy = PolicyFor(today.AddMonths(-2), today.AddDays(-1));

        policy.IsCurrentlyValid(today).Should().BeFalse();
    }

    [Fact]
    public void IsCurrentlyValid_ReturnsFalse_WhenDeactivated_EvenInsideDateRange()
    {
        var today = new DateTime(2026, 6, 15);
        var policy = PolicyFor(today.AddMonths(-1), today.AddMonths(1), isActive: false);

        policy.IsCurrentlyValid(today).Should().BeFalse();
    }
}
