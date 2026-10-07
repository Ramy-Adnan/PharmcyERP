using Microsoft.EntityFrameworkCore;
using PharmacyERP.Application.Common.Interfaces;
using PharmacyERP.Domain.Entities;
using PharmacyERP.Domain.Enums;

namespace PharmacyERP.Infrastructure.Persistence.Seed;

/// <summary>
/// Ensures a brand-new database has the minimum data required to actually
/// log in and operate: a System Administrator role with every permission,
/// a default Admin user, the base permission catalogue, and a main branch.
/// Safe to run on every startup — every step is idempotent.
/// </summary>
public class ApplicationDbSeeder
{
    private readonly ApplicationDbContext _context;
    private readonly IPasswordHasher _passwordHasher;

    public ApplicationDbSeeder(ApplicationDbContext context, IPasswordHasher passwordHasher)
    {
        _context = context;
        _passwordHasher = passwordHasher;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await SeedPermissionsAsync(cancellationToken);
        var adminRole = await SeedAdminRoleAsync(cancellationToken);
        var mainBranch = await SeedMainBranchAsync(cancellationToken);
        await SeedAdminUserAsync(adminRole, mainBranch, cancellationToken);
        await SeedDefaultInventoryLookupsAsync(cancellationToken);
        await SeedChartOfAccountsAndCashBoxAsync(mainBranch, cancellationToken);
    }

