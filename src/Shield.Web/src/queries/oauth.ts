import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'

import { api } from '@/lib/api'
import type { OAuthProviderName, OAuthStartResponse, OAuthStatus, SlackChannelsResponse } from '@/types/api'

const STATUS_KEY = (provider: OAuthProviderName) => ['oauth', provider, 'status']

export function useOAuthStatus(provider: OAuthProviderName) {
  return useQuery({
    queryKey: STATUS_KEY(provider),
    queryFn: async (): Promise<OAuthStatus> => {
      const { data } = await api.get<OAuthStatus>(`/oauth/${provider}/status`)
      return data
    },
  })
}

export function useStartOAuth(provider: OAuthProviderName) {
  return useMutation({
    mutationFn: async (): Promise<OAuthStartResponse> => {
      const { data } = await api.get<OAuthStartResponse>(`/oauth/${provider}/start`)
      return data
    },
  })
}

export function useDisconnectOAuth(provider: OAuthProviderName) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (): Promise<void> => {
      await api.post(`/oauth/${provider}/disconnect`)
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: STATUS_KEY(provider) })
    },
  })
}

export function useSlackChannels(enabled = true) {
  return useQuery({
    queryKey: ['oauth', 'slack', 'channels'],
    enabled,
    queryFn: async (): Promise<SlackChannelsResponse> => {
      const { data } = await api.get<SlackChannelsResponse>('/oauth/slack/channels')
      return data
    },
  })
}
