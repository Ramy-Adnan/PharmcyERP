using PharmacyERP.Application.Features.Hr;
using PharmacyERP.Application.Features.Hr.DTOs;
using PharmacyERP.WPF.MVVM;

namespace PharmacyERP.WPF.ViewModels.Hr;

public class ShiftEditViewModel : ViewModelBase
{
    private readonly IHrService _hrService;

    private int? _id;
    private string _name = string.Empty;
    private TimeSpan _startTime = new(9, 0, 0);
    private TimeSpan _endTime = new(17, 0, 0);
    private bool _isActive = true;
    private string _errorMessage = string.Empty;
    private bool _isBusy;

    public ShiftEditViewModel(IHrService hrService)
    {
        _hrService = hrService;
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);
    }

    public bool IsEditMode => _id.HasValue;
    public string DialogTitle => IsEditMode ? "تعديل الوردية" : "إضافة وردية جديدة";

    public string Name { get => _name; set => SetProperty(ref _name, value); }

    /// <summary>Bound as a plain string ("HH:mm") because WPF has no built-in TimeSpan editor control.</summary>
    public string StartTimeText
    {
        get => _startTime.ToString(@"hh\:mm");
        set { if (TimeSpan.TryParse(value, out var t)) SetProperty(ref _startTime, t); }
    }

    public string EndTimeText
    {
        get => _endTime.ToString(@"hh\:mm");
        set { if (TimeSpan.TryParse(value, out var t)) SetProperty(ref _endTime, t); }
    }

    public bool IsActive { get => _isActive; set => SetProperty(ref _isActive, value); }

    public string ErrorMessage { get => _errorMessage; set => SetProperty(ref _errorMessage, value); }
    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public AsyncRelayCommand SaveCommand { get; }
    public bool SavedSuccessfully { get; private set; }
    public event Action? RequestClose;

    public void LoadForCreate()
    {
        _id = null;
        IsActive = true;
    }

    public async Task LoadForEditAsync(int id)
    {
        var dto = await _hrService.GetShiftForEditAsync(id);
        if (dto is null) return;

        _id = dto.Id;
        Name = dto.Name;
        _startTime = dto.StartTime;
        _endTime = dto.EndTime;
        IsActive = dto.IsActive;

        OnPropertyChanged(nameof(StartTimeText));
        OnPropertyChanged(nameof(EndTimeText));
        OnPropertyChanged(nameof(IsEditMode));
        OnPropertyChanged(nameof(DialogTitle));
    }

    private async Task SaveAsync()
    {
        ErrorMessage = string.Empty;
        IsBusy = true;
        try
        {
            var dto = new ShiftUpsertDto
            {
                Id = _id,
                Name = Name,
                StartTime = _startTime,
                EndTime = _endTime,
                IsActive = IsActive
            };

            var result = _id.HasValue
                ? await _hrService.UpdateShiftAsync(dto)
                : await _hrService.CreateShiftAsync(dto);

            if (!result.Succeeded)
            {
                ErrorMessage = result.Errors.FirstOrDefault() ?? "تعذر حفظ الوردية.";
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
