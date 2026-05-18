<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'

import SortableTh from '@/components/SortableTh.vue'
import { useClientSort } from '@/composables/useClientSort'
import { useAuditQuery } from '@/queries/audit'
import { formatDate } from '@/lib/format'
import type { AuditEntry, AuditFilter } from '@/types/api'

const { t } = useI18n()

const TARGET_TYPES = ['Finding', 'Source', 'Channel', 'Setting', 'OAuth', 'Invite'] as const
const ACTIONS = [
  'finding.ack',
  'finding.resolve',
  'finding.suppress',
  'finding.bulk-ack',
  'finding.bulk-resolve',
  'finding.bulk-suppress',
  'source.create',
  'source.update',
  'source.delete',
  'source.bulk-create',
  'channel.create',
  'channel.update',
  'channel.delete',
  'settings.update',
  'oauth.connect',
  'oauth.disconnect',
  'invite.create',
  'invite.revoke',
  'invite.accept',
] as const

const page = ref(1)
const pageSize = ref(50)
const action = ref<string | null>(null)
const targetType = ref<string | null>(null)

const filter = computed<AuditFilter>(() => ({
  page: page.value,
  pageSize: pageSize.value,
  action: action.value,
  targetType: targetType.value,
}))

const { data, isLoading, isError } = useAuditQuery(filter)

// Client-side sort over the current page. The audit endpoint paginates server-side, so
// this only reorders what's currently visible — but it's still better than no sort, and
// the page is bounded by pageSize (default 50) so the reshuffle stays cheap.
const auditRows = computed<AuditEntry[]>(() => data.value?.items ?? [])
const { sortedRows, sortKey, sortDir, toggleSort } = useClientSort<AuditEntry>(auditRows, [
  { key: 'when', extract: row => row.at, defaultDirection: 'desc' },
  { key: 'actor', extract: row => row.actorDisplayName ?? row.actorUserId ?? '', defaultDirection: 'asc' },
  { key: 'action', extract: row => row.action, defaultDirection: 'asc' },
  { key: 'target', extract: row => `${row.targetType ?? ''}/${row.targetId ?? ''}`, defaultDirection: 'asc' },
  { key: 'ip', extract: row => row.ipAddress, defaultDirection: 'asc' },
])

const totalPages = computed(() => {
  if (!data.value)
    return 1
  return Math.max(1, Math.ceil(data.value.total / pageSize.value))
})

function toggleAction(value: string): void {
  action.value = action.value === value ? null : value
  page.value = 1
}

function toggleTargetType(value: string): void {
  targetType.value = targetType.value === value ? null : value
  page.value = 1
}

function actorDisplay(entry: AuditEntry): string {
  return entry.actorLogin ?? entry.actorName ?? 'system'
}

function actorInitial(entry: AuditEntry): string {
  const name = actorDisplay(entry)
  return name.charAt(0).toUpperCase() || '?'
}

function prettyDetails(json: string | null): string {
  if (!json) return ''
  try {
    return JSON.stringify(JSON.parse(json), null, 2)
  }
  catch {
    return json
  }
}
</script>

