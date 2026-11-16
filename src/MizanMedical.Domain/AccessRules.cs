using System.Security.Claims;

namespace MizanMedical.Domain;

/// <summary>Grants are independent of role labels and are never implicitly inherited.</summary>
public static class HospitalPermissions
{
    public const string DepartmentsManage = "departments.manage";
    public const string UsersManage = "users.manage";
    public const string PatientsRead = "patients.read";
    public const string PatientsCreate = "patients.create";
    public const string ServicesRead = "services.read";
    public const string ServicesManage = "services.manage";
    public const string DesksRead = "desks.read";
    public const string DesksManage = "desks.manage";
    public const string ShiftsRead = "shifts.read";
    public const string ShiftsOpen = "shifts.open";
    public const string ShiftsClose = "shifts.close";
    public const string InvoicesRead = "invoices.read";
    public const string InvoicesIssue = "invoices.issue";
    public const string ReceiptsCreate = "receipts.create";
    public const string ExpensesCreate = "expenses.create";
    public const string ReportsRead = "reports.read";
    public const string AuditRead = "audit.read";

    public static readonly IReadOnlyList<string> All = [
        DepartmentsManage, UsersManage, PatientsRead, PatientsCreate,
        ServicesRead, ServicesManage, DesksRead, DesksManage,
        ShiftsRead, ShiftsOpen, ShiftsClose, InvoicesRead,
        InvoicesIssue, ReceiptsCreate, ExpensesCreate, ReportsRead, AuditRead
    ];

    // Only the system owner manages accounts and departments; delegation would
    // require a separate audited, non-escalating grant flow.
    public static readonly IReadOnlySet<string> OwnerOnly = new HashSet<string>(
        [UsersManage, DepartmentsManage], StringComparer.Ordinal);
}

public static class AccessRules
{
    public static bool CanReadDepartment(bool allDepartments, Guid? assignedDepartment, Guid? recordDepartment) =>
        allDepartments || (assignedDepartment.HasValue && assignedDepartment != Guid.Empty &&
                           recordDepartment.HasValue && assignedDepartment == recordDepartment);

    public static bool CanAssignScope(bool actorIsOwner, Guid? departmentId, bool allDepartments) =>
        actorIsOwner && ((allDepartments && departmentId is null) ||
                         (!allDepartments && departmentId is { } id && id != Guid.Empty));

    public static bool ArePermissionsValid(IEnumerable<string> grants, bool targetIsOwner = false) =>
        grants.Distinct(StringComparer.Ordinal).All(key =>
            HospitalPermissions.All.Contains(key, StringComparer.Ordinal) &&
            (targetIsOwner || !HospitalPermissions.OwnerOnly.Contains(key)));
}

public static class DepartmentClaims
{
    public const string DepartmentId = "mizan_department_id";
    public const string AllDepartments = "mizan_all_departments";
    public const string SystemOwner = "mizan_system_owner";
    public const string Permission = "mizan_permission";

    public static bool IsOwner(ClaimsPrincipal user) => user.HasClaim(SystemOwner, "true");
    public static bool HasGlobalScope(ClaimsPrincipal user) =>
        IsOwner(user) || user.HasClaim(AllDepartments, "true");
    public static Guid? Department(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirst(DepartmentId)?.Value, out var id) ? id : null;
    public static bool CanRead(ClaimsPrincipal user, Guid? departmentId) =>
        AccessRules.CanReadDepartment(HasGlobalScope(user), Department(user), departmentId);
    public static bool HasPermission(ClaimsPrincipal user, string name) =>
        IsOwner(user) || user.HasClaim(Permission, name);
}
