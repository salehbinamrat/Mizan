using MizanMedical.Domain;
using Xunit;

namespace MizanMedical.Tests;

public sealed class PermissionPrerequisiteTests
{
    [Theory]
    [InlineData(HospitalPermissions.InvoicesIssue, HospitalPermissions.PatientsRead)]
    [InlineData(HospitalPermissions.InvoicesIssue, HospitalPermissions.ServicesRead)]
    [InlineData(HospitalPermissions.InvoicesIssue, HospitalPermissions.InvoicesRead)]
    [InlineData(HospitalPermissions.ReceiptsCreate, HospitalPermissions.InvoicesRead)]
    [InlineData(HospitalPermissions.ReceiptsCreate, HospitalPermissions.ShiftsRead)]
    [InlineData(HospitalPermissions.ExpensesCreate, HospitalPermissions.ShiftsRead)]
    [InlineData(HospitalPermissions.ShiftsOpen, HospitalPermissions.DesksRead)]
    [InlineData(HospitalPermissions.ShiftsClose, HospitalPermissions.ShiftsRead)]
    [InlineData(HospitalPermissions.PatientsCreate, HospitalPermissions.PatientsRead)]
    [InlineData(HospitalPermissions.ServicesManage, HospitalPermissions.ServicesRead)]
    public void Financial_action_requires_related_read_grant(string operation, string prerequisite)
    {
        var missing = PermissionPrerequisiteRules.Missing([operation]);
        Assert.Contains(prerequisite, missing);
        Assert.False(AccessRules.ArePermissionsValid([operation]));
        Assert.True(AccessRules.ArePermissionsValid([operation, .. PermissionPrerequisiteRules.Missing([operation])]));
    }

    [Fact]
    public void Read_only_access_never_grants_mutation_or_global_scope()
    {
        var reads = new[] { HospitalPermissions.InvoicesRead, HospitalPermissions.PatientsRead };
        Assert.Empty(PermissionPrerequisiteRules.Missing(reads));
        Assert.True(AccessRules.ArePermissionsValid(reads));
        Assert.DoesNotContain(HospitalPermissions.InvoicesIssue, PermissionPrerequisiteRules.Missing(reads));
        Assert.DoesNotContain(HospitalPermissions.UsersManage, PermissionPrerequisiteRules.Missing(reads));
        Assert.DoesNotContain(HospitalPermissions.DesksReadAll, PermissionPrerequisiteRules.Missing(reads));
    }

    [Fact]
    public void Reserved_administrator_rights_cannot_be_implicitly_granted()
    {
        var required = PermissionPrerequisiteRules.Missing([HospitalPermissions.ReceiptsCreate]);
        Assert.All(required, key => Assert.DoesNotContain(key, HospitalPermissions.OwnerOnly));
        Assert.False(AccessRules.ArePermissionsValid([HospitalPermissions.UsersManage]));
    }

    [Fact]
    public void Multiple_actions_share_prerequisites_without_duplicates()
    {
        var required = PermissionPrerequisiteRules.Missing([
            HospitalPermissions.ReceiptsCreate, HospitalPermissions.ExpensesCreate,
            HospitalPermissions.ShiftsClose
        ]);
        Assert.Equal(required.Distinct(StringComparer.Ordinal).Count(), required.Count);
        Assert.Equal(2, required.Count);
    }
}
