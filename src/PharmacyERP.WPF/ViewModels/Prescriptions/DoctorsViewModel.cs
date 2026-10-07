using System.Collections.ObjectModel;
using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.Application.Features.Prescriptions;
using PharmacyERP.Application.Features.Prescriptions.DTOs;
using PharmacyERP.WPF.MVVM;
using PharmacyERP.WPF.Services;
using PharmacyERP.WPF.Views.Prescriptions;

namespace PharmacyERP.WPF.ViewModels.Prescriptions;

public class DoctorsViewModel : ViewModelBase
{
    private readonly IPrescriptionService _prescriptionService;
    private readonly IDialogService _dialogService;
    private readonly ICurrentUserService _currentUserService;

    private DoctorDto? _selectedDoctor;
    private bool _isBusy;

    public DoctorsViewModel(IPrescriptionService prescriptionService, IDialogService dialogService, ICurrentUserService currentUserService)
    {
        _prescriptionService = prescriptionService;
        _dialogService = dialogService;
        _currentUserService = currentUserService;

        Doctors = new ObservableCollection<DoctorDto>();

        RefreshCommand = new AsyncRelayCommand(LoadDoctorsAsync);
        AddDoctorCommand = new AsyncRelayCommand(AddDoctorAsync, () => CanManage);
        EditDoctorCommand = new AsyncRelayCommand(EditDoctorAsync, () => CanManage && SelectedDoctor is not null);
        ToggleStatusCommand = new AsyncRelayCommand(ToggleStatusAsync, () => CanManage && SelectedDoctor is not null);
    }

    public bool CanManage => _currentUserService.HasPermission("Prescriptions.Manage");

    public ObservableCollection<DoctorDto> Doctors { get; }

    public DoctorDto? SelectedDoctor
    {
        get => _selectedDoctor;
        set { if (SetProperty(ref _selectedDoctor, value)) System.Windows.Input.CommandManager.InvalidateRequerySuggested(); }
    }

    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand AddDoctorCommand { get; }
    public AsyncRelayCommand EditDoctorCommand { get; }
    public AsyncRelayCommand ToggleStatusCommand { get; }

    public async Task InitializeAsync() => await LoadDoctorsAsync();

    private async Task LoadDoctorsAsync()
    {
        IsBusy = true;
        try
        {
            var doctors = await _prescriptionService.GetDoctorsAsync();
            Doctors.Clear();
            foreach (var d in doctors) Doctors.Add(d);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task AddDoctorAsync()
    {
        var window = _dialogService.CreateDialog<DoctorEditDialog>();
        var vm = (DoctorEditViewModel)window.DataContext;
        vm.LoadForCreate();

        if (_dialogService.ShowDialog(window) == true && vm.SavedSuccessfully)
            await LoadDoctorsAsync();
    }

    private async Task EditDoctorAsync()
    {
        if (SelectedDoctor is null) return;

        var window = _dialogService.CreateDialog<DoctorEditDialog>();
        var vm = (DoctorEditViewModel)window.DataContext;
        await vm.LoadForEditAsync(SelectedDoctor.Id);

        if (_dialogService.ShowDialog(window) == true && vm.SavedSuccessfully)
            await LoadDoctorsAsync();
    }

    private async Task ToggleStatusAsync()
    {
        if (SelectedDoctor is null) return;

        var activate = !SelectedDoctor.IsActive;
        var message = activate ? $"هل تريد تفعيل الطبيب '{SelectedDoctor.FullName}'؟" : $"هل تريد تعطيل الطبيب '{SelectedDoctor.FullName}'؟";
        if (!_dialogService.Confirm(message)) return;

        var result = await _prescriptionService.SetDoctorActiveStatusAsync(SelectedDoctor.Id, activate);
        if (!result.Succeeded)
        {
            _dialogService.ShowError(result.Errors.FirstOrDefault() ?? "تعذر تنفيذ العملية.");
            return;
        }

        await LoadDoctorsAsync();
    }
}
