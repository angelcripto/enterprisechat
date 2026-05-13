using EnterpriseChat.Licensing.Abstractions;
using FluentAssertions;

namespace EnterpriseChat.Tests.Licensing;

public sealed class FreeLicenseValidatorTests
{
    [Fact]
    public void Current_reports_Free_edition_with_hardcoded_cap()
    {
        var sut = new FreeLicenseValidator();

        sut.Current.Edition.Should().Be(LicenseEdition.Free);
        sut.Current.MaxConcurrentUsers.Should().Be(FreeLicenseValidator.FreeUserCap);
        sut.Current.ExpiresAt.Should().BeNull();
        sut.Current.LicensedTo.Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(9)]
    public void Admits_sessions_below_cap(int active)
    {
        var sut = new FreeLicenseValidator();

        var result = sut.TryAdmitSession(active);

        result.Admitted.Should().BeTrue();
        result.DeniedReason.Should().BeNull();
    }

    [Theory]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(int.MaxValue)]
    public void Rejects_sessions_at_or_above_cap(int active)
    {
        var sut = new FreeLicenseValidator();

        var result = sut.TryAdmitSession(active);

        result.Admitted.Should().BeFalse();
        result.DeniedReason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Throws_on_negative_session_count()
    {
        var sut = new FreeLicenseValidator();

        var act = () => sut.TryAdmitSession(-1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
