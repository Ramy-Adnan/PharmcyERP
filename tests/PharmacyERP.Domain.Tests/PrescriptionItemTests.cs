using FluentAssertions;
using PharmacyERP.Domain.Entities;
using Xunit;

namespace PharmacyERP.Domain.Tests;

/// <summary>
/// Covers PrescriptionItem.QuantityRemaining, which SalesService relies on to
/// decide whether a prescription-only drug may be dispensed. If this ever
/// returned a negative number, the "quantity exceeds prescription" guard in
/// checkout would silently pass and over-dispense a controlled drug.
/// </summary>
public class PrescriptionItemTests
{
    [Fact]
    public void QuantityRemaining_IsFullPrescribedAmount_BeforeAnyDispensing()
    {
        var item = new PrescriptionItem { QuantityPrescribed = 30, QuantityDispensed = 0 };

        item.QuantityRemaining.Should().Be(30);
    }

    [Fact]
    public void QuantityRemaining_SubtractsDispensedAmount_OnPartialFill()
    {
        var item = new PrescriptionItem { QuantityPrescribed = 30, QuantityDispensed = 10 };

        item.QuantityRemaining.Should().Be(20);
    }

    [Fact]
    public void QuantityRemaining_IsZero_WhenFullyDispensed()
    {
        var item = new PrescriptionItem { QuantityPrescribed = 30, QuantityDispensed = 30 };

        item.QuantityRemaining.Should().Be(0);
    }

    [Fact]
    public void QuantityRemaining_ClampsToZero_AndNeverGoesNegative()
    {
        // Over-dispensing should never happen, but if data drift ever produced it the
        // property must not return a negative that would corrupt downstream comparisons.
        var item = new PrescriptionItem { QuantityPrescribed = 10, QuantityDispensed = 25 };

        item.QuantityRemaining.Should().Be(0);
    }
}
