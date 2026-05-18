import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import type { MaybeRef } from 'vue'
import { unref } from 'vue'

import { api } from '@/lib/api'
import type { AuditFilter, AuditPage, AuditUndoResponse } from '@/types/api'

const serializeAuditFilter = (filter: AuditFilter): URLSearchParams => {
  const params = new URLSearchParams()
  if (filter.page !== undefined)
    params.append('page', String(filter.page))
  if (filter.pageSize !== undefined)
    params.append('pageSize', String(filter.pageSize))
  if (filter.action)
    params.append('action', filter.action)
  if (filter.targetType)
    params.append('targetType', filter.targetType)
  return params
}

export const useAuditQuery = (filter: MaybeRef<AuditFilter>) => useQuery({
  queryKey: ['audit', filter],
  queryFn: async (): Promise<AuditPage> => {
    const { data } = await api.get<AuditPage>('/audit', {
      params: serializeAuditFilter(unref(filter)),
    })
    return data
  },
})

// Reverses a previously-recorded reversible action. Server dispatches by Action string
// to a registered IAuditUndoHandler; the handler reads BeforeJson and rolls the target
// row back. Returns the new audit entry id (the inverse-action row) for timeline linking.
export const useUndoAuditEntryMutation = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (entryId: string): Promise<AuditUndoResponse> => {
      const { data } = await api.post<AuditUndoResponse>(`/audit/${entryId}/undo`)
      return data
    },
    onSuccess: () => {
      // Refresh audit page + anything that might have changed (sources/users/...).
      queryClient.invalidateQueries({ queryKey: ['audit'] })
      queryClient.invalidateQueries({ queryKey: ['sources'] })
      queryClient.invalidateQueries({ queryKey: ['access-users'] })
    },
  })
}
