import { useQuery } from '@tanstack/vue-query'
import type { MaybeRef } from 'vue'
import { unref } from 'vue'

import { api } from '@/lib/api'
import type { AuditFilter, AuditPage } from '@/types/api'

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
