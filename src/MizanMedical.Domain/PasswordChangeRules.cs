namespace MizanMedical.Domain;

public static class PasswordChangeRules
{
    public static void Validate(string? currentPassword, string? newPassword, string? confirmation)
    {
        if (!PasswordSecurity.IsAcceptableLoginPassword(currentPassword))
            throw new DomainException("كلمة المرور الحالية غير صالحة أو تتجاوز 128 حرفا.");
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length is < 12 or > 128)
            throw new DomainException("كلمة المرور الجديدة يجب أن تتكون من 12 إلى 128 حرفا.");
        if (!string.Equals(newPassword, confirmation, StringComparison.Ordinal))
            throw new DomainException("تأكيد كلمة المرور الجديدة غير متطابق.");
        if (string.Equals(newPassword, currentPassword, StringComparison.Ordinal))
            throw new DomainException("اختر كلمة مرور جديدة مختلفة عن الحالية.");
    }
}
