import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'

import { api } from '@/lib/api'
import type { AlertChannel, ChannelCreate } from '@/types/api'

export const useChannelsQuery = () => useQuery({
  queryKey: ['channels'],
  queryFn: async (): Promise<AlertChannel[]> => {
    const { data } = await api.get<AlertChannel[]>('/channels')
    return data
  },
})

export const useCreateChannelMutation = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (payload: ChannelCreate): Promise<AlertChannel> => {
      const { data } = await api.post<AlertChannel>('/channels', payload)
      return data
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['channels'] })
    },
  })
}

export interface ChannelUpdate {
  id: string
  name: string
  configJson: string
  minSeverity: number
  enabled: boolean
}

export const useUpdateChannelMutation = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (payload: ChannelUpdate): Promise<AlertChannel> => {
      const { id, ...body } = payload
      const { data } = await api.put<AlertChannel>(`/channels/${id}`, body)
      return data
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['channels'] })
    },
  })
}

export const useDeleteChannelMutation = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (id: string): Promise<void> => {
      await api.delete(`/channels/${id}`)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['channels'] })
    },
  })
}

export const useTestSendMutation = () => useMutation({
  mutationFn: async (id: string): Promise<void> => {
    await api.post(`/channels/${id}/test-send`)
  },
})
