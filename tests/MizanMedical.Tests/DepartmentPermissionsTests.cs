using System.Security.Claims;
using MizanMedical.Domain;
using Xunit;

namespace MizanMedical.Tests;

public sealed class DepartmentPermissionsTests
{
    private readonly Guid _ward = Guid.NewGuid();
    private readonly Guid _lab = Guid.NewGuid();

    private static ClaimsPrincipal Employee(Guid? department, bool global = false, bool owner = false,
        params string[] permissions)
    {
        var claims = new List<Claim>
        {
            new(DepartmentClaims.SystemOwner, owner ? "true" : "false"),
            new(DepartmentClaims.AllDepartments, global ? "true" : "false")
        };
        if (department.HasValue) claims.Add(new Claim(DepartmentClaims.DepartmentId, department.Value.ToString()));
        claims.AddRange(permissions.Select(p => new Claim(DepartmentClaims.Permission, p)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "tests"));
    }

    [Fact]
    public void Department_manager_can_see_own_department_only()
    {
        var user = Employee(_ward, permissions: [HospitalPermissions.InvoicesRead]);
        Assert.True(DepartmentClaims.CanRead(user, _ward));
        Assert.False(DepartmentClaims.CanRead(user, _lab));
        Assert.False(DepartmentClaims.CanRead(user, null));
    }

    [Fact]
    public void Role_names_do_not_grant_permissions()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, "Admin"),
            new Claim(DepartmentClaims.AllDepartments, "false")
        }, "tests"));
        Assert.False(DepartmentClaims.CanRead(user, _lab));
        Assert.False(DepartmentClaims.HasPermission(user, HospitalPermissions.UsersManage));
    }

    [Fact]
    public void Explicit_global_scope_does_not_grant_additional_actions()
    {
        var user = Employee(null, global: true, permissions: [HospitalPermissions.ReportsRead]);
        Assert.True(DepartmentClaims.CanRead(user, _ward));
        Assert.True(DepartmentClaims.HasPermission(user, HospitalPermissions.ReportsRead));
        Assert.False(DepartmentClaims.HasPermission(user, HospitalPermissions.ReceiptsCreate));
        Assert.False(DepartmentClaims.HasPermission(user, HospitalPermissions.UsersManage));
    }

    [Fact]
    public void System_owner_has_global_scope_and_all_operations()
    {
        var user = Employee(null, owner: true);
        Assert.True(DepartmentClaims.CanRead(user, _ward));
        Assert.True(DepartmentClaims.CanRead(user, _lab));
        Assert.True(DepartmentClaims.HasPermission(user, HospitalPermissions.UsersManage));
        Assert.True(DepartmentClaims.HasPermission(user, HospitalPermissions.AuditRead));
    }

    [Fact]
    public void Empty_department_assignment_has_no_access()
    {
        Assert.False(AccessRules.CanReadDepartment(false, null, _ward));
        Assert.False(AccessRules.CanReadDepartment(false, Guid.Empty, _ward));
        Assert.False(AccessRules.CanReadDepartment(false, _ward, null));
    }

    [Fact]
    public void Non_owner_must_not_delegate_scope()
    {
        Assert.False(AccessRules.CanAssignScope(false, _ward, false));
        Assert.False(AccessRules.CanAssignScope(false, null, true));
    }

    [Fact]
    public void Assignment_must_be_exclusively_single_department_or_global()
    {
        Assert.True(AccessRules.CanAssignScope(true, _ward, false));
        Assert.True(AccessRules.CanAssignScope(true, null, true));
        Assert.False(AccessRules.CanAssignScope(true, _ward, true));
        Assert.False(AccessRules.CanAssignScope(true, null, false));
        Assert.False(AccessRules.CanAssignScope(true, Guid.Empty, false));
    }

    [Fact]
    public void Reserved_permissions_are_not_delegable()
    {
        Assert.False(AccessRules.ArePermissionsValid([HospitalPermissions.UsersManage]));
        Assert.False(AccessRules.ArePermissionsValid([HospitalPermissions.DepartmentsManage]));
        Assert.True(AccessRules.ArePermissionsValid([HospitalPermissions.AuditRead]));
        Assert.True(AccessRules.ArePermissionsValid([HospitalPermissions.PatientsRead, HospitalPermissions.ReportsRead]));
        Assert.False(AccessRules.ArePermissionsValid(["accounts.export.all_without_permission"]));
    }
}
