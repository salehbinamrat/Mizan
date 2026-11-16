namespace MizanMedical.Domain;

/// <summary>
/// Avoid a duplicate lookup only if the already-authorized list endpoint
/// queried exactly the same first page and normalized filter.
/// </summary>
public static class RefreshLookupRules
{
    public static bool CanReuseFirstPage(int listPage, int listPageSize,
        string? listSearch, string? pickerSearch) =>
        listPage == 1 && listPageSize == 100 &&
        string.Equals(listSearch?.Trim() ?? "", pickerSearch?.Trim() ?? "",
            StringComparison.Ordinal);
}
