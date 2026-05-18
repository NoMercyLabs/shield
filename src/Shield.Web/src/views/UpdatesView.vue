<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { ArrowUpCircle, Loader2, RefreshCw, Rocket } from 'lucide-vue-next'

import { getFindingsConnection } from '@/lib/signalr'
import {
  type SourceApplyOutcome,
  UpdateApplyScope,
  useApplyUpdatesMutation,
  useRefreshUpdatesMutation,
  useUpdatesQuery,
  type UpdateRow,
} from '@/queries/updates'
import { useToasts } from '@/stores/toast'

const { t } = useI18n()
const { push } = useToasts()
const { data, isLoading, refetch } = useUpdatesQuery()
const refresh = useRefreshUpdatesMutation()
const apply = useApplyUpdatesMutation()

// Live job state — set when an apply is enqueued, updated by SignalR events, cleared on
// completion. While set, the buttons stay disabled and a progress strip shows under the header.
interface JobProgress {
  jobId: string
  totalSources: number
  completed: SourceApplyOutcome[]
  finished: boolean
}
const activeJob = ref<JobProgress | null>(null)

interface SourceGroup {
  sourceId: number
  sourceName: string
  rows: UpdateRow[]
}

const grouped = computed<SourceGroup[]>(() => {
  const rows = data.value ?? []
  const bySource = new Map<number, SourceGroup>()
  for (const row of rows) {
    let bucket = bySource.get(row.sourceId)
    if (!bucket) {
      bucket = { sourceId: row.sourceId, sourceName: row.sourceName, rows: [] }
      bySource.set(row.sourceId, bucket)
    }
    bucket.rows.push(row)
  }
  return Array.from(bySource.values()).sort((a, b) => a.sourceName.localeCompare(b.sourceName))
})

async function onRefresh(sourceId?: number): Promise<void> {
  try {
    const result = await refresh.mutateAsync(sourceId)
    push('success', t('updates_view.refresh_ok', { n: result.upserts }))
  }
  catch {
    push('error', t('updates_view.refresh_error'))
  }
}

async function onApply(scope: UpdateApplyScope): Promise<void> {
  try {
    const response = await apply.mutateAsync({ scope, confirmProduction: true })
    if (response.queued && response.jobId) {
      activeJob.value = {
        jobId: response.jobId,
        totalSources: 0,
        completed: [],
        finished: false,
      }
      push('info', t('updates_view.apply_queued'))
      return
    }
    // DryRun path (synchronous preview) — not currently exposed in the UI but supported.
    if (response.preview) {
      const opened = response.preview.sources.filter(source => source.pullRequestUrl !== null)
      push('info', t('updates_view.apply_ok', { prs: opened.length, failed: 0 }))
    }
  }
  catch {
    push('error', t('updates_view.apply_error'))
  }
}

let hubStarted = false
const hub = getFindingsConnection()

function attachHubHandlers(): void {
  hub.on('updates.job.started', (payload: { jobId: string, totalSources: number }) => {
    if (!activeJob.value || activeJob.value.jobId !== payload.jobId) {
      activeJob.value = {
        jobId: payload.jobId,
        totalSources: payload.totalSources,
        completed: [],
        finished: false,
      }
    }
    else {
      activeJob.value.totalSources = payload.totalSources
    }
  })

  hub.on(
    'updates.source.completed',
    (payload: SourceApplyOutcome & { jobId: string }) => {
      if (!activeJob.value || activeJob.value.jobId !== payload.jobId) return
      activeJob.value.completed.push({
        sourceId: payload.sourceId,
        sourceName: payload.sourceName,
        pullRequestUrl: payload.pullRequestUrl,
        bumpedCount: payload.bumpedCount,
        skippedYoungCount: payload.skippedYoungCount,
        skippedMajorCount: payload.skippedMajorCount,
        errors: payload.errors,
      })
    },
  )

  hub.on(
    'updates.job.completed',
    (payload: { jobId: string, opened: number, failed: number }) => {
      if (!activeJob.value || activeJob.value.jobId !== payload.jobId) return
      activeJob.value.finished = true
      const firstPr = activeJob.value.completed.find(source => source.pullRequestUrl !== null)
      push(
        'success',
        t('updates_view.apply_ok', { prs: payload.opened, failed: payload.failed }),
        firstPr?.pullRequestUrl
          ? { href: firstPr.pullRequestUrl, label: t('updates_view.open_first_pr') }
          : undefined,
      )
      void refetch()
    },
  )

  hub.on('updates.job.failed', (payload: { jobId: string, message: string }) => {
    if (!activeJob.value || activeJob.value.jobId !== payload.jobId) return
    activeJob.value.finished = true
    push('error', t('updates_view.apply_error'))
  })
}

