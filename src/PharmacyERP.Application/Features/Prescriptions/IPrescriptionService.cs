using PharmacyERP.Application.Common.Models;
using PharmacyERP.Application.Features.Prescriptions.DTOs;

namespace PharmacyERP.Application.Features.Prescriptions;

/// <summary>
/// Manages the physician registry and medical prescriptions. Fulfillment
/// (marking prescription lines dispensed) happens as a side effect of
/// ISalesService.CheckoutAsync when a sale is linked to a prescription — this
/// service exposes the read/write surface for the prescription record itself
/// plus the lookup POS needs to find a customer's fillable prescriptions.
/// </summary>
public interface IPrescriptionService
{
    // Doctors
    Task<List<DoctorDto>> GetDoctorsAsync(bool includeInactive = true, CancellationToken cancellationToken = default);
    Task<DoctorUpsertDto?> GetDoctorForEditAsync(int id, CancellationToken cancellationToken = default);
    Task<Result<DoctorDto>> CreateDoctorAsync(DoctorUpsertDto dto, CancellationToken cancellationToken = default);
    Task<Result<DoctorDto>> UpdateDoctorAsync(DoctorUpsertDto dto, CancellationToken cancellationToken = default);
    Task<Result> SetDoctorActiveStatusAsync(int doctorId, bool isActive, CancellationToken cancellationToken = default);

    // Prescriptions
    Task<List<PrescriptionDto>> GetPrescriptionsAsync(CancellationToken cancellationToken = default);
    Task<PrescriptionDetailDto?> GetPrescriptionDetailAsync(int id, CancellationToken cancellationToken = default);
    Task<Result<PrescriptionDto>> CreatePrescriptionAsync(PrescriptionUpsertDto dto, CancellationToken cancellationToken = default);
    Task<Result> CancelPrescriptionAsync(int prescriptionId, CancellationToken cancellationToken = default);

    /// <summary>Active/PartiallyFulfilled, non-expired prescriptions for a customer, used by the POS screen to let the cashier link a sale to one.</summary>
    Task<List<ActivePrescriptionSummaryDto>> GetFillablePrescriptionsForCustomerAsync(int customerId, CancellationToken cancellationToken = default);
}
