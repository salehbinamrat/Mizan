namespace MizanMedical.Domain;

/// <summary>
/// A fail-closed schema preflight is not a replacement for reviewed migrations.
/// It only prevents opening a hospital database with missing essential columns.
/// </summary>
public static class SchemaCompatibilityRules
{
    public static readonly IReadOnlyList<(string Table, string Column)> RequiredColumns =
    [
        ("HospitalIdentities", "Id"), ("HospitalIdentities", "Name"),
        ("Departments", "Id"), ("Departments", "MaxActiveUsers"),
        ("StaffUsers", "Id"), ("StaffUsers", "Username"), ("StaffUsers", "IsSystemOwner"),
        ("StaffUsers", "DepartmentId"), ("StaffUsers", "AllDepartments"),
        ("UserPermissions", "StaffUserId"), ("UserPermissions", "Key"),
        ("LoginSessions", "TokenSha256"),
        ("Patients", "DepartmentId"), ("Patients", "FileNumber"),
        ("MedicalServices", "DepartmentId"), ("MedicalServices", "Price"),
        ("CashDesks", "OwnerUserId"), ("CashDesks", "DepartmentId"),
        ("CashShifts", "CashDeskId"), ("CashShifts", "CashierId"),
        ("CashShifts", "ClosedAtUtc"),
        ("Invoices", "RequestId"), ("Invoices", "RequestSignature"),
        ("Invoices", "PatientId"), ("Invoices", "DepartmentId"), ("Invoices", "Paid"),
        ("InvoiceLines", "InvoiceId"), ("InvoiceLines", "Total"),
        ("CashReceipts", "RequestId"), ("CashReceipts", "CashShiftId"),
        ("CashExpenses", "RequestId"), ("CashExpenses", "CashShiftId"),
        ("JournalEntries", "ReferenceId"), ("JournalEntries", "DepartmentId"),
        ("JournalLines", "JournalEntryId"), ("JournalLines", "Debit"), ("JournalLines", "Credit"),
        ("AuditEvents", "StaffUserId"), ("AuditEvents", "RecordedAtUtc")
    ];

    public static IReadOnlyList<(string Table, string Column)> Missing(
        IEnumerable<(string Table, string Column)> existingColumns)
    {
        var existing = existingColumns.ToHashSet();
        return RequiredColumns.Where(x => !existing.Contains(x)).ToArray();
    }
}
