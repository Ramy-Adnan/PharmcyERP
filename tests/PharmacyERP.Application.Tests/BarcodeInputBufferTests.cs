using FluentAssertions;
using PharmacyERP.WPF.Services;
using Xunit;

namespace PharmacyERP.Application.Tests;

public class BarcodeInputBufferTests
{
    [Fact]
    public void PrefixedScan_ConsumesDigitsEvenWhileAnEditorHasFocus_AndCompletesOnlyOnce()
    {
        var input = new BarcodeInputBuffer(); input.BeginPrefixedScan(0);
        input.Append("123", 20, allowUnprefixed: false).Should().BeTrue();
        input.Append("4567890", 40, allowUnprefixed: false).Should().BeTrue();
        input.Complete().Should().Be("1234567890"); input.Complete().Should().BeNull();
        input.HasPendingInput.Should().BeFalse();
    }

    [Theory]
    [InlineData("1234567890123")]
    [InlineData("STRIP20")]
    [InlineData("123")]
    public void RapidUnprefixedScanOutsideEditors_IsRecognizedAndRepeatedScansRemainSeparate(string code)
    {
        var input = new BarcodeInputBuffer();
        for (var scan = 0; scan < 2; scan++)
        {
            for (var i = 0; i < code.Length; i++) input.Append(code[i].ToString(), scan * 500 + i * 10, true).Should().BeFalse();
            input.Complete().Should().Be(code);
        }
    }

    [Fact]
    public void OrdinaryTypingAndShortInput_DoNotBecomeUnprefixedScans()
    {
        var input = new BarcodeInputBuffer();
        for (var i = 0; i < 7; i++) input.Append("1", i * 150, true);
        input.Complete().Should().BeNull();
        input.Append("20", 2000, true); input.Complete().Should().BeNull();
    }

    [Fact]
    public void UnprefixedTypingInAnEditor_IsNotConsumedOrSubmitted()
    {
        var input = new BarcodeInputBuffer(); input.Append("123", 0, true);
        input.Append("200000", 10, false).Should().BeFalse();
        input.Complete().Should().BeNull();
    }

    [Fact]
    public void CancelledOrOversizedScan_IsDiscardedAndNextScanWorks()
    {
        var input = new BarcodeInputBuffer(); input.BeginPrefixedScan(0); input.Append("ABC", 10, false);
        input.Reset(); input.Complete().Should().BeNull();
        input.BeginPrefixedScan(20); input.Append(new string('1', 51), 30, false);
        input.HasPendingInput.Should().BeTrue();
        input.Append("12345", 40, false).Should().BeTrue();
        input.Complete().Should().BeNull();
        input.BeginPrefixedScan(40); input.Append("123", 50, false); input.Complete().Should().Be("123");
    }
}
