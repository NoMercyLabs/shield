import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import type { MaybeRef } from 'vue'
import { unref } from 'vue'

import { api } from '@/lib/api'
import type { Source, SourceCreate } from '@/types/api'

export const useSourcesQuery = () => useQuery({
  queryKey: ['sources'],
  queryFn: async (): Promise<Source[]> => {
    const { data } = await api.get<Source[]>('/sources')
    return data
  },
})

export const useSourceQuery = (id: MaybeRef<number>) => useQuery({
  queryKey: ['sources', id],
  queryFn: async (): Promise<Source> => {
    const { data } = await api.get<Source>(`/sources/${unref(id)}`)
    return data
  },
})

export const useCreateSourceMutation = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (payload: SourceCreate): Promise<Source> => {
      const { data } = await api.post<Source>('/sources', payload)
      return data
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['sources'] })
    },
  })
}

export const useScanNowMutation = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (id: number): Promise<void> => {
      await api.post(`/sources/${id}/scan-now`)
    },
    onSuccess: (_data, id) => {
      queryClient.invalidateQueries({ queryKey: ['sources'] })
      queryClient.invalidateQueries({ queryKey: ['sources', id] })
      queryClient.invalidateQueries({ queryKey: ['findings'] })
    },
  })
}
