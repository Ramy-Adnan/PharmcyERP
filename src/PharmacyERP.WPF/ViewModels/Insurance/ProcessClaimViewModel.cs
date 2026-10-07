using System.Collections.ObjectModel;
using PharmacyERP.Application.Features.Insurance;
using PharmacyERP.Application.Features.Insurance.DTOs;
using PharmacyERP.Domain.Enums;
using PharmacyERP.WPF.MVVM;

namespace PharmacyERP.WPF.ViewModels.Insurance;

/// <summary>ViewModel for advancing a claim's status (Submitted → UnderReview → Approved/Rejected → Paid).</summary>
public class ProcessClaimViewModel : ViewModelBase
{
    private readonly IInsuranceService _insuranceService;

    private InsuranceClaimDto? _claim;
    private InsuranceClaimStatus _newStatus;
    private decimal? _approvedAmount;
    private string? _notes;
    private string _errorMessage = string.Empty;
    private bool _isBusy;

    public ProcessClaimViewModel(IInsuranceService insuranceService)
    {
        _insuranceService = insuranceService;

        AvailableStatuses = new ObservableCollection<InsuranceClaimStatus>();
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);
    }

    public InsuranceClaimDto? Claim { get => _claim; private set => SetProperty(ref _claim, value); }
    public ObservableCollection<InsuranceClaimStatus> AvailableStatuses { get; }

    public InsuranceClaimStatus NewStatus { get => _newStatus; set => SetProperty(ref _newStatus, value); }
    public decimal? ApprovedAmount { get => _approvedAmount; set => SetProperty(ref _approvedAmount, value); }
    public string? Notes { get => _notes; set => SetProperty(ref _notes, value); }

    public string ErrorMessage { get => _errorMessage; set => SetProperty(ref _errorMessage, value); }
    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public AsyncRelayCommand SaveCommand { get; }
    public bool SavedSuccessfully { get; private set; }
    public event Action? RequestClose;

    public void Load(InsuranceClaimDto claim)
    {
        Claim = claim;
        ApprovedAmount = claim.ApprovedAmount ?? claim.ClaimedAmount;

        AvailableStatuses.Clear();
        // Only forward transitions are offered — a claim's lifecycle does not go backwards.
        foreach (var status in new[] { InsuranceClaimStatus.UnderReview, InsuranceClaimStatus.Approved, InsuranceClaimStatus.Rejected, InsuranceClaimStatus.Paid })
        {
            if ((int)status > (int)claim.Status || status == claim.Status)
                AvailableStatuses.Add(status);
        }
        NewStatus = AvailableStatuses.FirstOrDefault();
    }

    private async Task SaveAsync()
    {
        ErrorMessage = string.Empty;
        if (Claim is null) return;

        IsBusy = true;
        try
        {
            var dto = new ProcessClaimDto
            {
                ClaimId = Claim.Id,
                NewStatus = NewStatus,
                ApprovedAmount = ApprovedAmount,
                Notes = Notes
            };

            var result = await _insuranceService.ProcessClaimAsync(dto);
            if (!result.Succeeded)
            {
                ErrorMessage = result.Errors.FirstOrDefault() ?? "تعذر تحديث حالة المطالبة.";
                return;
            }

            SavedSuccessfully = true;
            RequestClose?.Invoke();
        }
        finally
        {
            IsBusy = false;
        }
    }
}
