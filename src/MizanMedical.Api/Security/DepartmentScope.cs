using System.Security.Claims;
using MizanMedical.Domain;

namespace MizanMedical.Api.Security;

public sealed class DepartmentAccessDeniedException : Exception { }

public static class DepartmentScope
{
    public static bool CanSee(ClaimsPrincipal staff, Guid? departmentId) =>
        DepartmentClaims.CanRead(staff, departmentId);

    public static Guid ForNewRecord(ClaimsPrincipal staff, Guid? requested)
    {
        Guid? selected = requested ?? DepartmentClaims.Department(staff);
        if (selected is not { } id || id == Guid.Empty || !CanSee(staff, id))
            throw new DepartmentAccessDeniedException();
        return id;
    }
}
