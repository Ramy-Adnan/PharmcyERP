namespace PharmacyERP.Infrastructure.Persistence.Seed;

public static class InitialSetupValidation
{
    public static void ValidateAdministratorPassword(string? password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8 || password.Length > 128)
            throw new InvalidOperationException("حدد كلمة مرور مدير النظام من 8 إلى 128 حرفاً في معالج الإعداد الأول. لا توجد كلمة مرور افتراضية.");
        // BCrypt only consumes the first 72 UTF-8 bytes. Do not accept a first
        // administrator password whose suffix would be silently discarded.
        if (System.Text.Encoding.UTF8.GetByteCount(password) > 72)
            throw new InvalidOperationException("كلمة مرور المدير طويلة جداً. اختر كلمة أقصر، على ألا تقل عن 8 أحرف.");
    }
}
