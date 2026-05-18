import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'

import { api } from '@/lib/api'
import type { SessionInfo, SessionListResponse } from '@/types/api'

const SESSIONS_KEY = ['sessions'] as const

export const useSessionsQuery = (all = false) => useQuery({
  queryKey: [...SESSIONS_KEY, { all }],
  queryFn: async (): Promise<SessionInfo[]> => {
    const { data } = await api.get<SessionListResponse>('/sessions', {
      params: all ? { all: true } : undefined,
    })
    return data.sessions
  },
})

export const useRevokeSessionMutation = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (id: string): Promise<void> => {
      await api.delete(`/sessions/${id}`)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: SESSIONS_KEY })
    },
  })
}

export const useRevokeOtherSessionsMutation = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (): Promise<number> => {
      const { data } = await api.post<{ revoked: number }>('/sessions/revoke-others')
      return data.revoked
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: SESSIONS_KEY })
    },
  })
}

// Panic-button: revokes EVERY session (including the caller's). The server signs out the
// current request + bumps SecurityStamp, so the caller's next request will 401. Consumers
// should bounce the user to /login on success.
export const useRevokeAllSessionsMutation = () => useMutation({
  mutationFn: async (): Promise<number> => {
    const { data } = await api.post<{ revoked: number }>('/sessions/revoke-all')
    return data.revoked
  },
})
