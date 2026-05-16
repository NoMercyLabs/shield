import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import type { MaybeRef } from 'vue'
import { unref } from 'vue'

import { api } from '@/lib/api'
import type {
  ApplyFixRequest,
  ApplyFixResponse,
  ApplyFixStrategy,
  BulkFindingsResponse,
  Finding,
  FindingDetail,
  FindingFilter,
  FindingsPage,
} from '@/types/api'

// Serialize `?severity=2&severity=3` (repeat-key) rather than axios default `severity[]=…`.
// .NET model binding accepts both, but the contract documented for clients is repeat-key.
const serializeFindingFilter = (filter: FindingFilter): URLSearchParams => {
  const params = new URLSearchParams()
  for (const value of filter.severity ?? [])
    params.append('severity', String(value))
  for (const value of filter.sourceId ?? [])
    params.append('sourceId', String(value))
  for (const value of filter.ecosystem ?? [])
    params.append('ecosystem', String(value))
  for (const value of filter.state ?? [])
    params.append('state', String(value))
  for (const value of filter.packageName ?? [])
    params.append('packageName', value)
  if (filter.page !== undefined)
    params.append('page', String(filter.page))
  if (filter.pageSize !== undefined)
    params.append('pageSize', String(filter.pageSize))
  return params
}

export const useFindingsQuery = (filter: MaybeRef<FindingFilter>) => useQuery({
  queryKey: ['findings', filter],
  queryFn: async (): Promise<FindingsPage> => {
    const { data } = await api.get<FindingsPage>('/findings', {
      params: serializeFindingFilter(unref(filter)),
    })
    return data
  },
})

export const useFindingQuery = (id: MaybeRef<string>) => useQuery({
  queryKey: ['findings', id],
  queryFn: async (): Promise<FindingDetail> => {
    const { data } = await api.get<FindingDetail>(`/findings/${unref(id)}`)
    return data
  },
})

interface TransitionPayload {
  id: string
  reason?: string
}

const transition = (verb: 'ack' | 'suppress' | 'resolve') => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async ({ id, reason }: TransitionPayload): Promise<Finding> => {
      const body = verb === 'suppress' ? { reason: reason ?? '' } : {}
      const { data } = await api.post<Finding>(`/findings/${id}/${verb}`, body)
      return data
    },
    onSuccess: (_data, { id }) => {
      queryClient.invalidateQueries({ queryKey: ['findings'] })
      queryClient.invalidateQueries({ queryKey: ['findings', id] })
    },
  })
}

export const useAckFindingMutation = () => transition('ack')
export const useSuppressFindingMutation = () => transition('suppress')
export const useResolveFindingMutation = () => transition('resolve')

interface BulkPayload {
  findingIds: string[]
  reason?: string
}

const bulkTransition = (verb: 'ack' | 'resolve' | 'suppress') => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async ({ findingIds, reason }: BulkPayload): Promise<BulkFindingsResponse> => {
      const body = verb === 'suppress'
        ? { findingIds, reason: reason ?? '' }
        : { findingIds }
      const { data } = await api.post<BulkFindingsResponse>(`/findings/bulk-${verb}`, body)
      return data
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['findings'] })
    },
  })
}

export const useBulkAckFindingsMutation = () => bulkTransition('ack')
export const useBulkResolveFindingsMutation = () => bulkTransition('resolve')
export const useBulkSuppressFindingsMutation = () => bulkTransition('suppress')

interface ApplyFixPayload {
  id: string
  strategy: ApplyFixStrategy
}

export const useApplyFixMutation = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async ({ id, strategy }: ApplyFixPayload): Promise<ApplyFixResponse> => {
      const body: ApplyFixRequest = { strategy }
      const { data } = await api.post<ApplyFixResponse>(`/findings/${id}/apply-fix`, body)
      return data
    },
    onSuccess: (_data, { id }) => {
      queryClient.invalidateQueries({ queryKey: ['findings'] })
      queryClient.invalidateQueries({ queryKey: ['findings', id] })
    },
  })
}
