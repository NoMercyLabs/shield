import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import type { MaybeRef, MaybeRefOrGetter } from 'vue'
import { toValue, unref } from 'vue'

import { api } from '@/lib/api'
import type {
  GithubDevicePollResponse,
  GithubDeviceStartResponse,
  GitHubRepoListResponse,
  OAuthProviderName,
  OAuthStartResponse,
  OAuthStatus,
  RepositoryListResponse,
  SlackChannelsResponse,
} from '@/types/api'

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

export function useGithubDeviceStart() {
  return useMutation({
    mutationFn: async (): Promise<GithubDeviceStartResponse> => {
      const { data } = await api.post<GithubDeviceStartResponse>('/oauth/github/device/start', {})
      return data
    },
  })
}

// Caller does the polling loop so the interval respects the server-returned `interval` value.
// Backend returns 202 for pending/slow_down, 410 for expired, 403 for denied, 200 for ok —
// accept all of those as the mutation result instead of letting axios reject on 4xx.
export function useGithubDevicePoll() {
  return useMutation({
    mutationFn: async (flowId: string): Promise<GithubDevicePollResponse> => {
      const { data } = await api.post<GithubDevicePollResponse>(
        '/oauth/github/device/poll',
        { flowId },
        { validateStatus: status => status === 200 || status === 202 || status === 403 || status === 410 },
      )
      return data
    },
  })
}

// Disabled by default; the picker dialog opts in when it opens so we don't hammer
// GitHub on every Sources page view. Server caches the response 5 min/user anyway.
export function useGitHubReposQuery(enabled: MaybeRefOrGetter<boolean> = false) {
  return useQuery({
    queryKey: ['oauth', 'github', 'repos'],
    enabled: () => toValue(enabled),
    queryFn: async (): Promise<GitHubRepoListResponse> => {
      const { data } = await api.get<GitHubRepoListResponse>('/oauth/github/repos')
      return data
    },
  })
}

export function useProviderReposQuery(
  provider: MaybeRef<OAuthProviderName>,
  enabled: MaybeRefOrGetter<boolean> = false,
) {
  return useQuery({
    queryKey: ['oauth-repos', provider],
    enabled: () => toValue(enabled),
    queryFn: async (): Promise<RepositoryListResponse> => {
      const { data } = await api.get<RepositoryListResponse>('/oauth/repos', {
        params: { provider: unref(provider) },
      })
      return data
    },
  })
}

