import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'

import { api } from '@/lib/api'
import type { Feed, FeedStatus } from '@/types/api'

export const useFeedsQuery = () => useQuery({
  queryKey: ['feeds'],
  queryFn: async (): Promise<FeedStatus[]> => {
    const { data } = await api.get<FeedStatus[]>('/feeds')
    return data
  },
})

export const useRefreshFeedMutation = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (feed: Feed): Promise<void> => {
      await api.post(`/feeds/${feed}/refresh`)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['feeds'] })
    },
  })
}
