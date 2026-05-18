<script setup lang="ts">
import { computed } from 'vue'
import { RefreshCw } from 'lucide-vue-next'
import { useI18n } from 'vue-i18n'

import SortableTh from '@/components/SortableTh.vue'
import { useClientSort } from '@/composables/useClientSort'
import { useFeedsQuery, useRefreshFeedMutation } from '@/queries/feeds'
import { enumName } from '@/stores/enums'
import { useToasts } from '@/stores/toast'
import { formatDate } from '@/lib/format'
import type { Feed, FeedStatus } from '@/types/api'

const { t } = useI18n()
const { data, isLoading, isError } = useFeedsQuery()
const refresh = useRefreshFeedMutation()
const { push } = useToasts()

const rows = computed<FeedStatus[]>(() => data.value ?? [])
const { sortedRows, sortKey, sortDir, toggleSort } = useClientSort<FeedStatus>(
  rows,
  [
    { key: 'feed', extract: row => enumName('Feed', row.feed), defaultDirection: 'asc' },
    { key: 'lastSuccess', extract: row => row.lastSuccessAt, defaultDirection: 'desc' },
    { key: 'nextRun', extract: row => row.nextRunAt, defaultDirection: 'asc' },
    {
      key: 'status',
      extract: row => (row.lastError ? 'error' : row.registered ? 'ok' : 'not-registered'),
      defaultDirection: 'asc',
    },
  ],
  { storageKey: 'shield.feeds.sort' },
)

async function onRefresh(feed: Feed): Promise<void> {
  try {
    await refresh.mutateAsync(feed)
    push('success', t('feeds.refresh_queued', { name: enumName('Feed', feed) }))
  }
  catch {
    push('error', t('feeds.refresh_failed', { name: enumName('Feed', feed) }))
  }
}
</script>

<template>
  <div class="space-y-6">
    <h1 class="text-2xl font-semibold">{{ t('feeds.title') }}</h1>

    <p v-if="isLoading" class="text-sm text-slate-400">{{ t('feeds.loading') }}</p>
    <p v-else-if="isError" class="text-sm text-red-300">{{ t('feeds.error') }}</p>

    <div v-else-if="data && data.length" class="overflow-x-auto rounded-lg border border-slate-800 bg-slate-900">
      <table class="w-full min-w-[640px] text-left text-sm">
        <thead class="border-b border-slate-800 text-xs uppercase text-slate-500">
          <tr>
            <SortableTh column-key="feed" :active-key="sortKey" :active-dir="sortDir" @toggle="toggleSort">
              {{ t('feeds.col_feed') }}
            </SortableTh>
            <SortableTh column-key="lastSuccess" :active-key="sortKey" :active-dir="sortDir" @toggle="toggleSort">
              {{ t('feeds.col_last_success') }}
            </SortableTh>
            <SortableTh column-key="nextRun" :active-key="sortKey" :active-dir="sortDir" @toggle="toggleSort">
              {{ t('feeds.col_next_run') }}
            </SortableTh>
            <SortableTh column-key="status" :active-key="sortKey" :active-dir="sortDir" @toggle="toggleSort">
              {{ t('feeds.col_status') }}
            </SortableTh>
            <th class="px-4 py-2 text-right">{{ t('feeds.col_actions') }}</th>
          </tr>
        </thead>
        <tbody class="divide-y divide-slate-800">
          <tr v-for="status in sortedRows" :key="status.feed" class="hover:bg-slate-800/50">
            <td class="px-4 py-2 font-medium">{{ enumName('Feed', status.feed) }}</td>
            <td class="px-4 py-2 text-slate-400">{{ formatDate(status.lastSuccessAt) }}</td>
            <td class="px-4 py-2 text-slate-400">{{ formatDate(status.nextRunAt) }}</td>
            <td class="px-4 py-2">
              <span v-if="status.lastError" class="text-red-300" :title="status.lastError">{{ t('feeds.status_error') }}</span>
              <span v-else-if="!status.registered" class="text-slate-500">{{ t('feeds.status_not_registered') }}</span>
              <span v-else class="text-green-300">{{ t('feeds.status_ok') }}</span>
            </td>
            <td class="px-4 py-2 text-right">
              <button
                type="button"
                class="inline-flex items-center gap-1 rounded border border-slate-700 px-2 py-1 text-xs text-slate-200 hover:bg-slate-800 disabled:opacity-40"
                :disabled="refresh.isPending.value || !status.registered"
                @click="onRefresh(status.feed)"
              >
                <RefreshCw class="h-3 w-3" />
                {{ t('feeds.refresh_btn') }}
              </button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <div
      v-else
      class="rounded-lg border border-dashed border-slate-700 bg-slate-900/40 px-6 py-10 text-center"
    >
      <p class="text-sm font-medium text-slate-200">{{ t('feeds.empty_title') }}</p>
      <p class="mt-1 text-xs text-slate-500">{{ t('feeds.empty_body') }}</p>
    </div>
  </div>
</template>
