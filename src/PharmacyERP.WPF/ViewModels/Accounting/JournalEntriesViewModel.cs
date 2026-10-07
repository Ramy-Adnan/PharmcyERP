using System.Collections.ObjectModel;
using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.Application.Features.Accounting;
using PharmacyERP.Application.Features.Accounting.DTOs;
using PharmacyERP.WPF.MVVM;
using PharmacyERP.WPF.Services;
using PharmacyERP.WPF.Views.Accounting;

namespace PharmacyERP.WPF.ViewModels.Accounting;

public class JournalEntriesViewModel : ViewModelBase
{
    private readonly IAccountingService _accountingService;
    private readonly IDialogService _dialogService;
    private readonly ICurrentUserService _currentUserService;

    private JournalEntryDto? _selectedEntry;
    private bool _isBusy;

    public JournalEntriesViewModel(IAccountingService accountingService, IDialogService dialogService, ICurrentUserService currentUserService)
    {
        _accountingService = accountingService;
        _dialogService = dialogService;
        _currentUserService = currentUserService;

        Entries = new ObservableCollection<JournalEntryDto>();

        RefreshCommand = new AsyncRelayCommand(LoadEntriesAsync);
        AddManualEntryCommand = new AsyncRelayCommand(AddManualEntryAsync, () => CanManage);
        OpenDetailCommand = new AsyncRelayCommand(OpenDetailAsync, () => SelectedEntry is not null);
    }

    public bool CanManage => _currentUserService.HasPermission("Accounting.ManageJournalEntries");

    public ObservableCollection<JournalEntryDto> Entries { get; }

    public JournalEntryDto? SelectedEntry
    {
        get => _selectedEntry;
        set { if (SetProperty(ref _selectedEntry, value)) System.Windows.Input.CommandManager.InvalidateRequerySuggested(); }
    }

    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand AddManualEntryCommand { get; }
    public AsyncRelayCommand OpenDetailCommand { get; }

    public async Task InitializeAsync() => await LoadEntriesAsync();

    private async Task LoadEntriesAsync()
    {
        IsBusy = true;
        try
        {
            var entries = await _accountingService.GetJournalEntriesAsync();
            Entries.Clear();
            foreach (var e in entries) Entries.Add(e);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task AddManualEntryAsync()
    {
        var window = _dialogService.CreateDialog<ManualJournalEntryDialog>();
        var vm = (ManualJournalEntryViewModel)window.DataContext;
        await vm.LoadAsync();

        if (_dialogService.ShowDialog(window) == true && vm.SavedSuccessfully)
            await LoadEntriesAsync();
    }

    private async Task OpenDetailAsync()
    {
        if (SelectedEntry is null) return;

        var window = _dialogService.CreateDialog<JournalEntryDetailDialog>();
        var vm = (JournalEntryDetailViewModel)window.DataContext;
        await vm.LoadAsync(SelectedEntry.Id);

        _dialogService.ShowDialog(window);
    }
}
