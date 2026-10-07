using System.Collections.ObjectModel;
using PharmacyERP.Application.Features.Accounting;
using PharmacyERP.Application.Features.Accounting.DTOs;
using PharmacyERP.WPF.MVVM;

namespace PharmacyERP.WPF.ViewModels.Accounting;

/// <summary>Read-only view of a journal entry's debit/credit lines — every entry (auto-posted or manual) is immutable once created.</summary>
public class JournalEntryDetailViewModel : ViewModelBase
{
    private readonly IAccountingService _accountingService;

    private JournalEntryDto? _header;

    public JournalEntryDetailViewModel(IAccountingService accountingService)
    {
        _accountingService = accountingService;
        Lines = new ObservableCollection<JournalEntryLineDto>();
    }

    public JournalEntryDto? Header { get => _header; private set => SetProperty(ref _header, value); }
    public ObservableCollection<JournalEntryLineDto> Lines { get; }

    public async Task LoadAsync(int journalEntryId)
    {
        var detail = await _accountingService.GetJournalEntryDetailAsync(journalEntryId);
        if (detail is null) return;

        Header = detail.Header;
        Lines.Clear();
        foreach (var line in detail.Lines) Lines.Add(line);
    }
}
