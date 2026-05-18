import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'

import { api } from '@/lib/api'
import type { Ecosystem } from '@/types/api'

export interface UpdateRow {
  id: number
  sourceId: number
  sourceName: string
  ecosystem: Ecosystem
  ecosystemLabel: string
  name: string
  currentVersion: string
  latestVersion: string
  publishedAt: string | null
  isBreakingMajor: boolean
  isTooYoung: boolean
  detectedAt: string
}

export const useUpdatesQuery = () => useQuery({
  queryKey: ['updates'],
  queryFn: async (): Promise<UpdateRow[]> => {
    const { data } = await api.get<UpdateRow[]>('/updates')
    return data
  },
})

export const useRefreshUpdatesMutation = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (sourceId?: number): Promise<{ upserts: number }> => {
      const { data } = await api.post<{ upserts: number }>(
        '/updates/refresh',
        null,
        { params: sourceId ? { sourceId } : {} },
      )
      return data
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['updates'] })
    },
  })
}

export const UpdateApplyScope = {
  Latest: 0,
  LatestMinor: 1,
} as const
export type UpdateApplyScope = (typeof UpdateApplyScope)[keyof typeof UpdateApplyScope]

export interface ApplyUpdatesRequest {
  scope: UpdateApplyScope
  sourceIds?: number[]
  dryRun?: boolean
  force?: boolean
  confirmProduction?: boolean
}

export interface SourceApplyOutcome {
  sourceId: number
  sourceName: string
  pullRequestUrl: string | null
  bumpedCount: number
  skippedYoungCount: number
  skippedMajorCount: number
  errors: string[]
}

export interface UpdateApplyResult {
  sources: SourceApplyOutcome[]
}

export interface ApplyUpdatesResponse {
  queued: boolean
  jobId: string | null
  preview: UpdateApplyResult | null
}

export const useApplyUpdatesMutation = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (payload: ApplyUpdatesRequest): Promise<ApplyUpdatesResponse> => {
      const { data } = await api.post<ApplyUpdatesResponse>('/updates/apply', payload)
      return data
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['updates'] })
    },
  })
}