    private async Task SeedPermissionsAsync(CancellationToken cancellationToken)
    {
        // Phase 0 only ships Security & Branch permissions. Each later phase
        // (Inventory, Sales, Purchasing, ...) appends its own permission codes
        // here as that module is implemented, so the catalogue grows module by module.
        var basePermissions = new[]
        {
            ("Security.ManageUsers", "Security", "إدارة المستخدمين"),
            ("Security.ManageRoles", "Security", "إدارة الأدوار والصلاحيات"),
            ("Security.ViewAuditLog", "Security", "عرض سجل التدقيق"),
            ("Branches.Manage", "Branches", "إدارة الفروع والمخازن"),
            ("Dashboard.View", "Dashboard", "عرض لوحة التحكم"),
            ("Inventory.ManageLookups", "Inventory", "إدارة تصنيفات ووحدات وشركات الأصناف"),
            ("Inventory.ManageItems", "Inventory", "إدارة بطاقات الأصناف"),
            ("Inventory.ReceiveStock", "Inventory", "استلام دفعات المخزون"),
            ("Inventory.AdjustStock", "Inventory", "تعديل أرصدة المخزون"),
            ("Inventory.ViewStock", "Inventory", "عرض تقارير المخزون والانتهاء"),
            ("Purchasing.ManageSuppliers", "Purchasing", "إدارة الموردين"),
            ("Purchasing.ManageOrders", "Purchasing", "إدارة أوامر الشراء"),
            ("Purchasing.ReceiveGoods", "Purchasing", "استلام البضاعة من المورد"),
            ("Purchasing.ManageInvoices", "Purchasing", "إدارة فواتير الشراء والدفعات"),
            ("Sales.ManageCustomers", "Sales", "إدارة العملاء"),
            ("Sales.UsePos", "Sales", "استخدام نقطة البيع (تسجيل مبيعات)"),
            ("Sales.ViewInvoices", "Sales", "عرض فواتير المبيعات"),
            ("Sales.ProcessReturns", "Sales", "معالجة مرتجعات المبيعات"),
            ("Sales.VoidInvoices", "Sales", "إلغاء فواتير المبيعات"),
            ("Prescriptions.Manage", "Prescriptions", "إدارة الوصفات الطبية والأطباء"),
            ("Insurance.ManageCompanies", "Insurance", "إدارة شركات التأمين"),
            ("Insurance.ManagePolicies", "Insurance", "إدارة بوليصات التأمين"),
            ("Insurance.ManageClaims", "Insurance", "إدارة مطالبات التأمين"),
            ("Accounting.ManageChartOfAccounts", "Accounting", "إدارة دليل الحسابات"),
            ("Accounting.ManageJournalEntries", "Accounting", "إدارة القيود اليومية"),
            ("Accounting.ManageCashAndBank", "Accounting", "إدارة الصناديق والحسابات البنكية"),
            ("Accounting.ManageExpenses", "Accounting", "إدارة المصاريف وفئاتها"),
            ("Accounting.ManageReceiptsPayments", "Accounting", "إدارة سندات القبض والصرف"),
            ("Reports.View", "Reports", "عرض لوحة التحكم والتقارير"),
            ("Hr.ManageEmployees", "Hr", "إدارة الموظفين والورديات"),
            ("Hr.ManageAttendance", "Hr", "تسجيل ومتابعة الحضور والانصراف"),
            ("Hr.ManageCommissions", "Hr", "تسجيل عمولات الموظفين"),
            ("Hr.ManagePayroll", "Hr", "إدارة دورات الرواتب واعتمادها وصرفها"),
            ("System.ManageBackup", "System", "إدارة النسخ الاحتياطي والاستعادة"),
            ("System.ManageLicense", "System", "إدارة تفعيل الترخيص"),
        };

        foreach (var (code, module, displayName) in basePermissions)
        {
            var exists = await _context.Permissions.AnyAsync(p => p.Code == code, cancellationToken);
            if (!exists)
            {
                _context.Permissions.Add(new Permission { Code = code, Module = module, DisplayName = displayName });
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task<Role> SeedAdminRoleAsync(CancellationToken cancellationToken)
    {
        var adminRole = await _context.Roles
            .Include(r => r.RolePermissions)
            .FirstOrDefaultAsync(r => r.Name == "System Administrator", cancellationToken);

        if (adminRole is null)
        {
            adminRole = new Role
            {
                Name = "System Administrator",
                Description = "صلاحيات كاملة على كافة موديولات النظام",
                IsSystemRole = true
            };
            _context.Roles.Add(adminRole);
            await _context.SaveChangesAsync(cancellationToken);
        }

        var allPermissionIds = await _context.Permissions.Select(p => p.Id).ToListAsync(cancellationToken);
        var grantedPermissionIds = adminRole.RolePermissions.Select(rp => rp.PermissionId).ToHashSet();

        foreach (var permissionId in allPermissionIds.Where(id => !grantedPermissionIds.Contains(id)))
        {
            _context.RolePermissions.Add(new RolePermission { RoleId = adminRole.Id, PermissionId = permissionId });
        }

        await _context.SaveChangesAsync(cancellationToken);
        return adminRole;
    }

    private async Task<Branch> SeedMainBranchAsync(CancellationToken cancellationToken)
    {
        var mainBranch = await _context.Branches.FirstOrDefaultAsync(b => b.IsMainBranch, cancellationToken);
        if (mainBranch is not null) return mainBranch;

        mainBranch = new Branch
        {
            Code = "MAIN",
            Name = "الفرع الرئيسي",
            Type = BranchType.MainPharmacy,
            IsMainBranch = true,
            IsActive = true
        };
        _context.Branches.Add(mainBranch);
        await _context.SaveChangesAsync(cancellationToken);

        _context.Warehouses.Add(new Warehouse
        {
            BranchId = mainBranch.Id,
            Code = "MAIN-WH",
            Name = "المخزن الرئيسي",
            IsDefault = true,
            IsActive = true
        });
        await _context.SaveChangesAsync(cancellationToken);

        return mainBranch;
    }

    private async Task SeedAdminUserAsync(Role adminRole, Branch mainBranch, CancellationToken cancellationToken)
    {
        var exists = await _context.Users.AnyAsync(u => u.Username == "admin", cancellationToken);
        if (exists) return;

        _context.Users.Add(new User
        {
            FullName = "مدير النظام",
            Username = "admin",
            PasswordHash = _passwordHasher.Hash(Environment.GetEnvironmentVariable("PHARMACYERP_INITIAL_ADMIN_PASSWORD")
                ?? throw new InvalidOperationException("Set PHARMACYERP_INITIAL_ADMIN_PASSWORD before initializing the administrator.")),
            Status = UserStatus.Active,
            RoleId = adminRole.Id,
            DefaultBranchId = mainBranch.Id
        });

        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Bootstraps a minimal set of lookups so the Items screen is immediately usable on a
    /// brand-new installation: one default unit ("قطعة") and one default category ("عام").
    /// The pharmacy administrator is expected to add their real categories/units afterward
    /// via the Inventory Lookups screen — this only prevents an empty-dropdown dead end.
    /// </summary>
    private async Task SeedDefaultInventoryLookupsAsync(CancellationToken cancellationToken)
    {
        if (!await _context.UnitsOfMeasure.AnyAsync(cancellationToken))
        {
            _context.UnitsOfMeasure.Add(new UnitOfMeasure { Code = "PCS", Name = "قطعة", IsActive = true });
        }

        if (!await _context.ItemCategories.AnyAsync(cancellationToken))
        {
            _context.ItemCategories.Add(new ItemCategory { Code = "GEN", Name = "عام", IsActive = true });
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Creates the minimal Chart of Accounts required for the automatic postings in
    /// AccountingService to function at all (Sales/Purchasing/Insurance call
    /// GetSystemAccountAsync by these exact codes and throw if one is missing) — this
    /// must run before the very first sale or purchase invoice is ever posted. Also
    /// creates one default cash box on the main branch so POS, Expenses, Receipts and
    /// Payments screens have at least one usable cash source out of the box.
    /// </summary>
    private async Task SeedChartOfAccountsAndCashBoxAsync(Branch mainBranch, CancellationToken cancellationToken)
    {
        var accountsByCode = await EnsureSystemAccountsAsync(cancellationToken);

        var hasCashBox = await _context.CashBoxes.AnyAsync(cancellationToken);
        if (!hasCashBox)
        {
            _context.CashBoxes.Add(new CashBox
            {
                BranchId = mainBranch.Id,
                Name = "الصندوق الرئيسي",
                AccountId = accountsByCode["1110"].Id,
                IsActive = true
            });
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<Dictionary<string, ChartOfAccount>> EnsureSystemAccountsAsync(CancellationToken cancellationToken)
    {
        // (code, name, type, parentCode, isSystemAccount). System accounts are the ones
        // AccountingService's automatic postings resolve by code — they must never be
        // renumbered or deleted. Non-system accounts (grouping headers, the default
        // expense/equity accounts) exist purely to give a new installation a usable
        // starting structure and can be freely edited by the pharmacy administrator.
        var definitions = new (string Code, string Name, AccountType Type, string? ParentCode, bool IsSystem)[]
        {
            ("1000", "الأصول", AccountType.Asset, null, false),
            ("1100", "الأصول المتداولة", AccountType.Asset, "1000", false),
            ("1110", "الصندوق (نقداً)", AccountType.Asset, "1100", true),
            ("1120", "البنك", AccountType.Asset, "1100", true),
            ("1130", "المخزون", AccountType.Asset, "1100", true),
            ("1140", "ضريبة مدخلات قابلة للاسترداد", AccountType.Asset, "1100", true),
            ("1150", "ذمم مدينة - شركات التأمين", AccountType.Asset, "1100", true),
            ("1160", "ذمم مدينة - العملاء", AccountType.Asset, "1100", true),

            ("2000", "الالتزامات", AccountType.Liability, null, false),
            ("2100", "ذمم دائنة - الموردون", AccountType.Liability, "2000", true),
            ("2200", "ضريبة مستحقة الدفع", AccountType.Liability, "2000", true),

            ("3000", "حقوق الملكية", AccountType.Equity, null, false),
            ("3100", "رأس المال", AccountType.Equity, "3000", false),

            ("4000", "الإيرادات", AccountType.Revenue, null, false),
            ("4100", "إيرادات المبيعات", AccountType.Revenue, "4000", true),

            ("5000", "المصاريف", AccountType.Expense, null, false),
            ("5100", "مصاريف عمومية وإدارية", AccountType.Expense, "5000", false),
            ("5200", "رواتب وأجور", AccountType.Expense, "5000", true),
        };

        var existing = await _context.ChartOfAccounts.ToDictionaryAsync(a => a.Code, cancellationToken);

        // Two passes: create every account first (so parent rows exist), then wire up
        // ParentAccountId — avoids ordering the definitions array by hierarchy depth.
        foreach (var def in definitions)
        {
            if (existing.ContainsKey(def.Code)) continue;

            var account = new ChartOfAccount
            {
                Code = def.Code,
                Name = def.Name,
                Type = def.Type,
                IsSystemAccount = def.IsSystem,
                IsActive = true
            };
            _context.ChartOfAccounts.Add(account);
            existing[def.Code] = account;
        }
        await _context.SaveChangesAsync(cancellationToken);

        foreach (var def in definitions)
        {
            if (def.ParentCode is null) continue;

            var account = existing[def.Code];
            if (account.ParentAccountId is null)
            {
                account.ParentAccountId = existing[def.ParentCode].Id;
            }
        }
        await _context.SaveChangesAsync(cancellationToken);

        return existing;
    }
}
