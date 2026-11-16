using MizanMedical.Domain;
using Xunit;

namespace MizanMedical.Tests;

public sealed class DatabaseConflictCodesTests
{
    [Theory]
    [InlineData("40001")] // serialization_failure
    [InlineData("40P01")] // deadlock_detected
    [InlineData("23505")] // unique_violation
    public void Known_postgresql_conflicts_require_user_verification_not_a_500(string sqlState)
        => Assert.True(DatabaseConflictCodeRules.IsConcurrentWriteConflict(sqlState));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("23503")] // foreign_key_violation
    [InlineData("23502")] // not_null_violation
    [InlineData("22003")] // numeric_value_out_of_range
    [InlineData("XX000")] // internal_error
    public void Unrelated_database_errors_must_not_be_misclassified_as_business_conflicts(string? sqlState)
        => Assert.False(DatabaseConflictCodeRules.IsConcurrentWriteConflict(sqlState));
}
