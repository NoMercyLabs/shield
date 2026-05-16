import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'

import { api } from '@/lib/api'
import type { OnboardingStatusResponse } from '@/types/api'

const STATUS_KEY = ['onboarding', 'status'] as const

export function useOnboardingStatus() {
  return useQuery({
    queryKey: STATUS_KEY,
    queryFn: async (): Promise<OnboardingStatusResponse> => {
      const { data } = await api.get<OnboardingStatusResponse>('/onboarding/status')
      return data
    },
    // Survives route changes so the gate doesn't refetch on every navigation.
    staleTime: 30_000,
  })
}

export function useDismissOnboarding() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (): Promise<OnboardingStatusResponse> => {
      const { data } = await api.post<OnboardingStatusResponse>('/onboarding/dismiss')
      return data
    },
    onSuccess: (data) => {
      queryClient.setQueryData(STATUS_KEY, data)
    },
  })
}

// Imperative fetch for the router gate — guards can't depend on the Vue Query reactive
// scope, so we ask the server directly and rely on the server response (which is fast).
export async function fetchOnboardingStatus(): Promise<OnboardingStatusResponse> {
  const { data } = await api.get<OnboardingStatusResponse>('/onboarding/status')
  return data
}
