<script setup lang="ts">
import { computed, ref } from 'vue'

import { useAuditQuery } from '@/queries/audit'
import { formatDate } from '@/lib/format'
import type { AuditFilter } from '@/types/api'

const TARGET_TYPES = ['Finding', 'Source', 'Channel', 'Setting', 'OAuth'] as const
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
</script>

<template>
  <div class="space-y-6">
    <div>
      <h1 class="text-2xl font-semibold">Audit log</h1>
      <p class="text-sm text-slate-400">
        Admin actions recorded by the server: finding transitions, source / channel mutations,
        settings changes, OAuth connect / disconnect.
      </p>
    </div>

    <div class="space-y-3 rounded-lg border border-slate-800 bg-slate-900/40 p-4">
      <div>
        <div class="mb-1 text-xs uppercase tracking-wide text-slate-500">Target type</div>
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
        <div class="mb-1 text-xs uppercase tracking-wide text-slate-500">Action</div>
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

    <p v-if="isLoading" class="text-sm text-slate-400">Loading audit entries…</p>
    <p v-else-if="isError" class="text-sm text-red-300">Failed to load audit entries.</p>
    <p v-else-if="!data || data.items.length === 0" class="text-sm text-slate-500">
      No audit entries match the current filters.
    </p>

    <div v-else class="overflow-hidden rounded-lg border border-slate-800 bg-slate-900">
      <table class="w-full text-left text-sm">
        <thead class="border-b border-slate-800 text-xs uppercase text-slate-500">
          <tr>
            <th class="px-4 py-2">When</th>
            <th class="px-4 py-2">Actor</th>
            <th class="px-4 py-2">Action</th>
            <th class="px-4 py-2">Target</th>
            <th class="px-4 py-2">IP</th>
          </tr>
        </thead>
        <tbody class="divide-y divide-slate-800">
          <tr v-for="entry in data.items" :key="entry.id" class="hover:bg-slate-800/50">
            <td class="whitespace-nowrap px-4 py-2 text-slate-400">{{ formatDate(entry.at) }}</td>
            <td class="px-4 py-2 text-slate-200">{{ entry.actorName }}</td>
            <td class="px-4 py-2">
              <span class="rounded bg-slate-800 px-2 py-0.5 font-mono text-xs text-blue-200">
                {{ entry.action }}
              </span>
            </td>
            <td class="px-4 py-2 text-slate-300">
              <span class="text-xs text-slate-500">{{ entry.targetType }}</span>
              <span class="ml-1 font-mono text-xs">{{ entry.targetId }}</span>
            </td>
            <td class="px-4 py-2 text-xs text-slate-500">{{ entry.remoteIp ?? '—' }}</td>
          </tr>
        </tbody>
      </table>
    </div>

    <div v-if="data && data.total > pageSize" class="flex items-center justify-between text-sm">
      <span class="text-slate-500">
        Page {{ page }} of {{ totalPages }} · {{ data.total }} entries
      </span>
      <div class="flex gap-2">
        <button
          type="button"
          class="rounded border border-slate-700 px-3 py-1 text-slate-200 hover:bg-slate-800 disabled:opacity-40"
          :disabled="page <= 1"
          @click="page = Math.max(1, page - 1)"
        >
          Previous
        </button>
        <button
          type="button"
          class="rounded border border-slate-700 px-3 py-1 text-slate-200 hover:bg-slate-800 disabled:opacity-40"
          :disabled="page >= totalPages"
          @click="page = Math.min(totalPages, page + 1)"
        >
          Next
        </button>
      </div>
    </div>
  </div>
</template>
