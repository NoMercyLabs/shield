<script setup lang="ts">
import { computed } from 'vue'
import { Play } from 'lucide-vue-next'

import { useScanNowMutation, useSnapshotItemsQuery, useSourceQuery } from '@/queries/sources'
import { useToasts } from '@/stores/toast'
import { formatDate } from '@/lib/format'
import { EcosystemNames, SourceTypeNames } from '@/types/api'

const props = defineProps<{ id: string }>()
const sourceId = computed(() => Number.parseInt(props.id, 10))

const { data, isLoading, isError } = useSourceQuery(sourceId)
const scan = useScanNowMutation()
const { push } = useToasts()

const source = computed(() => data.value?.source)
const snapshot = computed(() => data.value?.latestSnapshot ?? null)
const snapshotId = computed(() => snapshot.value?.id ?? null)

const inventory = useSnapshotItemsQuery(sourceId, snapshotId)
const items = computed(() => inventory.data.value?.items ?? [])

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

    <template v-else-if="source">
      <header class="flex items-center justify-between">
        <div>
          <h1 class="text-2xl font-semibold">{{ source.name }}</h1>
          <p class="text-sm text-slate-400">{{ SourceTypeNames[source.type] }}</p>
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
          <dd class="mt-1 text-sm">{{ formatDate(source.lastScannedAt) }}</dd>
        </div>
        <div class="rounded-lg border border-slate-800 bg-slate-900 p-4">
          <dt class="text-xs uppercase text-slate-500">Scan interval</dt>
          <dd class="mt-1 text-sm">{{ source.scanInterval }}</dd>
        </div>
        <div v-if="snapshot" class="rounded-lg border border-slate-800 bg-slate-900 p-4">
          <dt class="text-xs uppercase text-slate-500">Last snapshot</dt>
          <dd class="mt-1 text-sm">
            {{ formatDate(snapshot.takenAt) }} · {{ snapshot.itemCount }} items
          </dd>
        </div>
        <div v-if="snapshot" class="rounded-lg border border-slate-800 bg-slate-900 p-4">
          <dt class="text-xs uppercase text-slate-500">Contents SHA</dt>
          <dd class="mt-1 break-all font-mono text-xs text-slate-300">{{ snapshot.contentsSha }}</dd>
        </div>
        <div class="rounded-lg border border-slate-800 bg-slate-900 p-4 sm:col-span-2">
          <dt class="text-xs uppercase text-slate-500">Config</dt>
          <dd class="mt-1 whitespace-pre-wrap break-all font-mono text-xs text-slate-300">{{ source.configJson }}</dd>
        </div>
        <div v-if="source.lastError" class="rounded-lg border border-red-800 bg-red-950/40 p-4 sm:col-span-2">
          <dt class="text-xs uppercase text-red-300">Last error</dt>
          <dd class="mt-1 text-sm text-red-200">{{ source.lastError }}</dd>
        </div>
      </dl>

      <section v-if="snapshot" class="rounded-lg border border-slate-800 bg-slate-900">
        <header class="border-b border-slate-800 px-4 py-3">
          <h2 class="text-sm font-semibold uppercase tracking-wide text-slate-300">Inventory</h2>
          <p class="text-xs text-slate-500">{{ snapshot.itemCount }} packages parsed at {{ formatDate(snapshot.takenAt) }}</p>
        </header>
        <p v-if="inventory.isLoading.value" class="px-4 py-6 text-sm text-slate-400">Loading inventory…</p>
        <p v-else-if="inventory.isError.value" class="px-4 py-6 text-sm text-red-300">Failed to load inventory.</p>
        <p v-else-if="items.length === 0" class="px-4 py-6 text-sm text-slate-400">No packages.</p>
        <table v-else class="w-full text-sm">
          <thead class="text-xs uppercase text-slate-500">
            <tr>
              <th class="px-4 py-2 text-left font-medium">Ecosystem</th>
              <th class="px-4 py-2 text-left font-medium">Package</th>
              <th class="px-4 py-2 text-left font-medium">Version</th>
              <th class="px-4 py-2 text-left font-medium">Type</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="item in items" :key="item.id" class="border-t border-slate-800">
              <td class="px-4 py-2 text-slate-300">{{ EcosystemNames[item.ecosystem] }}</td>
              <td class="px-4 py-2 font-mono text-slate-100">{{ item.name }}</td>
              <td class="px-4 py-2 font-mono text-slate-300">{{ item.version }}</td>
              <td class="px-4 py-2 text-xs text-slate-400">{{ item.isDirect ? 'direct' : 'transitive' }}</td>
            </tr>
          </tbody>
        </table>
      </section>
    </template>
  </div>
</template>
