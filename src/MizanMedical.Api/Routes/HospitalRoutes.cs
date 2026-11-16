using System.Data;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MizanMedical.Api.Data;
using MizanMedical.Api.Security;
using MizanMedical.Domain;

namespace MizanMedical.Api.Routes;

public static class HospitalRoutes
{
    public static void MapHospitalRoutes(this WebApplication app)
    {
        var api = app.MapGroup("/api");
        api.MapPost("/auth/login", Login).AllowAnonymous().RequireRateLimiting("login");
        var secured = api.MapGroup("").RequireAuthorization();
        secured.MapGet("/auth/me", (ClaimsPrincipal staff) => Results.Ok(new
        {
            Id = CurrentStaff.Id(staff), Username = staff.Identity?.Name,
            Name = staff.FindFirstValue("display_name"), Role = staff.FindFirstValue(ClaimTypes.Role),
            DepartmentId = DepartmentClaims.Department(staff), AllDepartments = DepartmentClaims.HasGlobalScope(staff),
            IsSystemOwner = DepartmentClaims.IsOwner(staff),
            Permissions = HospitalPermissions.All.Where(p => DepartmentClaims.HasPermission(staff, p)).ToArray()
        }));
        secured.MapPost("/auth/logout", Logout);
        secured.MapGet("/permissions/catalog", () => Results.Ok(HospitalPermissions.All))
            .RequireAuthorization(HospitalPermissions.UsersManage);
        secured.MapPost("/departments", AddDepartment).RequireAuthorization(HospitalPermissions.DepartmentsManage);
        secured.MapGet("/departments", ListDepartments);
        secured.MapPost("/users", AddUser).RequireAuthorization(HospitalPermissions.UsersManage);
        secured.MapGet("/users", ListUsers).RequireAuthorization(HospitalPermissions.UsersManage);
        secured.MapPut("/users/{userId:guid}", UpdateUser).RequireAuthorization(HospitalPermissions.UsersManage);
        secured.MapPost("/users/{userId:guid}/password", ResetUserPassword).RequireAuthorization(HospitalPermissions.UsersManage);
        secured.MapPost("/patients", AddPatient).RequireAuthorization(HospitalPermissions.PatientsCreate);
        secured.MapGet("/patients", ListPatients).RequireAuthorization(HospitalPermissions.PatientsRead);
        secured.MapPost("/services", AddService).RequireAuthorization(HospitalPermissions.ServicesManage);
        secured.MapGet("/services", ListServices).RequireAuthorization(HospitalPermissions.ServicesRead);
        secured.MapPost("/cash-desks", AddCashDesk).RequireAuthorization(HospitalPermissions.DesksManage);
        secured.MapGet("/cash-desks", ListCashDesks).RequireAuthorization(HospitalPermissions.DesksRead);
        secured.MapPost("/shifts/open", OpenShift).RequireAuthorization(HospitalPermissions.ShiftsOpen);
        secured.MapGet("/shifts/open", OpenShifts).RequireAuthorization(HospitalPermissions.ShiftsRead);
        secured.MapPost("/shifts/close", CloseShift).RequireAuthorization(HospitalPermissions.ShiftsClose);
        secured.MapPost("/invoices", CreateInvoice).RequireAuthorization(HospitalPermissions.InvoicesIssue);
        secured.MapGet("/invoices", ListInvoices).RequireAuthorization(HospitalPermissions.InvoicesRead);
        secured.MapGet("/invoices/{invoiceId:guid}", GetInvoice).RequireAuthorization(HospitalPermissions.InvoicesRead);
        secured.MapPost("/receipts", ReceivePayment).RequireAuthorization(HospitalPermissions.ReceiptsCreate);
        secured.MapPost("/expenses", AddExpense).RequireAuthorization(HospitalPermissions.ExpensesCreate);
        secured.MapGet("/reports/daily", DailyReport).RequireAuthorization(HospitalPermissions.ReportsRead);
        secured.MapGet("/audit/recent", AuditRecent).RequireAuthorization(HospitalPermissions.AuditRead);
    }

    private static string ValidText(string? value, string field, int max = 200)
    {
        value = value?.Trim();
        if (string.IsNullOrWhiteSpace(value) || value.Length > max)
            throw new DomainException($"{field}: مطلوب نص من 1 إلى {max} حرف.");
        return value;
    }

    private static Guid RequiredId(Guid id)
    {
        if (id == Guid.Empty) throw new DomainException("المعرف غير صالح.");
        return id;
    }

    private static void RequireRequestId(Guid id)
    {
        if (id == Guid.Empty) throw new DomainException("RequestId مطلوب لمنع تكرار العملية.");
    }

    private static AuditEvent Audit(ClaimsPrincipal staff, string action, string type, Guid id, Guid? departmentId = null) =>
        new() { StaffUserId = CurrentStaff.Id(staff), Action = action, EntityType = type,
                EntityId = id, DepartmentId = departmentId ?? DepartmentClaims.Department(staff) };

