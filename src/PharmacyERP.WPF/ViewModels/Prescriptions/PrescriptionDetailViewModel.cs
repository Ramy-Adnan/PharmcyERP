using System.Collections.ObjectModel;
using PharmacyERP.Application.Features.Prescriptions;
using PharmacyERP.Application.Features.Prescriptions.DTOs;
using PharmacyERP.WPF.MVVM;

namespace PharmacyERP.WPF.ViewModels.Prescriptions;

/// <summary>Read-only view of a prescription's lines and dispensing progress, with a cancel action.</summary>
public class PrescriptionDetailViewModel : ViewModelBase
{
    private readonly IPrescriptionService _prescriptionService;

    private PrescriptionDto? _header;
    private string _errorMessage = string.Empty;
    private bool _isBusy;

    public PrescriptionDetailViewModel(IPrescriptionService prescriptionService)
    {
        _prescriptionService = prescriptionService;
        Lines = new ObservableCollection<PrescriptionLineDto>();
        CancelCommand = new AsyncRelayCommand(CancelAsync, () => !IsBusy);
    }

    public PrescriptionDto? Header { get => _header; private set => SetProperty(ref _header, value); }
    public ObservableCollection<PrescriptionLineDto> Lines { get; }

    public string ErrorMessage { get => _errorMessage; set => SetProperty(ref _errorMessage, value); }
    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public AsyncRelayCommand CancelCommand { get; }
    public bool WasCancelled { get; private set; }

    public async Task LoadAsync(int prescriptionId)
    {
        var detail = await _prescriptionService.GetPrescriptionDetailAsync(prescriptionId);
        if (detail is null) return;

        Header = detail.Header;
        Lines.Clear();
        foreach (var line in detail.Lines) Lines.Add(line);
    }

    private async Task CancelAsync()
    {
        if (Header is null) return;

        IsBusy = true;
        try
        {
            var result = await _prescriptionService.CancelPrescriptionAsync(Header.Id);
            if (!result.Succeeded)
            {
                ErrorMessage = result.Errors.FirstOrDefault() ?? "تعذر إلغاء الوصفة الطبية.";
                return;
            }

            WasCancelled = true;
            await LoadAsync(Header.Id);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
