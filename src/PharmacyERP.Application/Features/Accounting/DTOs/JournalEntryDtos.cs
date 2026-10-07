namespace PharmacyERP.Application.Features.Accounting.DTOs;

public class JournalEntryDto
{
    public int Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
    public DateTime EntryDate { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? ReferenceType { get; set; }
    public int? ReferenceId { get; set; }
    public bool IsPosted { get; set; }
    public decimal TotalDebit { get; set; }
    public decimal TotalCredit { get; set; }
}

public class JournalEntryLineDto
{
    public int Id { get; set; }
    public int AccountId { get; set; }
    public string AccountCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public decimal DebitAmount { get; set; }
    public decimal CreditAmount { get; set; }
    public string? Description { get; set; }
}

public class JournalEntryDetailDto
{
    public JournalEntryDto Header { get; set; } = null!;
    public List<JournalEntryLineDto> Lines { get; set; } = new();
}

public class JournalEntryLineInputDto
{
    public int AccountId { get; set; }
    public decimal DebitAmount { get; set; }
    public decimal CreditAmount { get; set; }
    public string? Description { get; set; }
}

public class ManualJournalEntryUpsertDto
{
    public int BranchId { get; set; }
    public DateTime EntryDate { get; set; } = DateTime.Today;
    public string Description { get; set; } = string.Empty;
    public List<JournalEntryLineInputDto> Lines { get; set; } = new();
}
