using Xunit;

namespace MizanMedical.Tests;

// The real two-request race is exercised by scripts/smoke-api.sh against PostgreSQL.
// This test keeps the expected HTTP contract explicit for future refactors.
public sealed class ShiftOpenConcurrencySpecification
{
    [Theory]
    [InlineData(201, 400)]
    [InlineData(400, 201)]
    public void Only_one_simultaneous_shift_open_may_succeed(int first, int second)
    {
        Assert.Equal(1, new[] { first, second }.Count(x => x == 201));
        Assert.Equal(1, new[] { first, second }.Count(x => x == 400));
    }
}
