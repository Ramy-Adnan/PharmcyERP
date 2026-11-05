using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PharmacyERP.WPF.Services;

/// <summary>Keep the print page detached: FixedPage must never join the RTL preview tree.</summary>
public static class ProductLabelPreview
{
    public const double Dpi = 203;

    public static Image Create(ProductLabelRequest request)
    {
        // Build and render the same page as the printer, before it can inherit
        // any visual-parent mirroring or layout transforms from the window.
        var page = ProductLabelDocumentBuilder.BuildLabel(request, Dpi, Dpi);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(page.Width * Dpi / 96),
            (int)Math.Ceiling(page.Height * Dpi / 96), Dpi, Dpi, PixelFormats.Pbgra32);
        bitmap.Render(page);
        bitmap.Freeze();
        var image = new Image
        {
            Source = bitmap, FlowDirection = FlowDirection.LeftToRight,
            Stretch = Stretch.Uniform, SnapsToDevicePixels = true
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        return image;
    }
}