    private static JournalEntry Journal(ClaimsPrincipal staff, string kind, Guid id, Guid departmentId,
        params (string Account, decimal Debit, decimal Credit)[] rows)
    {
        FinanceRules.RequireBalanced(rows.Select(x => (x.Debit, x.Credit)));
        return new JournalEntry
        {
            ReferenceType = kind, ReferenceId = id, CreatedByUserId = CurrentStaff.Id(staff), DepartmentId = departmentId,
            Lines = rows.Select(x => new JournalLine
            { AccountCode = x.Account, Debit = x.Debit, Credit = x.Credit }).ToList()
        };
    }

    private static async Task<IResult> Login(LoginRequest request, HospitalDbContext db)
    {
        string username = (request.Username ?? "").Trim().ToLowerInvariant();
        if (username.Length is < 3 or > 80 || request.Password is null)
            return Results.Unauthorized();
        var user = await db.StaffUsers.FirstOrDefaultAsync(u => u.Username == username && u.IsActive);
        if (user is null || (user.LockedUntilUtc.HasValue && user.LockedUntilUtc > DateTime.UtcNow))
            return Results.Unauthorized();
        if (!PasswordSecurity.Verify(request.Password, user.PasswordHash, user.PasswordSalt))
        {
            user.FailedLogins++;
            if (user.FailedLogins >= 5)
            {
                user.LockedUntilUtc = DateTime.UtcNow.AddMinutes(15);
                user.FailedLogins = 0;
            }
            await db.SaveChangesAsync();
            return Results.Unauthorized();
        }
        user.FailedLogins = 0;
        user.LockedUntilUtc = null;
        string token = PasswordSecurity.NewSessionToken();
        var session = new LoginSession
        {
            StaffUserId = user.Id, TokenSha256 = PasswordSecurity.HashToken(token),
            ExpiresAtUtc = DateTime.UtcNow.AddHours(8)
        };
        db.LoginSessions.Add(session);
        await db.SaveChangesAsync();
        return Results.Ok(new { Token = token, ExpiresAtUtc = session.ExpiresAtUtc, User = new { user.Id, user.Username, user.FullName, Role = user.Role.ToString(), user.DepartmentId, AllDepartments = user.AllDepartments || user.IsSystemOwner, user.IsSystemOwner, Permissions = user.IsSystemOwner ? HospitalPermissions.All.ToArray() : await db.UserPermissions.Where(p => p.StaffUserId == user.Id).Select(p => p.Key).ToArrayAsync() } });
    }

    private static async Task<IResult> Logout(HttpRequest request, HospitalDbContext db)
    {
        string token = request.Headers.Authorization.ToString()[7..].Trim();
        string hash = PasswordSecurity.HashToken(token);
        await db.LoginSessions.Where(x => x.TokenSha256 == hash).ExecuteDeleteAsync();
        return Results.NoContent();
    }

    private static async Task<Guid?> ValidateAccountScope(HospitalDbContext db, Guid? departmentId, bool allDepartments, IEnumerable<string> permissions)
    {
        if (!AccessRules.CanAssignScope(true, departmentId, allDepartments))
            throw new DomainException("حدد قسما واحدا للمستخدم أو اختر صلاحية جميع الأقسام دون تحديد قسم.");
        if (!AccessRules.ArePermissionsValid(permissions))
            throw new DomainException("توجد صلاحية غير معروفة أو محجوزة لمسؤول النظام.");
        if (departmentId.HasValue && !await db.Departments.AnyAsync(d => d.Id == departmentId && d.IsActive))
            throw new DomainException("القسم غير موجود أو متوقف.");
        return departmentId;
    }

