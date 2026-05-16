<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { RouterLink, useRoute, useRouter } from 'vue-router'

import SeverityBadge from '@/components/SeverityBadge.vue'
import {
  useBulkAckFindingsMutation,
  useBulkResolveFindingsMutation,
  useBulkSuppressFindingsMutation,
  useFindingsQuery,
} from '@/queries/findings'
import { useSourcesQuery } from '@/queries/sources'
import { formatDate } from '@/lib/format'
import { useToasts } from '@/stores/toast'
import {
  Ecosystem,
  EcosystemNames,
  FindingState,
  FindingStateNames,
  Severity,
  SeverityNames,
} from '@/types/api'
import type { Finding, FindingFilter, FindingsPage } from '@/types/api'

const route = useRoute()
const router = useRouter()
const toasts = useToasts()

// ---------- persisted filter state ----------

interface PersistedFilters {
  severity: Severity[]
  state: FindingState[]
  ecosystem: Ecosystem[]
  packageName: string[]
  sourceId: number[]
}

const FILTERS_KEY = 'shield.findings.filters'

function loadFilters(): PersistedFilters {
  const empty: PersistedFilters = {
    severity: [],
    state: [],
    ecosystem: [],
    packageName: [],
    sourceId: [],
  }
  try {
    const raw = localStorage.getItem(FILTERS_KEY)
    if (!raw) return empty
    const parsed = JSON.parse(raw) as Partial<PersistedFilters>
    return {
      severity: Array.isArray(parsed.severity) ? parsed.severity : [],
      state: Array.isArray(parsed.state) ? parsed.state : [],
      ecosystem: Array.isArray(parsed.ecosystem) ? parsed.ecosystem : [],
      packageName: Array.isArray(parsed.packageName) ? parsed.packageName : [],
      sourceId: Array.isArray(parsed.sourceId) ? parsed.sourceId : [],
    }
  }
  catch {
    return empty
  }
}

const severityFilter = ref<Severity[]>([])
const stateFilter = ref<FindingState[]>([])
const ecosystemFilter = ref<Ecosystem[]>([])
const packageNameFilter = ref<string[]>([])
const sourceIdFilter = ref<number[]>([])
const last24hOnly = ref(false)
const page = ref(1)

onMounted(() => {
  const persisted = loadFilters()
  severityFilter.value = persisted.severity
  stateFilter.value = persisted.state
  ecosystemFilter.value = persisted.ecosystem
  packageNameFilter.value = persisted.packageName
  sourceIdFilter.value = persisted.sourceId

  // Allow ?packageName=foo on the URL to seed the package filter once (back-compat with
  // Inventory tree deep-links). After load it merges into the persisted set.
  const raw = route.query.packageName
  if (typeof raw === 'string' && raw.length > 0 && !packageNameFilter.value.includes(raw))
    packageNameFilter.value = [...packageNameFilter.value, raw]
})

watch(
  [severityFilter, stateFilter, ecosystemFilter, packageNameFilter, sourceIdFilter],
  () => {
    const snapshot: PersistedFilters = {
      severity: severityFilter.value,
      state: stateFilter.value,
      ecosystem: ecosystemFilter.value,
      packageName: packageNameFilter.value,
      sourceId: sourceIdFilter.value,
    }
    localStorage.setItem(FILTERS_KEY, JSON.stringify(snapshot))
    page.value = 1
    selectedIds.value = new Set()
  },
  { deep: true },
)

// ---------- query ----------

const filter = computed<FindingFilter>(() => ({
  severity: severityFilter.value.length ? severityFilter.value : undefined,
  state: stateFilter.value.length ? stateFilter.value : undefined,
  ecosystem: ecosystemFilter.value.length ? ecosystemFilter.value : undefined,
  packageName: packageNameFilter.value.length ? packageNameFilter.value : undefined,
  sourceId: sourceIdFilter.value.length ? sourceIdFilter.value : undefined,
  page: page.value,
  pageSize: 50,
}))

const { data, isLoading, isError, refetch } = useFindingsQuery(filter)
const { data: sourcesData } = useSourcesQuery()

const sourceNameById = computed<Record<number, string>>(() => {
  const out: Record<number, string> = {}
  for (const source of sourcesData.value ?? [])
    out[source.id] = source.name
  return out
})

