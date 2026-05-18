import { computed, ref } from 'vue'

export interface ToastAction {
  href: string
  label: string
}

export interface Toast {
  id: number
  kind: 'info' | 'success' | 'error'
  message: string
  action?: ToastAction
}

const items = ref<Toast[]>([])
let nextId = 1

export const useToasts = () => ({
  items: computed(() => items.value),
  // Action-bearing toasts stay visible longer (8s) so the user actually has time to click;
  // plain toasts keep the original 4s.
  push: (kind: Toast['kind'], message: string, action?: ToastAction): void => {
    const id = nextId++
    items.value.push({ id, kind, message, action })
    const ttl = action ? 8000 : 4000
    setTimeout(() => {
      items.value = items.value.filter(toast => toast.id !== id)
    }, ttl)
  },
  dismiss: (id: number): void => {
    items.value = items.value.filter(toast => toast.id !== id)
  },
})
