<script setup lang="ts">
import { computed, ref } from 'vue'
import { RouterLink, useRoute, useRouter } from 'vue-router'

import SeverityBadge from '@/components/SeverityBadge.vue'
import { useFindingsQuery } from '@/queries/findings'
import { formatDate } from '@/lib/format'
import {
  Ecosystem,
  EcosystemNames,
  FindingState,
  FindingStateNames,
  Severity,
} from '@/types/api'
import type { Finding, FindingFilter, FindingsPage } from '@/types/api'

const route = useRoute()
const router = useRouter()

// Empty-string sentinel = no filter; numeric values map to enum ints.
const severityFilter = ref<Severity | ''>('')
const stateFilter = ref<FindingState | ''>('')
const ecosystemFilter = ref<Ecosystem | ''>('')
const page = ref(1)

// Package filter comes from query params (set by Inventory tree/flat links).
// Backend doesn't expose package filtering yet, so we narrow client-side.
const packageNameFilter = computed<string | null>(() => {
  const raw = route.query.packageName
  return typeof raw === 'string' && raw.length > 0 ? raw : null
})
const packageVersionFilter = computed<string | null>(() => {
  const raw = route.query.packageVersion
  return typeof raw === 'string' && raw.length > 0 ? raw : null
})

const filter = computed<FindingFilter>(() => ({
  severity: severityFilter.value === '' ? undefined : severityFilter.value,
  state: stateFilter.value === '' ? undefined : stateFilter.value,
  ecosystem: ecosystemFilter.value === '' ? undefined : ecosystemFilter.value,
  page: page.value,
  // When package filter is active, pull a larger window so client-side narrow has data to work with.
  pageSize: packageNameFilter.value ? 200 : 50,
}))

const { data, isLoading, isError } = useFindingsQuery(filter)

const filteredData = computed<FindingsPage | undefined>(() => {
  if (!data.value) return data.value
  if (!packageNameFilter.value) return data.value
  const name = packageNameFilter.value
  const version = packageVersionFilter.value
  const items: Finding[] = data.value.items.filter((finding) => {
    if (finding.packageName !== name) return false
    if (version !== null && finding.packageVersion !== version) return false
    return true
  })
  return { ...data.value, items, total: items.length }
})

function clearPackageFilter(): void {
  const next = { ...route.query }
  delete next.packageName
  delete next.packageVersion
  router.replace({ path: route.path, query: next })
}
</script>

<template>
  <div class="space-y-6">
    <h1 class="text-2xl font-semibold">Findings</h1>

    <div
      v-if="packageNameFilter"
      class="flex items-center gap-2 rounded-lg border border-blue-900/40 bg-blue-950/30 px-3 py-2 text-sm text-blue-200"
    >
      <span class="text-xs uppercase text-blue-400">Package</span>
      <span class="font-mono text-slate-100">{{ packageNameFilter }}</span>
      <span v-if="packageVersionFilter" class="font-mono text-slate-400">@{{ packageVersionFilter }}</span>
      <button
        type="button"
        class="ml-auto rounded border border-blue-900/40 px-2 py-0.5 text-xs hover:bg-blue-900/30"
        @click="clearPackageFilter"
      >
        Clear
      </button>
    </div>

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

    <div v-else-if="filteredData && filteredData.items.length" class="overflow-hidden rounded-lg border border-slate-800 bg-slate-900">
      <table class="w-full text-left text-sm">
        <thead class="border-b border-slate-800 text-xs uppercase text-slate-500">
          <tr>
            <th class="px-4 py-2">Severity</th>
            <th class="px-4 py-2">Package</th>
            <th class="px-4 py-2">Advisory</th>
            <th class="px-4 py-2">Source</th>
            <th class="px-4 py-2">Last seen</th>
            <th class="px-4 py-2">State</th>
          </tr>
        </thead>
        <tbody class="divide-y divide-slate-800">
          <tr v-for="finding in filteredData.items" :key="finding.id" class="hover:bg-slate-800/50">
            <td class="px-4 py-2">
              <SeverityBadge :severity="finding.severity" />
            </td>
            <td class="px-4 py-2">
              <RouterLink :to="`/findings/${finding.id}`" class="font-mono text-sm text-blue-300 hover:underline">
                <span class="text-slate-100">{{ finding.packageName ?? '—' }}</span><span
                  v-if="finding.packageVersion"
                  class="text-slate-400"
                >@{{ finding.packageVersion }}</span>
              </RouterLink>
              <p v-if="finding.ecosystem !== null && finding.ecosystem !== undefined" class="text-xs uppercase text-slate-500">
                {{ EcosystemNames[finding.ecosystem] }}
              </p>
            </td>
            <td class="px-4 py-2">
              <p class="line-clamp-1 text-slate-200">{{ finding.advisorySummary ?? '—' }}</p>
              <p v-if="finding.advisoryExternalId" class="font-mono text-xs text-slate-500">{{ finding.advisoryExternalId }}</p>
            </td>
            <td class="px-4 py-2 text-slate-300">{{ finding.sourceName ?? `#${finding.sourceId}` }}</td>
            <td class="px-4 py-2 text-slate-400">{{ formatDate(finding.lastSeenAt) }}</td>
            <td class="px-4 py-2 text-slate-400">{{ FindingStateNames[finding.state] }}</td>
          </tr>
        </tbody>
      </table>
      <footer v-if="!packageNameFilter && filteredData.total > filteredData.pageSize" class="flex items-center justify-between border-t border-slate-800 px-4 py-2 text-xs text-slate-400">
        <span>Page {{ filteredData.page }} of {{ Math.ceil(filteredData.total / filteredData.pageSize) }} · {{ filteredData.total }} findings</span>
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
            :disabled="page >= Math.ceil(filteredData.total / filteredData.pageSize)"
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
