using ClaimsModule.Domain.Common;
using ClaimsModule.Domain.Rules;
using FluentAssertions;

namespace ClaimsModule.Tests.Domain;

public class ClaimNumberFormatterTests
{
    [Theory]
    [InlineData(2026, 142, "CLM-2026-0000142")]
    [InlineData(2026, 1, "CLM-2026-0000001")]
    [InlineData(2027, 9_999_999, "CLM-2027-9999999")]
    public void Formats_year_and_zero_padded_sequence(int year, int seq, string expected) =>
        ClaimNumberFormatter.Format(year, seq).Should().Be(expected);

    [Theory]
    [InlineData(0)]
    [InlineData(10_000_000)]
    public void Rejects_out_of_range_sequences(int seq) =>
        FluentActions.Invoking(() => ClaimNumberFormatter.Format(2026, seq)).Should().Throw<ArgumentOutOfRangeException>();

    [Fact]
    public void Sequential_guids_are_unique()
    {
        var ids = Enumerable.Range(0, 10_000).Select(_ => SequentialGuid.Next()).ToList();
        ids.Distinct().Should().HaveCount(ids.Count);
    }
}
