<script setup lang="ts">
import { RefreshCw } from 'lucide-vue-next'

import { useFeedsQuery, useRefreshFeedMutation } from '@/queries/feeds'
import { useToasts } from '@/stores/toast'
import { formatDate } from '@/lib/format'
import type { Feed } from '@/types/api'

const { data, isLoading, isError } = useFeedsQuery()
const refresh = useRefreshFeedMutation()
const { push } = useToasts()

async function onRefresh(feed: Feed): Promise<void> {
  try {
    await refresh.mutateAsync(feed)
    push('success', `${feed} refresh queued.`)
  }
  catch {
    push('error', `Failed to refresh ${feed}.`)
  }
}
</script>

<template>
  <div class="space-y-6">
    <h1 class="text-2xl font-semibold">Feeds</h1>

    <p v-if="isLoading" class="text-sm text-slate-400">Loading…</p>
    <p v-else-if="isError" class="text-sm text-red-300">Failed to load feed status.</p>

    <div v-else-if="data && data.length" class="overflow-hidden rounded-lg border border-slate-800 bg-slate-900">
      <table class="w-full text-left text-sm">
        <thead class="border-b border-slate-800 text-xs uppercase text-slate-500">
          <tr>
            <th class="px-4 py-2">Feed</th>
            <th class="px-4 py-2">Last success</th>
            <th class="px-4 py-2">Next run</th>
            <th class="px-4 py-2">Status</th>
            <th class="px-4 py-2 text-right">Actions</th>
          </tr>
        </thead>
        <tbody class="divide-y divide-slate-800">
          <tr v-for="status in data" :key="status.feed" class="hover:bg-slate-800/50">
            <td class="px-4 py-2 font-medium">{{ status.feed }}</td>
            <td class="px-4 py-2 text-slate-400">{{ formatDate(status.lastSuccessAt) }}</td>
            <td class="px-4 py-2 text-slate-400">{{ formatDate(status.nextRunAt) }}</td>
            <td class="px-4 py-2">
              <span v-if="status.lastError" class="text-red-300" :title="status.lastError">Error</span>
              <span v-else class="text-green-300">OK</span>
            </td>
            <td class="px-4 py-2 text-right">
              <button
                type="button"
                class="inline-flex items-center gap-1 rounded border border-slate-700 px-2 py-1 text-xs text-slate-200 hover:bg-slate-800"
                @click="onRefresh(status.feed)"
              >
                <RefreshCw class="h-3 w-3" />
                Refresh
              </button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <p v-else class="text-sm text-slate-500">No feeds configured.</p>
  </div>
</template>
