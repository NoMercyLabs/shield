import { useQuery } from '@tanstack/vue-query'

import { api } from '@/lib/api'
import type { DashboardStats } from '@/types/api'

export const useDashboardQuery = () => useQuery({
  queryKey: ['dashboard'],
  queryFn: async (): Promise<DashboardStats> => {
    const { data } = await api.get<DashboardStats>('/dashboard')
    return data
  },
})
