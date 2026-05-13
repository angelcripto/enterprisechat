using EnterpriseChat.Licensing.Abstractions;
using EnterpriseChat.Server.Licensing;
using FluentAssertions;

namespace EnterpriseChat.Tests.Licensing;

public sealed class ConcurrentSessionCounterTests
{
    private static ConcurrentSessionCounter CreateSut()
        => new(new FreeLicenseValidator());

    [Fact]
    public void Admits_up_to_cap_distinct_users()
    {
        var sut = CreateSut();

        for (var userId = 1; userId <= FreeLicenseValidator.FreeUserCap; userId++)
        {
            sut.TryAdmit(userId).Admitted
                .Should().BeTrue($"user {userId} should fit within the cap");
        }

        sut.DistinctActiveUsers.Should().Be(FreeLicenseValidator.FreeUserCap);
    }

    [Fact]
    public void Rejects_eleventh_distinct_user_under_free_cap()
    {
        var sut = CreateSut();
        for (var i = 1; i <= FreeLicenseValidator.FreeUserCap; i++)
        {
            sut.TryAdmit(i);
        }

        var verdict = sut.TryAdmit(FreeLicenseValidator.FreeUserCap + 1);

        verdict.Admitted.Should().BeFalse();
        verdict.DeniedReason.Should().Contain("Free");
        sut.DistinctActiveUsers.Should().Be(FreeLicenseValidator.FreeUserCap);
    }

    [Fact]
    public void Second_connection_for_same_user_does_not_consume_extra_slot()
    {
        var sut = CreateSut();
        for (var i = 1; i <= FreeLicenseValidator.FreeUserCap; i++)
        {
            sut.TryAdmit(i);
        }

        // Same user as #1 opens a second window; still under cap.
        var verdict = sut.TryAdmit(userId: 1);

        verdict.Admitted.Should().BeTrue();
        sut.DistinctActiveUsers.Should().Be(FreeLicenseValidator.FreeUserCap);
    }

    [Fact]
    public void Release_frees_slot_when_user_fully_disconnects()
    {
        var sut = CreateSut();
        sut.TryAdmit(1);
        sut.TryAdmit(1); // second window for user 1

        sut.Release(1);
        sut.DistinctActiveUsers.Should().Be(1, "user 1 still has one window open");

        sut.Release(1);
        sut.DistinctActiveUsers.Should().Be(0, "user 1 fully disconnected");
    }
}
