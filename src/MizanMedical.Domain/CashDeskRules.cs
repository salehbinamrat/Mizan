namespace MizanMedical.Domain;

public static class CashDeskRules
{
    public static bool CanOperate(Guid userId, Guid? userDepartmentId, Guid? deskOwnerUserId,
        Guid? deskDepartmentId, bool deskActive) =>
        deskActive && userId != Guid.Empty && userDepartmentId is { } userDepartment &&
        userDepartment != Guid.Empty && deskOwnerUserId == userId &&
        deskDepartmentId == userDepartment;

    public static bool WithinDepartmentLimit(int activeUsers, int maxActiveUsers) =>
        maxActiveUsers is >= 1 and <= 10000 && activeUsers >= 0 && activeUsers < maxActiveUsers;

    public static bool CanSetUserLimit(int requestedLimit, int currentlyActive) =>
        requestedLimit is >= 1 and <= 10000 && currentlyActive >= 0 && requestedLimit >= currentlyActive;
}
