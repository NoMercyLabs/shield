import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'

import { api } from '@/lib/api'
import type { Ecosystem, PackageWatch, WatchSummaryRow } from '@/types/api'

export const useWatchesQuery = () => useQuery({
  queryKey: ['watches'],
  queryFn: async (): Promise<PackageWatch[]> => {
    const { data } = await api.get<PackageWatch[]>('/watch')
    return data
  },
})

export const useWatchSummaryQuery = () => useQuery({
  queryKey: ['watches', 'summary'],
  queryFn: async (): Promise<WatchSummaryRow[]> => {
    const { data } = await api.get<WatchSummaryRow[]>('/watch/summary')
    return data
  },
})

interface CreateWatchPayload {
  ecosystem: Ecosystem
  packageName: string
}

export const useCreateWatchMutation = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (payload: CreateWatchPayload): Promise<PackageWatch> => {
      const { data } = await api.post<PackageWatch>('/watch', payload)
      return data
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['watches'] })
    },
  })
}

export const useDeleteWatchMutation = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (id: string): Promise<void> => {
      await api.delete(`/watch/${id}`)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['watches'] })
    },
  })
}
