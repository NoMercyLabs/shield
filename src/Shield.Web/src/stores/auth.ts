import { computed, ref } from 'vue'

import { api } from '@/lib/api'
import type { Me } from '@/types/api'

const me = ref<Me | null>(null)
const ready = ref(false)

export const useAuth = () => ({
  user: computed(() => me.value),
  isAuthenticated: computed(() => me.value !== null),
  isReady: computed(() => ready.value),
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

export async function login(username: string, password: string, totpCode?: string): Promise<Me> {
  const { data } = await api.post<Me>('/auth/login', { username, password, totpCode })
  me.value = data
  return data
}

export async function logout(): Promise<void> {
  await api.post('/auth/logout').catch(() => undefined)
  me.value = null
}
