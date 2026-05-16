<script setup lang="ts">
import { computed, ref } from 'vue'
import { RouterLink } from 'vue-router'

import SeverityBadge from '@/components/SeverityBadge.vue'
import { useFindingsQuery } from '@/queries/findings'
import { formatDate } from '@/lib/format'
import type { Ecosystem, FindingFilter, FindingState, Severity } from '@/types/api'

const severityFilter = ref<Severity | ''>('')
const stateFilter = ref<FindingState | ''>('')
const ecosystemFilter = ref<Ecosystem | ''>('')
const page = ref(1)

const filter = computed<FindingFilter>(() => ({
  severity: severityFilter.value || undefined,
  state: stateFilter.value || undefined,
  ecosystem: ecosystemFilter.value || undefined,
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
        v-model="severityFilter"
        class="rounded border border-slate-700 bg-slate-800 px-3 py-1.5 text-sm focus:border-blue-500 focus:outline-none"
      >
        <option value="">All severities</option>
        <option value="Critical">Critical</option>
        <option value="High">High</option>
        <option value="Medium">Medium</option>
        <option value="Low">Low</option>
      </select>
      <select
        v-model="stateFilter"
        class="rounded border border-slate-700 bg-slate-800 px-3 py-1.5 text-sm focus:border-blue-500 focus:outline-none"
      >
        <option value="">All states</option>
        <option value="Open">Open</option>
        <option value="Acked">Acked</option>
        <option value="Resolved">Resolved</option>
        <option value="Suppressed">Suppressed</option>
      </select>
      <select
        v-model="ecosystemFilter"
        class="rounded border border-slate-700 bg-slate-800 px-3 py-1.5 text-sm focus:border-blue-500 focus:outline-none"
      >
        <option value="">All ecosystems</option>
        <option value="Npm">npm</option>
        <option value="Nuget">NuGet</option>
        <option value="Composer">Composer</option>
        <option value="Gradle">Gradle</option>
        <option value="Os">OS</option>
      </select>
    </div>

    <p v-if="isLoading" class="text-sm text-slate-400">Loading…</p>
    <p v-else-if="isError" class="text-sm text-red-300">Failed to load findings.</p>

    <div v-else-if="data && data.items.length" class="overflow-hidden rounded-lg border border-slate-800 bg-slate-900">
      <table class="w-full text-left text-sm">
        <thead class="border-b border-slate-800 text-xs uppercase text-slate-500">
          <tr>
            <th class="px-4 py-2">Severity</th>
            <th class="px-4 py-2">Package</th>
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
              <RouterLink :to="`/findings/${finding.id}`" class="text-blue-400 hover:underline">
                {{ finding.packageName }}@{{ finding.packageVersion }}
              </RouterLink>
            </td>
            <td class="px-4 py-2 text-slate-400">{{ finding.sourceName }}</td>
            <td class="px-4 py-2 text-slate-400">{{ formatDate(finding.firstSeenAt) }}</td>
            <td class="px-4 py-2 text-slate-400">{{ finding.state }}</td>
          </tr>
        </tbody>
      </table>
    </div>

    <p v-else class="text-sm text-slate-500">No findings match the current filters.</p>
  </div>
</template>
