using FluentAssertions;
using PharmacyERP.Application.Features.Auth.DTOs;
using PharmacyERP.Application.Features.Sales.DTOs;
using PharmacyERP.Application.Tests.Common;
using PharmacyERP.Domain.Enums;
using PharmacyERP.WPF.Services;
using PharmacyERP.WPF.ViewModels.SystemManagement;
using Xunit;

namespace PharmacyERP.Application.Tests;

public sealed class ReceiptSettingsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "pharmacy-receipts-" + Guid.NewGuid().ToString("N"));
    private ReceiptSettingsStore Store => new(Path.Combine(_directory, "receipt-settings.json"));
    private const string Logo = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl6b9sAAAAASUVORK5CYII=";

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }

    [Fact]
    public void Settings_SurviveReloadAndDeletingTheOriginalLogo()
    {
        Directory.CreateDirectory(_directory);
        var originalLogo = Path.Combine(_directory, "logo.png");
        File.WriteAllBytes(originalLogo, Convert.FromBase64String(Logo));
        Store.Save(new() { PharmacyName = "صيدلية الصحة", Address = "بغداد — شارع فلسطين",
            FooterMessage = "نتمنى لكم الشفاء\nشكراً لزيارتكم", LogoBase64 = Convert.ToBase64String(File.ReadAllBytes(originalLogo)), ShowAddress = false });
        File.Delete(originalLogo);
        var restored = Store.Load();
        restored.PharmacyName.Should().Be("صيدلية الصحة"); restored.Address.Should().Be("بغداد — شارع فلسطين");
        restored.FooterMessage.Should().Be("نتمنى لكم الشفاء\nشكراً لزيارتكم");
        restored.LogoBase64.Should().Be(Logo); restored.ShowAddress.Should().BeFalse();
        Directory.GetFiles(_directory, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public void InvalidSave_DoesNotReplacePreviouslySavedSettings()
    {
        Store.Save(new() { PharmacyName = "الصيدلية الأصلية" });
        var save = () => Store.Save(new() { PharmacyName = "اسم بديل", LogoBase64 = "invalid image data" });
        save.Should().Throw<InvalidOperationException>();
        Store.Load().PharmacyName.Should().Be("الصيدلية الأصلية");
        Directory.GetFiles(_directory, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public void CorruptSettings_ReportFailureRatherThanSilentlyUsingOtherBranding()
    {
        Store.Save(new() { PharmacyName = "الصيدلية" });
        File.WriteAllText(Path.Combine(_directory, "receipt-settings.json"), "broken json");
        var load = () => Store.Load();
        load.Should().Throw<System.Text.Json.JsonException>();
        var viewModel = ViewModel();
        viewModel.Initialize(); viewModel.StatusMessage.Should().Contain("تعذر تحميل");
        viewModel.PharmacyName = "صيدلية الشفاء"; viewModel.Save();
        Store.Load().PharmacyName.Should().Be("صيدلية الشفاء");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Receipt_RespectsBrandingVisibilityAndKeepsSeparateProductSections(bool visible)
    {
        var invoice = Invoice(PaymentMethod.Cash);
        var settings = new ReceiptSettings { PharmacyName = "صيدلية الشفاء", Address = "بغداد",
            FooterMessage = "دوام الصحة والعافية", LogoBase64 = Logo, ShowPharmacyName = visible,
            ShowAddress = visible, ShowLogo = visible, ShowFooterMessage = visible };
        var content = ReceiptContentBuilder.Build(invoice, settings);
        content.Header.Any(t => t.Value == settings.PharmacyName).Should().Be(visible);
        content.Header.Any(t => t.Value == settings.Address).Should().Be(visible);
        (content.LogoBase64 is not null).Should().Be(visible);
        content.Footer.Any(t => t.Value == settings.FooterMessage).Should().Be(visible);
        content.Header.Should().Contain(t => t.Value == invoice.Header.Number);
        content.Products.Should().HaveCount(2);
        content.Products[0][0].Value.Should().Be("منتج أول");
        content.Products[1][0].Value.Should().Be("منتج ثانٍ");
        content.Products[0].Should().Contain(t => t.Value.StartsWith("خصم الصنف:"));
        content.Totals.Should().Contain(t => t.Value == $"الإجمالي: {2000:N2}" && t.Bold);
    }

    [Fact]
    public void CreditReceipt_UsesActualInitialPaymentInsteadOfTenderedMetadata()
    {
        var invoice = Invoice(PaymentMethod.Credit);
        invoice.Header.InitialPaymentAmount = 1000; invoice.Header.AmountTendered = 1900;
        var content = ReceiptContentBuilder.Build(invoice, new());
        content.Totals.Should().Contain(t => t.Value == $"المستلم عند البيع: {1000:N2}");
        content.Totals.Should().Contain(t => t.Value == $"المتبقي على العميل عند البيع: {1000:N2}");
        content.Totals.Should().Contain(t => t.Value == "طريقة الدفع: آجل");
    }

    [Theory]
    [InlineData(PaymentMethod.Cash, "نقدي")]
    [InlineData(PaymentMethod.Card, "بطاقة")]
    public void PaidReceipt_DoesNotShowCustomerDebt(PaymentMethod method, string label)
    {
        var content = ReceiptContentBuilder.Build(Invoice(method), new());
        content.Totals.Should().Contain(t => t.Value == "طريقة الدفع: " + label);
        content.Totals.Should().Contain(t => t.Value == $"المستلم: {2000:N2}");
        content.Totals.Should().NotContain(t => t.Value.Contains("المتبقي على العميل"));
    }

    [Fact]
    public void SettingsViewModel_ChangesOnlyPreviewUntilSavedAndRetainsHiddenValues()
    {
        Store.Save(new() { PharmacyName = "اسم محفوظ", FooterMessage = "شكراً", LogoBase64 = Logo });
        var vm = ViewModel(); vm.Initialize();
        vm.PharmacyName = "اسم جديد"; vm.ShowLogo = false; vm.ShowFooterMessage = false;
        vm.HasUnsavedChanges.Should().BeTrue(); Store.Load().PharmacyName.Should().Be("اسم محفوظ");
        ReceiptContentBuilder.Build(Invoice(PaymentMethod.Cash), vm.Snapshot()).LogoBase64.Should().BeNull();
        vm.Save(); vm.HasUnsavedChanges.Should().BeFalse();
        var restored = Store.Load(); restored.PharmacyName.Should().Be("اسم جديد");
        restored.LogoBase64.Should().Be(Logo); restored.FooterMessage.Should().Be("شكراً");
        restored.ShowLogo.Should().BeFalse(); restored.ShowFooterMessage.Should().BeFalse();
        vm.RemoveLogoCommand.Execute(null); vm.HasLogo.Should().BeFalse();
        vm.Save(); Store.Load().LogoBase64.Should().BeNull();
    }

    [Fact]
    public void SettingsViewModel_RejectsBlankDisplayedNameWithoutOverwritingSavedData()
    {
        Store.Save(new() { PharmacyName = "اسم محفوظ" });
        var vm = ViewModel(); vm.Initialize(); vm.PharmacyName = "   "; vm.Save();
        vm.StatusMessage.Should().Contain("أدخل اسم الصيدلية");
        vm.HasUnsavedChanges.Should().BeTrue(); Store.Load().PharmacyName.Should().Be("اسم محفوظ");
    }

    [Fact]
    public void SettingsViewModel_RequiresPosOrBranchPermissionEvenForDirectSaveCalls()
    {
        Store.Save(new() { PharmacyName = "اسم محفوظ" });
        var currentUser = new PharmacyERP.Infrastructure.Identity.CurrentUserService();
        var session = new SessionService(currentUser);
        session.Start(new LoginResultDto { UserId = 1, BranchName = "فرع", Permissions = new() { "Reports.View" } });
        var vm = new ReceiptSettingsViewModel(Store, currentUser, session); vm.Initialize();
        vm.CanManage.Should().BeFalse(); vm.SaveCommand.CanExecute(null).Should().BeFalse();
        vm.PharmacyName = "اسم آخر"; vm.SetLogo(Logo); vm.Save();
        vm.HasLogo.Should().BeFalse(); vm.StatusMessage.Should().Contain("صلاحية");
        Store.Load().PharmacyName.Should().Be("اسم محفوظ");
    }

    private ReceiptSettingsViewModel ViewModel()
    {
        var user = new FakeCurrentUserService(); var session = new SessionService(user);
        session.Start(new LoginResultDto { UserId = 1, BranchName = "فرع الاختبار", Permissions = new() { "Sales.UsePos" } });
        return new(Store, user, session);
    }

    private static SalesInvoiceDetailDto Invoice(PaymentMethod method) => new()
    {
        Header = new() { Number = "SI-TEST", BranchName = "الفرع الرئيسي", CashierName = "الصيدلاني",
            SaleAtUtc = new(2026, 10, 7, 10, 0, 0, DateTimeKind.Utc), SubTotal = 2000,
            TotalAmount = 2000, AmountTendered = method == PaymentMethod.Credit ? 0 : 2000, PaymentMethod = method },
        Lines = new()
        {
            new() { ItemName = "منتج أول", Quantity = 1, UnitPrice = 1100, DiscountAmount = 100, LineTotal = 1000 },
            new() { ItemName = "منتج ثانٍ", Quantity = 2, UnitPrice = 500, LineTotal = 1000 }
        }
    };
}
