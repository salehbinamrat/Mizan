using MizanMedical.Domain;
using Xunit;

namespace MizanMedical.Tests;

public sealed class UnifiedLoginNavigationTests
{
    [Fact]
    public void System_owner_lands_on_dashboard_even_when_no_grants_are_serialized()
    {
        var pages = LoginNavigationRules.VisibleModules(true, []);
        Assert.Contains(DesktopModule.Dashboard, pages);
        Assert.Contains(DesktopModule.Administration, pages);
        Assert.Equal(DesktopModule.Dashboard,
            LoginNavigationRules.LandingModule("Admin", true, []));
    }

    [Fact]
    public void Receptionist_opens_patients_but_cannot_see_admin_or_accounting()
    {
        var permissions = new[] { HospitalPermissions.PatientsRead, HospitalPermissions.PatientsCreate,
            HospitalPermissions.InvoicesIssue };
        var pages = LoginNavigationRules.VisibleModules(false, permissions);
        Assert.Contains(DesktopModule.Patients, pages);
        Assert.Contains(DesktopModule.Invoices, pages);
        Assert.DoesNotContain(DesktopModule.Administration, pages);
        Assert.DoesNotContain(DesktopModule.Dashboard, pages);
        Assert.Equal(DesktopModule.Patients,
            LoginNavigationRules.LandingModule("Receptionist", false, permissions));
    }

    [Fact]
    public void Cashier_goes_to_cash_desk_even_if_receipt_and_invoice_permissions_are_present()
    {
        string[] permissions = [HospitalPermissions.ShiftsRead, HospitalPermissions.ShiftsOpen,
            HospitalPermissions.ReceiptsCreate, HospitalPermissions.InvoicesRead];
        var pages = LoginNavigationRules.VisibleModules(false, permissions);
        Assert.Contains(DesktopModule.CashDesk, pages);
        Assert.Contains(DesktopModule.Receipts, pages);
        Assert.DoesNotContain(DesktopModule.Expenses, pages);
        Assert.Equal(DesktopModule.CashDesk,
            LoginNavigationRules.LandingModule("Cashier", false, permissions));
    }

    [Fact]
    public void Global_scope_and_manager_role_do_not_add_ungranted_pages()
    {
        var permissions = new[] { HospitalPermissions.ReportsRead };
        var pages = LoginNavigationRules.VisibleModules(false, permissions);
        Assert.Equal(new[] { DesktopModule.Dashboard }, pages);
        Assert.Equal(DesktopModule.Dashboard,
            LoginNavigationRules.LandingModule("Manager", false, permissions));
        Assert.Empty(LoginNavigationRules.VisibleModules(false, []));
        Assert.Null(LoginNavigationRules.LandingModule("Manager", false, []));
    }

    [Fact]
    public void Permission_only_user_gets_first_allowed_work_area()
    {
        Assert.Equal(DesktopModule.Expenses,
            LoginNavigationRules.LandingModule("Cashier", false,
                [HospitalPermissions.ExpensesCreate]));
        Assert.Equal(DesktopModule.Services,
            LoginNavigationRules.LandingModule("DepartmentManager", false,
                [HospitalPermissions.ServicesManage]));
        Assert.Equal(DesktopModule.Invoices,
            LoginNavigationRules.LandingModule("Receptionist", false,
                [HospitalPermissions.InvoicesIssue]));
    }

    [Fact]
    public void Human_readable_role_names_are_display_only()
    {
        Assert.Equal("مدير القسم", LoginNavigationRules.ArabicRole("DepartmentManager"));
        Assert.Equal("أمين صندوق", LoginNavigationRules.ArabicRole("Cashier"));
        Assert.Equal("مسؤول البرنامج", LoginNavigationRules.ArabicRole("Admin", true));
    }
}
