using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace PharmacyERP.WPF.Services;

/// <summary>Local product photos survive source updates; item ID/code define a safe file name.</summary>
public sealed class ProductImageStore
{
    private readonly string _directory;
    public ProductImageStore(string? directory = null) => _directory = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PharmacyERP", "product-images");
    private string Prefix(int id, string code) => Path.Combine(_directory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{id}|{code}"))));
    public string? GetPath(int id, string code)
    {
        var prefix = Prefix(id, code);
        return File.Exists(prefix + ".png") ? prefix + ".png" : File.Exists(prefix + ".jpg") ? prefix + ".jpg" : null;
    }
    public string Save(int id, string code, byte[] data)
    {
        if (data.Length < 8 || data.Length > 5 * 1024 * 1024) throw new InvalidOperationException("اختر صورة PNG أو JPEG لا يتجاوز حجمها 5MB.");
        var png = data.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var jpg = data[0] == 255 && data[1] == 216 && data[2] == 255;
        if (!png && !jpg) throw new InvalidOperationException("نوع الصورة غير مدعوم؛ اختر PNG أو JPEG.");
        Directory.CreateDirectory(_directory);
        var prefix = Prefix(id, code); var target = prefix + (png ? ".png" : ".jpg");
        var temp = prefix + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temp, data); File.Move(temp, target, overwrite: true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        var previous = prefix + (png ? ".jpg" : ".png");
        if (File.Exists(previous)) File.Delete(previous);
        return target;
    }
}
