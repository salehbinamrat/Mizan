using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MizanMedical.Api.Data;
using MizanMedical.Domain;

namespace MizanMedical.Api.Security;

public sealed class SessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    HospitalDbContext db)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string auth = Request.Headers.Authorization.ToString();
        if (!auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();
        string token = auth[7..].Trim();
        if (token.Length != 64 || !token.All(Uri.IsHexDigit))
            return AuthenticateResult.Fail("Invalid session token.");
        string hashed = PasswordSecurity.HashToken(token);
        var session = await db.LoginSessions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TokenSha256 == hashed && x.ExpiresAtUtc > DateTime.UtcNow);
        if (session is null) return AuthenticateResult.Fail("Session expired.");
        var user = await db.StaffUsers.AsNoTracking().Include(x => x.Permissions)
            .FirstOrDefaultAsync(x => x.Id == session.StaffUserId && x.IsActive);
        if (user is null) return AuthenticateResult.Fail("User disabled.");
        ClaimsIdentity identity = new([
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim("display_name", user.FullName),
            new Claim(DepartmentClaims.SystemOwner, user.IsSystemOwner ? "true" : "false"),
            new Claim(DepartmentClaims.AllDepartments, user.AllDepartments ? "true" : "false")
        ], Scheme.Name);
        if (user.DepartmentId.HasValue)
            identity.AddClaim(new Claim(DepartmentClaims.DepartmentId, user.DepartmentId.Value.ToString()));
        foreach (var grant in user.Permissions)
            if (HospitalPermissions.All.Contains(grant.Key, StringComparer.Ordinal) &&
                (user.IsSystemOwner || !HospitalPermissions.OwnerOnly.Contains(grant.Key)))
                identity.AddClaim(new Claim(DepartmentClaims.Permission, grant.Key));
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }
}

public static class CurrentStaff
{
    public static Guid Id(ClaimsPrincipal principal) =>
        Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException("Not signed in"));
}
