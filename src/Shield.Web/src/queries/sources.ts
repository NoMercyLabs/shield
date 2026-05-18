import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import type { MaybeRef } from 'vue'
import { unref } from 'vue'

import { api } from '@/lib/api'
import type {
  AutoFixMode,
  BulkApplyRequest,
  BulkApplyResponse,
  BulkFromGithubRequest,
  BulkFromGithubResponse,
  BulkLocalFoldersRequest,
  BulkLocalFoldersResponse,
  FsBrowseResponse,
  InventoryItemResponse,
  PagedResponse,
  SetIsProductionRequest,
  SnapshotDiffResponse,
  SnapshotListItem,
  Source,
  SourceCreate,
  SourceDetail,
  SourceUpdate,
} from '@/types/api'

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

export const useSnapshotsListQuery = (sourceId: MaybeRef<number>) => useQuery({
  queryKey: ['sources', sourceId, 'snapshots'],
  queryFn: async (): Promise<SnapshotListItem[]> => {
    const { data } = await api.get<SnapshotListItem[]>(
      `/sources/${unref(sourceId)}/snapshots`,
    )
    return data
  },
})

// Disabled until both IDs are set and differ. The diff endpoint 400s on
// matching IDs server-side; gating here keeps the query cache from filling
// with predictable error pairs while the operator picks dropdowns.
export const useSnapshotDiffQuery = (
  sourceId: MaybeRef<number>,
  olderId: MaybeRef<string | null | undefined>,
  newerId: MaybeRef<string | null | undefined>,
) => useQuery({
  queryKey: ['sources', sourceId, 'snapshots', 'diff', olderId, newerId],
  enabled: () => {
    const o = unref(olderId)
    const n = unref(newerId)
    return !!o && !!n && o !== n
  },
  queryFn: async (): Promise<SnapshotDiffResponse> => {
    const o = unref(olderId)
    const n = unref(newerId)
    if (!o || !n) throw new Error('olderId and newerId required')
    const { data } = await api.get<SnapshotDiffResponse>(
      `/sources/${unref(sourceId)}/snapshots/${o}/diff/${n}`,
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

export const useFsBrowse = (path: MaybeRef<string | null>) => useQuery({
  queryKey: ['fs-browse', path],
  queryFn: async (): Promise<FsBrowseResponse> => {
    const current = unref(path)
    const { data } = await api.get<FsBrowseResponse>('/fs/browse', {
      params: current ? { path: current } : {},
    })
    return data
  },
})

export const useBulkLocalFoldersMutation = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (payload: BulkLocalFoldersRequest): Promise<BulkLocalFoldersResponse> => {
      const { data } = await api.post<BulkLocalFoldersResponse>('/sources/bulk-local-folders', payload)
      return data
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['sources'] })
    },
  })
}

export const useBulkFromGithubMutation = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (payload: BulkFromGithubRequest): Promise<BulkFromGithubResponse> => {
      const { data } = await api.post<BulkFromGithubResponse>('/sources/bulk-from-github', payload)
      return data
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['sources'] })
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

export const useApplyAllFixesMutation = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (input: { id: number, payload: BulkApplyRequest }): Promise<BulkApplyResponse> => {
      const { data } = await api.post<BulkApplyResponse>(
        `/sources/${input.id}/apply-all-fixes`,
        input.payload,
      )
      return data
    },
    onSuccess: (_data, input) => {
      queryClient.invalidateQueries({ queryKey: ['sources'] })
      queryClient.invalidateQueries({ queryKey: ['sources', input.id] })
    },
  })
}

export const useUpdateSourceAutoFixModeMutation = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (input: { id: number, autoFixMode: AutoFixMode }): Promise<Source> => {
      const { data } = await api.patch<Source>(`/sources/${input.id}/auto-fix-mode`, { autoFixMode: input.autoFixMode })
      return data
    },
    onSuccess: (_data, input) => {
      queryClient.invalidateQueries({ queryKey: ['sources'] })
      queryClient.invalidateQueries({ queryKey: ['sources', input.id] })
    },
  })
}

export const useSetIsProductionMutation = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (input: { id: number, payload: SetIsProductionRequest }): Promise<Source> => {
      const { data } = await api.patch<Source>(`/sources/${input.id}/is-production`, input.payload)
      return data
    },
    onSuccess: (_data, input) => {
      queryClient.invalidateQueries({ queryKey: ['sources'] })
      queryClient.invalidateQueries({ queryKey: ['sources', input.id] })
    },
  })
}
