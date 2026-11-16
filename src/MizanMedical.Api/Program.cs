using System.Net;
using System.Data;
using System.Security.Cryptography;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using MizanMedical.Api.Data;
using MizanMedical.Api.Routes;
using MizanMedical.Api.Security;
using MizanMedical.Domain;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
string connectionString = Environment.GetEnvironmentVariable("MIZAN_DB_CONNECTION")
    ?? throw new InvalidOperationException("MIZAN_DB_CONNECTION must be configured with a central PostgreSQL database.");

builder.Services.AddDbContext<HospitalDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddSingleton<InitialOwnerSetupState>();
builder.Services.AddAuthentication("Session").AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>("Session", _ => { });
builder.Services.AddAuthorization(options =>
{
    foreach (var permission in HospitalPermissions.All)
        options.AddPolicy(permission, policy => policy.RequireAuthenticatedUser()
            .RequireAssertion(context => DepartmentClaims.HasPermission(context.User, permission)));
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0,
            AutoReplenishment = true
        }));
});

var app = builder.Build();

// Initial schema for pilot only; production deployments must use reviewed EF migrations.
// First-run setup does not reset databases, create a second owner, or recover lost owner credentials.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
    var setup = scope.ServiceProvider.GetRequiredService<InitialOwnerSetupState>();
    await db.Database.EnsureCreatedAsync();
    // Fail closed before any staff/password queries if an existing database has
    // not received its reviewed PostgreSQL schema upgrades.
    await DatabaseSchemaCompatibilityGuard.EnsureCompatibleAsync(connectionString);
    bool anyOwner = await db.StaffUsers.AnyAsync(u => u.IsSystemOwner);
    if (!anyOwner)
    {
        // Existing accounts with no owner require a supervised recovery procedure,
        // never a public endpoint that recreates an owner.
        if (await db.StaffUsers.AnyAsync())
            throw new InvalidOperationException(
                "Staff accounts exist but no system owner was found. Stop and use an audited recovery procedure.");
        string? initialName = Environment.GetEnvironmentVariable("MIZAN_BOOTSTRAP_USERNAME");
        string? initialPassword = Environment.GetEnvironmentVariable("MIZAN_BOOTSTRAP_PASSWORD");
        if (!string.IsNullOrWhiteSpace(initialName) && !string.IsNullOrWhiteSpace(initialPassword))
        {
            string normalized = InitialOwnerSetupRules.Validate("مدير النظام", initialName,
                initialPassword, initialPassword);
            var (hash, salt) = PasswordSecurity.Create(initialPassword);
            db.StaffUsers.Add(new StaffUser
            {
                Username = normalized, FullName = "مدير النظام",
                Role = HospitalRole.Admin, IsSystemOwner = true, AllDepartments = true,
                PasswordHash = hash, PasswordSalt = salt
            });
            await db.SaveChangesAsync();
        }
        else if (initialName is not null || initialPassword is not null)
        {
            throw new InvalidOperationException("Both bootstrap credentials must be provided together.");
        }
        else
        {
            // Do not emit the pairing code into redirected service logs. For non-interactive
            // deployments, the existing server-side bootstrap environment method remains available.
            if (!Environment.UserInteractive || Console.IsOutputRedirected)
                throw new InvalidOperationException(
                    "First-run interactive setup needs the local server console. " +
                    "For Windows services, provision the first owner using server-only bootstrap environment variables.");
            string pairingCode = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            setup.Activate(pairingCode);
            Console.WriteLine("==============================================");
            Console.WriteLine("Mizan Medical FIRST-RUN setup (local server only)");
            Console.WriteLine("One-time setup code: " + pairingCode);
            Console.WriteLine("Enter this code in the Windows first-run setup wizard ON THIS SERVER.");
            Console.WriteLine("It expires when the server stops or after the owner is created.");
            Console.WriteLine("==============================================");
        }
    }
}

