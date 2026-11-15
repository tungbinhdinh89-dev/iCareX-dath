using iCareX.Domain.Common;
using iCareX.Domain.Entities;
using Xunit;

namespace iCareX.UnitTests;

public sealed class VehicleInvitationRulesTests
{
    [Fact]
    public void PendingInvitationWaitsSixtySecondsWithoutExtendingExpiry()
    {
        var now = DateTimeOffset.UtcNow;
        var invitation = new VehicleInvitation { UpdatedAtUtc = now, ExpiresAtUtc = now.AddDays(7) };
        Assert.Equal(now.AddSeconds(60), VehicleInvitationRules.ResendAvailableAt(invitation, now));
        Assert.Null(VehicleInvitationRules.ResendAvailableAt(invitation, now.AddDays(7)));
    }

    [Theory]
    [InlineData(VehicleInvitationStatus.Accepted)]
    [InlineData(VehicleInvitationStatus.Rejected)]
    [InlineData(VehicleInvitationStatus.Revoked)]
    [InlineData(VehicleInvitationStatus.Expired)]
    public void ProcessedInvitationsCannotBeResent(VehicleInvitationStatus status)
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Null(VehicleInvitationRules.ResendAvailableAt(new VehicleInvitation { Status = status, ExpiresAtUtc = now.AddDays(7) }, now));
    }
}
