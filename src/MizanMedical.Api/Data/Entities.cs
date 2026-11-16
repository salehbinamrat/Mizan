namespace MizanMedical.Api.Data;

/// <summary>One immutable hospital identity per locally deployed server.</summary>
public sealed class HospitalIdentity
{
    public int Id { get; set; } = 1;
    public string Name { get; set; } = "";
}

public enum HospitalRole { Admin, Manager, DepartmentManager, Accountant, Cashier, Receptionist }

public sealed class Department
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public int MaxActiveUsers { get; set; } = 20;
}

public sealed class UserPermission
{
    public Guid StaffUserId { get; set; }
    public StaffUser StaffUser { get; set; } = null!;
    public string Key { get; set; } = "";
}

public sealed class StaffUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Username { get; set; } = "";
    public string FullName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string PasswordSalt { get; set; } = "";
    public HospitalRole Role { get; set; }
    // Only the original bootstrap account is the system owner; the role name alone grants nothing.
    public bool IsSystemOwner { get; set; }
    public Guid? DepartmentId { get; set; }
    public bool AllDepartments { get; set; }
    public List<UserPermission> Permissions { get; set; } = [];
    public bool IsActive { get; set; } = true;
    public int FailedLogins { get; set; }
    public DateTime? LockedUntilUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class LoginSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StaffUserId { get; set; }
    public string TokenSha256 { get; set; } = "";
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class Patient
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? DepartmentId { get; set; }
    public string FileNumber { get; set; } = "";
    public string FullName { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class MedicalService
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? DepartmentId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Department { get; set; } = "";
    public decimal Price { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class CashDesk
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? DepartmentId { get; set; }
    public Guid? OwnerUserId { get; set; }
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

public sealed class CashShift
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? DepartmentId { get; set; }
    public Guid CashDeskId { get; set; }
    public Guid CashierId { get; set; }
    public decimal OpeningCash { get; set; }
    public decimal? CountedCash { get; set; }
    public decimal? ExpectedCash { get; set; }
    public decimal? Variance { get; set; }
    public string? CloseComment { get; set; }
    public DateTime OpenedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ClosedAtUtc { get; set; }
}

public sealed class Invoice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? DepartmentId { get; set; }
    public Guid RequestId { get; set; }
    public string RequestSignature { get; set; } = "";
    public string InvoiceNumber { get; set; } = "";
    public Guid PatientId { get; set; }
    public Guid IssuedByUserId { get; set; }
    public DateTime IssuedAtUtc { get; set; } = DateTime.UtcNow;
    public decimal Total { get; set; }
    public decimal Paid { get; set; }
    public List<InvoiceLine> Lines { get; set; } = [];
}

public sealed class InvoiceLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? DepartmentId { get; set; }
    public Guid InvoiceId { get; set; }
    public Guid MedicalServiceId { get; set; }
    public string ServiceCode { get; set; } = "";
    public string ServiceName { get; set; } = "";
    public string Department { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal Total { get; set; }
}

public sealed class CashReceipt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? DepartmentId { get; set; }
    public Guid RequestId { get; set; }
    public Guid InvoiceId { get; set; }
    public Guid CashShiftId { get; set; }
    public Guid ReceivedByUserId { get; set; }
    public decimal Amount { get; set; }
    public DateTime ReceivedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class CashExpense
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? DepartmentId { get; set; }
    public Guid RequestId { get; set; }
    public Guid CashShiftId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Amount { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class JournalEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? DepartmentId { get; set; }
    public string ReferenceType { get; set; } = "";
    public Guid ReferenceId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public List<JournalLine> Lines { get; set; } = [];
}

public sealed class JournalLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid JournalEntryId { get; set; }
    public string AccountCode { get; set; } = "";
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
}

public sealed class AuditEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? DepartmentId { get; set; }
    public Guid StaffUserId { get; set; }
    public string Action { get; set; } = "";
    public string? Details { get; set; }
    public string EntityType { get; set; } = "";
    public Guid EntityId { get; set; }
    public DateTime RecordedAtUtc { get; set; } = DateTime.UtcNow;
}
