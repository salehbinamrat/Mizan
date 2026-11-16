using System.Net;
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

// Initial schema for pilot; production upgrades must use reviewed EF migrations.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<HospitalDbContext>();
    await db.Database.EnsureCreatedAsync();
    if (!await db.StaffUsers.AnyAsync(u => u.IsSystemOwner))
    {
        string? initialName = Environment.GetEnvironmentVariable("MIZAN_BOOTSTRAP_USERNAME");
        string? initialPassword = Environment.GetEnvironmentVariable("MIZAN_BOOTSTRAP_PASSWORD");
        if (string.IsNullOrWhiteSpace(initialName) || string.IsNullOrWhiteSpace(initialPassword))
            throw new InvalidOperationException("Set MIZAN_BOOTSTRAP_USERNAME and MIZAN_BOOTSTRAP_PASSWORD for the first start. No default admin account exists.");
        string adminUsername = initialName.Trim().ToLowerInvariant();
        if (adminUsername.Length is < 3 or > 80 ||
            !adminUsername.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '.'))
            throw new InvalidOperationException("Bootstrap admin username must contain only lowercase English letters, digits, underscores or periods.");
        var (hash, salt) = PasswordSecurity.Create(initialPassword);
        db.StaffUsers.Add(new StaffUser
        {
            Username = adminUsername, FullName = "مدير النظام",
            Role = HospitalRole.Admin, IsSystemOwner = true, AllDepartments = true,
            PasswordHash = hash, PasswordSalt = salt
        });
        await db.SaveChangesAsync();
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
app.MapHospitalRoutes();
await app.RunAsync();
