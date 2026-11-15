import type { VehicleInvitationResponse } from '@shared/vehicle-contracts'

export function canResendInvitation(invitation: Pick<VehicleInvitationResponse, 'status' | 'expiresAtUtc' | 'resendAvailableAtUtc'>, now = Date.now()) {
  return invitation.status === 'Pending' && invitation.resendAvailableAtUtc != null &&
    Date.parse(invitation.expiresAtUtc) > now && Date.parse(invitation.resendAvailableAtUtc) <= now
}
