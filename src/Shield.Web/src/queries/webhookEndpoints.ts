import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'

import { api } from '@/lib/api'
import type {
  CreateWebhookEndpointRequest,
  CreateWebhookEndpointResponse,
  WebhookEndpoint,
} from '@/types/api'

export const useWebhookEndpointsQuery = () => useQuery({
  queryKey: ['webhook-endpoints'],
  queryFn: async (): Promise<WebhookEndpoint[]> => {
    const { data } = await api.get<WebhookEndpoint[]>('/webhook-endpoints')
    return data
  },
})

export const useCreateWebhookEndpointMutation = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (
      payload: CreateWebhookEndpointRequest,
    ): Promise<CreateWebhookEndpointResponse> => {
      const { data } = await api.post<CreateWebhookEndpointResponse>(
        '/webhook-endpoints',
        payload,
      )
      return data
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['webhook-endpoints'] })
    },
  })
}

export const useDeleteWebhookEndpointMutation = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (id: string): Promise<void> => {
      await api.delete(`/webhook-endpoints/${id}`)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['webhook-endpoints'] })
    },
  })
}
