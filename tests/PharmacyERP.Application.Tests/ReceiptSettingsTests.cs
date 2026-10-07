using FluentAssertions;
using PharmacyERP.Application.Features.Auth.DTOs;
using PharmacyERP.Application.Features.Sales.DTOs;
using PharmacyERP.Application.Tests.Common;
using PharmacyERP.Domain.Enums;
using PharmacyERP.WPF.Services;
using PharmacyERP.WPF.ViewModels;
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
        content.Products[0].Should().ContainSingle(); content.Products[1].Should().ContainSingle();
        content.Products[0][0].Value.Should().Be("منتج أول  —  الكمية: 1");
        content.Products[1][0].Value.Should().Be("منتج ثانٍ  —  الكمية: 2");
        content.Totals.Should().HaveCount(3);
        content.Totals.Should().Contain(t => t.Value == $"الإجمالي: {2000:N2}" && t.Bold);
    }

    [Fact]
    public void CreditReceipt_UsesActualInitialPaymentInsteadOfTenderedMetadata()
    {
        var invoice = Invoice(PaymentMethod.Credit);
        invoice.Header.InitialPaymentAmount = 1000; invoice.Header.AmountTendered = 1900;
        var content = ReceiptContentBuilder.Build(invoice, new());
        content.Totals.Should().Contain(t => t.Value == $"المستلم: {1000:N2}");
        content.Totals.Should().Contain(t => t.Value == $"الباقي: {1000:N2}");
        content.Totals.Should().HaveCount(3);
    }

    [Theory]
    [InlineData(PaymentMethod.Cash)]
    [InlineData(PaymentMethod.Card)]
    public void PaidReceipt_ShowsReceivedAmountAndZeroRemaining(PaymentMethod method)
    {
        var content = ReceiptContentBuilder.Build(Invoice(method), new());
        content.Totals.Should().Contain(t => t.Value == $"المستلم: {2000:N2}");
        content.Totals.Should().Contain(t => t.Value == $"الباقي: {0:N2}");
        content.Totals.Should().HaveCount(3);
    }

    [Fact]
    public void CompactReceipt_HidesDetailWithoutChangingTotalIncludingTaxAndDiscount()
    {
        var invoice = Invoice(PaymentMethod.Cash);
        invoice.Header.TaxAmount = 200; invoice.Header.DiscountAmount = 100;
        invoice.Header.TotalAmount = 2100; invoice.Header.AmountTendered = 2500; invoice.Header.ChangeGiven = 400;
        var content = ReceiptContentBuilder.Build(invoice, new());
        content.Totals.Select(t => t.Value).Should().Equal($"الإجمالي: {2100:N2}", $"المستلم: {2500:N2}", $"الباقي: {400:N2}");
        var printed = content.Header.Concat(content.Products.SelectMany(p => p)).Concat(content.Totals).Concat(content.Footer);
        printed.Should().NotContain(t => t.Value.Contains("الضريبة") || t.Value.Contains("الخصم")
            || t.Value.Contains("السعر") || t.Value.Contains("طريقة الدفع") || t.Value.Contains("الموظف") || t.Value.Contains("العميل"));
        invoice.Header.TotalAmount.Should().Be(2100); invoice.Header.TaxAmount.Should().Be(200);
    }

    [Fact]
    public void SettingsViewModel_ChangesOnlyPreviewUntilSavedAndRetainsHiddenValues()
    {
        Store.Save(new() { PharmacyName = "اسم محفوظ", FooterMessage = "شكراً", LogoBase64 = Logo });
        var vm = ViewModel(); vm.Initialize();
        var confirmations = new List<ReceiptSettingsSaveResult>();
        vm.SaveCompleted += (_, result) => confirmations.Add(result);
        vm.PharmacyName = "اسم جديد"; vm.ShowLogo = false; vm.ShowFooterMessage = false;
        vm.HasUnsavedChanges.Should().BeTrue(); Store.Load().PharmacyName.Should().Be("اسم محفوظ");
        ReceiptContentBuilder.Build(Invoice(PaymentMethod.Cash), vm.Snapshot()).LogoBase64.Should().BeNull();
        vm.Save(); vm.HasUnsavedChanges.Should().BeFalse();
        confirmations.Should().ContainSingle(); confirmations[0].Succeeded.Should().BeTrue();
        confirmations[0].Message.Should().Contain("بنجاح");
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
        var vm = ViewModel(); vm.Initialize();
        var confirmations = new List<ReceiptSettingsSaveResult>();
        vm.SaveCompleted += (_, result) => confirmations.Add(result);
        vm.PharmacyName = "   "; vm.Save();
        confirmations.Should().ContainSingle(); confirmations[0].Succeeded.Should().BeFalse();
        confirmations[0].Message.Should().Contain("أدخل اسم الصيدلية");
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

    [Fact]
    public void Header_UsesSavedIdentityAndUpdatesOnlyAfterSuccessfulSaveIncludingLogoRemoval()
    {
        var store = Store;
        store.Save(new() { PharmacyName = "صيدلية قديمة", LogoBase64 = Logo, ShowLogo = false, ShowPharmacyName = false });
        var user = new FakeCurrentUserService(); var session = new SessionService(user);
        session.Start(new() { UserId = 1, BranchName = "الفرع", Permissions = new() { "Sales.UsePos" } });
        using var branding = new PharmacyBrandingViewModel(store, session); branding.Activate();
        branding.PharmacyName.Should().Be("صيدلية قديمة"); branding.LogoBase64.Should().Be(Logo);
        var editor = new ReceiptSettingsViewModel(store, user, session); editor.Initialize();
        editor.PharmacyName = "صيدلية جديدة"; editor.SetLogo(null);
        branding.PharmacyName.Should().Be("صيدلية قديمة"); branding.LogoBase64.Should().Be(Logo);
        editor.Save();
        branding.PharmacyName.Should().Be("صيدلية جديدة"); branding.LogoBase64.Should().BeNull();
        editor.ShowPharmacyName = true; editor.PharmacyName = "   "; editor.Save();
        branding.PharmacyName.Should().Be("صيدلية جديدة");
        store.Load().PharmacyName.Should().Be("صيدلية جديدة");
    }

    [Fact]
    public void Header_RefreshesOnOpeningAndUnsubscribesWhenClosed()
    {
        var store = Store;
        var session = new SessionService(new FakeCurrentUserService());
        session.Start(new() { BranchName = "الفرع المسجل" });
        using var branding = new PharmacyBrandingViewModel(store, session); branding.Activate();
        branding.PharmacyName.Should().Be("الفرع المسجل"); branding.LogoBase64.Should().BeNull();
        store.Save(new() { PharmacyName = "صيدلية الشفاء" });
        branding.PharmacyName.Should().Be("صيدلية الشفاء");
        branding.Dispose(); store.Save(new() { PharmacyName = "اسم بعد الإغلاق" });
        branding.PharmacyName.Should().Be("صيدلية الشفاء");
        branding.Activate(); branding.PharmacyName.Should().Be("اسم بعد الإغلاق");
    }

    [Fact]
    public void Header_CorruptSettingsDoNotBlockOpeningAndSavingRepairsTheDisplayedIdentity()
    {
        var store = Store; store.Save(new() { PharmacyName = "اسم" });
        File.WriteAllText(Path.Combine(_directory, "receipt-settings.json"), "broken json");
        var session = new SessionService(new FakeCurrentUserService());
        session.Start(new() { BranchName = "الفرع المسجل" });
        using var branding = new PharmacyBrandingViewModel(store, session); branding.Activate();
        branding.PharmacyName.Should().Be("الفرع المسجل");
        store.Save(new() { PharmacyName = "الاسم الصحيح", LogoBase64 = Logo });
        branding.PharmacyName.Should().Be("الاسم الصحيح"); branding.LogoBase64.Should().Be(Logo);
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
