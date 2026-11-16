namespace MizanMedical.Domain;

/// <summary>
/// Login navigation is based on verified server grants. Role strings can only
/// prioritize an already authorized module, never add a new capability.
/// </summary>
public enum DesktopModule { Dashboard, Patients, Services, Invoices, CashDesk, Receipts, Expenses, Administration }

public static class LoginNavigationRules
{
    public static IReadOnlyList<DesktopModule> VisibleModules(bool isSystemOwner, IEnumerable<string>? grants)
    {
        var allowed = new HashSet<string>(grants ?? [], StringComparer.Ordinal);
        bool Has(string permission) => isSystemOwner || allowed.Contains(permission);
        var modules = new List<DesktopModule>();
        if (Has(HospitalPermissions.ReportsRead)) modules.Add(DesktopModule.Dashboard);
        if (Has(HospitalPermissions.PatientsRead) || Has(HospitalPermissions.PatientsCreate))
            modules.Add(DesktopModule.Patients);
        if (Has(HospitalPermissions.ServicesRead) || Has(HospitalPermissions.ServicesManage))
            modules.Add(DesktopModule.Services);
        if (Has(HospitalPermissions.InvoicesRead) || Has(HospitalPermissions.InvoicesIssue))
            modules.Add(DesktopModule.Invoices);
        if (Has(HospitalPermissions.DesksRead) || Has(HospitalPermissions.ShiftsRead) ||
            Has(HospitalPermissions.ShiftsOpen) || Has(HospitalPermissions.ShiftsClose))
            modules.Add(DesktopModule.CashDesk);
        if (Has(HospitalPermissions.ReceiptsCreate)) modules.Add(DesktopModule.Receipts);
        if (Has(HospitalPermissions.ExpensesCreate)) modules.Add(DesktopModule.Expenses);
        if (Has(HospitalPermissions.UsersManage) || Has(HospitalPermissions.DesksManage))
            modules.Add(DesktopModule.Administration);
        return modules;
    }

    public static DesktopModule? LandingModule(string? role, bool isSystemOwner, IEnumerable<string>? grants)
    {
        var visible = VisibleModules(isSystemOwner, grants);
        if (visible.Count == 0) return null;
        // Role selects an ergonomic starting tab; it does not grant access.
        if (!isSystemOwner && string.Equals(role, "Cashier", StringComparison.OrdinalIgnoreCase) &&
            visible.Contains(DesktopModule.CashDesk))
            return DesktopModule.CashDesk;
        if (!isSystemOwner && string.Equals(role, "Receptionist", StringComparison.OrdinalIgnoreCase) &&
            visible.Contains(DesktopModule.Patients))
            return DesktopModule.Patients;
        return visible[0];
    }

    public static string ArabicRole(string? role, bool isSystemOwner = false) =>
        isSystemOwner ? "مسؤول البرنامج" : role switch
        {
            "Admin" => "مدير",
            "Manager" => "مسؤول عام",
            "DepartmentManager" => "مدير القسم",
            "Accountant" => "محاسب",
            "Cashier" => "أمين صندوق",
            "Receptionist" => "موظف استقبال",
            _ => "مستخدم"
        };
}
