import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import type { MaybeRefOrGetter } from 'vue'
import { toValue } from 'vue'

import { api } from '@/lib/api'
import type { Notification, NotificationsPage } from '@/types/api'

export const useNotificationsQuery = (
  unreadOnly: MaybeRefOrGetter<boolean> = false,
  limit: MaybeRefOrGetter<number> = 50,
) => useQuery({
  // toValue lets the caller pass a ref or a getter so the toggle in NotificationsView
  // actually re-fires the request when flipped. The query key reads the current values
  // each render so vue-query refetches automatically on change.
  queryKey: ['notifications', { unreadOnly, limit }],
  queryFn: async (): Promise<NotificationsPage> => {
    const params = new URLSearchParams()
    if (toValue(unreadOnly)) params.append('unreadOnly', 'true')
    params.append('limit', String(toValue(limit)))
    const { data } = await api.get<NotificationsPage>('/notifications', { params })
    return data
  },
})

export const useMarkNotificationReadMutation = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (id: string): Promise<Notification> => {
      const { data } = await api.post<Notification>(`/notifications/${id}/read`)
      return data
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['notifications'] })
    },
  })
}

export const useArchiveNotificationMutation = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (id: string): Promise<Notification> => {
      const { data } = await api.post<Notification>(`/notifications/${id}/archive`)
      return data
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['notifications'] })
    },
  })
}

export const useMarkAllNotificationsReadMutation = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (): Promise<{ updated: number }> => {
      const { data } = await api.post<{ updated: number }>('/notifications/mark-all-read')
      return data
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['notifications'] })
    },
  })
}
