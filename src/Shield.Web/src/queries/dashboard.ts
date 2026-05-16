import { useQuery } from '@tanstack/vue-query'

import { api } from '@/lib/api'
import type { DashboardResponse } from '@/types/api'

export const useDashboardQuery = () => useQuery({
  queryKey: ['dashboard'],
  queryFn: async (): Promise<DashboardResponse> => {
    const { data } = await api.get<DashboardResponse>('/dashboard')
    return data
  },
})
