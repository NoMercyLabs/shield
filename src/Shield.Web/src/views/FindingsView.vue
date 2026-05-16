<script setup lang="ts">
import { computed, ref } from 'vue'
import { RouterLink } from 'vue-router'

import SeverityBadge from '@/components/SeverityBadge.vue'
import { useFindingsQuery } from '@/queries/findings'
import { formatDate } from '@/lib/format'
import {
  Ecosystem,
  FindingState,
  FindingStateNames,
  Severity,
} from '@/types/api'
import type { FindingFilter } from '@/types/api'

// Empty-string sentinel = no filter; numeric values map to enum ints.
const severityFilter = ref<Severity | ''>('')
const stateFilter = ref<FindingState | ''>('')
const ecosystemFilter = ref<Ecosystem | ''>('')
const page = ref(1)

const filter = computed<FindingFilter>(() => ({
  severity: severityFilter.value === '' ? undefined : severityFilter.value,
  state: stateFilter.value === '' ? undefined : stateFilter.value,
  ecosystem: ecosystemFilter.value === '' ? undefined : ecosystemFilter.value,
  page: page.value,
  pageSize: 50,
}))

const { data, isLoading, isError } = useFindingsQuery(filter)
</script>

<template>
  <div class="space-y-6">
    <h1 class="text-2xl font-semibold">Findings</h1>

    <div class="flex flex-wrap gap-2">
      <select
        v-model.number="severityFilter"
        class="rounded border border-slate-700 bg-slate-800 px-3 py-1.5 text-sm focus:border-blue-500 focus:outline-none"
      >
        <option value="">All severities</option>
        <option :value="Severity.Critical">Critical</option>
        <option :value="Severity.High">High</option>
        <option :value="Severity.Medium">Medium</option>
        <option :value="Severity.Low">Low</option>
      </select>
      <select
        v-model.number="stateFilter"
        class="rounded border border-slate-700 bg-slate-800 px-3 py-1.5 text-sm focus:border-blue-500 focus:outline-none"
      >
        <option value="">All states</option>
        <option :value="FindingState.Open">Open</option>
        <option :value="FindingState.Acked">Acked</option>
        <option :value="FindingState.Resolved">Resolved</option>
        <option :value="FindingState.Suppressed">Suppressed</option>
      </select>
      <select
        v-model.number="ecosystemFilter"
        class="rounded border border-slate-700 bg-slate-800 px-3 py-1.5 text-sm focus:border-blue-500 focus:outline-none"
      >
        <option value="">All ecosystems</option>
        <option :value="Ecosystem.Npm">npm</option>
        <option :value="Ecosystem.Nuget">NuGet</option>
        <option :value="Ecosystem.Composer">Composer</option>
        <option :value="Ecosystem.Gradle">Gradle</option>
        <option :value="Ecosystem.Os">OS</option>
      </select>
    </div>

    <p v-if="isLoading" class="text-sm text-slate-400">Loading…</p>
    <p v-else-if="isError" class="text-sm text-red-300">Failed to load findings.</p>

    <div v-else-if="data && data.items.length" class="overflow-hidden rounded-lg border border-slate-800 bg-slate-900">
      <table class="w-full text-left text-sm">
        <thead class="border-b border-slate-800 text-xs uppercase text-slate-500">
          <tr>
            <th class="px-4 py-2">Severity</th>
            <th class="px-4 py-2">Dedup key</th>
            <th class="px-4 py-2">Source</th>
            <th class="px-4 py-2">First seen</th>
            <th class="px-4 py-2">State</th>
          </tr>
        </thead>
        <tbody class="divide-y divide-slate-800">
          <tr v-for="finding in data.items" :key="finding.id" class="hover:bg-slate-800/50">
            <td class="px-4 py-2">
              <SeverityBadge :severity="finding.severity" />
            </td>
            <td class="px-4 py-2">
              <RouterLink :to="`/findings/${finding.id}`" class="font-mono text-xs text-blue-400 hover:underline">
                {{ finding.dedupKey }}
              </RouterLink>
            </td>
            <td class="px-4 py-2 text-slate-400">#{{ finding.sourceId }}</td>
            <td class="px-4 py-2 text-slate-400">{{ formatDate(finding.firstSeenAt) }}</td>
            <td class="px-4 py-2 text-slate-400">{{ FindingStateNames[finding.state] }}</td>
          </tr>
        </tbody>
      </table>
      <footer v-if="data.total > data.pageSize" class="flex items-center justify-between border-t border-slate-800 px-4 py-2 text-xs text-slate-400">
        <span>Page {{ data.page }} of {{ Math.ceil(data.total / data.pageSize) }} · {{ data.total }} findings</span>
        <div class="flex gap-2">
          <button
            type="button"
            class="rounded border border-slate-700 px-2 py-1 hover:bg-slate-800 disabled:opacity-40"
            :disabled="page <= 1"
            @click="page = page - 1"
          >
            Prev
          </button>
          <button
            type="button"
            class="rounded border border-slate-700 px-2 py-1 hover:bg-slate-800 disabled:opacity-40"
            :disabled="page >= Math.ceil(data.total / data.pageSize)"
            @click="page = page + 1"
          >
            Next
          </button>
        </div>
      </footer>
    </div>

    <p v-else class="text-sm text-slate-500">No findings match the current filters.</p>
  </div>
</template>
