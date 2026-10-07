using System.Collections.ObjectModel;
using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.Application.Features.Prescriptions;
using PharmacyERP.Application.Features.Prescriptions.DTOs;
using PharmacyERP.WPF.MVVM;
using PharmacyERP.WPF.Services;
using PharmacyERP.WPF.Views.Prescriptions;

namespace PharmacyERP.WPF.ViewModels.Prescriptions;

public class PrescriptionsViewModel : ViewModelBase
{
    private readonly IPrescriptionService _prescriptionService;
    private readonly IDialogService _dialogService;
    private readonly ICurrentUserService _currentUserService;

    private PrescriptionDto? _selectedPrescription;
    private bool _isBusy;

    public PrescriptionsViewModel(IPrescriptionService prescriptionService, IDialogService dialogService, ICurrentUserService currentUserService)
    {
        _prescriptionService = prescriptionService;
        _dialogService = dialogService;
        _currentUserService = currentUserService;

        Prescriptions = new ObservableCollection<PrescriptionDto>();

        RefreshCommand = new AsyncRelayCommand(LoadPrescriptionsAsync);
        AddPrescriptionCommand = new AsyncRelayCommand(AddPrescriptionAsync, () => CanManage);
        OpenDetailCommand = new AsyncRelayCommand(OpenDetailAsync, () => SelectedPrescription is not null);
    }

    public bool CanManage => _currentUserService.HasPermission("Prescriptions.Manage");

    public ObservableCollection<PrescriptionDto> Prescriptions { get; }

    public PrescriptionDto? SelectedPrescription
    {
        get => _selectedPrescription;
        set { if (SetProperty(ref _selectedPrescription, value)) System.Windows.Input.CommandManager.InvalidateRequerySuggested(); }
    }

    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand AddPrescriptionCommand { get; }
    public AsyncRelayCommand OpenDetailCommand { get; }

    public async Task InitializeAsync() => await LoadPrescriptionsAsync();

    private async Task LoadPrescriptionsAsync()
    {
        IsBusy = true;
        try
        {
            var prescriptions = await _prescriptionService.GetPrescriptionsAsync();
            Prescriptions.Clear();
            foreach (var p in prescriptions) Prescriptions.Add(p);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task AddPrescriptionAsync()
    {
        var window = _dialogService.CreateDialog<PrescriptionEditDialog>();
        var vm = (PrescriptionEditViewModel)window.DataContext;
        await vm.LoadForCreateAsync();

        if (_dialogService.ShowDialog(window) == true && vm.SavedSuccessfully)
            await LoadPrescriptionsAsync();
    }

    private async Task OpenDetailAsync()
    {
        if (SelectedPrescription is null) return;

        var window = _dialogService.CreateDialog<PrescriptionDetailDialog>();
        var vm = (PrescriptionDetailViewModel)window.DataContext;
        await vm.LoadAsync(SelectedPrescription.Id);

        _dialogService.ShowDialog(window);
        await LoadPrescriptionsAsync();
    }
}
