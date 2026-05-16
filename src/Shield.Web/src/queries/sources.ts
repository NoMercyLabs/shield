import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import type { MaybeRef } from 'vue'
import { unref } from 'vue'

import { api } from '@/lib/api'
import type { InventoryItemResponse, PagedResponse, Source, SourceCreate, SourceDetail, SourceUpdate } from '@/types/api'

export const useSourcesQuery = () => useQuery({
  queryKey: ['sources'],
  queryFn: async (): Promise<Source[]> => {
    const { data } = await api.get<Source[]>('/sources')
    return data
  },
})

export const useSourceQuery = (id: MaybeRef<number>) => useQuery({
  queryKey: ['sources', id],
  queryFn: async (): Promise<SourceDetail> => {
    const { data } = await api.get<SourceDetail>(`/sources/${unref(id)}`)
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

export const useSnapshotItemsQuery = (
  sourceId: MaybeRef<number>,
  snapshotId: MaybeRef<string | null | undefined>,
) => useQuery({
  queryKey: ['sources', sourceId, 'snapshots', snapshotId, 'items'],
  enabled: () => !!unref(snapshotId),
  queryFn: async (): Promise<PagedResponse<InventoryItemResponse>> => {
    const snapId = unref(snapshotId)
    if (!snapId) throw new Error('snapshotId required')
    const { data } = await api.get<PagedResponse<InventoryItemResponse>>(
      `/sources/${unref(sourceId)}/snapshots/${snapId}/items`,
      { params: { pageSize: 200 } },
    )
    return data
  },
})

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

export const useUpdateSourceMutation = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (input: { id: number, patch: SourceUpdate }): Promise<Source> => {
      const { data } = await api.put<Source>(`/sources/${input.id}`, input.patch)
      return data
    },
    onSuccess: (_data, input) => {
      queryClient.invalidateQueries({ queryKey: ['sources'] })
      queryClient.invalidateQueries({ queryKey: ['sources', input.id] })
    },
  })
}

export const useDeleteSourceMutation = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (id: number): Promise<void> => {
      await api.delete(`/sources/${id}`)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['sources'] })
      queryClient.invalidateQueries({ queryKey: ['findings'] })
    },
  })
}

export const useToggleSourceMutation = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (id: number): Promise<Source> => {
      const { data } = await api.post<Source>(`/sources/${id}/toggle`)
      return data
    },
    onSuccess: (_data, id) => {
      queryClient.invalidateQueries({ queryKey: ['sources'] })
      queryClient.invalidateQueries({ queryKey: ['sources', id] })
    },
  })
}

export const usePromoteSourceToGithubMutation = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (id: number): Promise<Source> => {
      const { data } = await api.post<Source>(`/sources/${id}/promote-to-github`)
      return data
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['sources'] })
    },
  })
}
