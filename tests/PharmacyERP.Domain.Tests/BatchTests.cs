using FluentAssertions;
using PharmacyERP.Domain.Entities;
using Xunit;

namespace PharmacyERP.Domain.Tests;

/// <summary>
/// Covers Batch.IsExpired() and DaysUntilExpiry(), which drive both the expiry
/// dashboard alerts and the decision of whether a batch may be dispensed. The
/// boundary case (a batch expiring *today*) is the one worth pinning down: it is
/// still usable, so an off-by-one here would either block valid stock or allow
/// expired stock to be sold.
/// </summary>
public class BatchTests
{
    private static readonly DateTime Now = new(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void IsExpired_ReturnsTrue_WhenExpiryWasBeforeToday()
    {
        new Batch { ExpiryDate = Now.AddDays(-1) }.IsExpired(Now).Should().BeTrue();
    }

    [Fact]
    public void IsExpired_ReturnsFalse_OnTheExpiryDateItself()
    {
        new Batch { ExpiryDate = Now }.IsExpired(Now).Should().BeFalse("a batch expiring today is still dispensable");
    }

    [Fact]
    public void IsExpired_IgnoresTimeOfDay_AndComparesDatesOnly()
    {
        // Expiry earlier the same calendar day must not count as expired.
        var batch = new Batch { ExpiryDate = new DateTime(2026, 6, 15, 1, 0, 0, DateTimeKind.Utc) };

        batch.IsExpired(Now).Should().BeFalse();
    }

    [Theory]
    [InlineData(30, 30)]
    [InlineData(1, 1)]
    [InlineData(0, 0)]
    [InlineData(-5, -5)]
    public void DaysUntilExpiry_ReturnsSignedDayDifference(int offsetDays, int expected)
    {
        new Batch { ExpiryDate = Now.AddDays(offsetDays) }.DaysUntilExpiry(Now).Should().Be(expected);
    }
}
