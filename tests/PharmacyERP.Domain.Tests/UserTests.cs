using FluentAssertions;
using PharmacyERP.Domain.Entities;
using PharmacyERP.Domain.Enums;
using Xunit;

namespace PharmacyERP.Domain.Tests;

/// <summary>
/// Covers User.IsLockedOut(), which gates every login attempt in AuthService.
/// A false negative here would let a locked-out account keep brute-forcing.
/// </summary>
public class UserTests
{
    [Fact]
    public void IsLockedOut_ReturnsTrue_WhenStatusIsLocked()
    {
        var user = new User { Status = UserStatus.Locked };

        user.IsLockedOut().Should().BeTrue();
    }

    [Fact]
    public void IsLockedOut_ReturnsTrue_WhenLockoutEndIsInTheFuture()
    {
        var user = new User { Status = UserStatus.Active, LockoutEndUtc = DateTime.UtcNow.AddMinutes(10) };

        user.IsLockedOut().Should().BeTrue();
    }

    [Fact]
    public void IsLockedOut_ReturnsFalse_WhenLockoutEndHasPassed()
    {
        var user = new User { Status = UserStatus.Active, LockoutEndUtc = DateTime.UtcNow.AddMinutes(-1) };

        user.IsLockedOut().Should().BeFalse();
    }

    [Fact]
    public void IsLockedOut_ReturnsFalse_ForANormalActiveUser()
    {
        var user = new User { Status = UserStatus.Active, LockoutEndUtc = null };

        user.IsLockedOut().Should().BeFalse();
    }
}
