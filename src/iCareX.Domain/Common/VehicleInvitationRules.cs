using iCareX.Domain.Entities;

namespace iCareX.Domain.Common;

public static class VehicleInvitationRules
{
    public static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(60);
    public static DateTimeOffset? ResendAvailableAt(VehicleInvitation invitation, DateTimeOffset now) =>
        invitation.Status == VehicleInvitationStatus.Pending && invitation.ExpiresAtUtc > now
            ? invitation.UpdatedAtUtc.Add(ResendCooldown) : null;
}