<template>
  <div class="space-y-6">
    <div>
      <h1 class="text-2xl font-semibold">{{ t('audit.title') }}</h1>
      <p class="text-sm text-slate-400">{{ t('audit.subtitle') }}</p>
    </div>

    <div class="space-y-3 rounded-lg border border-slate-800 bg-slate-900/40 p-4">
      <div>
        <div class="mb-1 text-xs uppercase tracking-wide text-slate-500">{{ t('audit.filter_target_type') }}</div>
        <div class="flex flex-wrap gap-1">
          <button
            v-for="type in TARGET_TYPES"
            :key="type"
            type="button"
            class="rounded-full border px-3 py-1 text-xs"
            :class="targetType === type
              ? 'border-blue-400 bg-blue-500/20 text-blue-100'
              : 'border-slate-700 text-slate-300 hover:bg-slate-800'"
            @click="toggleTargetType(type)"
          >
            {{ type }}
          </button>
        </div>
      </div>

      <div>
        <div class="mb-1 text-xs uppercase tracking-wide text-slate-500">{{ t('audit.filter_action') }}</div>
        <div class="flex flex-wrap gap-1">
          <button
            v-for="value in ACTIONS"
            :key="value"
            type="button"
            class="rounded-full border px-3 py-1 text-xs"
            :class="action === value
              ? 'border-blue-400 bg-blue-500/20 text-blue-100'
              : 'border-slate-700 text-slate-300 hover:bg-slate-800'"
            @click="toggleAction(value)"
          >
            {{ value }}
          </button>
        </div>
      </div>
    </div>

    <p v-if="isLoading" class="text-sm text-slate-400">{{ t('audit.loading') }}</p>
    <p v-else-if="isError" class="text-sm text-red-300">{{ t('audit.error') }}</p>
    <p v-else-if="!data || data.items.length === 0" class="text-sm text-slate-500">{{ t('audit.empty') }}</p>

    <div v-else class="overflow-x-auto rounded-lg border border-slate-800 bg-slate-900">
      <table class="w-full min-w-[800px] text-left text-sm">
        <thead class="border-b border-slate-800 text-xs uppercase text-slate-500">
          <tr>
            <SortableTh column-key="when" :active-key="sortKey" :active-dir="sortDir" @toggle="toggleSort">
              {{ t('audit.col_when') }}
            </SortableTh>
            <SortableTh column-key="actor" :active-key="sortKey" :active-dir="sortDir" @toggle="toggleSort">
              {{ t('audit.col_actor') }}
            </SortableTh>
            <SortableTh column-key="action" :active-key="sortKey" :active-dir="sortDir" @toggle="toggleSort">
              {{ t('audit.col_action') }}
            </SortableTh>
            <SortableTh column-key="target" :active-key="sortKey" :active-dir="sortDir" @toggle="toggleSort">
              {{ t('audit.col_target') }}
            </SortableTh>
            <SortableTh column-key="ip" :active-key="sortKey" :active-dir="sortDir" @toggle="toggleSort">
              {{ t('audit.col_ip') }}
            </SortableTh>
          </tr>
        </thead>
        <tbody class="divide-y divide-slate-800">
          <template v-for="entry in sortedRows" :key="entry.id">
            <tr class="hover:bg-slate-800/50">
              <td class="whitespace-nowrap px-4 py-2 text-slate-400">{{ formatDate(entry.at) }}</td>
              <td class="px-4 py-2 text-slate-200">
                <div class="flex items-center gap-2">
                  <img
                    v-if="entry.actorAvatarUrl"
                    :src="entry.actorAvatarUrl"
                    :alt="actorDisplay(entry)"
                    loading="lazy"
                    referrerpolicy="no-referrer"
                    class="h-6 w-6 shrink-0 rounded-full object-cover ring-1 ring-slate-700"
                  />
                  <span
                    v-else
                    class="grid h-6 w-6 shrink-0 place-items-center rounded-full bg-slate-700 text-[10px] font-semibold text-slate-200"
                  >
                    {{ actorInitial(entry) }}
                  </span>
                  <span class="truncate">{{ actorDisplay(entry) }}</span>
                </div>
              </td>
              <td class="px-4 py-2">
                <span class="rounded bg-slate-800 px-2 py-0.5 font-mono text-xs text-blue-200">
                  {{ entry.action }}
                </span>
              </td>
              <td class="px-4 py-2 text-slate-300">
                <span v-if="entry.targetLabel" class="truncate">{{ entry.targetLabel }}</span>
                <span v-else class="text-xs">
                  <span class="text-slate-500">{{ entry.targetType }}</span>
                  <span class="ml-1 font-mono">#{{ entry.targetId }}</span>
                </span>
              </td>
              <td class="px-4 py-2 text-xs text-slate-500">{{ entry.remoteIp ?? '—' }}</td>
            </tr>
            <tr v-if="entry.detailsJson">
              <td colspan="5" class="px-4 pb-2">
                <details class="group">
                  <summary class="cursor-pointer text-xs text-slate-500 hover:text-slate-300">
                    {{ t('audit.details_summary') }}
                  </summary>
                  <pre class="mt-2 max-h-64 overflow-auto rounded border border-slate-800 bg-slate-950 p-3 font-mono text-xs text-slate-300"><code>{{ prettyDetails(entry.detailsJson) }}</code></pre>
                </details>
              </td>
            </tr>
          </template>
        </tbody>
      </table>
    </div>

    <div v-if="data && data.total > pageSize" class="flex items-center justify-between text-sm">
      <span class="text-slate-500">
        {{ t('audit.page_of_total', { page, total: totalPages, count: data.total }) }}
      </span>
      <div class="flex gap-2">
        <button
          type="button"
          class="rounded border border-slate-700 px-3 py-1 text-slate-200 hover:bg-slate-800 disabled:opacity-40"
          :disabled="page <= 1"
          @click="page = Math.max(1, page - 1)"
        >
          {{ t('audit.prev') }}
        </button>
        <button
          type="button"
          class="rounded border border-slate-700 px-3 py-1 text-slate-200 hover:bg-slate-800 disabled:opacity-40"
          :disabled="page >= totalPages"
          @click="page = Math.min(totalPages, page + 1)"
        >
          {{ t('audit.next') }}
        </button>
      </div>
    </div>
  </div>
</template>
