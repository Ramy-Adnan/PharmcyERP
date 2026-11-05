using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PharmacyERP.WPF.Services;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        try
        {
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
            foreach (var (width, height) in new[] { (50, 30), (80, 30), (55, 85) })
            {
                var request = new ProductLabelRequest("بنادول Panadol 20mg", "8901111080122", "باكيت", 2500, 1, width, height);
                var page = ProductLabelDocumentBuilder.BuildLabel(request, ProductLabelPreview.Dpi, ProductLabelPreview.Dpi);
                var expected = Capture(page);
                var preview = ProductLabelPreview.Create(request);
                if (preview.Source is not BitmapSource source || !source.IsFrozen || !Pixels(source).AsSpan().SequenceEqual(Pixels(expected)))
                    throw new InvalidOperationException("Preview pixels differ from the detached print page.");

                // Use the production preview control in both flows. Pixel comparison
                // is essential: a barcode decoder may also accept a mirrored barcode.
                var ltr = CaptureHost(request, FlowDirection.LeftToRight);
                var rtl = CaptureHost(request, FlowDirection.RightToLeft);
                var mirrored = CaptureHost(request, FlowDirection.LeftToRight, mirror: true);
                if (Difference(ltr, mirrored) < 0.002)
                    throw new InvalidOperationException("The test image is not asymmetric enough to detect mirroring.");
                if (Difference(ltr, rtl) > 0.0005)
                    throw new InvalidOperationException("RTL hosting changed the preview pixels.");
                Console.WriteLine($"PASS {width}x{height}: print pixels match preview, RTL and LTR render alike.");
            }
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static BitmapSource CaptureHost(ProductLabelRequest request, FlowDirection flow, bool mirror = false)
    {
        var image = ProductLabelPreview.Create(request);
        if (mirror) image.LayoutTransform = new ScaleTransform(-1, 1);
        image.Margin = new Thickness(8);
        var source = (BitmapSource)image.Source;
        var host = new Grid
        {
            Width = source.PixelWidth * 96 / ProductLabelPreview.Dpi + 16,
            Height = source.PixelHeight * 96 / ProductLabelPreview.Dpi + 16,
            FlowDirection = flow, Background = Brushes.White
        };
        host.Children.Add(image);
        // Keep the visual root LTR so both flow boundaries occur inside the
        // captured tree, as they do inside an actual WPF window.
        var root = new Grid { Width = host.Width, Height = host.Height, FlowDirection = FlowDirection.LeftToRight };
        root.Children.Add(host);
        return Capture(root);
    }

    private static BitmapSource Capture(FrameworkElement visual)
    {
        visual.Measure(new Size(visual.Width, visual.Height));
        visual.Arrange(new Rect(0, 0, visual.Width, visual.Height)); visual.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(visual.Width * ProductLabelPreview.Dpi / 96),
            (int)Math.Ceiling(visual.Height * ProductLabelPreview.Dpi / 96), ProductLabelPreview.Dpi, ProductLabelPreview.Dpi, PixelFormats.Pbgra32);
        bitmap.Render(visual); bitmap.Freeze(); return bitmap;
    }

    private static byte[] Pixels(BitmapSource bitmap)
    {
        var bytes = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(bytes, bitmap.PixelWidth * 4, 0); return bytes;
    }

    private static double Difference(BitmapSource expected, BitmapSource actual)
    {
        if (expected.PixelWidth != actual.PixelWidth || expected.PixelHeight != actual.PixelHeight) return 1;
        var left = Pixels(expected); var right = Pixels(actual); var changed = 0;
        for (var index = 0; index < left.Length; index += 4)
            if (Math.Abs(left[index] - right[index]) > 8 || Math.Abs(left[index + 1] - right[index + 1]) > 8 || Math.Abs(left[index + 2] - right[index + 2]) > 8) changed++;
        return changed / (double)(left.Length / 4);
    }
}
