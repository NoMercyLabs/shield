<script setup lang="ts">
import { computed } from 'vue'
import { Play } from 'lucide-vue-next'

import { useScanNowMutation, useSourceQuery } from '@/queries/sources'
import { useToasts } from '@/stores/toast'
import { formatDate } from '@/lib/format'

const props = defineProps<{ id: string }>()
const sourceId = computed(() => Number.parseInt(props.id, 10))

const { data, isLoading, isError } = useSourceQuery(sourceId)
const scan = useScanNowMutation()
const { push } = useToasts()

async function onScanNow(): Promise<void> {
  try {
    await scan.mutateAsync(sourceId.value)
    push('success', 'Scan queued.')
  }
  catch {
    push('error', 'Failed to queue scan.')
  }
}
</script>

<template>
  <div class="space-y-6">
    <p v-if="isLoading" class="text-sm text-slate-400">Loading…</p>
    <p v-else-if="isError" class="text-sm text-red-300">Failed to load source.</p>

    <template v-else-if="data">
      <header class="flex items-center justify-between">
        <div>
          <h1 class="text-2xl font-semibold">{{ data.name }}</h1>
          <p class="text-sm text-slate-400">{{ data.type }}</p>
        </div>
        <button
          type="button"
          class="flex items-center gap-1 rounded bg-blue-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-blue-500 disabled:bg-blue-900"
          :disabled="scan.isPending.value"
          @click="onScanNow"
        >
          <Play class="h-4 w-4" />
          Scan now
        </button>
      </header>

      <dl class="grid grid-cols-1 gap-4 sm:grid-cols-2">
        <div class="rounded-lg border border-slate-800 bg-slate-900 p-4">
          <dt class="text-xs uppercase text-slate-500">Last scanned</dt>
          <dd class="mt-1 text-sm">{{ formatDate(data.lastScannedAt) }}</dd>
        </div>
        <div class="rounded-lg border border-slate-800 bg-slate-900 p-4">
          <dt class="text-xs uppercase text-slate-500">Scan interval</dt>
          <dd class="mt-1 text-sm">{{ data.scanInterval }}</dd>
        </div>
        <div class="rounded-lg border border-slate-800 bg-slate-900 p-4 sm:col-span-2">
          <dt class="text-xs uppercase text-slate-500">Config</dt>
          <dd class="mt-1 whitespace-pre-wrap break-all font-mono text-xs text-slate-300">{{ data.configJson }}</dd>
        </div>
        <div v-if="data.lastError" class="rounded-lg border border-red-800 bg-red-950/40 p-4 sm:col-span-2">
          <dt class="text-xs uppercase text-red-300">Last error</dt>
          <dd class="mt-1 text-sm text-red-200">{{ data.lastError }}</dd>
        </div>
      </dl>
    </template>
  </div>
</template>
