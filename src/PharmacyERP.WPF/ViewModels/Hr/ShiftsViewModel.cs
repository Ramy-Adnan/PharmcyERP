using System.Collections.ObjectModel;
using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.Application.Features.Hr;
using PharmacyERP.Application.Features.Hr.DTOs;
using PharmacyERP.WPF.MVVM;
using PharmacyERP.WPF.Services;
using PharmacyERP.WPF.Views.Hr;

namespace PharmacyERP.WPF.ViewModels.Hr;

public class ShiftsViewModel : ViewModelBase
{
    private readonly IHrService _hrService;
    private readonly IDialogService _dialogService;
    private readonly ICurrentUserService _currentUserService;

    private ShiftDto? _selectedShift;
    private bool _isBusy;

    public ShiftsViewModel(IHrService hrService, IDialogService dialogService, ICurrentUserService currentUserService)
    {
        _hrService = hrService;
        _dialogService = dialogService;
        _currentUserService = currentUserService;

        Shifts = new ObservableCollection<ShiftDto>();

        RefreshCommand = new AsyncRelayCommand(LoadShiftsAsync);
        AddShiftCommand = new AsyncRelayCommand(AddShiftAsync, () => CanManage);
        EditShiftCommand = new AsyncRelayCommand(EditShiftAsync, () => CanManage && SelectedShift is not null);
    }

    public bool CanManage => _currentUserService.HasPermission("Hr.ManageEmployees");

    public ObservableCollection<ShiftDto> Shifts { get; }

    public ShiftDto? SelectedShift
    {
        get => _selectedShift;
        set { if (SetProperty(ref _selectedShift, value)) System.Windows.Input.CommandManager.InvalidateRequerySuggested(); }
    }

    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand AddShiftCommand { get; }
    public AsyncRelayCommand EditShiftCommand { get; }

    public async Task InitializeAsync() => await LoadShiftsAsync();

    private async Task LoadShiftsAsync()
    {
        IsBusy = true;
        try
        {
            var shifts = await _hrService.GetShiftsAsync();
            Shifts.Clear();
            foreach (var s in shifts) Shifts.Add(s);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task AddShiftAsync()
    {
        var window = _dialogService.CreateDialog<ShiftEditDialog>();
        var vm = (ShiftEditViewModel)window.DataContext;
        vm.LoadForCreate();

        if (_dialogService.ShowDialog(window) == true && vm.SavedSuccessfully)
            await LoadShiftsAsync();
    }

    private async Task EditShiftAsync()
    {
        if (SelectedShift is null) return;

        var window = _dialogService.CreateDialog<ShiftEditDialog>();
        var vm = (ShiftEditViewModel)window.DataContext;
        await vm.LoadForEditAsync(SelectedShift.Id);

        if (_dialogService.ShowDialog(window) == true && vm.SavedSuccessfully)
            await LoadShiftsAsync();
    }
}
