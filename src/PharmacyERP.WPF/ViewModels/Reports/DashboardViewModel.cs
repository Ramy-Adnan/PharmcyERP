using System.Collections.ObjectModel;
using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.Application.Features.Reports;
using PharmacyERP.Application.Features.Reports.DTOs;
using PharmacyERP.WPF.MVVM;

namespace PharmacyERP.WPF.ViewModels.Reports;

/// <summary>
/// ViewModel for the main dashboard shown right after login. Always scoped
/// to the cashier/manager's current session branch — a chain-wide rollup is
/// deliberately out of scope here since the Reports screens already offer
/// an explicit "all branches" option for anyone who needs that view.
/// </summary>
public class DashboardViewModel : ViewModelBase
{
    private readonly IReportingService _reportingService;
    private readonly ICurrentUserService _currentUserService;

    private DashboardSummaryDto? _summary;
    private bool _isBusy;

    public DashboardViewModel(IReportingService reportingService, ICurrentUserService currentUserService)
    {
        _reportingService = reportingService;
        _currentUserService = currentUserService;

        TopSellingItems = new ObservableCollection<TopSellingItemDto>();
        RefreshCommand = new AsyncRelayCommand(LoadAsync);
    }

    public DashboardSummaryDto? Summary { get => _summary; private set => SetProperty(ref _summary, value); }
    public ObservableCollection<TopSellingItemDto> TopSellingItems { get; }

    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public AsyncRelayCommand RefreshCommand { get; }

    public async Task InitializeAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            Summary = await _reportingService.GetDashboardSummaryAsync(_currentUserService.CurrentBranchId);

            TopSellingItems.Clear();
            foreach (var item in Summary.TopSellingItemsToday) TopSellingItems.Add(item);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
