import { computed, ref } from 'vue'

export interface Toast {
  id: number
  kind: 'info' | 'success' | 'error'
  message: string
}

const items = ref<Toast[]>([])
let nextId = 1

export const useToasts = () => ({
  items: computed(() => items.value),
  push: (kind: Toast['kind'], message: string): void => {
    const id = nextId++
    items.value.push({ id, kind, message })
    setTimeout(() => {
      items.value = items.value.filter(toast => toast.id !== id)
    }, 4000)
  },
  dismiss: (id: number): void => {
    items.value = items.value.filter(toast => toast.id !== id)
  },
})
