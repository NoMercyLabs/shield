<script setup lang="ts">
import { ExternalLink } from 'lucide-vue-next'

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
    <div
      v-for="toast in items"
      :key="toast.id"
      class="pointer-events-auto flex flex-col gap-2 rounded border px-3 py-2 text-sm shadow-lg"
      :class="kindClass(toast.kind)"
    >
      <button type="button" class="text-left" @click="dismiss(toast.id)">
        {{ toast.message }}
      </button>
      <a
        v-if="toast.action"
        :href="toast.action.href"
        target="_blank"
        rel="noopener noreferrer"
        class="inline-flex items-center gap-1 self-start rounded bg-white/10 px-2 py-1 text-xs font-medium hover:bg-white/20"
      >
        <ExternalLink class="h-3 w-3" />
        {{ toast.action.label }}
      </a>
    </div>
  </div>
</template>
