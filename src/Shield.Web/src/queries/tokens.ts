import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'

import { api } from '@/lib/api'
import type { ApiToken, CreateTokenRequest, CreateTokenResponse } from '@/types/api'

export const useApiTokensQuery = () => useQuery({
  queryKey: ['tokens'],
  queryFn: async (): Promise<ApiToken[]> => {
    const { data } = await api.get<ApiToken[]>('/apitokens')
    return data
  },
})

export const useCreateTokenMutation = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (payload: CreateTokenRequest): Promise<CreateTokenResponse> => {
      const { data } = await api.post<CreateTokenResponse>('/apitokens', payload)
      return data
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['tokens'] })
    },
  })
}

export const useRevokeTokenMutation = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (id: string): Promise<void> => {
      await api.delete(`/apitokens/${id}`)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['tokens'] })
    },
  })
}
