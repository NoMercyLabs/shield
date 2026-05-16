import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import type { MaybeRef } from 'vue'
import { unref } from 'vue'

import { api } from '@/lib/api'
import type { Finding, FindingFilter, PagedResult } from '@/types/api'

export const useFindingsQuery = (filter: MaybeRef<FindingFilter>) => useQuery({
  queryKey: ['findings', filter],
  queryFn: async (): Promise<PagedResult<Finding>> => {
    const { data } = await api.get<PagedResult<Finding>>('/findings', { params: unref(filter) })
    return data
  },
})

export const useFindingQuery = (id: MaybeRef<string>) => useQuery({
  queryKey: ['findings', id],
  queryFn: async (): Promise<Finding> => {
    const { data } = await api.get<Finding>(`/findings/${unref(id)}`)
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
      const { data } = await api.post<Finding>(`/findings/${id}/${verb}`, reason ? { reason } : {})
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
