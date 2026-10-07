using PharmacyERP.Domain.Enums;

namespace PharmacyERP.Application.Features.Prescriptions.DTOs;

public class PrescriptionDto
{
    public int Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string DoctorName { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
    public DateTime PrescriptionDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public PrescriptionStatus Status { get; set; }
    public string? Notes { get; set; }
    public int LineCount { get; set; }
}

public class PrescriptionLineDto
{
    public int Id { get; set; }
    public int ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public int QuantityPrescribed { get; set; }
    public int QuantityDispensed { get; set; }
    public int QuantityRemaining { get; set; }
    public string? DosageInstructions { get; set; }
}

public class PrescriptionDetailDto
{
    public PrescriptionDto Header { get; set; } = null!;
    public List<PrescriptionLineDto> Lines { get; set; } = new();
}

public class PrescriptionLineInputDto
{
    public int ItemId { get; set; }
    public int QuantityPrescribed { get; set; }
    public string? DosageInstructions { get; set; }
}

public class PrescriptionUpsertDto
{
    public int? Id { get; set; }
    public int CustomerId { get; set; }
    public int DoctorId { get; set; }
    public int BranchId { get; set; }
    public DateTime PrescriptionDate { get; set; } = DateTime.Today;
    public DateTime? ExpiryDate { get; set; }
    public string? Notes { get; set; }
    public List<PrescriptionLineInputDto> Lines { get; set; } = new();
}

/// <summary>Lightweight projection used by the POS screen to list a customer's fillable prescriptions and check item coverage.</summary>
public class ActivePrescriptionSummaryDto
{
    public int Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public DateTime PrescriptionDate { get; set; }
    public PrescriptionStatus Status { get; set; }
    public List<PrescriptionLineDto> Lines { get; set; } = new();
}
