import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'

import { api } from '@/lib/api'
import type {
  OidcTestRequest,
  OidcTestResponse,
  RuntimeInfo,
  Settings,
  SettingsUpdate,
  SettingsUpdateResponse,
} from '@/types/api'

export const useSettingsQuery = () => useQuery({
  queryKey: ['settings'],
  queryFn: async (): Promise<Settings> => {
    const { data } = await api.get<Settings>('/settings')
    return data
  },
})

export const useUpdateSettings = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (payload: SettingsUpdate): Promise<SettingsUpdateResponse> => {
      const { data } = await api.put<SettingsUpdateResponse>('/settings', payload)
      return data
    },
    onSuccess: (data) => {
      queryClient.setQueryData(['settings'], data.settings)
    },
  })
}

export const useTestOidc = () => useMutation({
  mutationFn: async (payload: OidcTestRequest): Promise<OidcTestResponse> => {
    const { data } = await api.post<OidcTestResponse>('/settings/test-oidc', payload)
    return data
  },
})

export const useRuntimeInfo = () => useQuery({
  queryKey: ['settings', 'runtime'],
  queryFn: async (): Promise<RuntimeInfo> => {
    const { data } = await api.get<RuntimeInfo>('/settings/runtime')
    return data
  },
})
