using MizanMedical.Domain;
using Xunit;

namespace MizanMedical.Tests;

public sealed class PagingRulesTests
{
    [Theory]
    [InlineData(1, 100, 0)]
    [InlineData(2, 100, 100)]
    [InlineData(3, 25, 50)]
    [InlineData(100000, 100, 9999900)]
    public void Valid_page_is_offset_and_size(int page, int size, int expectedOffset)
    {
        var result = PagingRules.Validate(page, size);
        Assert.Equal(expectedOffset, result.Offset);
        Assert.Equal(size, result.PageSize);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(-1, 100)]
    [InlineData(100001, 100)]
    [InlineData(1, 0)]
    [InlineData(1, 201)]
    public void Invalid_or_unbounded_query_is_rejected(int page, int size) =>
        Assert.Throws<DomainException>(() => PagingRules.Validate(page, size));
}
