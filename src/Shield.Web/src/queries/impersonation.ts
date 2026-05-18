import { useMutation, useQueryClient } from '@tanstack/vue-query'

import { api } from '@/lib/api'
import { bootstrapAuth } from '@/stores/auth'
import { resetLiveFindings } from '@/stores/liveFindings'
import type { ImpersonationStartResponse } from '@/types/api'

// Starts an "Admin viewing as X" override. Server validates that the caller is Admin and
// the target is not Admin; on success it mints the shield.impersonate cookie. The SPA then
// re-bootstraps `/me` (the response now reflects the impersonated identity) and invalidates
// every query so cached admin-view data is dropped.
export const useStartImpersonationMutation = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (userId: string): Promise<ImpersonationStartResponse> => {
      const { data } = await api.post<ImpersonationStartResponse>('/impersonation/start', { userId })
      return data
    },
    onSuccess: async () => {
      await bootstrapAuth()
      queryClient.clear()
      // Sidebar badge holds module-scoped refs that aren't part of TanStack's cache.
      await resetLiveFindings()
    },
  })
}

// Drops the override. The cookie is deleted server-side; `/me` will return the real admin
// identity on the next call.
export const useStopImpersonationMutation = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (): Promise<void> => {
      await api.post('/impersonation/stop')
    },
    onSuccess: async () => {
      await bootstrapAuth()
      queryClient.clear()
      await resetLiveFindings()
    },
  })
}
