using Microsoft.EntityFrameworkCore;

namespace MizanMedical.Api.Data;

public sealed class HospitalDbContext(DbContextOptions<HospitalDbContext> options) : DbContext(options)
{
    public DbSet<StaffUser> StaffUsers => Set<StaffUser>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<UserPermission> UserPermissions => Set<UserPermission>();
    public DbSet<LoginSession> LoginSessions => Set<LoginSession>();
    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<MedicalService> MedicalServices => Set<MedicalService>();
    public DbSet<CashDesk> CashDesks => Set<CashDesk>();
    public DbSet<CashShift> CashShifts => Set<CashShift>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();
    public DbSet<CashReceipt> CashReceipts => Set<CashReceipt>();
    public DbSet<CashExpense> CashExpenses => Set<CashExpense>();
    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();
    public DbSet<JournalLine> JournalLines => Set<JournalLine>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<StaffUser>().HasIndex(x => x.Username).IsUnique();
        builder.Entity<Department>().HasIndex(x => x.Code).IsUnique();
        builder.Entity<Department>().Property(x => x.Code).HasMaxLength(40);
        builder.Entity<Department>().Property(x => x.Name).HasMaxLength(150);
        builder.Entity<StaffUser>().HasOne<Department>().WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<UserPermission>().HasKey(x => new { x.StaffUserId, x.Key });
        builder.Entity<UserPermission>().HasOne(x => x.StaffUser).WithMany(x => x.Permissions).HasForeignKey(x => x.StaffUserId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<UserPermission>().Property(x => x.Key).HasMaxLength(64);

        builder.Entity<LoginSession>().HasOne<StaffUser>().WithMany().HasForeignKey(x => x.StaffUserId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Invoice>().HasOne<Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Invoice>().HasOne<StaffUser>().WithMany().HasForeignKey(x => x.IssuedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<InvoiceLine>().HasOne<MedicalService>().WithMany().HasForeignKey(x => x.MedicalServiceId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<CashShift>().HasOne<CashDesk>().WithMany().HasForeignKey(x => x.CashDeskId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<CashShift>().HasOne<StaffUser>().WithMany().HasForeignKey(x => x.CashierId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<CashReceipt>().HasOne<Invoice>().WithMany().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<CashReceipt>().HasOne<CashShift>().WithMany().HasForeignKey(x => x.CashShiftId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<CashExpense>().HasOne<CashShift>().WithMany().HasForeignKey(x => x.CashShiftId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<JournalLine>().ToTable(t => t.HasCheckConstraint("CK_JournalLine_DebitCredit", "\"Debit\" >= 0 AND \"Credit\" >= 0 AND ((\"Debit\" > 0 AND \"Credit\" = 0) OR (\"Debit\" = 0 AND \"Credit\" > 0))"));
        builder.Entity<StaffUser>().Property(x => x.Role).HasConversion<string>().HasMaxLength(30);
        builder.Entity<StaffUser>().Property(x => x.Username).HasMaxLength(80);
        builder.Entity<StaffUser>().Property(x => x.FullName).HasMaxLength(200);
        builder.Entity<LoginSession>().HasIndex(x => x.TokenSha256).IsUnique();
        builder.Entity<LoginSession>().HasIndex(x => x.ExpiresAtUtc);
        builder.Entity<Patient>().HasIndex(x => x.FileNumber).IsUnique();
        builder.Entity<Patient>().Property(x => x.FileNumber).HasMaxLength(60);
        builder.Entity<Patient>().Property(x => x.FullName).HasMaxLength(200);
        builder.Entity<MedicalService>().HasIndex(x => new { x.DepartmentId, x.Code }).IsUnique();
        builder.Entity<MedicalService>().Property(x => x.Code).HasMaxLength(60);
        builder.Entity<MedicalService>().Property(x => x.Name).HasMaxLength(200);
        builder.Entity<MedicalService>().Property(x => x.Department).HasMaxLength(100);
        builder.Entity<MedicalService>().Property(x => x.Price).HasPrecision(20, 2);
        builder.Entity<CashDesk>().HasIndex(x => new { x.DepartmentId, x.Name }).IsUnique();
        builder.Entity<CashDesk>().Property(x => x.Name).HasMaxLength(80);
        builder.Entity<CashShift>().HasIndex(x => x.CashDeskId).IsUnique().HasFilter("\"ClosedAtUtc\" IS NULL");
        builder.Entity<CashShift>().HasIndex(x => x.CashierId);
        builder.Entity<CashShift>().Property(x => x.OpeningCash).HasPrecision(20, 2);
        builder.Entity<CashShift>().Property(x => x.ExpectedCash).HasPrecision(20, 2);
        builder.Entity<CashShift>().Property(x => x.CountedCash).HasPrecision(20, 2);
        builder.Entity<CashShift>().Property(x => x.Variance).HasPrecision(20, 2);
        builder.Entity<CashShift>().Property(x => x.CloseComment).HasMaxLength(500);
        builder.Entity<Invoice>().HasIndex(x => x.RequestId).IsUnique();
        builder.Entity<Invoice>().Property(x => x.RequestSignature).HasMaxLength(64);
        builder.Entity<Invoice>().HasIndex(x => x.InvoiceNumber).IsUnique();
        builder.Entity<Invoice>().HasIndex(x => x.PatientId);
        builder.Entity<Invoice>().HasIndex(x => x.IssuedAtUtc);
        builder.Entity<Invoice>().Property(x => x.Total).HasPrecision(20, 2);
        builder.Entity<Invoice>().Property(x => x.Paid).HasPrecision(20, 2);
        builder.Entity<Invoice>().HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<InvoiceLine>().Property(x => x.UnitPrice).HasPrecision(20, 2);
        builder.Entity<InvoiceLine>().Property(x => x.Total).HasPrecision(20, 2);
        builder.Entity<InvoiceLine>().Property(x => x.ServiceName).HasMaxLength(200);
        builder.Entity<InvoiceLine>().Property(x => x.ServiceCode).HasMaxLength(60);
        builder.Entity<InvoiceLine>().Property(x => x.Department).HasMaxLength(100);
        builder.Entity<CashReceipt>().HasIndex(x => x.RequestId).IsUnique();
        builder.Entity<CashReceipt>().HasIndex(x => x.CashShiftId);
        builder.Entity<CashReceipt>().HasIndex(x => x.ReceivedAtUtc);
        builder.Entity<CashReceipt>().Property(x => x.Amount).HasPrecision(20, 2);
        builder.Entity<CashExpense>().HasIndex(x => x.RequestId).IsUnique();
        builder.Entity<CashExpense>().HasIndex(x => x.CashShiftId);
        builder.Entity<CashExpense>().HasIndex(x => x.CreatedAtUtc);
        builder.Entity<CashExpense>().Property(x => x.Amount).HasPrecision(20, 2);
        builder.Entity<CashExpense>().Property(x => x.Category).HasMaxLength(100);
        builder.Entity<CashExpense>().Property(x => x.Description).HasMaxLength(500);
        builder.Entity<JournalEntry>().HasIndex(x => new { x.ReferenceType, x.ReferenceId }).IsUnique();
        builder.Entity<JournalEntry>().HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.JournalEntryId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<JournalLine>().Property(x => x.AccountCode).HasMaxLength(20);
        builder.Entity<JournalLine>().Property(x => x.Debit).HasPrecision(20, 2);
        builder.Entity<JournalLine>().Property(x => x.Credit).HasPrecision(20, 2);
        builder.Entity<AuditEvent>().HasIndex(x => x.RecordedAtUtc);
        // Null DepartmentId denotes pre-migration data, NEVER a public/global department.
        // All ordinary data is tagged; users with limited scopes cannot query legacy null rows.

        builder.Entity<AuditEvent>().Property(x => x.Action).HasMaxLength(80);
        builder.Entity<AuditEvent>().Property(x => x.EntityType).HasMaxLength(60);
        builder.Entity<AuditEvent>().Property(x => x.Details).HasMaxLength(2048);
        foreach (var type in new[] { typeof(Patient), typeof(MedicalService), typeof(CashDesk),
            typeof(CashShift), typeof(Invoice), typeof(InvoiceLine), typeof(CashReceipt),
            typeof(CashExpense), typeof(JournalEntry), typeof(AuditEvent) })
        {
            builder.Entity(type).HasOne(typeof(Department), null).WithMany()
                .HasForeignKey(nameof(Patient.DepartmentId)).OnDelete(DeleteBehavior.Restrict);
            builder.Entity(type).HasIndex(nameof(Patient.DepartmentId));
        }
    }
}
