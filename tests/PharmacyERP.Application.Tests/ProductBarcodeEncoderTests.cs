using FluentAssertions;
using PharmacyERP.WPF.Services;
using Xunit;
using ZXing;
using ZXing.Common;

namespace PharmacyERP.Application.Tests;

public class ProductBarcodeEncoderTests
{
    [Theory]
    [InlineData("4006381333931", BarcodeFormat.EAN_13)]
    [InlineData("0000000000000", BarcodeFormat.EAN_13)]
    [InlineData("00001234", BarcodeFormat.CODE_128)]
    [InlineData("STRIP-20.A", BarcodeFormat.CODE_128)]
    [InlineData("4006381333932", BarcodeFormat.CODE_128)]
    [InlineData("12345678901234567890123456789012", BarcodeFormat.CODE_128)]
    public void PrintedModules_DecodeToTheExactStoredValue(string value, BarcodeFormat expectedFormat)
    {
        var encoded = ProductBarcodeEncoder.Encode(value);
        encoded.Text.Should().Be(value); encoded.Format.Should().Be(expectedFormat);
        encoded.Modules.Height.Should().Be(1);

        // Render whole integer modules at a low, realistic thermal-print density.
        // The decoder receives pixels, not the original string or encoder hints.
        const int scale = 2, height = 80;
        var width = encoded.Width * scale;
        var pixels = new byte[width * height * 3];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var shade = encoded.Modules[x / scale, 0] ? (byte)0 : (byte)255;
                var index = (y * width + x) * 3;
                pixels[index] = pixels[index + 1] = pixels[index + 2] = shade;
            }
        var source = new RGBLuminanceSource(pixels, width, height, RGBLuminanceSource.BitmapFormat.RGB24);
        var reader = new BarcodeReaderGeneric
        {
            Options = new DecodingOptions { TryHarder = true, PossibleFormats = new[] { expectedFormat } }
        };
        var decoded = reader.Decode(source);
        decoded.Should().NotBeNull(); decoded!.Text.Should().Be(value);
        decoded.BarcodeFormat.Should().Be(expectedFormat);
    }

    [Fact]
    public void BarcodeQuietZones_LeaveAtLeastTenWhiteModulesOnEachSide()
    {
        foreach (var value in new[] { "4006381333931", "STRIP-20" })
        {
            var encoded = ProductBarcodeEncoder.Encode(value);
            for (var i = 0; i < ProductBarcodeEncoder.QuietZoneModules; i++)
            {
                encoded.Modules[i, 0].Should().BeFalse();
                encoded.Modules[encoded.Width - 1 - i, 0].Should().BeFalse();
            }
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("123 456")]
    [InlineData(" 123456")]
    [InlineData("123456\n")]
    [InlineData("دواء123")]
    [InlineData("١٢٣٤٥٦")]
    [InlineData("123456789012345678901234567890123")]
    public void UnsupportedOrOverlongBarcode_IsRejectedInsteadOfChangedOrPrinted(string? value)
    {
        FluentActions.Invoking(() => ProductBarcodeEncoder.Encode(value)).Should().Throw<ArgumentException>();
    }
}
