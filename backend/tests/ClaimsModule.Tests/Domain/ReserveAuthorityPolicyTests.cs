using ClaimsModule.Domain.Enums;
using ClaimsModule.Domain.Rules;
using FluentAssertions;

namespace ClaimsModule.Tests.Domain;

public class ReserveAuthorityPolicyTests
{
    [Theory]
    [InlineData(0.01, AuthorityLevel.Auto)]
    [InlineData(10_000, AuthorityLevel.Auto)]
    [InlineData(10_000.01, AuthorityLevel.Supervisor)]
    [InlineData(100_000, AuthorityLevel.Supervisor)]
    [InlineData(100_000.01, AuthorityLevel.Manager)]
    [InlineData(10_000_000, AuthorityLevel.Manager)]
    [InlineData(-8_000, AuthorityLevel.Auto)]
    [InlineData(-50_000, AuthorityLevel.Supervisor)]
    public void Required_level_uses_the_transaction_amount_boundaries(double amount, AuthorityLevel expected) =>
        ReserveAuthorityPolicy.RequiredLevel((decimal)amount).Should().Be(expected);

    [Theory]
    [InlineData(UserRole.Handler, 10_000, true)]
    [InlineData(UserRole.Handler, 10_001, false)]
    [InlineData(UserRole.Supervisor, 100_000, true)]
    [InlineData(UserRole.Supervisor, 100_001, false)]
    [InlineData(UserRole.Manager, 100_001, true)]
    [InlineData(UserRole.Manager, 10_000_000, true)]
    [InlineData(UserRole.Manager, 10_000_001, false)]
    public void Can_approve_respects_role_ceilings(UserRole role, double amount, bool expected) =>
        ReserveAuthorityPolicy.CanApprove(role, (decimal)amount).Should().Be(expected);

    [Fact]
    public void Only_subrogation_may_go_negative()
    {
        ReserveAuthorityPolicy.AllowsNegativeBalance(ReserveComponentType.SubrogationRecoverable).Should().BeTrue();
        ReserveAuthorityPolicy.AllowsNegativeBalance(ReserveComponentType.Indemnity).Should().BeFalse();
        ReserveAuthorityPolicy.AllowsNegativeBalance(ReserveComponentType.Expense).Should().BeFalse();
        ReserveAuthorityPolicy.AllowsNegativeBalance(ReserveComponentType.ALAE).Should().BeFalse();
    }
}