app.Use(async (context, next) =>
{
    try { await next(); }
    catch (DomainException ex) when (!context.Response.HasStarted)
    {
        await Results.Problem(statusCode: 400, title: "الطلب غير صحيح", detail: ex.Message)
            .ExecuteAsync(context);
    }
    catch (DepartmentAccessDeniedException) when (!context.Response.HasStarted)
    {
        await Results.Problem(statusCode: 403, title: "لا تملك صلاحية الوصول إلى بيانات هذا القسم")
            .ExecuteAsync(context);
    }
    catch (DbUpdateException ex) when (!context.Response.HasStarted)
    {
        context.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("Database").LogWarning(ex, "Database write conflict");
        await Results.Problem(statusCode: 409, title: "تعارض في البيانات", detail: "قد تكون العملية مكررة أو تغيرت البيانات. حدث الصفحة وتحقق من النتيجة قبل إعادة المحاولة.")
            .ExecuteAsync(context);
    }
    catch (PostgresException ex) when (!context.Response.HasStarted && (ex.SqlState == "40001" || ex.SqlState == "40P01" || ex.SqlState == "23505"))
    {
        context.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("Database").LogWarning(ex, "Concurrent transaction conflict");
        await Results.Problem(statusCode: 409, title: "تعارض عمليات متزامنة", detail: "تحقق من العملية السابقة قبل إعادة الإرسال.")
            .ExecuteAsync(context);
    }
});

// Never accept staff credentials over unencrypted LAN connections.
app.Use(async (context, next) =>
{
    IPAddress? remote = context.Connection.RemoteIpAddress;
    if (!context.Request.IsHttps && remote is not null && !IPAddress.IsLoopback(remote))
    {
        await Results.Problem(statusCode: 426, title: "يجب الاتصال عبر HTTPS")
            .ExecuteAsync(context);
        return;
    }
    await next();
});
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/health", () => Results.Ok(new { Status = "ok", App = "MizanMedical" }));

// The status is safe to check from desktop clients, but the owner creation
// endpoint can be used only from the physical server over loopback.
app.MapGet("/api/initial-setup/status", async (
    HttpContext context, HospitalDbContext db, InitialOwnerSetupState setup) =>
{
    bool empty = !await db.StaffUsers.AnyAsync();
    bool local = context.Connection.RemoteIpAddress is { } address && IPAddress.IsLoopback(address);
    return Results.Ok(new
    {
        Required = empty,
        CanInitialize = InitialOwnerSetupRules.CanInitialize(local, !empty, setup.Enabled)
    });
}).AllowAnonymous();

app.MapPost("/api/initial-setup/complete", async (
    HttpContext context, InitialOwnerSetupRequest request,
    HospitalDbContext db, InitialOwnerSetupState setup) =>
{
    bool local = context.Connection.RemoteIpAddress is { } address && IPAddress.IsLoopback(address);
    if (!local) return Results.StatusCode(StatusCodes.Status403Forbidden);
    if (!setup.Matches(request.SetupCode)) return Results.Unauthorized();

    // Serializable predicate read ensures concurrent first-run requests cannot create
    // two owners. PostgreSQL aborts a conflicting transaction with SQLSTATE 40001.
    await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
    bool anyStaff = await db.StaffUsers.AnyAsync();
    if (!InitialOwnerSetupRules.CanInitialize(local, anyStaff, setup.Enabled))
        return Results.Conflict(new { Title = "تمت تهيئة حساب المسؤول الأول مسبقا." });

    string username = InitialOwnerSetupRules.Validate(
        request.FullName, request.Username, request.Password, request.ConfirmPassword);
    var (hash, salt) = PasswordSecurity.Create(request.Password!);
    var owner = new StaffUser
    {
        Username = username, FullName = request.FullName!.Trim(),
        Role = HospitalRole.Admin, IsSystemOwner = true, AllDepartments = true,
        PasswordHash = hash, PasswordSalt = salt
    };
    db.StaffUsers.Add(owner);
    await db.SaveChangesAsync();
    await transaction.CommitAsync();
    setup.Disable(); // the pairing code is now unusable
    return Results.Created("/api/auth/login", new { owner.Id, owner.Username, owner.FullName });
}).AllowAnonymous().RequireRateLimiting("login");
app.MapHospitalRoutes();
await app.RunAsync();
