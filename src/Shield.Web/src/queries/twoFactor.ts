import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'

import { api } from '@/lib/api'
import type { TwoFactorEnrollResponse, TwoFactorStatus } from '@/types/api'

const TWO_FACTOR_STATUS_KEY = ['auth', 'two-factor', 'status'] as const

export const useTwoFactorStatusQuery = () => useQuery({
  queryKey: TWO_FACTOR_STATUS_KEY,
  queryFn: async (): Promise<TwoFactorStatus> => {
    const { data } = await api.get<TwoFactorStatus>('/auth/2fa/status')
    return data
  },
})

export const useEnrollTwoFactorMutation = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (): Promise<TwoFactorEnrollResponse> => {
      const { data } = await api.post<TwoFactorEnrollResponse>('/auth/2fa/enroll')
      return data
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: TWO_FACTOR_STATUS_KEY })
    },
  })
}

export const useVerifyTwoFactorMutation = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (code: string): Promise<{ recoveryCodes: string[] }> => {
      const { data } = await api.post<{ recoveryCodes: string[] }>('/auth/2fa/verify', { code })
      return data
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: TWO_FACTOR_STATUS_KEY })
    },
  })
}

export const useDisableTwoFactorMutation = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (currentPassword: string): Promise<void> => {
      await api.post('/auth/2fa/disable', { currentPassword })
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: TWO_FACTOR_STATUS_KEY })
    },
  })
}
