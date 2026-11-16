namespace MizanMedical.Domain;

/// <summary>
/// Operation grants do not silently confer unrelated access. Every read prerequisite
/// must be explicitly approved by the system owner when the account is saved.
/// </summary>
public static class PermissionPrerequisiteRules
{
    private static readonly IReadOnlyDictionary<string, string[]> Requirements =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [HospitalPermissions.PatientsCreate] = [HospitalPermissions.PatientsRead],
            [HospitalPermissions.ServicesManage] = [HospitalPermissions.ServicesRead],
            [HospitalPermissions.InvoicesIssue] = [
                HospitalPermissions.PatientsRead,
                HospitalPermissions.ServicesRead,
                HospitalPermissions.InvoicesRead
            ],
            [HospitalPermissions.ShiftsOpen] = [HospitalPermissions.DesksRead, HospitalPermissions.ShiftsRead],
            [HospitalPermissions.ShiftsClose] = [HospitalPermissions.ShiftsRead],
            [HospitalPermissions.ReceiptsCreate] = [HospitalPermissions.InvoicesRead, HospitalPermissions.ShiftsRead],
            [HospitalPermissions.ExpensesCreate] = [HospitalPermissions.ShiftsRead],
            [HospitalPermissions.DesksReadAll] = [HospitalPermissions.DesksRead]
        };

    public static IReadOnlyList<string> Missing(IEnumerable<string> selectedPermissions)
    {
        var assigned = selectedPermissions.ToHashSet(StringComparer.Ordinal);
        var needed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var action in assigned)
            if (Requirements.TryGetValue(action, out var prerequisites))
                foreach (var prerequisite in prerequisites)
                    if (!assigned.Contains(prerequisite))
                        needed.Add(prerequisite);
        return needed.OrderBy(x => x, StringComparer.Ordinal).ToArray();
    }
}
