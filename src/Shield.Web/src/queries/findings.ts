import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import type { MaybeRef } from 'vue'
import { unref } from 'vue'

import { api } from '@/lib/api'
import type {
  ApplyFixRequest,
  ApplyFixResponse,
  ApplyFixStrategy,
  Finding,
  FindingDetail,
  FindingFilter,
  FindingsPage,
} from '@/types/api'

export const useFindingsQuery = (filter: MaybeRef<FindingFilter>) => useQuery({
  queryKey: ['findings', filter],
  queryFn: async (): Promise<FindingsPage> => {
    const { data } = await api.get<FindingsPage>('/findings', { params: unref(filter) })
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
