import { useQuery } from '@tanstack/vue-query'

import { api } from '@/lib/api'
import type { Settings } from '@/types/api'

export const useSettingsQuery = () => useQuery({
  queryKey: ['settings'],
  queryFn: async (): Promise<Settings> => {
    const { data } = await api.get<Settings>('/settings')
    return data
  },
})
