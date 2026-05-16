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
