import { useMutation, useQuery } from '@tanstack/vue-query'

import { api } from '@/lib/api'
import type { ChangePasswordRequest, RegistrationAllowed } from '@/types/api'

export const useRegistrationAllowed = () => useQuery({
  queryKey: ['auth', 'registration-allowed'],
  queryFn: async (): Promise<RegistrationAllowed> => {
    const { data } = await api.get<RegistrationAllowed>('/auth/registration-allowed')
    return data
  },
})

export const useChangePassword = () => useMutation({
  mutationFn: async (payload: ChangePasswordRequest): Promise<void> => {
    await api.post('/auth/password', payload)
  },
})