const filteredData = computed<FindingsPage | undefined>(() => {
  if (!data.value) return data.value
  if (!last24hOnly.value) return data.value
  const cutoff = Date.now() - 24 * 60 * 60 * 1000
  const items: Finding[] = data.value.items.filter((finding) => {
    const seen = Date.parse(finding.lastSeenAt)
    return Number.isFinite(seen) && seen >= cutoff
  })
  return { ...data.value, items, total: items.length }
})

// ---------- selection ----------

const selectedIds = ref<Set<string>>(new Set())

const pageIds = computed<string[]>(() => filteredData.value?.items.map(finding => finding.id) ?? [])
const allOnPageSelected = computed<boolean>(() =>
  pageIds.value.length > 0 && pageIds.value.every(id => selectedIds.value.has(id)),
)
const someOnPageSelected = computed<boolean>(() =>
  pageIds.value.some(id => selectedIds.value.has(id)) && !allOnPageSelected.value,
)

function toggleAllOnPage(): void {
  const next = new Set(selectedIds.value)
  if (allOnPageSelected.value) {
    for (const id of pageIds.value) next.delete(id)
  }
  else {
    for (const id of pageIds.value) next.add(id)
  }
  selectedIds.value = next
}

function toggleOne(id: string): void {
  const next = new Set(selectedIds.value)
  if (next.has(id)) next.delete(id)
  else next.add(id)
  selectedIds.value = next
}

function clearSelection(): void {
  selectedIds.value = new Set()
}

// ---------- bulk mutations ----------

const bulkAck = useBulkAckFindingsMutation()
const bulkResolve = useBulkResolveFindingsMutation()
const bulkSuppress = useBulkSuppressFindingsMutation()

const suppressOpen = ref(false)
const suppressReason = ref('')

async function runBulk(verb: 'ack' | 'resolve' | 'suppress'): Promise<void> {
  const ids = Array.from(selectedIds.value)
  if (ids.length === 0) return
  try {
    let response: { updated: number, notFound: string[] }
    if (verb === 'ack') {
      response = await bulkAck.mutateAsync({ findingIds: ids })
    }
    else if (verb === 'resolve') {
      response = await bulkResolve.mutateAsync({ findingIds: ids })
    }
    else {
      response = await bulkSuppress.mutateAsync({ findingIds: ids, reason: suppressReason.value })
      suppressOpen.value = false
      suppressReason.value = ''
    }
    const verbPast = verb === 'ack' ? 'acked' : verb === 'resolve' ? 'resolved' : 'suppressed'
    const missingNote = response.notFound.length > 0 ? ` (${response.notFound.length} not found)` : ''
    toasts.push('success', `${response.updated} finding(s) ${verbPast}${missingNote}.`)
    clearSelection()
    await refetch()
  }
  catch (error) {
    toasts.push('error', `Bulk ${verb} failed: ${error instanceof Error ? error.message : String(error)}`)
  }
}

// ---------- chip + filter UI ----------

interface Chip {
  key: string
  label: string
  remove: () => void
}

const chips = computed<Chip[]>(() => {
  const out: Chip[] = []
  for (const value of severityFilter.value) {
    out.push({
      key: `sev-${value}`,
      label: `Severity: ${SeverityNames[value]}`,
      remove: () => { severityFilter.value = severityFilter.value.filter(item => item !== value) },
    })
  }
  for (const value of stateFilter.value) {
    out.push({
      key: `state-${value}`,
      label: `State: ${FindingStateNames[value]}`,
      remove: () => { stateFilter.value = stateFilter.value.filter(item => item !== value) },
    })
  }
  for (const value of ecosystemFilter.value) {
    out.push({
      key: `eco-${value}`,
      label: `Ecosystem: ${EcosystemNames[value]}`,
      remove: () => { ecosystemFilter.value = ecosystemFilter.value.filter(item => item !== value) },
    })
  }
  for (const value of packageNameFilter.value) {
    out.push({
      key: `pkg-${value}`,
      label: `Package: ${value}`,
      remove: () => { packageNameFilter.value = packageNameFilter.value.filter(item => item !== value) },
    })
  }
  for (const value of sourceIdFilter.value) {
    const name = sourceNameById.value[value] ?? `#${value}`
    out.push({
      key: `src-${value}`,
      label: `Source: ${name}`,
      remove: () => { sourceIdFilter.value = sourceIdFilter.value.filter(item => item !== value) },
    })
  }
  if (last24hOnly.value) {
    out.push({
      key: 'last24h',
      label: 'Last 24h',
      remove: () => { last24hOnly.value = false },
    })
  }
  return out
})

