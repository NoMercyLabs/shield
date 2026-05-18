// Wire-format types for `/api/auth/external/*`. Kept separate from `types/api.ts` so the
// signin device-flow surface can evolve without touching the rest of the SPA's contracts.

export interface ExternalLoginProvider {
  key: string
  displayName: string
  iconKey: string
}

export interface ExternalLoginStartResponse {
  flowId: string
  userCode: string
  verificationUri: string
  verificationUriComplete: string | null
  interval: number
  expiresIn: number
}

export interface ExternalLoginIdentity {
  provider: string
  login: string
  email: string | null
  avatarUrl: string | null
}

export interface ExternalLoginPollResponse {
  status: 'pending' | 'slow_down' | 'expired' | 'denied' | 'ok' | 'error'
  needsInvite?: boolean
  identity?: ExternalLoginIdentity
  returnPath?: string | null
  acceptanceTicket?: string | null
}

// Emitted by DeviceLoginPanel when external login completed but no Shield user is linked yet.
// The AcceptInvite page consumes this to POST /api/auth/accept-invite with the signed ticket.
export interface DevicePanelNeedsInvitePayload {
  identity: ExternalLoginIdentity
  acceptanceTicket: string
}
