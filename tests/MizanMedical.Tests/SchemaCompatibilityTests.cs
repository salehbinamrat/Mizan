using MizanMedical.Domain;
using Xunit;

namespace MizanMedical.Tests;

public sealed class SchemaCompatibilityTests
{
    [Fact]
    public void Fresh_current_schema_passes_without_required_migrations()
    {
        Assert.Empty(SchemaCompatibilityRules.Missing(SchemaCompatibilityRules.RequiredColumns));
    }

    [Fact]
    public void Previous_schema_missing_hospital_identity_is_blocked_without_silent_reset()
    {
        var old = SchemaCompatibilityRules.RequiredColumns
            .Where(x => x.Table != "HospitalIdentities").ToArray();
        Assert.Contains(("HospitalIdentities", "Id"), SchemaCompatibilityRules.Missing(old));
        Assert.Contains(("HospitalIdentities", "Name"), SchemaCompatibilityRules.Missing(old));
    }

    [Fact]
    public void Old_schema_missing_personal_desk_or_department_limits_is_detected()
    {
        var columns = SchemaCompatibilityRules.RequiredColumns
            .Where(x => x != ("Departments", "MaxActiveUsers") &&
                        x != ("CashDesks", "OwnerUserId")).ToArray();
        var missing = SchemaCompatibilityRules.Missing(columns);
        Assert.Contains(("Departments", "MaxActiveUsers"), missing);
        Assert.Contains(("CashDesks", "OwnerUserId"), missing);
        Assert.Equal(2, missing.Count);
    }

    [Fact]
    public void Completely_different_database_must_never_pass_preflight()
    {
        Assert.NotEmpty(SchemaCompatibilityRules.Missing(
            [("SomeOtherProduct", "Id")]));
    }
}
