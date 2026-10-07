using System.IO;
using System.Windows.Media.Imaging;

namespace PharmacyERP.WPF.Services;

public static class ReceiptLogo
{
    public static string Import(string path)
    {
        if (new FileInfo(path).Length > ReceiptSettingsStore.MaxLogoBytes)
            throw new InvalidOperationException("اختر صورة PNG أو JPG بحجم لا يتجاوز 2 MB.");
        var bytes = File.ReadAllBytes(path);
        using var source = new MemoryStream(bytes);
        var decoder = BitmapDecoder.Create(source, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
        if (decoder is not PngBitmapDecoder && decoder is not JpegBitmapDecoder)
            throw new InvalidOperationException("اختر صورة PNG أو JPG للشعار.");
        var frame = decoder.Frames[0];
        if (frame.PixelWidth > 4096 || frame.PixelHeight > 4096)
            throw new InvalidOperationException("أبعاد الشعار يجب ألا تتجاوز 4096 × 4096 بكسل.");
        using var input = new MemoryStream(bytes);
        var bitmap = new BitmapImage();
        bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = input;
        if (frame.PixelWidth >= frame.PixelHeight && frame.PixelWidth > 360) bitmap.DecodePixelWidth = 360;
        else if (frame.PixelHeight > 360) bitmap.DecodePixelHeight = 360;
        bitmap.EndInit(); bitmap.Freeze();
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = new MemoryStream(); encoder.Save(output);
        return Convert.ToBase64String(output.ToArray());
    }

    public static BitmapImage Load(string base64)
    {
        using var stream = new MemoryStream(Convert.FromBase64String(base64));
        var image = new BitmapImage();
        image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream;
        image.DecodePixelWidth = 360;
        image.EndInit(); image.Freeze();
        return image;
    }
}
