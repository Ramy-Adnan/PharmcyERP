using FluentAssertions;
using PharmacyERP.Application.Features.Updates;
using PharmacyERP.WPF.ViewModels.SystemManagement;
using Xunit;

namespace PharmacyERP.Application.Tests;

public sealed class UpdateStatusViewModelTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("تعذر الوصول إلى خادم التحديث", false)]
    public async Task OnlyACompletedSuccessfulCheck_CanReportTheInstalledVersionIsCurrent(string? error, bool expected)
    {
        var model = new UpdateCheckViewModel(new UpdateStub(error));
        await model.CheckAsync();
        model.IsUpToDate.Should().Be(expected);
        model.ErrorMessage.Should().Be(error ?? string.Empty);
        model.IsChecking.Should().BeFalse();
    }

    private sealed class UpdateStub(string? error) : IUpdateService
    {
        public Task<UpdateCheckResultDto> CheckForUpdateAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new UpdateCheckResultDto { CurrentVersion = "1.1.0", ErrorMessage = error });

        public Task DownloadAndLaunchInstallerAsync(string downloadUrl, IProgress<int>? progress = null,
            CancellationToken cancellationToken = default, string? expectedSha256 = null) => throw new NotSupportedException();
    }
}
