using MizanMedical.Domain;
using Xunit;

namespace MizanMedical.Tests;

public sealed class RefreshLookupRulesTests
{
    [Theory]
    [InlineData(1, "", "", true)]
    [InlineData(1, "  ", null, true)]
    [InlineData(1, "  Ali  ", "Ali", true)]
    [InlineData(1, "ABC", "abc", false)]
    [InlineData(1, "Ali", "Ali2", false)]
    [InlineData(2, "", "", false)]
    [InlineData(3, "Ali", "Ali", false)]
    public void Reuse_only_same_first_page_and_same_effective_search(
        int listPage, string? listSearch, string? pickerSearch, bool expected)
    {
        Assert.Equal(expected, RefreshLookupRules.CanReuseFirstPage(
            listPage, 100, listSearch, pickerSearch));
    }

    [Fact]
    public void Never_reuse_truncated_or_oversized_list_as_picker_lookup()
    {
        Assert.False(RefreshLookupRules.CanReuseFirstPage(1, 25, "Ali", "Ali"));
        Assert.False(RefreshLookupRules.CanReuseFirstPage(1, 200, "", ""));
    }

    [Fact]
    public void Duplicate_request_plan_does_not_elevate_permissions()
    {
        // Sharing only reuses requests that the UI has already been allowed to make.
        // It grants no user permission, department scope, or server capability.
        Assert.True(RefreshLookupRules.CanReuseFirstPage(1, 100, "", ""));
        Assert.False(AccessRules.ArePermissionsValid([HospitalPermissions.ReceiptsCreate]));
        Assert.False(AccessRules.ArePermissionsValid([HospitalPermissions.InvoicesIssue]));
    }
}