const hasAnyFilter = computed<boolean>(() => chips.value.length > 0)

function resetAllFilters(): void {
  severityFilter.value = []
  stateFilter.value = []
  ecosystemFilter.value = []
  packageNameFilter.value = []
  sourceIdFilter.value = []
  last24hOnly.value = false
  const next = { ...route.query }
  delete next.packageName
  delete next.packageVersion
  router.replace({ path: route.path, query: next })
}

// ---------- presets ----------

function applyCriticalOnly(): void {
  severityFilter.value = [Severity.Critical]
  stateFilter.value = []
  ecosystemFilter.value = []
  last24hOnly.value = false
}

function applyLast24h(): void {
  last24hOnly.value = true
}

function applyUnackedOpen(): void {
  stateFilter.value = [FindingState.Open]
  severityFilter.value = []
  ecosystemFilter.value = []
  last24hOnly.value = false
}

// In single-user mode the only operator is "me", so "By me" surfaces the work that's
// still on my plate (Open + Acked) — i.e. excludes Resolved + Suppressed.
function applyByMe(): void {
  stateFilter.value = [FindingState.Open, FindingState.Acked]
  severityFilter.value = []
  ecosystemFilter.value = []
  last24hOnly.value = false
}

// ---------- add-filter popover ----------

const addFilterOpen = ref(false)

function toggleSeverity(value: Severity): void {
  severityFilter.value = severityFilter.value.includes(value)
    ? severityFilter.value.filter(item => item !== value)
    : [...severityFilter.value, value]
}
function toggleState(value: FindingState): void {
  stateFilter.value = stateFilter.value.includes(value)
    ? stateFilter.value.filter(item => item !== value)
    : [...stateFilter.value, value]
}
function toggleEcosystem(value: Ecosystem): void {
  ecosystemFilter.value = ecosystemFilter.value.includes(value)
    ? ecosystemFilter.value.filter(item => item !== value)
    : [...ecosystemFilter.value, value]
}
function toggleSource(value: number): void {
  sourceIdFilter.value = sourceIdFilter.value.includes(value)
    ? sourceIdFilter.value.filter(item => item !== value)
    : [...sourceIdFilter.value, value]
}

const allSeverities: Severity[] = [Severity.Critical, Severity.High, Severity.Medium, Severity.Low]
const allStates: FindingState[] = [
  FindingState.Open,
  FindingState.Acked,
  FindingState.Resolved,
  FindingState.Suppressed,
]
const allEcosystems: Ecosystem[] = [
  Ecosystem.Npm,
  Ecosystem.Nuget,
  Ecosystem.Composer,
  Ecosystem.Gradle,
  Ecosystem.Os,
  Ecosystem.Python,
  Ecosystem.Go,
  Ecosystem.Rust,
]

const headerCheckboxRef = ref<HTMLInputElement | null>(null)
watch([allOnPageSelected, someOnPageSelected], () => {
  if (headerCheckboxRef.value)
    headerCheckboxRef.value.indeterminate = someOnPageSelected.value
})
</script>

