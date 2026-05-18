import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'

import { api } from '@/lib/api'
import type { SavedFilter } from '@/types/api'

export const useSavedFiltersQuery = (kind = 'findings') => useQuery({
  queryKey: ['saved-filters', kind],
  queryFn: async (): Promise<SavedFilter[]> => {
    const { data } = await api.get<SavedFilter[]>('/saved-filters', { params: { kind } })
    return data
  },
})

interface CreateSavedFilterPayload {
  name: string
  kind: string
  queryJson: string
}

export const useCreateSavedFilterMutation = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (payload: CreateSavedFilterPayload): Promise<SavedFilter> => {
      const { data } = await api.post<SavedFilter>('/saved-filters', payload)
      return data
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['saved-filters'] })
    },
  })
}

export const useDeleteSavedFilterMutation = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (id: string): Promise<void> => {
      await api.delete(`/saved-filters/${id}`)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['saved-filters'] })
    },
  })
}
