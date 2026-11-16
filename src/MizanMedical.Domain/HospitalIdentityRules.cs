using System.Text;

namespace MizanMedical.Domain;

/// <summary>Validates the server-owned hospital name for offline installations.</summary>
public static class HospitalIdentityRules
{
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException("اسم المستشفى مطلوب.");
        // Normalize Unicode and whitespace, preserving Arabic text and spaces.
        string name = string.Join(" ", value.Normalize(NormalizationForm.FormKC)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (name.Length is < 3 or > 200 || name.Any(char.IsControl))
            throw new DomainException("اسم المستشفى يجب أن يتكون من 3 إلى 200 حرف صالح.");
        return name;
    }

    public static bool Matches(string? configuredName, string? suppliedName)
    {
        if (string.IsNullOrWhiteSpace(configuredName) || string.IsNullOrWhiteSpace(suppliedName))
            return false;
        try
        {
            return string.Equals(Normalize(configuredName), Normalize(suppliedName),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (DomainException) { return false; }
    }
}