<template>
  <div class="space-y-6 pb-24">
    <h1 class="text-2xl font-semibold">Findings</h1>

    <!-- Preset row -->
    <div class="flex flex-wrap items-center gap-2 text-sm">
      <span class="text-xs uppercase text-slate-500">Quick filters</span>
      <button
        type="button"
        class="rounded-full border border-red-900/50 bg-red-950/30 px-3 py-1 text-red-200 hover:bg-red-900/40"
        @click="applyCriticalOnly"
      >
        Critical only
      </button>
      <button
        type="button"
        class="rounded-full border border-slate-700 px-3 py-1 text-slate-200 hover:bg-slate-800"
        @click="applyLast24h"
      >
        Last 24h
      </button>
      <button
        type="button"
        class="rounded-full border border-slate-700 px-3 py-1 text-slate-200 hover:bg-slate-800"
        @click="applyUnackedOpen"
      >
        Unacked open
      </button>
      <button
        type="button"
        class="rounded-full border border-slate-700 px-3 py-1 text-slate-200 hover:bg-slate-800"
        @click="applyByMe"
      >
        By me
      </button>
    </div>

    <!-- Active chips + add-filter dropdown -->
    <div class="flex flex-wrap items-center gap-2">
      <span
        v-for="chip in chips"
        :key="chip.key"
        class="inline-flex items-center gap-1 rounded-full border border-blue-900/40 bg-blue-950/30 px-3 py-1 text-xs text-blue-200"
      >
        {{ chip.label }}
        <button
          type="button"
          class="ml-1 rounded text-blue-300 hover:text-white"
          :aria-label="`Remove ${chip.label}`"
          @click="chip.remove()"
        >
          ✕
        </button>
      </span>

      <div class="relative">
        <button
          type="button"
          class="rounded border border-dashed border-slate-600 px-3 py-1 text-xs text-slate-300 hover:border-slate-500 hover:bg-slate-800"
          @click="addFilterOpen = !addFilterOpen"
        >
          + Add filter
        </button>
        <div
          v-if="addFilterOpen"
          class="absolute z-20 mt-2 grid w-[420px] grid-cols-2 gap-4 rounded-lg border border-slate-700 bg-slate-900 p-4 shadow-xl"
        >
          <section>
            <p class="mb-1 text-xs uppercase text-slate-400">Severity</p>
            <label v-for="value in allSeverities" :key="value" class="flex items-center gap-2 py-0.5 text-sm">
              <input
                type="checkbox"
                :checked="severityFilter.includes(value)"
                @change="toggleSeverity(value)"
              >
              {{ SeverityNames[value] }}
            </label>
          </section>

          <section>
            <p class="mb-1 text-xs uppercase text-slate-400">State</p>
            <label v-for="value in allStates" :key="value" class="flex items-center gap-2 py-0.5 text-sm">
              <input
                type="checkbox"
                :checked="stateFilter.includes(value)"
                @change="toggleState(value)"
              >
              {{ FindingStateNames[value] }}
            </label>
          </section>

          <section>
            <p class="mb-1 text-xs uppercase text-slate-400">Ecosystem</p>
            <label v-for="value in allEcosystems" :key="value" class="flex items-center gap-2 py-0.5 text-sm">
              <input
                type="checkbox"
                :checked="ecosystemFilter.includes(value)"
                @change="toggleEcosystem(value)"
              >
              {{ EcosystemNames[value] }}
            </label>
          </section>

          <section>
            <p class="mb-1 text-xs uppercase text-slate-400">Source</p>
            <div v-if="(sourcesData ?? []).length === 0" class="text-xs text-slate-500">
              No sources yet.
            </div>
            <label
              v-for="source in sourcesData ?? []"
              :key="source.id"
              class="flex items-center gap-2 py-0.5 text-sm"
            >
              <input
                type="checkbox"
                :checked="sourceIdFilter.includes(source.id)"
                @change="toggleSource(source.id)"
              >
              <span class="truncate">{{ source.name }}</span>
            </label>
          </section>
        </div>
      </div>

      <button
        v-if="hasAnyFilter"
        type="button"
        class="ml-auto text-xs text-slate-400 underline-offset-2 hover:text-slate-200 hover:underline"
        @click="resetAllFilters"
      >
        Reset all
      </button>
    </div>

    <p v-if="isLoading" class="text-sm text-slate-400">Loading…</p>
    <p v-else-if="isError" class="text-sm text-red-300">Failed to load findings.</p>

    <div
      v-else-if="filteredData && filteredData.items.length"
      class="overflow-hidden rounded-lg border border-slate-800 bg-slate-900"
    >
      <table class="w-full text-left text-sm">
        <thead class="border-b border-slate-800 text-xs uppercase text-slate-500">
          <tr>
            <th class="w-10 px-3 py-2">
              <input
                ref="headerCheckboxRef"
                type="checkbox"
                :checked="allOnPageSelected"
                :aria-label="allOnPageSelected ? 'Deselect all on page' : 'Select all on page'"
                @change="toggleAllOnPage"
              >
            </th>
            <th class="px-4 py-2">Severity</th>
            <th class="px-4 py-2">Package</th>
            <th class="px-4 py-2">Advisory</th>
            <th class="px-4 py-2">Source</th>
            <th class="px-4 py-2">Last seen</th>
            <th class="px-4 py-2">State</th>
          </tr>
        </thead>
        <tbody class="divide-y divide-slate-800">
          <tr
            v-for="finding in filteredData.items"
            :key="finding.id"
            class="hover:bg-slate-800/50"
            :class="selectedIds.has(finding.id) ? 'bg-blue-950/30' : ''"
          >
            <td class="px-3 py-2">
              <input
                type="checkbox"
                :checked="selectedIds.has(finding.id)"
                :aria-label="`Select finding ${finding.dedupKey}`"
                @change="toggleOne(finding.id)"
              >
            </td>
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
              <p
                v-if="finding.ecosystem !== null && finding.ecosystem !== undefined"
                class="text-xs uppercase text-slate-500"
              >
                {{ EcosystemNames[finding.ecosystem] }}
              </p>
            </td>
            <td class="px-4 py-2">
              <p class="line-clamp-1 text-slate-200">{{ finding.advisorySummary ?? '—' }}</p>
              <p v-if="finding.advisoryExternalId" class="font-mono text-xs text-slate-500">
                {{ finding.advisoryExternalId }}
              </p>
            </td>
            <td class="px-4 py-2 text-slate-300">{{ finding.sourceName ?? `#${finding.sourceId}` }}</td>
            <td class="px-4 py-2 text-slate-400">{{ formatDate(finding.lastSeenAt) }}</td>
            <td class="px-4 py-2 text-slate-400">{{ FindingStateNames[finding.state] }}</td>
          </tr>
        </tbody>
      </table>
      <footer
        v-if="filteredData.total > filteredData.pageSize"
        class="flex items-center justify-between border-t border-slate-800 px-4 py-2 text-xs text-slate-400"
      >
        <span>
          Page {{ filteredData.page }} of {{ Math.ceil(filteredData.total / filteredData.pageSize) }}
          · {{ filteredData.total }} findings
        </span>
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

    <!-- Floating bulk action bar -->
    <div
      v-if="selectedIds.size > 0"
      class="fixed inset-x-0 bottom-4 z-30 mx-auto flex w-fit max-w-3xl items-center gap-3 rounded-lg border border-slate-700 bg-slate-900/95 px-4 py-3 shadow-2xl backdrop-blur"
    >
      <span class="text-sm text-slate-200">{{ selectedIds.size }} selected</span>
      <button
        type="button"
        class="rounded border border-slate-700 px-3 py-1 text-sm hover:bg-slate-800"
        :disabled="bulkAck.isPending.value"
        @click="runBulk('ack')"
      >
        Ack ({{ selectedIds.size }})
      </button>
      <button
        type="button"
        class="rounded border border-green-800 bg-green-950/40 px-3 py-1 text-sm text-green-200 hover:bg-green-900/40"
        :disabled="bulkResolve.isPending.value"
        @click="runBulk('resolve')"
      >
        Resolve ({{ selectedIds.size }})
      </button>
      <div v-if="suppressOpen" class="flex items-center gap-2">
        <input
          v-model="suppressReason"
          type="text"
          placeholder="Reason"
          class="rounded border border-slate-700 bg-slate-800 px-2 py-1 text-sm focus:border-blue-500 focus:outline-none"
        >
        <button
          type="button"
          class="rounded border border-yellow-800 bg-yellow-950/40 px-3 py-1 text-sm text-yellow-200 hover:bg-yellow-900/40 disabled:opacity-50"
          :disabled="bulkSuppress.isPending.value || !suppressReason.trim()"
          @click="runBulk('suppress')"
        >
          Confirm
        </button>
        <button
          type="button"
          class="text-xs text-slate-400 hover:text-slate-200"
          @click="() => { suppressOpen = false; suppressReason = '' }"
        >
          Cancel
        </button>
      </div>
      <button
        v-else
        type="button"
        class="rounded border border-yellow-800 bg-yellow-950/40 px-3 py-1 text-sm text-yellow-200 hover:bg-yellow-900/40"
        @click="suppressOpen = true"
      >
        Suppress ({{ selectedIds.size }})
      </button>
      <button
        type="button"
        class="ml-2 text-xs text-slate-400 hover:text-slate-200"
        @click="clearSelection"
      >
        Clear
      </button>
    </div>
  </div>
</template>