    private static string ValidateUsername(string value)
    {
        string username = ValidText(value, "اسم الدخول", 80).ToLowerInvariant();
        if (username.Length < 3 || !username.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '.'))
            throw new DomainException("اسم الدخول يجب أن يكون أحرفا إنجليزية صغيرة أو أرقاما أو شرطة سفلية أو نقطة.");
        return username;
    }

    private static HospitalRole ValidateRole(string input)
    {
        if (!Enum.TryParse<HospitalRole>(input, true, out var role) || !Enum.IsDefined(role))
            throw new DomainException("الدور الوظيفي غير معروف.");
        return role;
    }

    private static async Task<IResult> AddDepartment(CreateDepartmentRequest input, HospitalDbContext db, ClaimsPrincipal staff)
    {
        var dept = new Department
        {
            Code = ValidText(input.Code, "رمز القسم", 40).ToUpperInvariant(),
            Name = ValidText(input.Name, "اسم القسم", 150)
        };
        db.Departments.Add(dept);
        db.AuditEvents.Add(Audit(staff, "department.create", "department", dept.Id));
        await db.SaveChangesAsync();
        return Results.Created($"/api/departments/{dept.Id}", new { dept.Id, dept.Code, dept.Name });
    }

    private static async Task<IResult> ListDepartments(HospitalDbContext db, ClaimsPrincipal staff)
    {
        bool global = DepartmentClaims.HasGlobalScope(staff);
        Guid? own = DepartmentClaims.Department(staff);
        return Results.Ok(await db.Departments.AsNoTracking()
            .Where(d => d.IsActive && (global || (own != null && d.Id == own)))
            .OrderBy(d => d.Name).Select(d => new { d.Id, d.Code, d.Name }).ToListAsync());
    }

    private static async Task<IResult> AddUser(CreateUserRequest input, HospitalDbContext db, ClaimsPrincipal staff)
    {
        var permissions = input.Permissions ?? [];
        await ValidateAccountScope(db, input.DepartmentId, input.AllDepartments, permissions);
        var (hash, salt) = PasswordSecurity.Create(input.Password);
        var user = new StaffUser
        {
            Username = ValidateUsername(input.Username),
            FullName = ValidText(input.FullName, "اسم الموظف"), Role = ValidateRole(input.Role),
            DepartmentId = input.DepartmentId, AllDepartments = input.AllDepartments,
            IsSystemOwner = false, PasswordHash = hash, PasswordSalt = salt,
            Permissions = permissions.Distinct(StringComparer.Ordinal)
                .Select(k => new UserPermission { Key = k }).ToList()
        };
        db.StaffUsers.Add(user);
        db.AuditEvents.Add(new AuditEvent
        {
            StaffUserId = CurrentStaff.Id(staff), Action = "user.create.scoped", EntityType = "user", EntityId = user.Id,
            Details = JsonSerializer.Serialize(new { user.Role, user.DepartmentId, user.AllDepartments,
                Permissions = user.Permissions.Select(x => x.Key).OrderBy(x => x).ToArray() })
        });
        await db.SaveChangesAsync();
        return Results.Created($"/api/users/{user.Id}", new { user.Id, user.Username, user.FullName,
            Role = user.Role.ToString(), user.DepartmentId, user.AllDepartments, Permissions = permissions });
    }

    private static async Task<IResult> ListUsers(HospitalDbContext db) => Results.Ok(
        (await db.StaffUsers.AsNoTracking().Include(x => x.Permissions)
            .OrderBy(x => x.Username).ToListAsync()).Select(x => new
            {
                x.Id, x.Username, x.FullName, Role = x.Role.ToString(), x.IsActive,
                x.IsSystemOwner, x.DepartmentId, x.AllDepartments,
                Permissions = x.Permissions.Select(p => p.Key).OrderBy(y => y).ToArray()
            }).ToList());

    private static async Task<IResult> UpdateUser(Guid userId, UpdateUserRequest input, HospitalDbContext db, ClaimsPrincipal staff)
    {
        var user = await db.StaffUsers.Include(x => x.Permissions).FirstOrDefaultAsync(x => x.Id == userId);
        if (user is null) return Results.NotFound();
        if (user.IsSystemOwner) return Results.Problem(statusCode: 403, title: "حساب مسؤول البرنامج محمي.");
        var permissions = input.Permissions ?? [];
        await ValidateAccountScope(db, input.DepartmentId, input.AllDepartments, permissions);
        await using var trx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var before = new
        {
            Role = user.Role.ToString(), user.DepartmentId, user.AllDepartments, user.IsActive,
            Permissions = user.Permissions.Select(x => x.Key).OrderBy(x => x).ToArray()
        };
        user.FullName = ValidText(input.FullName, "الاسم");
        user.Role = ValidateRole(input.Role);
        user.DepartmentId = input.DepartmentId;
        user.AllDepartments = input.AllDepartments;
        user.IsActive = input.IsActive;
        var requested = permissions.ToHashSet(StringComparer.Ordinal);
        db.UserPermissions.RemoveRange(user.Permissions.Where(x => !requested.Contains(x.Key)).ToList());
        var existing = user.Permissions.Select(x => x.Key).ToHashSet(StringComparer.Ordinal);
        foreach (string key in requested.Except(existing))
            user.Permissions.Add(new UserPermission { StaffUserId = user.Id, Key = key });
        // Force re-authentication after any permission change, role change, or disable.
        await db.LoginSessions.Where(x => x.StaffUserId == user.Id).ExecuteDeleteAsync();
        db.AuditEvents.Add(new AuditEvent
        {
            StaffUserId = CurrentStaff.Id(staff), Action = "user.permissions.update", EntityType = "user", EntityId = user.Id,
            Details = JsonSerializer.Serialize(new { Before = before, After = new
            {
                Role = user.Role.ToString(), user.DepartmentId, user.AllDepartments, user.IsActive,
                Permissions = requested.OrderBy(x => x).ToArray()
            } })
        });
        await db.SaveChangesAsync();
        await trx.CommitAsync();
        return Results.Ok(new { user.Id, user.Username, user.DepartmentId, user.AllDepartments, user.IsActive });
    }

    private static async Task<IResult> ResetUserPassword(Guid userId, ResetPasswordRequest input, HospitalDbContext db, ClaimsPrincipal staff)
    {
        var user = await db.StaffUsers.FirstOrDefaultAsync(x => x.Id == userId);
        if (user is null) return Results.NotFound();
        if (user.IsSystemOwner) return Results.Problem(statusCode: 403, title: "إعادة تعيين كلمة مرور المسؤول الأساسي تتم بإجراء استعادة مستقل.");
        var (hash, salt) = PasswordSecurity.Create(input.Password);
        await using var trx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        user.PasswordHash = hash; user.PasswordSalt = salt;
        await db.LoginSessions.Where(x => x.StaffUserId == user.Id).ExecuteDeleteAsync();
        db.AuditEvents.Add(Audit(staff, "user.password.reset", "user", user.Id));
        await db.SaveChangesAsync();
        await trx.CommitAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> AddPatient(CreatePatientRequest input, HospitalDbContext db, ClaimsPrincipal staff)
    {
        Guid departmentId = DepartmentScope.ForNewRecord(staff, input.DepartmentId);
        if (!await db.Departments.AnyAsync(x => x.Id == departmentId && x.IsActive)) throw new DomainException("القسم غير متاح.");
        var patient = new Patient { FileNumber = ValidText(input.FileNumber, "رقم الملف", 60),
            FullName = ValidText(input.FullName, "اسم المريض"), DepartmentId = departmentId };
        db.Patients.Add(patient);
        db.AuditEvents.Add(Audit(staff, "patient.create", "patient", patient.Id, departmentId));
        await db.SaveChangesAsync();
        return Results.Created($"/api/patients/{patient.Id}", patient);
    }

    private static async Task<IResult> ListPatients(HospitalDbContext db, ClaimsPrincipal staff, string? search)
    {
        if (search?.Length > 100) throw new DomainException("البحث طويل جدا.");
        bool global = DepartmentClaims.HasGlobalScope(staff);
        Guid? own = DepartmentClaims.Department(staff);
        var query = db.Patients.AsNoTracking().Where(p => global || (own != null && p.DepartmentId == own));
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(x => x.FileNumber.Contains(search) || x.FullName.Contains(search));
        return Results.Ok(await query.OrderByDescending(x => x.CreatedAtUtc).Take(100)
            .Select(x => new { x.Id, x.FileNumber, x.FullName }).ToListAsync());
    }

    private static async Task<IResult> AddService(CreateServiceRequest input, HospitalDbContext db, ClaimsPrincipal staff)
    {
        FinanceRules.RequirePositiveMoney(input.Price, "سعر الخدمة");
        Guid departmentId = DepartmentScope.ForNewRecord(staff, input.DepartmentId);
        var department = await db.Departments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == departmentId && x.IsActive);
        if (department is null) throw new DomainException("القسم غير متاح.");
        var service = new MedicalService
        {
            DepartmentId = departmentId, Code = ValidText(input.Code, "رمز الخدمة", 60),
            Name = ValidText(input.Name, "الخدمة"), Department = department.Name,
            Price = input.Price
        };
        db.MedicalServices.Add(service);
        db.AuditEvents.Add(Audit(staff, "service.create", "service", service.Id, departmentId));
        await db.SaveChangesAsync();
        return Results.Created($"/api/services/{service.Id}", service);
    }

    private static async Task<IResult> ListServices(HospitalDbContext db, ClaimsPrincipal staff)
    {
        bool global = DepartmentClaims.HasGlobalScope(staff); Guid? own = DepartmentClaims.Department(staff);
        return Results.Ok(await db.MedicalServices.AsNoTracking()
            .Where(x => x.IsActive && (global || (own != null && x.DepartmentId == own)))
            .OrderBy(x => x.Department).ThenBy(x => x.Name)
            .Select(x => new { x.Id, x.Code, x.Name, x.Department, x.DepartmentId, x.Price }).ToListAsync());
    }

    private static async Task<IResult> AddCashDesk(CreateDeskRequest input, HospitalDbContext db, ClaimsPrincipal staff)
    {
        Guid dept = DepartmentScope.ForNewRecord(staff, input.DepartmentId);
        if (!await db.Departments.AnyAsync(x => x.Id == dept && x.IsActive)) throw new DomainException("القسم غير متاح.");
        var desk = new CashDesk { Name = ValidText(input.Name, "اسم الصندوق", 80), DepartmentId = dept };
        db.CashDesks.Add(desk);
        db.AuditEvents.Add(Audit(staff, "cashdesk.create", "cashdesk", desk.Id, dept));
        await db.SaveChangesAsync();
        return Results.Created($"/api/cash-desks/{desk.Id}", desk);
    }

    private static async Task<IResult> ListCashDesks(HospitalDbContext db, ClaimsPrincipal staff)
    {
        bool global = DepartmentClaims.HasGlobalScope(staff); Guid? own = DepartmentClaims.Department(staff);
        return Results.Ok(await db.CashDesks.AsNoTracking()
            .Where(x => x.IsActive && (global || (own != null && x.DepartmentId == own)))
            .OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Name, x.DepartmentId }).ToListAsync());
    }

    private static async Task<IResult> OpenShift(OpenShiftRequest input, HospitalDbContext db, ClaimsPrincipal staff)
    {
        FinanceRules.RequireNonNegativeMoney(input.OpeningCash, "رصيد افتتاح الصندوق");
        var desk = await db.CashDesks.AsNoTracking().FirstOrDefaultAsync(x => x.Id == RequiredId(input.CashDeskId) && x.IsActive);
        if (desk is null || !DepartmentScope.CanSee(staff, desk.DepartmentId))
            return Results.NotFound();
        var shift = new CashShift
        { CashDeskId = input.CashDeskId, CashierId = CurrentStaff.Id(staff),
          DepartmentId = desk.DepartmentId, OpeningCash = input.OpeningCash };
        db.CashShifts.Add(shift);
        db.AuditEvents.Add(Audit(staff, "shift.open", "shift", shift.Id, shift.DepartmentId));
        await db.SaveChangesAsync();
        return Results.Created($"/api/shifts/{shift.Id}", new { shift.Id, shift.CashDeskId, shift.OpeningCash, shift.OpenedAtUtc });
    }

    private static async Task<IResult> OpenShifts(HospitalDbContext db, ClaimsPrincipal staff)
    {
        bool global = DepartmentClaims.HasGlobalScope(staff); Guid? own = DepartmentClaims.Department(staff);
        Guid userId = CurrentStaff.Id(staff);
        return Results.Ok(await db.CashShifts.AsNoTracking()
            .Where(s => s.CashierId == userId && s.ClosedAtUtc == null &&
                        (global || (own != null && s.DepartmentId == own)))
            .Select(s => new { s.Id, s.CashDeskId, s.OpeningCash, s.OpenedAtUtc }).ToListAsync());
    }

    private static async Task<IResult> CloseShift(CloseShiftRequest input, HospitalDbContext db, ClaimsPrincipal staff)
    {
        FinanceRules.RequireNonNegativeMoney(input.CountedCash, "النقد الفعلي");
        await using var trx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var shift = await db.CashShifts.FirstOrDefaultAsync(x => x.Id == RequiredId(input.ShiftId));
        if (shift is null || !DepartmentScope.CanSee(staff, shift.DepartmentId) || shift.ClosedAtUtc is not null || shift.CashierId != CurrentStaff.Id(staff))
            throw new DomainException("هذه المناوبة ليست مفتوحة لحسابك.");
        if (shift.DepartmentId is null) return Results.NotFound();
        decimal received = await db.CashReceipts.Where(x => x.CashShiftId == shift.Id).SumAsync(x => (decimal?)x.Amount) ?? 0m;
        decimal spent = await db.CashExpenses.Where(x => x.CashShiftId == shift.Id).SumAsync(x => (decimal?)x.Amount) ?? 0m;
        shift.ExpectedCash = shift.OpeningCash + received - spent;
        shift.CountedCash = input.CountedCash;
        shift.Variance = input.CountedCash - shift.ExpectedCash;
        if (shift.Variance != 0 && string.IsNullOrWhiteSpace(input.Comment))
            throw new DomainException("يوجد فرق في الصندوق، يجب كتابة توضيح قبل الإقفال.");
        shift.CloseComment = input.Comment?.Trim() is { Length: > 0 } comment ? ValidText(comment, "الملاحظة", 500) : null;
        shift.ClosedAtUtc = DateTime.UtcNow;
        db.AuditEvents.Add(Audit(staff, "shift.close", "shift", shift.Id, shift.DepartmentId));
        await db.SaveChangesAsync();
        await trx.CommitAsync();
        return Results.Ok(new { shift.Id, shift.OpeningCash, shift.ExpectedCash, shift.CountedCash, shift.Variance, shift.ClosedAtUtc });
    }

    private static async Task<IResult> CreateInvoice(CreateInvoiceRequest input, HospitalDbContext db, ClaimsPrincipal staff)
    {
        RequireRequestId(input.RequestId);
        if (input.Lines is null || input.Lines.Count is < 1 or > 100)
            throw new DomainException("عدد أسطر الفاتورة يجب أن يكون بين 1 و100.");
        if (input.Lines.GroupBy(x => x.ServiceId).Any(g => g.Count() > 1))
            throw new DomainException("لا تكرر نفس الخدمة داخل الفاتورة، عدل الكمية.");
        var normalizedLines = input.Lines.OrderBy(x => x.ServiceId)
            .Select(x => $"{x.ServiceId:N}:{x.Quantity}");
        string fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{input.PatientId:N}|{string.Join("|", normalizedLines)}")));
        await using var trx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var previous = await db.Invoices.AsNoTracking().FirstOrDefaultAsync(i => i.RequestId == input.RequestId);
        if (previous is not null)
        {
            if (!DepartmentScope.CanSee(staff, previous.DepartmentId)) return Results.NotFound();
            if (previous.PatientId != input.PatientId || previous.RequestSignature != fingerprint)
                throw new DomainException("معرف العملية مستعمل لفاتورة أخرى.");
            return Results.Ok(new { previous.Id, previous.InvoiceNumber, previous.Total, previous.Paid, Repeated = true });
        }
        var patient = await db.Patients.AsNoTracking().FirstOrDefaultAsync(x => x.Id == RequiredId(input.PatientId));
        if (patient is null || !DepartmentScope.CanSee(staff, patient.DepartmentId)) return Results.NotFound();
        var ids = input.Lines.Select(x => RequiredId(x.ServiceId)).ToArray();
        var services = await db.MedicalServices.Where(x => ids.Contains(x.Id) && x.IsActive).ToDictionaryAsync(x => x.Id);
        if (services.Count != ids.Length) throw new DomainException("إحدى الخدمات غير موجودة أو موقوفة.");
        if (services.Values.Select(x => x.DepartmentId).Distinct().Count() != 1 ||
            services.Values.Any(x => x.DepartmentId != patient.DepartmentId) ||
            !DepartmentScope.CanSee(staff, patient.DepartmentId))
            throw new DepartmentAccessDeniedException();
        Guid dept = patient.DepartmentId ?? throw new DepartmentAccessDeniedException();
        var lines = input.Lines.Select(x =>
        {
            var service = services[x.ServiceId];
            return new InvoiceLine
            {
                MedicalServiceId = service.Id, ServiceCode = service.Code, ServiceName = service.Name,
                Department = service.Department, DepartmentId = dept, UnitPrice = service.Price, Quantity = x.Quantity,
                Total = FinanceRules.Total([(service.Price, x.Quantity)])
            };
        }).ToList();
        decimal total = FinanceRules.Total(lines.Select(x => (x.UnitPrice, x.Quantity)));
        var invoice = new Invoice
        {
            RequestId = input.RequestId, RequestSignature = fingerprint,
            InvoiceNumber = $"INV-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..10].ToUpperInvariant()}",
            PatientId = input.PatientId, IssuedByUserId = CurrentStaff.Id(staff),
            DepartmentId = dept, Total = total, Paid = 0m, Lines = lines
        };
        db.Invoices.Add(invoice);
        db.JournalEntries.Add(Journal(staff, "invoice", invoice.Id, dept,
            (Accounts.PatientReceivables, total, 0m), (Accounts.MedicalRevenue, 0m, total)));
        db.AuditEvents.Add(Audit(staff, "invoice.issue", "invoice", invoice.Id, dept));
        await db.SaveChangesAsync();
        await trx.CommitAsync();
        return Results.Created($"/api/invoices/{invoice.Id}", new { invoice.Id, invoice.InvoiceNumber, invoice.Total, invoice.Paid });
    }

    private static async Task<IResult> ListInvoices(HospitalDbContext db, ClaimsPrincipal staff)
    {
        bool global = DepartmentClaims.HasGlobalScope(staff); Guid? own = DepartmentClaims.Department(staff);
        return Results.Ok(await db.Invoices.AsNoTracking()
            .Where(i => global || (own != null && i.DepartmentId == own))
            .OrderByDescending(i => i.IssuedAtUtc).Take(100)
            .Select(i => new
            {
                i.Id, i.InvoiceNumber, i.IssuedAtUtc, i.Total, i.Paid,
                Balance = i.Total - i.Paid,
                PatientName = db.Patients.Where(p => p.Id == i.PatientId).Select(p => p.FullName).FirstOrDefault()
            }).ToListAsync());
    }

    private static async Task<IResult> GetInvoice(Guid invoiceId, HospitalDbContext db, ClaimsPrincipal staff)
    {
        var invoice = await db.Invoices.AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == invoiceId);
        return invoice is null || !DepartmentScope.CanSee(staff, invoice.DepartmentId) ? Results.NotFound() : Results.Ok(new
        {
            invoice.Id, invoice.InvoiceNumber, invoice.PatientId, invoice.Total, invoice.Paid,
            Balance = invoice.Total - invoice.Paid, invoice.IssuedAtUtc,
            Lines = invoice.Lines.Select(x => new { x.ServiceName, x.Department, x.UnitPrice, x.Quantity, x.Total })
        });
    }

    private static async Task<IResult> ReceivePayment(ReceivePaymentRequest input, HospitalDbContext db, ClaimsPrincipal staff)
    {
        RequireRequestId(input.RequestId);
        FinanceRules.RequirePositiveMoney(input.Amount, "المبلغ المحصل");
        await using var trx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var previous = await db.CashReceipts.AsNoTracking().FirstOrDefaultAsync(x => x.RequestId == input.RequestId);
        if (previous is not null)
        {
            if (!DepartmentScope.CanSee(staff, previous.DepartmentId)) return Results.NotFound();
            if (previous.InvoiceId != input.InvoiceId || previous.CashShiftId != input.ShiftId || previous.Amount != input.Amount)
                throw new DomainException("معرف العملية مستخدم لتحصيل آخر.");
            return Results.Ok(new { previous.Id, previous.Amount, Repeated = true });
        }
        var shift = await db.CashShifts.FirstOrDefaultAsync(x => x.Id == RequiredId(input.ShiftId));
        if (shift is null || !DepartmentScope.CanSee(staff, shift.DepartmentId) || shift.ClosedAtUtc is not null || shift.CashierId != CurrentStaff.Id(staff))
            throw new DomainException("افتح مناوبة صندوق لحسابك أولا.");
        if (shift.DepartmentId is null) return Results.NotFound();
        var invoice = await db.Invoices.FirstOrDefaultAsync(x => x.Id == RequiredId(input.InvoiceId));
        if (invoice is null || !DepartmentScope.CanSee(staff, invoice.DepartmentId) ||
            invoice.DepartmentId != shift.DepartmentId) return Results.NotFound();
        FinanceRules.CheckPayment(invoice.Total, invoice.Paid, input.Amount);
        invoice.Paid += input.Amount;
        var receipt = new CashReceipt
        {
            RequestId = input.RequestId, InvoiceId = invoice.Id,
            CashShiftId = shift.Id, ReceivedByUserId = CurrentStaff.Id(staff),
            DepartmentId = shift.DepartmentId, Amount = input.Amount
        };
        db.CashReceipts.Add(receipt);
        db.JournalEntries.Add(Journal(staff, "receipt", receipt.Id, shift.DepartmentId!.Value,
            (Accounts.Cash, input.Amount, 0m), (Accounts.PatientReceivables, 0m, input.Amount)));
        db.AuditEvents.Add(Audit(staff, "receipt.create", "receipt", receipt.Id, shift.DepartmentId));
        await db.SaveChangesAsync();
        await trx.CommitAsync();
        return Results.Created($"/api/receipts/{receipt.Id}", new { receipt.Id, receipt.Amount, InvoiceBalance = invoice.Total - invoice.Paid });
    }

    private static async Task<IResult> AddExpense(CreateExpenseRequest input, HospitalDbContext db, ClaimsPrincipal staff)
    {
        RequireRequestId(input.RequestId);
        FinanceRules.RequirePositiveMoney(input.Amount, "المصروف");
        string category = ValidText(input.Category, "نوع المصروف", 100);
        string description = ValidText(input.Description, "بيان المصروف", 500);
        await using var trx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var previous = await db.CashExpenses.AsNoTracking().FirstOrDefaultAsync(x => x.RequestId == input.RequestId);
        if (previous is not null)
        {
            if (!DepartmentScope.CanSee(staff, previous.DepartmentId)) return Results.NotFound();
            if (previous.Amount != input.Amount || previous.CashShiftId != input.ShiftId
                || previous.Category != category || previous.Description != description)
                throw new DomainException("معرف العملية مستخدم لمصروف آخر.");
            return Results.Ok(new { previous.Id, previous.Amount, Repeated = true });
        }
        var shift = await db.CashShifts.FirstOrDefaultAsync(x => x.Id == RequiredId(input.ShiftId));
        if (shift is null || !DepartmentScope.CanSee(staff, shift.DepartmentId) || shift.ClosedAtUtc is not null || shift.CashierId != CurrentStaff.Id(staff))
            throw new DomainException("يجب فتح مناوبة صندوق على الحساب نفسه قبل تسجيل مصروف نقدي.");
        if (shift.DepartmentId is null) return Results.NotFound();
        decimal receiptsInShift = await db.CashReceipts.Where(x => x.CashShiftId == shift.Id)
            .SumAsync(x => (decimal?)x.Amount) ?? 0m;
        decimal expensesInShift = await db.CashExpenses.Where(x => x.CashShiftId == shift.Id)
            .SumAsync(x => (decimal?)x.Amount) ?? 0m;
        if (input.Amount > shift.OpeningCash + receiptsInShift - expensesInShift)
            throw new DomainException("الصندوق لا يحتوي على نقدية كافية لتسجيل هذا المصروف.");
        var expense = new CashExpense
        {
            RequestId = input.RequestId, CashShiftId = shift.Id, CreatedByUserId = CurrentStaff.Id(staff),
            Category = category, Description = description, Amount = input.Amount,
            DepartmentId = shift.DepartmentId
        };
        db.CashExpenses.Add(expense);
        db.JournalEntries.Add(Journal(staff, "expense", expense.Id, shift.DepartmentId!.Value,
            (Accounts.OperatingExpenses, input.Amount, 0m), (Accounts.Cash, 0m, input.Amount)));
        db.AuditEvents.Add(Audit(staff, "expense.create", "expense", expense.Id, shift.DepartmentId));
        await db.SaveChangesAsync();
        await trx.CommitAsync();
        return Results.Created($"/api/expenses/{expense.Id}", new { expense.Id, expense.Amount, expense.Category });
    }

    private static async Task<IResult> DailyReport(HospitalDbContext db, ClaimsPrincipal staff, string? day)
    {
        TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById(Environment.GetEnvironmentVariable("MIZAN_TIMEZONE") ?? "Asia/Aden");
        DateOnly date;
        if (string.IsNullOrWhiteSpace(day))
            date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone));
        else if (!DateOnly.TryParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            throw new DomainException("صيغة التاريخ المطلوبة yyyy-MM-dd.");
        DateTime startLocal = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        DateTime startUtc = TimeZoneInfo.ConvertTimeToUtc(startLocal, zone);
        DateTime endUtc = TimeZoneInfo.ConvertTimeToUtc(startLocal.AddDays(1), zone);
        bool global = DepartmentClaims.HasGlobalScope(staff); Guid? own = DepartmentClaims.Department(staff);
        decimal invoiced = await db.Invoices.Where(x => (global || (own != null && x.DepartmentId == own)) && x.IssuedAtUtc >= startUtc && x.IssuedAtUtc < endUtc)
            .SumAsync(x => (decimal?)x.Total) ?? 0m;
        decimal received = await db.CashReceipts.Where(x => (global || (own != null && x.DepartmentId == own)) && x.ReceivedAtUtc >= startUtc && x.ReceivedAtUtc < endUtc)
            .SumAsync(x => (decimal?)x.Amount) ?? 0m;
        decimal spent = await db.CashExpenses.Where(x => (global || (own != null && x.DepartmentId == own)) && x.CreatedAtUtc >= startUtc && x.CreatedAtUtc < endUtc)
            .SumAsync(x => (decimal?)x.Amount) ?? 0m;
        int invoices = await db.Invoices.CountAsync(x => (global || (own != null && x.DepartmentId == own)) && x.IssuedAtUtc >= startUtc && x.IssuedAtUtc < endUtc);
        return Results.Ok(new
        {
            Day = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Currency = Environment.GetEnvironmentVariable("MIZAN_CURRENCY") ?? "YER",
            InvoiceCount = invoices, InvoicedAmount = invoiced, CashReceived = received,
            CashSpent = spent, NetCashMovement = received - spent,
            Note = "الإيراد المحاسبي ليس بالضرورة نقدا محصلا، ولا تمثل هذه المؤشرات قائمة أرباح وخسائر مكتملة."
        });
    }

    private static async Task<IResult> AuditRecent(HospitalDbContext db, ClaimsPrincipal staff)
    {
        bool global = DepartmentClaims.HasGlobalScope(staff); Guid? own = DepartmentClaims.Department(staff);
        return Results.Ok(await db.AuditEvents.AsNoTracking()
            .Where(x => global || (own != null && x.DepartmentId == own))
            .OrderByDescending(x => x.RecordedAtUtc).Take(100).ToListAsync());
    }

    public sealed record LoginRequest(string Username, string Password);
    public sealed record CreateDepartmentRequest(string Code, string Name);
    public sealed record CreateUserRequest(string Username, string FullName, string Password, string Role,
        Guid? DepartmentId, bool AllDepartments, List<string>? Permissions);
    public sealed record UpdateUserRequest(string FullName, string Role, Guid? DepartmentId,
        bool AllDepartments, bool IsActive, List<string>? Permissions);
    public sealed record ResetPasswordRequest(string Password);
    public sealed record CreatePatientRequest(string FileNumber, string FullName, Guid? DepartmentId = null);
    public sealed record CreateServiceRequest(string Code, string Name, string Department, decimal Price, Guid? DepartmentId = null);
    public sealed record CreateDeskRequest(string Name, Guid? DepartmentId = null);
    public sealed record OpenShiftRequest(Guid CashDeskId, decimal OpeningCash);
    public sealed record CloseShiftRequest(Guid ShiftId, decimal CountedCash, string? Comment);
    public sealed record InvoiceServiceRequest(Guid ServiceId, int Quantity);
    public sealed record CreateInvoiceRequest(Guid RequestId, Guid PatientId, List<InvoiceServiceRequest> Lines);
    public sealed record ReceivePaymentRequest(Guid RequestId, Guid InvoiceId, Guid ShiftId, decimal Amount);
    public sealed record CreateExpenseRequest(Guid RequestId, Guid ShiftId, string Category, string Description, decimal Amount);
}
