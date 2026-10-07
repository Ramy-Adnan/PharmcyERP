using System.Collections.ObjectModel;
using PharmacyERP.Application.Features.Insurance;
using PharmacyERP.Application.Features.Insurance.DTOs;
using PharmacyERP.Application.Features.Sales;
using PharmacyERP.Application.Features.Sales.DTOs;
using PharmacyERP.WPF.MVVM;

namespace PharmacyERP.WPF.ViewModels.Insurance;

public class InsurancePolicyEditViewModel : ViewModelBase
{
    private readonly IInsuranceService _insuranceService;
    private readonly ISalesService _salesService;

    private int? _id;
    private int _customerId;
    private int _insuranceCompanyId;
    private string _policyNumber = string.Empty;
    private decimal _coveragePercent;
    private DateTime _startDate = DateTime.Today;
    private DateTime? _endDate;
    private bool _isActive = true;
    private string _errorMessage = string.Empty;
    private bool _isBusy;

    public InsurancePolicyEditViewModel(IInsuranceService insuranceService, ISalesService salesService)
    {
        _insuranceService = insuranceService;
        _salesService = salesService;

        Customers = new ObservableCollection<CustomerDto>();
        Companies = new ObservableCollection<InsuranceCompanyDto>();

        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);
    }

    public bool IsEditMode => _id.HasValue;
    public string DialogTitle => IsEditMode ? "تعديل بوليصة التأمين" : "إضافة بوليصة تأمين جديدة";

    public ObservableCollection<CustomerDto> Customers { get; }
    public ObservableCollection<InsuranceCompanyDto> Companies { get; }

    public int CustomerId { get => _customerId; set => SetProperty(ref _customerId, value); }
    public int InsuranceCompanyId { get => _insuranceCompanyId; set => SetProperty(ref _insuranceCompanyId, value); }
    public string PolicyNumber { get => _policyNumber; set => SetProperty(ref _policyNumber, value); }
    public decimal CoveragePercent { get => _coveragePercent; set => SetProperty(ref _coveragePercent, value); }
    public DateTime StartDate { get => _startDate; set => SetProperty(ref _startDate, value); }
    public DateTime? EndDate { get => _endDate; set => SetProperty(ref _endDate, value); }
    public bool IsActive { get => _isActive; set => SetProperty(ref _isActive, value); }

    public string ErrorMessage { get => _errorMessage; set => SetProperty(ref _errorMessage, value); }
    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public AsyncRelayCommand SaveCommand { get; }
    public bool SavedSuccessfully { get; private set; }
    public event Action? RequestClose;

    public async Task LoadForCreateAsync()
    {
        _id = null;
        IsActive = true;
        await LoadLookupsAsync();
    }

    public async Task LoadForEditAsync(int id)
    {
        await LoadLookupsAsync();

        var dto = await _insuranceService.GetPolicyForEditAsync(id);
        if (dto is null) return;

        _id = dto.Id;
        CustomerId = dto.CustomerId;
        InsuranceCompanyId = dto.InsuranceCompanyId;
        PolicyNumber = dto.PolicyNumber;
        CoveragePercent = dto.CoveragePercent;
        StartDate = dto.StartDate;
        EndDate = dto.EndDate;
        IsActive = dto.IsActive;

        OnPropertyChanged(nameof(IsEditMode));
        OnPropertyChanged(nameof(DialogTitle));
    }

    private async Task LoadLookupsAsync()
    {
        var customers = await _salesService.GetCustomersAsync();
        Customers.Clear();
        foreach (var c in customers.Where(c => c.IsActive)) Customers.Add(c);

        var companies = await _insuranceService.GetCompaniesAsync();
        Companies.Clear();
        foreach (var c in companies.Where(c => c.IsActive)) Companies.Add(c);
    }

    private async Task SaveAsync()
    {
        ErrorMessage = string.Empty;
        IsBusy = true;
        try
        {
            var dto = new InsurancePolicyUpsertDto
            {
                Id = _id,
                CustomerId = CustomerId,
                InsuranceCompanyId = InsuranceCompanyId,
                PolicyNumber = PolicyNumber,
                CoveragePercent = CoveragePercent,
                StartDate = StartDate,
                EndDate = EndDate,
                IsActive = IsActive
            };

            var result = _id.HasValue
                ? await _insuranceService.UpdatePolicyAsync(dto)
                : await _insuranceService.CreatePolicyAsync(dto);

            if (!result.Succeeded)
            {
                ErrorMessage = result.Errors.FirstOrDefault() ?? "تعذر حفظ بوليصة التأمين.";
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
