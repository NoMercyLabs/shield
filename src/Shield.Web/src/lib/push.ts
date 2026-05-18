import { api } from '@/lib/api'

// Web Push helper — encapsulates the VAPID handshake + browser subscription dance so the
// composable/component layer doesn't need to think about base64url URL-safety, the public
// key conversion to a Uint8Array, or the `getSubscription()` vs `subscribe()` branching.
//
// Browser support: Chrome / Edge / Firefox / Android Chrome work from any HTTPS context.
// iOS 16.4+ Safari requires the SPA to be installed as a PWA (Add to Home Screen) before
// `Notification.requestPermission()` resolves — the UI surfaces this prerequisite when
// pushSupported() returns false on iOS.

export function pushSupported(): boolean {
  return typeof window !== 'undefined'
    && 'serviceWorker' in navigator
    && 'PushManager' in window
    && 'Notification' in window
}

export function notificationPermission(): NotificationPermission {
  if (typeof Notification === 'undefined')
    return 'default'
  return Notification.permission
}

function urlBase64ToUint8Array(base64: string): Uint8Array {
  const padding = '='.repeat((4 - base64.length % 4) % 4)
  const normalized = (base64 + padding).replace(/-/g, '+').replace(/_/g, '/')
  const raw = atob(normalized)
  const buffer = new Uint8Array(raw.length)
  for (let index = 0; index < raw.length; index += 1)
    buffer[index] = raw.charCodeAt(index)
  return buffer
}

function bufferToBase64Url(buffer: ArrayBuffer | null): string {
  if (!buffer) return ''
  const bytes = new Uint8Array(buffer)
  let binary = ''
  for (let index = 0; index < bytes.byteLength; index += 1)
    binary += String.fromCharCode(bytes[index])
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')
}

async function fetchVapidPublicKey(): Promise<string> {
  const { data } = await api.get<{ publicKey: string }>('/push/vapid-public-key')
  return data.publicKey
}

// Returns the active service worker registration once it's ready. The SW URL is /service-worker.js
// (scope = root); main.ts registers it during bootstrap.
async function getRegistration(): Promise<ServiceWorkerRegistration | null> {
  if (!('serviceWorker' in navigator))
    return null
  return navigator.serviceWorker.ready
}

export async function getCurrentSubscription(): Promise<PushSubscription | null> {
  const registration = await getRegistration()
  if (!registration)
    return null
  return registration.pushManager.getSubscription()
}

export async function requestPermissionAndSubscribe(): Promise<{ ok: true } | { ok: false, reason: string }> {
  if (!pushSupported())
    return { ok: false, reason: 'unsupported' }

  // Notification.requestPermission accepts the legacy callback signature in Safari — the
  // Promise form works on all browsers we care about. The user gesture must originate from
  // the caller's click handler; this function MUST be invoked synchronously from one.
  const permission = await Notification.requestPermission()
  if (permission !== 'granted')
    return { ok: false, reason: 'denied' }

  const registration = await getRegistration()
  if (!registration)
    return { ok: false, reason: 'no_service_worker' }

  let subscription = await registration.pushManager.getSubscription()
  if (!subscription) {
    const publicKey = await fetchVapidPublicKey()
    subscription = await registration.pushManager.subscribe({
      userVisibleOnly: true,
      applicationServerKey: urlBase64ToUint8Array(publicKey),
    })
  }

  const json = subscription.toJSON() as {
    endpoint?: string
    keys?: { p256dh?: string, auth?: string }
  }
  const endpoint = json.endpoint ?? subscription.endpoint
  const p256dh = json.keys?.p256dh ?? bufferToBase64Url(subscription.getKey('p256dh'))
  const authKey = json.keys?.auth ?? bufferToBase64Url(subscription.getKey('auth'))

  if (!endpoint || !p256dh || !authKey)
    return { ok: false, reason: 'bad_subscription' }

  await api.post('/push/subscribe', {
    endpoint,
    keys: { p256dh, auth: authKey },
    userAgent: navigator.userAgent,
  })
  return { ok: true }
}

export async function unsubscribeCurrentDevice(): Promise<boolean> {
  const subscription = await getCurrentSubscription()
  if (!subscription)
    return false
  const endpoint = subscription.endpoint
  try {
    await api.delete('/push/unsubscribe', { data: { endpoint } })
  }
  catch {
    // Ignore — local unsubscribe still proceeds. Stale row will 410 on next push.
  }
  await subscription.unsubscribe()
  return true
}

export async function sendTestPush(): Promise<number> {
  const { data } = await api.post<{ delivered: number }>('/push/test')
  return data.delivered
}

export interface PushSubscriptionRow {
  id: string
  endpoint: string
  userAgent: string | null
  createdAt: string
  lastDeliveredAt: string | null
  isCurrentDevice: boolean
  endpointHash: string
}

// SHA-256 of the full endpoint URL, first 16 hex chars — mirrors PushController.HashEndpoint.
export async function hashEndpoint(endpoint: string): Promise<string> {
  const encoded = new TextEncoder().encode(endpoint)
  const digest = await crypto.subtle.digest('SHA-256', encoded)
  const hexChars = Array.from(new Uint8Array(digest))
    .map(byte => byte.toString(16).padStart(2, '0'))
    .join('')
  return hexChars.slice(0, 16)
}

export async function listSubscriptions(): Promise<PushSubscriptionRow[]> {
  const { data } = await api.get<{ subscriptions: PushSubscriptionRow[] }>('/push/subscriptions')
  return data.subscriptions
}

export async function deleteSubscriptionById(id: string): Promise<void> {
  await api.delete(`/push/subscriptions/${id}`)
}
