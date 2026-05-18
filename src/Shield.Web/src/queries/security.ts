import { useQuery } from '@tanstack/vue-query'
import type { MaybeRef } from 'vue'
import { unref } from 'vue'

import { api } from '@/lib/api'
import type {
  IpDetail,
  IpReputationsPage,
  SecurityEventFilter,
  SecurityEventsPage,
  SecurityHostsResponse,
} from '@/types/api'

const serializeEventFilter = (filter: SecurityEventFilter): URLSearchParams => {
  const params = new URLSearchParams()
  if (filter.page !== undefined)
    params.append('page', String(filter.page))
  if (filter.pageSize !== undefined)
    params.append('pageSize', String(filter.pageSize))
  if (filter.minSeverity !== undefined && filter.minSeverity !== null)
    params.append('minSeverity', String(filter.minSeverity))
  if (filter.source)
    params.append('source', filter.source)
  if (filter.jail)
    params.append('jail', filter.jail)
  if (filter.ip)
    params.append('ip', filter.ip)
  if (filter.userName)
    params.append('userName', filter.userName)
  if (filter.since)
    params.append('since', filter.since)
  if (filter.until)
    params.append('until', filter.until)
  return params
}

export const useSecurityEventsQuery = (filter: MaybeRef<SecurityEventFilter>) => useQuery({
  queryKey: ['security', 'events', filter],
  queryFn: async (): Promise<SecurityEventsPage> => {
    const { data } = await api.get<SecurityEventsPage>('/security/events', {
      params: serializeEventFilter(unref(filter)),
    })
    return data
  },
})

export const useIpReputationsQuery = (params: MaybeRef<{
  page?: number
  pageSize?: number
  bannedOnly?: boolean
  search?: string | null
}>) => useQuery({
  queryKey: ['security', 'ips', params],
  queryFn: async (): Promise<IpReputationsPage> => {
    const value = unref(params)
    const search = new URLSearchParams()
    if (value.page !== undefined)
      search.append('page', String(value.page))
    if (value.pageSize !== undefined)
      search.append('pageSize', String(value.pageSize))
    if (value.bannedOnly)
      search.append('bannedOnly', 'true')
    if (value.search)
      search.append('search', value.search)
    const { data } = await api.get<IpReputationsPage>('/security/ips', { params: search })
    return data
  },
})

export const useIpDetailQuery = (ip: MaybeRef<string | null>) => useQuery({
  queryKey: ['security', 'ip-detail', ip],
  queryFn: async (): Promise<IpDetail | null> => {
    const value = unref(ip)
    if (!value) return null
    const { data } = await api.get<IpDetail>(`/security/ips/${encodeURIComponent(value)}`)
    return data
  },
  enabled: () => Boolean(unref(ip)),
})

export const useSecurityHostsQuery = () => useQuery({
  queryKey: ['security', 'hosts'],
  queryFn: async (): Promise<SecurityHostsResponse> => {
    const { data } = await api.get<SecurityHostsResponse>('/security/hosts')
    return data
  },
})

export async function requestBan(ip: string, jail: string, reason: string, hours?: number): Promise<void> {
  await api.post(`/security/ips/${encodeURIComponent(ip)}/request-ban`, { jail, reason, hours })
}

export async function updateIpNotes(ip: string, notes: string | null): Promise<void> {
  await api.post(`/security/ips/${encodeURIComponent(ip)}/notes`, { notes })
}
