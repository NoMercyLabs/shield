<script setup lang="ts">
import { computed, ref } from 'vue'
import { RouterLink } from 'vue-router'
import { Star, X } from 'lucide-vue-next'
import { useI18n } from 'vue-i18n'

import {
  useCreateWatchMutation,
  useDeleteWatchMutation,
  useWatchesQuery,
  useWatchSummaryQuery,
} from '@/queries/watches'
import { useToasts } from '@/stores/toast'
import { Ecosystem, EcosystemNames, type WatchSummaryRow } from '@/types/api'

const { t } = useI18n()
const { data: watches } = useWatchesQuery()
const { data: summary, isLoading } = useWatchSummaryQuery()
const createWatch = useCreateWatchMutation()
const deleteWatch = useDeleteWatchMutation()
const toasts = useToasts()

const addOpen = ref(false)
const newName = ref('')
const newEcosystem = ref<Ecosystem>(Ecosystem.Npm)

const rows = computed<WatchSummaryRow[]>(() => summary.value ?? [])

const watchIdByKey = computed<Record<string, string>>(() => {
  const out: Record<string, string> = {}
  for (const watch of watches.value ?? [])
    out[`${watch.ecosystem}::${watch.packageName}`] = watch.id
  return out
})

const ecosystemOptions: Ecosystem[] = [
  Ecosystem.Npm,
  Ecosystem.Nuget,
  Ecosystem.Composer,
  Ecosystem.Gradle,
  Ecosystem.Os,
  Ecosystem.Python,
  Ecosystem.Go,
  Ecosystem.Rust,
]

async function onAdd(): Promise<void> {
  const name = newName.value.trim()
  if (!name) return
  try {
    await createWatch.mutateAsync({ ecosystem: newEcosystem.value, packageName: name })
    newName.value = ''
    addOpen.value = false
    toasts.push('success', t('watches.watching_toast', { name }))
  }
  catch {
    toasts.push('error', t('watches.watch_error'))
  }
}

async function onRemove(row: WatchSummaryRow): Promise<void> {
  const id = watchIdByKey.value[`${row.ecosystem}::${row.packageName}`]
  if (!id) return
  try {
    await deleteWatch.mutateAsync(id)
  }
  catch {
    toasts.push('error', t('watches.remove_error'))
  }
}
</script>

<template>
  <section class="rounded-lg border border-slate-800 bg-slate-900">
    <header class="flex items-center justify-between border-b border-slate-800 px-4 py-3">
      <h2 class="flex items-center gap-2 text-lg font-medium">
        <Star class="h-4 w-4 text-yellow-400" />
        {{ t('watches.title') }}
      </h2>
      <button
        type="button"
        class="rounded border border-dashed border-slate-600 px-3 py-1 text-xs text-slate-300 hover:border-slate-500 hover:bg-slate-800"
        @click="addOpen = !addOpen"
      >
        {{ t('watches.add_btn') }}
      </button>
    </header>

    <div v-if="addOpen" class="flex items-center gap-2 border-b border-slate-800 px-4 py-2">
      <select
        v-model="newEcosystem"
        class="rounded border border-slate-700 bg-slate-800 px-2 py-1 text-xs text-slate-200"
      >
        <option v-for="value in ecosystemOptions" :key="value" :value="value">
          {{ EcosystemNames[value] }}
        </option>
      </select>
      <input
        v-model="newName"
        type="text"
        :placeholder="t('watches.package_placeholder')"
        class="flex-1 rounded border border-slate-700 bg-slate-800 px-2 py-1 text-xs focus:border-blue-500 focus:outline-none"
        @keydown.enter="onAdd"
      >
      <button
        type="button"
        class="rounded border border-blue-700 bg-blue-950/40 px-2 py-1 text-xs text-blue-200 hover:bg-blue-900/40 disabled:opacity-40"
        :disabled="!newName.trim() || createWatch.isPending.value"
        @click="onAdd"
      >
        {{ t('watches.save_btn') }}
      </button>
    </div>

    <p v-if="isLoading" class="px-4 py-6 text-sm text-slate-400">{{ t('watches.loading') }}</p>
    <ul v-else-if="rows.length" class="divide-y divide-slate-800">
      <li
        v-for="row in rows"
        :key="`${row.ecosystem}-${row.packageName}`"
        class="flex items-center justify-between gap-3 px-4 py-2 text-sm"
      >
        <div class="min-w-0 flex-1">
          <RouterLink
            :to="`/findings?packageName=${encodeURIComponent(row.packageName)}`"
            class="font-mono text-blue-300 hover:underline"
          >
            {{ row.packageName }}
          </RouterLink>
          <p class="text-xs text-slate-500">
            {{ EcosystemNames[row.ecosystem] }} · {{ t('watches.source_count', row.sourceCount) }}
          </p>
        </div>
        <div class="flex shrink-0 items-center gap-1 text-xs">
          <span v-if="row.openFindings.critical > 0" class="rounded bg-red-950/40 px-1.5 py-0.5 text-red-200">
            C {{ row.openFindings.critical }}
          </span>
          <span v-if="row.openFindings.high > 0" class="rounded bg-orange-950/40 px-1.5 py-0.5 text-orange-200">
            H {{ row.openFindings.high }}
          </span>
          <span v-if="row.openFindings.medium > 0" class="rounded bg-yellow-950/40 px-1.5 py-0.5 text-yellow-200">
            M {{ row.openFindings.medium }}
          </span>
          <span v-if="row.openFindings.low > 0" class="rounded bg-slate-800 px-1.5 py-0.5 text-slate-300">
            L {{ row.openFindings.low }}
          </span>
          <span
            v-if="!row.openFindings.critical && !row.openFindings.high && !row.openFindings.medium && !row.openFindings.low"
            class="text-slate-500"
          >
            {{ t('watches.no_open_findings') }}
          </span>
          <button
            type="button"
            class="ml-2 rounded text-slate-500 hover:text-slate-200"
            :aria-label="t('watches.stop_watching_aria', { name: row.packageName })"
            @click="onRemove(row)"
          >
            <X class="h-3.5 w-3.5" />
          </button>
        </div>
      </li>
    </ul>
    <p v-else class="px-4 py-6 text-sm text-slate-500">{{ t('watches.empty') }}</p>
  </section>
</template>
