using FluentAssertions;
using PharmacyERP.WPF.Services;
using Xunit;

namespace PharmacyERP.Application.Tests;

public class ProductImageStoreTests
{
    [Fact]
    public void Images_SurviveStoreReload_AndCodesCannotEscapeThePhotoDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pos-photos-" + Guid.NewGuid().ToString("N"));
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jH8sAAAAASUVORK5CYII=");
        try
        {
            var store = new ProductImageStore(directory);
            var path = store.Save(1, "../../outside", png);
            Path.GetDirectoryName(path).Should().Be(directory);
            new ProductImageStore(directory).GetPath(1, "../../outside").Should().Be(path);
            File.ReadAllBytes(path).Should().Equal(png);
            store.GetPath(2, "../../outside").Should().BeNull();
            store.Save(1, "../../outside", png); Directory.GetFiles(directory).Should().ContainSingle();
            Action invalid = () => store.Save(1, "../../outside", new byte[100]); invalid.Should().Throw<InvalidOperationException>();
            Action tooLarge = () => store.Save(1, "../../outside", new byte[5 * 1024 * 1024 + 1]); tooLarge.Should().Throw<InvalidOperationException>();
            File.ReadAllBytes(path).Should().Equal(png);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