onMounted(async () => {
  attachHubHandlers()
  if (!hubStarted && hub.state === 'Disconnected') {
    try {
      await hub.start()
      hubStarted = true
    }
    catch {
      /* hub will reconnect via automatic reconnect when available */
    }
  }
})

onBeforeUnmount(() => {
  hub.off('updates.job.started')
  hub.off('updates.source.completed')
  hub.off('updates.job.completed')
  hub.off('updates.job.failed')
})
</script>

<template>
  <div class="mx-auto max-w-6xl space-y-6 p-6 text-slate-100">
    <header class="flex items-center justify-between">
      <div>
        <h1 class="flex items-center gap-2 text-2xl font-semibold">
          <ArrowUpCircle class="h-6 w-6 text-emerald-400" />
          {{ t('updates_view.title') }}
        </h1>
        <p class="text-sm text-slate-400">{{ t('updates_view.subtitle') }}</p>
      </div>
      <div class="flex items-center gap-2">
        <button
          type="button"
          class="flex items-center gap-2 rounded border border-slate-700 px-3 py-1.5 text-sm text-slate-300 hover:bg-slate-800 disabled:opacity-50"
          :disabled="refresh.isPending.value || apply.isPending.value"
          @click="onRefresh()"
        >
          <Loader2 v-if="refresh.isPending.value" class="h-4 w-4 animate-spin" />
          <RefreshCw v-else class="h-4 w-4" />
          {{ t('updates_view.refresh_all_btn') }}
        </button>
        <button
          type="button"
          class="flex items-center gap-2 rounded border border-sky-800/60 bg-sky-950/40 px-3 py-1.5 text-sm font-medium text-sky-200 hover:bg-sky-900/40 disabled:opacity-50"
          :disabled="apply.isPending.value || refresh.isPending.value || grouped.length === 0 || (activeJob !== null && !activeJob.finished)"
          @click="onApply(UpdateApplyScope.LatestMinor)"
        >
          <Rocket class="h-4 w-4" />
          {{ t('updates_view.apply_minor_btn') }}
        </button>
        <button
          type="button"
          class="flex items-center gap-2 rounded border border-amber-800/60 bg-amber-950/40 px-3 py-1.5 text-sm font-medium text-amber-200 hover:bg-amber-900/40 disabled:opacity-50"
          :disabled="apply.isPending.value || refresh.isPending.value || grouped.length === 0 || (activeJob !== null && !activeJob.finished)"
          @click="onApply(UpdateApplyScope.Latest)"
        >
          <Rocket class="h-4 w-4" />
          {{ t('updates_view.apply_latest_btn') }}
        </button>
      </div>
    </header>

    <div
      v-if="activeJob"
      class="rounded border border-sky-800/60 bg-sky-950/30 p-4 text-sm text-sky-200"
    >
      <div class="flex items-center gap-2 font-medium">
        <Loader2 v-if="!activeJob.finished" class="h-4 w-4 animate-spin" />
        <Rocket v-else class="h-4 w-4" />
        <span v-if="!activeJob.finished">
          {{ t('updates_view.job_running', { done: activeJob.completed.length }) }}
        </span>
        <span v-else>{{ t('updates_view.job_finished', { done: activeJob.completed.length }) }}</span>
      </div>
      <ul v-if="activeJob.completed.length > 0" class="mt-2 space-y-1 text-xs">
        <li
          v-for="outcome in activeJob.completed"
          :key="outcome.sourceId"
          class="flex items-center justify-between"
        >
          <span class="text-slate-300">{{ outcome.sourceName }}</span>
          <a
            v-if="outcome.pullRequestUrl"
            :href="outcome.pullRequestUrl"
            target="_blank"
            rel="noopener noreferrer"
            class="text-sky-300 hover:underline"
          >
            {{ t('updates_view.open_pr', { n: outcome.bumpedCount }) }}
          </a>
          <span v-else-if="outcome.errors.length > 0" class="text-red-300">
            {{ outcome.errors[0] }}
          </span>
          <span v-else class="text-slate-500">
            {{ t('updates_view.no_changes') }}
          </span>
        </li>
      </ul>
    </div>

    <div v-if="isLoading" class="rounded border border-slate-800 bg-slate-900/40 p-4 text-sm text-slate-400">
      {{ t('updates_view.loading') }}
    </div>

    <div v-else-if="grouped.length === 0" class="rounded border border-slate-800 bg-slate-900/40 p-6 text-center">
      <p class="text-slate-300">{{ t('updates_view.empty_title') }}</p>
      <p class="mt-2 text-xs text-slate-500">{{ t('updates_view.empty_hint') }}</p>
    </div>

    <section
      v-for="group in grouped"
      :key="group.sourceId"
      class="rounded border border-slate-800 bg-slate-900/40"
    >
      <header class="flex items-center justify-between border-b border-slate-800 px-4 py-3">
        <h2 class="font-semibold text-slate-100">{{ group.sourceName }}</h2>
        <span class="text-xs text-slate-500">{{ t('updates_view.outdated_count', { n: group.rows.length }) }}</span>
      </header>
      <table class="w-full text-sm">
        <thead class="text-left text-xs uppercase text-slate-500">
          <tr>
            <th class="px-4 py-2">{{ t('updates_view.col_package') }}</th>
            <th class="px-4 py-2">{{ t('updates_view.col_ecosystem') }}</th>
            <th class="px-4 py-2">{{ t('updates_view.col_current') }}</th>
            <th class="px-4 py-2">{{ t('updates_view.col_latest') }}</th>
            <th class="px-4 py-2">{{ t('updates_view.col_published') }}</th>
            <th class="px-4 py-2">{{ t('updates_view.col_flags') }}</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="row in group.rows" :key="row.id" class="border-t border-slate-800">
            <td class="px-4 py-2 font-mono text-slate-100">{{ row.name }}</td>
            <td class="px-4 py-2 text-slate-400">{{ row.ecosystemLabel }}</td>
            <td class="px-4 py-2 font-mono text-xs text-slate-400">{{ row.currentVersion }}</td>
            <td class="px-4 py-2 font-mono text-xs text-emerald-300">{{ row.latestVersion }}</td>
            <td class="px-4 py-2 text-xs text-slate-500">
              {{ row.publishedAt ? new Date(row.publishedAt).toLocaleDateString() : '—' }}
            </td>
            <td class="px-4 py-2">
              <span
                v-if="row.isBreakingMajor"
                class="mr-1 inline-block rounded bg-amber-900/40 px-1.5 py-0.5 text-[10px] uppercase text-amber-300"
              >
                {{ t('updates_view.flag_major') }}
              </span>
              <span
                v-if="row.isTooYoung"
                class="inline-block rounded bg-red-900/40 px-1.5 py-0.5 text-[10px] uppercase text-red-300"
              >
                {{ t('updates_view.flag_too_young') }}
              </span>
            </td>
          </tr>
        </tbody>
      </table>
    </section>
  </div>
</template>
