using Microsoft.EntityFrameworkCore;
using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.Application.Common.Models;
using PharmacyERP.Application.Features.Accounting;
using PharmacyERP.Application.Features.Accounting.DTOs;
using PharmacyERP.Application.Features.Insurance;
using PharmacyERP.Application.Features.Insurance.DTOs;
using PharmacyERP.Domain.Entities;
using PharmacyERP.Domain.Enums;

namespace PharmacyERP.Infrastructure.Services;

public class InsuranceService : IInsuranceService
{
    private readonly IApplicationDbContext _context;
    private readonly IAccountingService _accountingService;
    private readonly IDateTime _dateTime;

    public InsuranceService(IApplicationDbContext context, IAccountingService accountingService, IDateTime dateTime)
    {
        _context = context;
        _accountingService = accountingService;
        _dateTime = dateTime;
    }

    // ===================== Insurance Companies =====================

    public async Task<List<InsuranceCompanyDto>> GetCompaniesAsync(CancellationToken cancellationToken = default)
    {
        var companies = await _context.InsuranceCompanies
            .Include(c => c.Policies)
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);

        return companies.Select(c => new InsuranceCompanyDto
        {
            Id = c.Id,
            Name = c.Name,
            ContactPhone = c.ContactPhone,
            ContactEmail = c.ContactEmail,
            IsActive = c.IsActive,
            PolicyCount = c.Policies.Count
        }).ToList();
    }

    public async Task<InsuranceCompanyUpsertDto?> GetCompanyForEditAsync(int id, CancellationToken cancellationToken = default)
    {
        var company = await _context.InsuranceCompanies.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (company is null) return null;

        return new InsuranceCompanyUpsertDto
        {
            Id = company.Id,
            Name = company.Name,
            ContactPhone = company.ContactPhone,
            ContactEmail = company.ContactEmail,
            IsActive = company.IsActive
        };
    }

    public async Task<Result<InsuranceCompanyDto>> CreateCompanyAsync(InsuranceCompanyUpsertDto dto, CancellationToken cancellationToken = default)
    {
        var validation = await ValidateCompanyAsync(dto, cancellationToken);
        if (validation is not null) return Result<InsuranceCompanyDto>.Failure(validation);

        var company = new InsuranceCompany
        {
            Name = dto.Name.Trim(),
            ContactPhone = dto.ContactPhone,
            ContactEmail = dto.ContactEmail,
            IsActive = dto.IsActive
        };

        _context.InsuranceCompanies.Add(company);
        await _context.SaveChangesAsync(cancellationToken);

        return Result<InsuranceCompanyDto>.Success(new InsuranceCompanyDto
        {
            Id = company.Id, Name = company.Name, ContactPhone = company.ContactPhone,
            ContactEmail = company.ContactEmail, IsActive = company.IsActive, PolicyCount = 0
        });
    }

    public async Task<Result<InsuranceCompanyDto>> UpdateCompanyAsync(InsuranceCompanyUpsertDto dto, CancellationToken cancellationToken = default)
    {
        if (dto.Id is null) return Result<InsuranceCompanyDto>.Failure("معرّف شركة التأمين مطلوب.");

        var company = await _context.InsuranceCompanies.Include(c => c.Policies).FirstOrDefaultAsync(c => c.Id == dto.Id, cancellationToken);
        if (company is null) return Result<InsuranceCompanyDto>.Failure("شركة التأمين غير موجودة.");

        var validation = await ValidateCompanyAsync(dto, cancellationToken);
        if (validation is not null) return Result<InsuranceCompanyDto>.Failure(validation);

        company.Name = dto.Name.Trim();
        company.ContactPhone = dto.ContactPhone;
        company.ContactEmail = dto.ContactEmail;
        company.IsActive = dto.IsActive;

        await _context.SaveChangesAsync(cancellationToken);

        return Result<InsuranceCompanyDto>.Success(new InsuranceCompanyDto
        {
            Id = company.Id, Name = company.Name, ContactPhone = company.ContactPhone,
            ContactEmail = company.ContactEmail, IsActive = company.IsActive, PolicyCount = company.Policies.Count
        });
    }

    public async Task<Result> SetCompanyActiveStatusAsync(int companyId, bool isActive, CancellationToken cancellationToken = default)
    {
        var company = await _context.InsuranceCompanies.FirstOrDefaultAsync(c => c.Id == companyId, cancellationToken);
        if (company is null) return Result.Failure("شركة التأمين غير موجودة.");

        company.IsActive = isActive;
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    // ===================== Policies =====================

    public async Task<List<InsurancePolicyDto>> GetPoliciesAsync(CancellationToken cancellationToken = default)
    {
        var policies = await _context.InsurancePolicies
            .Include(p => p.Customer)
            .Include(p => p.InsuranceCompany)
            .OrderByDescending(p => p.StartDate)
            .ToListAsync(cancellationToken);

        return policies.Select(MapPolicyToDto).ToList();
    }

    public async Task<InsurancePolicyUpsertDto?> GetPolicyForEditAsync(int id, CancellationToken cancellationToken = default)
    {
        var policy = await _context.InsurancePolicies.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (policy is null) return null;

        return new InsurancePolicyUpsertDto
        {
            Id = policy.Id,
            CustomerId = policy.CustomerId,
            InsuranceCompanyId = policy.InsuranceCompanyId,
            PolicyNumber = policy.PolicyNumber,
            CoveragePercent = policy.CoveragePercent,
            StartDate = policy.StartDate,
            EndDate = policy.EndDate,
            IsActive = policy.IsActive
        };
    }

    public async Task<Result<InsurancePolicyDto>> CreatePolicyAsync(InsurancePolicyUpsertDto dto, CancellationToken cancellationToken = default)
    {
        var validation = await ValidatePolicyAsync(dto, cancellationToken);
        if (validation is not null) return Result<InsurancePolicyDto>.Failure(validation);

        var policy = new InsurancePolicy
        {
            CustomerId = dto.CustomerId,
            InsuranceCompanyId = dto.InsuranceCompanyId,
            PolicyNumber = dto.PolicyNumber.Trim(),
            CoveragePercent = dto.CoveragePercent,
            StartDate = dto.StartDate,
            EndDate = dto.EndDate,
            IsActive = dto.IsActive
        };

        _context.InsurancePolicies.Add(policy);
        await _context.SaveChangesAsync(cancellationToken);

        var reloaded = await _context.InsurancePolicies.Include(p => p.Customer).Include(p => p.InsuranceCompany)
            .FirstAsync(p => p.Id == policy.Id, cancellationToken);
        return Result<InsurancePolicyDto>.Success(MapPolicyToDto(reloaded));
    }

    public async Task<Result<InsurancePolicyDto>> UpdatePolicyAsync(InsurancePolicyUpsertDto dto, CancellationToken cancellationToken = default)
    {
        if (dto.Id is null) return Result<InsurancePolicyDto>.Failure("معرّف البوليصة مطلوب.");

        var policy = await _context.InsurancePolicies.FirstOrDefaultAsync(p => p.Id == dto.Id, cancellationToken);
        if (policy is null) return Result<InsurancePolicyDto>.Failure("البوليصة غير موجودة.");

        var validation = await ValidatePolicyAsync(dto, cancellationToken);
        if (validation is not null) return Result<InsurancePolicyDto>.Failure(validation);

        policy.CustomerId = dto.CustomerId;
        policy.InsuranceCompanyId = dto.InsuranceCompanyId;
        policy.PolicyNumber = dto.PolicyNumber.Trim();
        policy.CoveragePercent = dto.CoveragePercent;
        policy.StartDate = dto.StartDate;
        policy.EndDate = dto.EndDate;
        policy.IsActive = dto.IsActive;

        await _context.SaveChangesAsync(cancellationToken);

        var reloaded = await _context.InsurancePolicies.Include(p => p.Customer).Include(p => p.InsuranceCompany)
            .FirstAsync(p => p.Id == policy.Id, cancellationToken);
        return Result<InsurancePolicyDto>.Success(MapPolicyToDto(reloaded));
    }

    public async Task<List<InsurancePolicyDto>> GetActivePoliciesForCustomerAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var today = _dateTime.UtcNow.Date;

        var policies = await _context.InsurancePolicies
            .Include(p => p.Customer)
            .Include(p => p.InsuranceCompany)
            .Where(p => p.CustomerId == customerId && p.IsActive
                && p.StartDate.Date <= today && (!p.EndDate.HasValue || p.EndDate.Value.Date >= today))
            .ToListAsync(cancellationToken);

        return policies.Select(MapPolicyToDto).ToList();
    }

    // ===================== Claims =====================

    public async Task<List<InsuranceClaimDto>> GetClaimsAsync(CancellationToken cancellationToken = default)
    {
        var claims = await _context.InsuranceClaims
            .Include(c => c.SalesInvoice)
            .Include(c => c.InsurancePolicy).ThenInclude(p => p.Customer)
            .Include(c => c.InsurancePolicy).ThenInclude(p => p.InsuranceCompany)
            .OrderByDescending(c => c.SubmittedAtUtc)
            .ToListAsync(cancellationToken);

        return claims.Select(MapClaimToDto).ToList();
    }

    public async Task<Result<InsuranceClaimDto>> SubmitClaimAsync(SubmitClaimDto dto, CancellationToken cancellationToken = default)
    {
        if (dto.ClaimedAmount <= 0) return Result<InsuranceClaimDto>.Failure("مبلغ المطالبة يجب أن يكون أكبر من صفر.");

        var invoice = await _context.SalesInvoices.FirstOrDefaultAsync(i => i.Id == dto.SalesInvoiceId, cancellationToken);
        if (invoice is null) return Result<InsuranceClaimDto>.Failure("فاتورة المبيعات غير موجودة.");

        var policy = await _context.InsurancePolicies.FirstOrDefaultAsync(p => p.Id == dto.InsurancePolicyId, cancellationToken);
        if (policy is null) return Result<InsuranceClaimDto>.Failure("بوليصة التأمين غير موجودة.");

        if (dto.ClaimedAmount > invoice.TotalAmount)
            return Result<InsuranceClaimDto>.Failure("مبلغ المطالبة لا يمكن أن يتجاوز إجمالي الفاتورة.");

        var alreadyClaimed = await _context.InsuranceClaims
            .AnyAsync(c => c.SalesInvoiceId == dto.SalesInvoiceId && c.Status != InsuranceClaimStatus.Rejected, cancellationToken);
        if (alreadyClaimed)
            return Result<InsuranceClaimDto>.Failure("توجد مطالبة نشطة بالفعل مرتبطة بهذه الفاتورة.");

        var claim = new InsuranceClaim
        {
            ClaimNumber = await GenerateClaimNumberAsync(),
            SalesInvoiceId = dto.SalesInvoiceId,
            InsurancePolicyId = dto.InsurancePolicyId,
            ClaimedAmount = dto.ClaimedAmount,
            Status = InsuranceClaimStatus.Submitted,
            SubmittedAtUtc = _dateTime.UtcNow,
            Notes = dto.Notes
        };

        _context.InsuranceClaims.Add(claim);
        await _context.SaveChangesAsync(cancellationToken);

        var reloaded = await LoadClaimAsync(claim.Id, cancellationToken);
        return Result<InsuranceClaimDto>.Success(MapClaimToDto(reloaded!));
    }

    public async Task<Result<InsuranceClaimDto>> ProcessClaimAsync(ProcessClaimDto dto, CancellationToken cancellationToken = default)
    {
        var claim = await _context.InsuranceClaims
            .Include(c => c.SalesInvoice)
            .Include(c => c.InsurancePolicy).ThenInclude(p => p.InsuranceCompany)
            .FirstOrDefaultAsync(c => c.Id == dto.ClaimId, cancellationToken);
        if (claim is null) return Result<InsuranceClaimDto>.Failure("المطالبة غير موجودة.");

        if (claim.Status == InsuranceClaimStatus.Paid)
            return Result<InsuranceClaimDto>.Failure("لا يمكن تعديل مطالبة مدفوعة بالكامل بالفعل.");

        if (dto.NewStatus == InsuranceClaimStatus.Approved || dto.NewStatus == InsuranceClaimStatus.Paid)
        {
            if (dto.ApprovedAmount is null || dto.ApprovedAmount <= 0)
                return Result<InsuranceClaimDto>.Failure("يجب تحديد المبلغ المعتمد.");
            if (dto.ApprovedAmount > claim.ClaimedAmount)
                return Result<InsuranceClaimDto>.Failure("المبلغ المعتمد لا يمكن أن يتجاوز المبلغ المطالب به.");

            claim.ApprovedAmount = dto.ApprovedAmount;
        }

        claim.Status = dto.NewStatus;
        claim.ProcessedAtUtc = _dateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(dto.Notes)) claim.Notes = dto.Notes;

        await _context.SaveChangesAsync(cancellationToken);

        if (dto.NewStatus == InsuranceClaimStatus.Paid)
        {
            await _accountingService.PostInsuranceClaimPaymentAsync(new InsuranceClaimPaymentPostingRequest
            {
                BranchId = claim.SalesInvoice.BranchId,
                InsuranceClaimId = claim.Id,
                ClaimNumber = claim.ClaimNumber,
                InsuranceCompanyName = claim.InsurancePolicy.InsuranceCompany.Name,
                PaymentDate = _dateTime.UtcNow,
                Amount = claim.ApprovedAmount ?? claim.ClaimedAmount
            }, cancellationToken);
        }

        var reloaded = await LoadClaimAsync(claim.Id, cancellationToken);
        return Result<InsuranceClaimDto>.Success(MapClaimToDto(reloaded!));
    }

    private async Task<InsuranceClaim?> LoadClaimAsync(int id, CancellationToken cancellationToken) =>
        await _context.InsuranceClaims
            .Include(c => c.SalesInvoice)
            .Include(c => c.InsurancePolicy).ThenInclude(p => p.Customer)
            .Include(c => c.InsurancePolicy).ThenInclude(p => p.InsuranceCompany)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    private async Task<string> GenerateClaimNumberAsync()
    {
        var count = await _context.InsuranceClaims.CountAsync();
        return $"CLM-{DateTime.UtcNow:yyyyMM}-{count + 1:D5}";
    }

    private async Task<string?> ValidateCompanyAsync(InsuranceCompanyUpsertDto dto, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dto.Name)) return "اسم شركة التأمين مطلوب.";

        var nameTaken = await _context.InsuranceCompanies.AnyAsync(c => c.Name == dto.Name.Trim() && c.Id != dto.Id, cancellationToken);
        if (nameTaken) return "اسم شركة التأمين مستخدم مسبقاً.";

        return null;
    }

    private async Task<string?> ValidatePolicyAsync(InsurancePolicyUpsertDto dto, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dto.PolicyNumber)) return "رقم البوليصة مطلوب.";
        if (dto.CoveragePercent < 0 || dto.CoveragePercent > 100) return "نسبة التغطية يجب أن تكون بين 0 و 100.";
        if (dto.EndDate.HasValue && dto.EndDate.Value.Date < dto.StartDate.Date) return "تاريخ الانتهاء لا يمكن أن يسبق تاريخ البداية.";

        var customerExists = await _context.Customers.AnyAsync(c => c.Id == dto.CustomerId, cancellationToken);
        if (!customerExists) return "العميل المحدد غير موجود.";

        var companyExists = await _context.InsuranceCompanies.AnyAsync(c => c.Id == dto.InsuranceCompanyId, cancellationToken);
        if (!companyExists) return "شركة التأمين المحددة غير موجودة.";

        var numberTaken = await _context.InsurancePolicies
            .AnyAsync(p => p.InsuranceCompanyId == dto.InsuranceCompanyId && p.PolicyNumber == dto.PolicyNumber.Trim() && p.Id != dto.Id, cancellationToken);
        if (numberTaken) return "رقم البوليصة مستخدم مسبقاً لدى هذه الشركة.";

        return null;
    }

    private static InsurancePolicyDto MapPolicyToDto(InsurancePolicy p) => new()
    {
        Id = p.Id,
        CustomerName = p.Customer.Name,
        InsuranceCompanyName = p.InsuranceCompany.Name,
        PolicyNumber = p.PolicyNumber,
        CoveragePercent = p.CoveragePercent,
        StartDate = p.StartDate,
        EndDate = p.EndDate,
        IsActive = p.IsActive
    };

    private static InsuranceClaimDto MapClaimToDto(InsuranceClaim c) => new()
    {
        Id = c.Id,
        ClaimNumber = c.ClaimNumber,
        SalesInvoiceNumber = c.SalesInvoice.Number,
        CustomerName = c.InsurancePolicy.Customer.Name,
        InsuranceCompanyName = c.InsurancePolicy.InsuranceCompany.Name,
        PolicyNumber = c.InsurancePolicy.PolicyNumber,
        ClaimedAmount = c.ClaimedAmount,
        ApprovedAmount = c.ApprovedAmount,
        Status = c.Status,
        SubmittedAtUtc = c.SubmittedAtUtc,
        ProcessedAtUtc = c.ProcessedAtUtc,
        Notes = c.Notes
    };
}
