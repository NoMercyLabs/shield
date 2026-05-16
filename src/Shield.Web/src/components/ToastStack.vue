<script setup lang="ts">
import { useToasts } from '@/stores/toast'

const { items, dismiss } = useToasts()

function kindClass(kind: 'info' | 'success' | 'error'): string {
  switch (kind) {
    case 'success': return 'border-green-700 bg-green-900/40 text-green-200'
    case 'error': return 'border-red-700 bg-red-900/40 text-red-200'
    case 'info': return 'border-slate-700 bg-slate-800 text-slate-200'
  }
}
</script>

<template>
  <div class="pointer-events-none fixed right-4 bottom-4 z-50 flex w-80 flex-col gap-2">
    <button
      v-for="toast in items"
      :key="toast.id"
      type="button"
      class="pointer-events-auto rounded border px-3 py-2 text-left text-sm shadow-lg"
      :class="kindClass(toast.kind)"
      @click="dismiss(toast.id)"
    >
      {{ toast.message }}
    </button>
  </div>
</template>
