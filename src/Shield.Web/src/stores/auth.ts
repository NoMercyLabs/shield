import { computed, ref } from 'vue'

import { api } from '@/lib/api'
import type { LoginRequest, LoginResponse, Me } from '@/types/api'

const me = ref<Me | null>(null)
const ready = ref(false)

export const useAuth = () => ({
  user: computed(() => me.value),
  isAuthenticated: computed(() => me.value !== null),
  isReady: computed(() => ready.value),
  isAdmin: computed(() => me.value?.roles.includes('Admin') ?? false),
  setUser: (value: Me | null): void => { me.value = value },
})

export async function bootstrapAuth(): Promise<void> {
  try {
    const { data } = await api.get<Me>('/auth/me')
    me.value = data
  }
  catch {
    me.value = null
  }
  finally {
    ready.value = true
  }
}

export async function login(username: string, password: string, twoFactorCode?: string): Promise<Me> {
  const payload: LoginRequest = { username, password, twoFactorCode }
  const { data } = await api.post<LoginResponse>('/auth/login', payload)
  if (!data.succeeded) {
    throw new Error(data.error ?? (data.requiresTwoFactor ? '2FA code required.' : 'Sign-in failed.'))
  }
  // Cookie is set by the server; refresh /me so we have the resolved identity.
  const { data: meData } = await api.get<Me>('/auth/me')
  me.value = meData
  return meData
}

export async function logout(): Promise<void> {
  await api.post('/auth/logout').catch(() => undefined)
  me.value = null
}

// Minimal stubs so RegisterView / AccountView keep type-checking until they're fully
// rewritten. The underlying endpoints exist on the server.
export async function register(username: string, password: string, email?: string): Promise<void> {
  await api.post('/auth/register', { username, password, email: email || null })
  await login(username, password)
}

export async function fetchRegistrationAllowed(): Promise<{ allowed: boolean, reason: string | null }> {
  try {
    const { data } = await api.get<{ allowed: boolean, reason: string | null }>('/auth/registration-allowed')
    return data
  }
  catch {
    return { allowed: false, reason: 'unavailable' }
  }
}

export async function changePassword(currentPassword: string, newPassword: string): Promise<void> {
  await api.post('/auth/password', { currentPassword, newPassword })
}

// Kicks off the OAuth signin flow: server responds with the provider's authorization URL,
// then we navigate the same tab to it. The provider redirects back to /api/oauth/<p>/callback
// which sets the cookie and 302s back to `redirect`.
export async function oauthSignin(provider: string, redirect?: string): Promise<void> {
  const target = redirect ?? '/'
  const startUrl = `/api/oauth/${provider}/start?intent=signin&redirect=${encodeURIComponent(target)}`
  const { data } = await api.get<{ authorizationUrl: string }>(startUrl)
  window.location.href = data.authorizationUrl
}

export interface AuthProvider {
  provider: string
  displayName: string
  iconUrl: string
}

export async function fetchAuthProviders(): Promise<AuthProvider[]> {
  try {
    const { data } = await api.get<{ providers: AuthProvider[] }>('/auth/providers')
    return data.providers
  }
  catch {
    return []
  }
}
