using PharmacyERP.WPF.MVVM;

namespace PharmacyERP.WPF.ViewModels.Accounting;

/// <summary>One editable line in the manual Journal Entry editor's line grid.</summary>
public class JournalEntryLineRow : ViewModelBase
{
    private int _accountId;
    private string _accountName = string.Empty;
    private decimal _debitAmount;
    private decimal _creditAmount;
    private string? _description;

    public int AccountId { get => _accountId; set => SetProperty(ref _accountId, value); }
    public string AccountName { get => _accountName; set => SetProperty(ref _accountName, value); }

    public decimal DebitAmount
    {
        get => _debitAmount;
        set { if (SetProperty(ref _debitAmount, value) && value > 0) CreditAmount = 0; }
    }

    public decimal CreditAmount
    {
        get => _creditAmount;
        set { if (SetProperty(ref _creditAmount, value) && value > 0) DebitAmount = 0; }
    }

    public string? Description { get => _description; set => SetProperty(ref _description, value); }
}
