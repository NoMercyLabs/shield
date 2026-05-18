<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { RouterLink, useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'

import EpssBadge from '@/components/EpssBadge.vue'
import KevBadge from '@/components/KevBadge.vue'
import SavedFiltersStrip from '@/components/SavedFiltersStrip.vue'
import SeverityBadge from '@/components/SeverityBadge.vue'
import {
  useBulkAckFindingsMutation,
  useBulkResolveFindingsMutation,
  useBulkSuppressFindingsMutation,
  useFindingsQuery,
} from '@/queries/findings'
import { useSourcesQuery } from '@/queries/sources'
import { formatDate } from '@/lib/format'
import { enumName } from '@/stores/enums'
import { useToasts } from '@/stores/toast'
import {
  Ecosystem,
  FindingState,
  Severity,
  SortBy,
  SortDir,
} from '@/types/api'
import type { Finding, FindingFilter, FindingsPage, SavedFilter } from '@/types/api'

const { t } = useI18n()
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
  hasFix: boolean | null
  kevOnly: boolean
  epssMin: number
  advisoryQuery: string
  sortBy: SortBy
  sortDir: SortDir
  last24h: boolean
}

const FILTERS_KEY = 'shield.findings.filters'

function emptyFilters(): PersistedFilters {
  return {
    severity: [],
    state: [],
    ecosystem: [],
    packageName: [],
    sourceId: [],
    hasFix: null,
    kevOnly: false,
    epssMin: 0,
    advisoryQuery: '',
    sortBy: SortBy.Severity,
    sortDir: SortDir.Desc,
    last24h: false,
  }
}

function loadFilters(): PersistedFilters {
  const empty = emptyFilters()
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
      hasFix: parsed.hasFix === true || parsed.hasFix === false ? parsed.hasFix : null,
      kevOnly: parsed.kevOnly === true,
      epssMin: typeof parsed.epssMin === 'number' ? parsed.epssMin : 0,
      advisoryQuery: typeof parsed.advisoryQuery === 'string' ? parsed.advisoryQuery : '',
      sortBy: typeof parsed.sortBy === 'string' ? (parsed.sortBy as SortBy) : SortBy.Severity,
      sortDir: typeof parsed.sortDir === 'string' ? (parsed.sortDir as SortDir) : SortDir.Desc,
      last24h: parsed.last24h === true,
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
const hasFixFilter = ref<boolean | null>(null)
const kevOnlyFilter = ref<boolean>(false)
const epssMinFilter = ref<number>(0)
const advisoryQueryFilter = ref<string>('')
const sortByFilter = ref<SortBy>(SortBy.Severity)
const sortDirFilter = ref<SortDir>(SortDir.Desc)
const last24hOnly = ref(false)
const page = ref(1)

// Debounced advisory query to avoid firing on every keystroke
const advisoryQueryDebounced = ref<string>('')
let advisoryDebounceTimer: ReturnType<typeof setTimeout> | null = null
watch(advisoryQueryFilter, (value) => {
  if (advisoryDebounceTimer) clearTimeout(advisoryDebounceTimer)
  advisoryDebounceTimer = setTimeout(() => {
    advisoryQueryDebounced.value = value.trim()
  }, 250)
})

onMounted(() => {
  // If the URL carries any filter-shaped query parameter, the user came from a deep link
  // (notification, dashboard link, etc.) and the explicit intent is "show me this
  // specific thing". Persisted localStorage filters — most painfully the "Critical only"
  // severity floor — would silently hide whatever the deep link is pointing at, which is
  // how the y18n typosquat notification ended up landing on an empty Findings page.
  const filterQueryKeys = [
    'packageName',
    'severity',
    'state',
    'ecosystem',
    'sourceId',
    'kevOnly',
    'epssMin',
    'advisoryQuery',
  ]
  const hasDeepLinkFilter = filterQueryKeys.some(key => key in route.query)

  const persisted = hasDeepLinkFilter ? emptyFilters() : loadFilters()
  severityFilter.value = persisted.severity
  stateFilter.value = persisted.state
  ecosystemFilter.value = persisted.ecosystem
  packageNameFilter.value = persisted.packageName
  sourceIdFilter.value = persisted.sourceId
  hasFixFilter.value = persisted.hasFix
  kevOnlyFilter.value = persisted.kevOnly
  epssMinFilter.value = persisted.epssMin
  advisoryQueryFilter.value = persisted.advisoryQuery
  advisoryQueryDebounced.value = persisted.advisoryQuery
  sortByFilter.value = persisted.sortBy
  sortDirFilter.value = persisted.sortDir
  last24hOnly.value = persisted.last24h

  // Allow ?packageName=foo on the URL to seed the package filter once.
  const raw = route.query.packageName
  if (typeof raw === 'string' && raw.length > 0 && !packageNameFilter.value.includes(raw))
    packageNameFilter.value = [...packageNameFilter.value, raw]
})

watch(
  [
    severityFilter,
    stateFilter,
    ecosystemFilter,
    packageNameFilter,
    sourceIdFilter,
    hasFixFilter,
    kevOnlyFilter,
    epssMinFilter,
    advisoryQueryDebounced,
    sortByFilter,
    sortDirFilter,
    last24hOnly,
  ],
  () => {
    const snapshot: PersistedFilters = {
      severity: severityFilter.value,
      state: stateFilter.value,
      ecosystem: ecosystemFilter.value,
      packageName: packageNameFilter.value,
      sourceId: sourceIdFilter.value,
      hasFix: hasFixFilter.value,
      kevOnly: kevOnlyFilter.value,
      epssMin: epssMinFilter.value,
      advisoryQuery: advisoryQueryDebounced.value,
      sortBy: sortByFilter.value,
      sortDir: sortDirFilter.value,
      last24h: last24hOnly.value,
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
  hasFix: hasFixFilter.value,
  kevOnly: kevOnlyFilter.value || undefined,
  epssMin: epssMinFilter.value > 0 ? epssMinFilter.value : undefined,
  advisoryQuery: advisoryQueryDebounced.value || undefined,
  sortBy: sortByFilter.value,
  sortDir: sortDirFilter.value,
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
    toasts.push('success', t(`findings.bulk.${verb}_done`, { n: response.updated, missed: response.notFound.length }))
    clearSelection()
    await refetch()
  }
  catch {
    toasts.push('error', t(`findings.bulk.${verb}_error`))
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
      label: t('findings.chip.severity', { name: enumName('Severity', value) }),
      remove: () => { severityFilter.value = severityFilter.value.filter(item => item !== value) },
    })
  }
  for (const value of stateFilter.value) {
    out.push({
      key: `state-${value}`,
      label: t('findings.chip.state', { name: enumName('FindingState', value) }),
      remove: () => { stateFilter.value = stateFilter.value.filter(item => item !== value) },
    })
  }
  for (const value of ecosystemFilter.value) {
    out.push({
      key: `eco-${value}`,
      label: t('findings.chip.ecosystem', { name: enumName('Ecosystem', value) }),
      remove: () => { ecosystemFilter.value = ecosystemFilter.value.filter(item => item !== value) },
    })
  }
  for (const value of packageNameFilter.value) {
    out.push({
      key: `pkg-${value}`,
      label: t('findings.chip.package', { name: value }),
      remove: () => { packageNameFilter.value = packageNameFilter.value.filter(item => item !== value) },
    })
  }
  for (const value of sourceIdFilter.value) {
    const name = sourceNameById.value[value] ?? `#${value}`
    out.push({
      key: `src-${value}`,
      label: t('findings.chip.source', { name }),
      remove: () => { sourceIdFilter.value = sourceIdFilter.value.filter(item => item !== value) },
    })
  }
  if (last24hOnly.value) {
    out.push({
      key: 'last24h',
      label: t('findings.chip.last_24h'),
      remove: () => { last24hOnly.value = false },
    })
  }
  if (hasFixFilter.value === true) {
    out.push({
      key: 'hasFix-true',
      label: t('findings.chip.has_fix'),
      remove: () => { hasFixFilter.value = null },
    })
  }
  if (hasFixFilter.value === false) {
    out.push({
      key: 'hasFix-false',
      label: t('findings.chip.no_fix'),
      remove: () => { hasFixFilter.value = null },
    })
  }
  if (kevOnlyFilter.value) {
    out.push({
      key: 'kev',
      label: t('findings.chip.kev_only'),
      remove: () => { kevOnlyFilter.value = false },
    })
  }
  if (epssMinFilter.value > 0) {
    out.push({
      key: 'epss',
      label: t('findings.chip.epss_min', { value: epssMinFilter.value.toFixed(2) }),
      remove: () => { epssMinFilter.value = 0 },
    })
  }
  if (advisoryQueryDebounced.value) {
    out.push({
      key: 'advisory',
      label: t('findings.chip.advisory_id', { value: advisoryQueryDebounced.value }),
      remove: () => { advisoryQueryFilter.value = ''; advisoryQueryDebounced.value = '' },
    })
  }
  return out
})

const hasAnyFilter = computed<boolean>(() => chips.value.length > 0)

// Serialized snapshot of the current filter selection — fed to SavedFiltersStrip as the
// payload to save, and read back on apply. Shape matches PersistedFilters for symmetric round-trip.
const currentFilterJson = computed<string>(() =>
  JSON.stringify({
    severity: severityFilter.value,
    state: stateFilter.value,
    ecosystem: ecosystemFilter.value,
    packageName: packageNameFilter.value,
    sourceId: sourceIdFilter.value,
    hasFix: hasFixFilter.value,
    kevOnly: kevOnlyFilter.value,
    epssMin: epssMinFilter.value,
    advisoryQuery: advisoryQueryDebounced.value,
    sortBy: sortByFilter.value,
    sortDir: sortDirFilter.value,
    last24h: last24hOnly.value,
  }),
)

function applySavedFilter(filter: SavedFilter): void {
  try {
    const parsed = JSON.parse(filter.queryJson) as Partial<PersistedFilters>
    severityFilter.value = Array.isArray(parsed.severity) ? parsed.severity : []
    stateFilter.value = Array.isArray(parsed.state) ? parsed.state : []
    ecosystemFilter.value = Array.isArray(parsed.ecosystem) ? parsed.ecosystem : []
    packageNameFilter.value = Array.isArray(parsed.packageName) ? parsed.packageName : []
    sourceIdFilter.value = Array.isArray(parsed.sourceId) ? parsed.sourceId : []
    hasFixFilter.value = parsed.hasFix === true || parsed.hasFix === false ? parsed.hasFix : null
    kevOnlyFilter.value = parsed.kevOnly === true
    epssMinFilter.value = typeof parsed.epssMin === 'number' ? parsed.epssMin : 0
    const parsedAdvisory = typeof parsed.advisoryQuery === 'string' ? parsed.advisoryQuery : ''
    advisoryQueryFilter.value = parsedAdvisory
    advisoryQueryDebounced.value = parsedAdvisory
    sortByFilter.value = typeof parsed.sortBy === 'string' ? (parsed.sortBy as SortBy) : SortBy.Severity
    sortDirFilter.value = typeof parsed.sortDir === 'string' ? (parsed.sortDir as SortDir) : SortDir.Desc
    last24hOnly.value = parsed.last24h === true
  }
  catch {
    toasts.push('error', t('saved_filters.filter_parse_error', { name: filter.name }))
  }
}

function resetAllFilters(): void {
  severityFilter.value = []
  stateFilter.value = []
  ecosystemFilter.value = []
  packageNameFilter.value = []
  sourceIdFilter.value = []
  hasFixFilter.value = null
  kevOnlyFilter.value = false
  epssMinFilter.value = 0
  advisoryQueryFilter.value = ''
  advisoryQueryDebounced.value = ''
  last24hOnly.value = false
  const next = { ...route.query }
  delete next.packageName
  delete next.packageVersion
  router.replace({ path: route.path, query: next })
}

// ---------- quick presets ----------

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

function applyByMe(): void {
  stateFilter.value = [FindingState.Open, FindingState.Acked]
  severityFilter.value = []
  ecosystemFilter.value = []
  last24hOnly.value = false
}

// ---------- add-filter popover ----------

const addFilterOpen = ref(false)
const sourceSearch = ref('')

const filteredSources = computed(() => {
  const term = sourceSearch.value.trim().toLowerCase()
  const sources = sourcesData.value ?? []
  if (!term) return sources
  return sources.filter(source => source.name.toLowerCase().includes(term))
})

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

// Cycle has-fix tri-state: null → true → false → null
function cycleHasFix(): void {
  if (hasFixFilter.value === null) hasFixFilter.value = true
  else if (hasFixFilter.value === true) hasFixFilter.value = false
  else hasFixFilter.value = null
}

const hasFixLabel = computed<string>(() => {
  if (hasFixFilter.value === true) return t('findings.filter.has_fix_yes')
  if (hasFixFilter.value === false) return t('findings.filter.has_fix_no')
  return t('findings.filter.has_fix')
})

const hasFixActive = computed<boolean>(() => hasFixFilter.value !== null)

interface SortOption {
  sortBy: SortBy
  sortDir: SortDir
  label: string
}

const sortOptions: SortOption[] = [
  { sortBy: SortBy.Severity, sortDir: SortDir.Desc, label: 'findings.sort.severity_desc' },
  { sortBy: SortBy.Severity, sortDir: SortDir.Asc, label: 'findings.sort.severity_asc' },
  { sortBy: SortBy.DiscoveredAt, sortDir: SortDir.Desc, label: 'findings.sort.discovered_desc' },
  { sortBy: SortBy.DiscoveredAt, sortDir: SortDir.Asc, label: 'findings.sort.discovered_asc' },
  { sortBy: SortBy.PackageName, sortDir: SortDir.Asc, label: 'findings.sort.package_asc' },
  { sortBy: SortBy.SourceName, sortDir: SortDir.Asc, label: 'findings.sort.source_asc' },
]

const activeSortLabel = computed<string>(() => {
  const match = sortOptions.find(
    opt => opt.sortBy === sortByFilter.value && opt.sortDir === sortDirFilter.value,
  )
  return match ? t(match.label) : t('findings.sort.severity_desc')
})

function applySortOption(opt: SortOption): void {
  sortByFilter.value = opt.sortBy
  sortDirFilter.value = opt.sortDir
}

// Column-header click handler. Mirrors the conventional spreadsheet behaviour: clicking
// the active column toggles direction, clicking a different column switches to it with
// the default sensible direction for that field.
function toggleSort(by: SortBy): void {
  if (sortByFilter.value === by) {
    sortDirFilter.value = sortDirFilter.value === SortDir.Asc ? SortDir.Desc : SortDir.Asc
    return
  }
  sortByFilter.value = by
  // Names default ASC (a→z), severity/date default DESC (most important / newest first).
  sortDirFilter.value
    = by === SortBy.PackageName || by === SortBy.SourceName ? SortDir.Asc : SortDir.Desc
}

const sortDropdownOpen = ref(false)

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
    <h1 class="text-2xl font-semibold">{{ $t('nav.findings') }}</h1>

    <!-- Quick filter row -->
    <div class="flex flex-wrap items-center gap-2 text-sm">
      <span class="text-xs uppercase text-slate-500">{{ $t('findings.filter.quick_filters') }}</span>
      <button
        type="button"
        class="rounded-full border border-red-900/50 bg-red-950/30 px-3 py-1 text-red-200 hover:bg-red-900/40"
        @click="applyCriticalOnly"
      >
        {{ $t('findings.filter.critical_only') }}
      </button>
      <button
        type="button"
        class="rounded-full border border-slate-700 px-3 py-1 text-slate-200 hover:bg-slate-800"
        @click="applyLast24h"
      >
        {{ $t('findings.filter.last_24h') }}
      </button>
      <button
        type="button"
        class="rounded-full border border-slate-700 px-3 py-1 text-slate-200 hover:bg-slate-800"
        @click="applyUnackedOpen"
      >
        {{ $t('findings.filter.unacked_open') }}
      </button>
      <button
        type="button"
        class="rounded-full border border-slate-700 px-3 py-1 text-slate-200 hover:bg-slate-800"
        @click="applyByMe"
      >
        {{ $t('findings.filter.by_me') }}
      </button>
    </div>

    <!-- Saved filters strip -->
    <SavedFiltersStrip :current-query-json="currentFilterJson" @apply="applySavedFilter" />

    <!-- Security-signal filter bar: has-fix, KEV, EPSS, advisory ID search, sort -->
    <div class="flex flex-wrap items-center gap-2">
      <!-- has-fix tri-state pill -->
      <button
        type="button"
        class="rounded-full border px-3 py-1 text-xs transition-colors"
        :class="hasFixActive
          ? 'border-emerald-700 bg-emerald-950/40 text-emerald-200 hover:bg-emerald-900/40'
          : 'border-slate-700 text-slate-300 hover:bg-slate-800'"
        :aria-pressed="hasFixActive"
        @click="cycleHasFix"
      >
        {{ hasFixLabel }}
      </button>

      <!-- KEV-only toggle -->
      <button
        type="button"
        class="rounded-full border px-3 py-1 text-xs transition-colors"
        :class="kevOnlyFilter
          ? 'border-orange-700 bg-orange-950/40 text-orange-200 hover:bg-orange-900/40'
          : 'border-slate-700 text-slate-300 hover:bg-slate-800'"
        :aria-pressed="kevOnlyFilter"
        @click="kevOnlyFilter = !kevOnlyFilter"
      >
        {{ $t('findings.filter.kev_only') }}
      </button>

      <!-- EPSS slider -->
      <div class="flex items-center gap-2">
        <label class="text-xs text-slate-400" for="epss-slider">{{ $t('findings.filter.epss_min') }}</label>
        <input
          id="epss-slider"
          v-model.number="epssMinFilter"
          type="range"
          min="0"
          max="1"
          step="0.05"
          class="h-1 w-24 cursor-pointer accent-blue-500"
        >
        <span class="w-8 text-right text-xs tabular-nums text-slate-300">
          {{ epssMinFilter > 0 ? epssMinFilter.toFixed(2) : 'off' }}
        </span>
      </div>

      <!-- Advisory ID / CVE search -->
      <input
        v-model="advisoryQueryFilter"
        type="search"
        :placeholder="$t('findings.filter.advisory_id_placeholder')"
        class="rounded border border-slate-700 bg-slate-800/60 px-2 py-1 text-xs text-slate-200 placeholder-slate-500 focus:border-blue-500 focus:outline-none"
        style="width: 18ch;"
        :aria-label="$t('findings.filter.advisory_id')"
      >

      <!-- Sort dropdown -->
      <div class="relative ml-auto">
        <button
          type="button"
          class="flex items-center gap-1 rounded border border-slate-700 px-3 py-1 text-xs text-slate-300 hover:bg-slate-800"
          @click="sortDropdownOpen = !sortDropdownOpen"
        >
          {{ activeSortLabel }}
          <span class="ml-1 text-slate-500">▾</span>
        </button>
        <div
          v-if="sortDropdownOpen"
          class="absolute right-0 z-20 mt-2 w-52 rounded-lg border border-slate-700 bg-slate-900 py-1 shadow-xl"
        >
          <button
            v-for="opt in sortOptions"
            :key="`${opt.sortBy}-${opt.sortDir}`"
            type="button"
            class="w-full px-4 py-1.5 text-left text-xs hover:bg-slate-800"
            :class="opt.sortBy === sortByFilter && opt.sortDir === sortDirFilter
              ? 'text-blue-300'
              : 'text-slate-300'"
            @click="() => { applySortOption(opt); sortDropdownOpen = false }"
          >
            {{ $t(opt.label) }}
          </button>
        </div>
      </div>
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
          {{ $t('findings.filter.add_filter') }}
        </button>
        <div
          v-if="addFilterOpen"
          class="absolute z-20 mt-2 grid w-[440px] grid-cols-2 gap-4 rounded-lg border border-slate-700 bg-slate-900 p-4 shadow-xl"
        >
          <section>
            <p class="mb-1 text-xs uppercase text-slate-400">{{ $t('findings.filter.severity') }}</p>
            <label v-for="value in allSeverities" :key="value" class="flex items-center gap-2 py-0.5 text-sm">
              <input
                type="checkbox"
                :checked="severityFilter.includes(value)"
                @change="toggleSeverity(value)"
              >
              {{ enumName('Severity', value) }}
            </label>
          </section>

          <section>
            <p class="mb-1 text-xs uppercase text-slate-400">{{ $t('findings.filter.state') }}</p>
            <label v-for="value in allStates" :key="value" class="flex items-center gap-2 py-0.5 text-sm">
              <input
                type="checkbox"
                :checked="stateFilter.includes(value)"
                @change="toggleState(value)"
              >
              {{ enumName('FindingState', value) }}
            </label>
          </section>

          <section>
            <p class="mb-1 text-xs uppercase text-slate-400">{{ $t('findings.filter.ecosystem') }}</p>
            <label v-for="value in allEcosystems" :key="value" class="flex items-center gap-2 py-0.5 text-sm">
              <input
                type="checkbox"
                :checked="ecosystemFilter.includes(value)"
                @change="toggleEcosystem(value)"
              >
              {{ enumName('Ecosystem', value) }}
            </label>
          </section>

          <section>
            <p class="mb-1 text-xs uppercase text-slate-400">{{ $t('findings.filter.source') }}</p>
            <!-- Fuzzy source search -->
            <input
              v-model="sourceSearch"
              type="search"
              :placeholder="$t('findings.filter.source_search')"
              class="mb-1 w-full rounded border border-slate-700 bg-slate-800 px-2 py-0.5 text-xs text-slate-200 placeholder-slate-500 focus:border-blue-500 focus:outline-none"
            >
            <div v-if="filteredSources.length === 0" class="text-xs text-slate-500">
              {{ $t('findings.filter.no_sources') }}
            </div>
            <div class="max-h-40 overflow-y-auto">
              <label
                v-for="source in filteredSources"
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
            </div>
          </section>
        </div>
      </div>

      <button
        v-if="hasAnyFilter"
        type="button"
        class="ml-auto text-xs text-slate-400 underline-offset-2 hover:text-slate-200 hover:underline"
        @click="resetAllFilters"
      >
        {{ $t('findings.filter.reset_all') }}
      </button>
    </div>

    <p v-if="isLoading" class="text-sm text-slate-400">{{ $t('findings.loading') }}</p>
    <p v-else-if="isError" class="text-sm text-red-300">{{ $t('findings.error') }}</p>

    <!-- Mobile / narrow viewport: card list -->
    <ul
      v-if="filteredData && filteredData.items.length"
      class="space-y-2 md:hidden"
    >
      <li
        v-for="finding in filteredData.items"
        :key="`card-${finding.id}`"
        class="rounded-lg border border-slate-800 bg-slate-900 p-3"
        :class="selectedIds.has(finding.id) ? 'border-blue-600/60 bg-blue-950/30' : ''"
      >
        <div class="flex items-start gap-3">
          <input
            type="checkbox"
            :checked="selectedIds.has(finding.id)"
            :aria-label="`Select finding ${finding.dedupKey}`"
            class="mt-1 h-4 w-4 shrink-0 accent-blue-500"
            @change="toggleOne(finding.id)"
          >
          <div class="min-w-0 flex-1">
            <div class="flex flex-wrap items-center gap-2">
              <SeverityBadge :severity="finding.severity" />
              <span class="text-xs uppercase tracking-wide text-slate-500">
                {{ enumName('FindingState', finding.state) }}
              </span>
              <span
                v-if="finding.ecosystem !== null && finding.ecosystem !== undefined"
                class="text-[10px] uppercase tracking-wider text-slate-600"
              >
                {{ enumName('Ecosystem', finding.ecosystem) }}
              </span>
            </div>
            <RouterLink :to="`/findings/${finding.id}`" class="mt-2 block font-mono text-sm text-blue-300 hover:underline">
              <span class="text-slate-100">{{ finding.packageName ?? '—' }}</span><span
                v-if="finding.packageVersion"
                class="text-slate-400"
              >@{{ finding.packageVersion }}</span>
            </RouterLink>
            <p class="mt-1 line-clamp-2 text-sm text-slate-300">{{ finding.advisorySummary ?? '—' }}</p>
            <p v-if="finding.advisoryExternalId" class="mt-1 font-mono text-xs text-slate-500">
              {{ finding.advisoryExternalId }}
            </p>
            <div
              v-if="finding.isKev || finding.epssScore != null"
              class="mt-1 flex flex-wrap items-center gap-1"
            >
              <KevBadge :is-kev="finding.isKev" :due-date="finding.kevDueDate" />
              <EpssBadge :score="finding.epssScore" :percentile="finding.epssPercentile" />
            </div>
            <p class="mt-2 text-xs text-slate-500">
              {{ finding.sourceName ?? `#${finding.sourceId}` }} · {{ formatDate(finding.lastSeenAt) }}
            </p>
          </div>
        </div>
      </li>
    </ul>

    <footer
      v-if="filteredData && filteredData.items.length && filteredData.total > filteredData.pageSize"
      class="flex items-center justify-between rounded-lg border border-slate-800 bg-slate-900 px-3 py-2 text-xs text-slate-400 md:hidden"
    >
      <span>
        {{ $t('findings.page_of_total', { page: filteredData.page, total: Math.ceil(filteredData.total / filteredData.pageSize), count: filteredData.total }) }}
      </span>
      <div class="flex gap-2">
        <button
          type="button"
          class="h-11 rounded border border-slate-700 px-3 hover:bg-slate-800 disabled:opacity-40"
          :disabled="page <= 1"
          @click="page = page - 1"
        >
          {{ $t('action.prev') }}
        </button>
        <button
          type="button"
          class="h-11 rounded border border-slate-700 px-3 hover:bg-slate-800 disabled:opacity-40"
          :disabled="page >= Math.ceil(filteredData.total / filteredData.pageSize)"
          @click="page = page + 1"
        >
          {{ $t('action.next') }}
        </button>
      </div>
    </footer>

    <div
      v-if="filteredData && filteredData.items.length"
      class="hidden overflow-hidden rounded-lg border border-slate-800 bg-slate-900 md:block"
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
            <th class="px-4 py-2">
              <button
                type="button"
                class="inline-flex items-center gap-1 uppercase hover:text-slate-200"
                @click="toggleSort(SortBy.Severity)"
              >
                {{ $t('findings.col_severity') }}
                <span v-if="sortByFilter === SortBy.Severity" class="text-blue-300">
                  {{ sortDirFilter === SortDir.Asc ? '▲' : '▼' }}
                </span>
              </button>
            </th>
            <th class="px-4 py-2">
              <button
                type="button"
                class="inline-flex items-center gap-1 uppercase hover:text-slate-200"
                @click="toggleSort(SortBy.PackageName)"
              >
                {{ $t('findings.col_package') }}
                <span v-if="sortByFilter === SortBy.PackageName" class="text-blue-300">
                  {{ sortDirFilter === SortDir.Asc ? '▲' : '▼' }}
                </span>
              </button>
            </th>
            <th class="px-4 py-2">{{ $t('findings.col_advisory') }}</th>
            <th class="px-4 py-2">
              <button
                type="button"
                class="inline-flex items-center gap-1 uppercase hover:text-slate-200"
                @click="toggleSort(SortBy.SourceName)"
              >
                {{ $t('findings.col_source') }}
                <span v-if="sortByFilter === SortBy.SourceName" class="text-blue-300">
                  {{ sortDirFilter === SortDir.Asc ? '▲' : '▼' }}
                </span>
              </button>
            </th>
            <th class="px-4 py-2">
              <button
                type="button"
                class="inline-flex items-center gap-1 uppercase hover:text-slate-200"
                @click="toggleSort(SortBy.DiscoveredAt)"
              >
                {{ $t('findings.col_last_seen') }}
                <span v-if="sortByFilter === SortBy.DiscoveredAt" class="text-blue-300">
                  {{ sortDirFilter === SortDir.Asc ? '▲' : '▼' }}
                </span>
              </button>
            </th>
            <th class="px-4 py-2">{{ $t('findings.col_state') }}</th>
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
                {{ enumName('Ecosystem', finding.ecosystem) }}
              </p>
            </td>
            <td class="px-4 py-2">
              <p class="line-clamp-1 text-slate-200">{{ finding.advisorySummary ?? '—' }}</p>
              <p v-if="finding.advisoryExternalId" class="font-mono text-xs text-slate-500">
                {{ finding.advisoryExternalId }}
              </p>
              <div
                v-if="finding.isKev || finding.epssScore != null"
                class="mt-1 flex flex-wrap items-center gap-1"
              >
                <KevBadge :is-kev="finding.isKev" :due-date="finding.kevDueDate" />
                <EpssBadge :score="finding.epssScore" :percentile="finding.epssPercentile" />
              </div>
            </td>
            <td class="px-4 py-2 text-slate-300">{{ finding.sourceName ?? `#${finding.sourceId}` }}</td>
            <td class="px-4 py-2 text-slate-400">{{ formatDate(finding.lastSeenAt) }}</td>
            <td class="px-4 py-2 text-slate-400">{{ enumName('FindingState', finding.state) }}</td>
          </tr>
        </tbody>
      </table>
      <footer
        v-if="filteredData.total > filteredData.pageSize"
        class="flex items-center justify-between border-t border-slate-800 px-4 py-2 text-xs text-slate-400"
      >
        <span>
          {{ $t('findings.page_of_total', { page: filteredData.page, total: Math.ceil(filteredData.total / filteredData.pageSize), count: filteredData.total }) }}
        </span>
        <div class="flex gap-2">
          <button
            type="button"
            class="rounded border border-slate-700 px-2 py-1 hover:bg-slate-800 disabled:opacity-40"
            :disabled="page <= 1"
            @click="page = page - 1"
          >
            {{ $t('action.prev') }}
          </button>
          <button
            type="button"
            class="rounded border border-slate-700 px-2 py-1 hover:bg-slate-800 disabled:opacity-40"
            :disabled="page >= Math.ceil(filteredData.total / filteredData.pageSize)"
            @click="page = page + 1"
          >
            {{ $t('action.next') }}
          </button>
        </div>
      </footer>
    </div>

    <p v-if="filteredData && !isLoading && !filteredData.items.length" class="text-sm text-slate-500">
      {{ $t('findings.empty') }}
    </p>

    <!-- Floating bulk action bar -->
    <div
      v-if="selectedIds.size > 0"
      class="fixed inset-x-2 z-30 mx-auto flex w-fit max-w-3xl flex-wrap items-center gap-3 rounded-lg border border-slate-700 bg-slate-900/95 px-4 py-3 shadow-2xl backdrop-blur"
      style="bottom: max(env(safe-area-inset-bottom), 1rem);"
    >
      <span class="text-sm text-slate-200">{{ $t('findings.bulk.selected', { n: selectedIds.size }) }}</span>
      <button
        type="button"
        class="rounded border border-slate-700 px-3 py-1 text-sm hover:bg-slate-800"
        :disabled="bulkAck.isPending.value"
        @click="runBulk('ack')"
      >
        {{ $t('findings.bulk.ack_btn', { n: selectedIds.size }) }}
      </button>
      <button
        type="button"
        class="rounded border border-green-800 bg-green-950/40 px-3 py-1 text-sm text-green-200 hover:bg-green-900/40"
        :disabled="bulkResolve.isPending.value"
        @click="runBulk('resolve')"
      >
        {{ $t('findings.bulk.resolve_btn', { n: selectedIds.size }) }}
      </button>
      <div v-if="suppressOpen" class="flex items-center gap-2">
        <input
          v-model="suppressReason"
          type="text"
          :placeholder="$t('finding_detail.suppress_reason')"
          class="rounded border border-slate-700 bg-slate-800 px-2 py-1 text-sm focus:border-blue-500 focus:outline-none"
        >
        <button
          type="button"
          class="rounded border border-yellow-800 bg-yellow-950/40 px-3 py-1 text-sm text-yellow-200 hover:bg-yellow-900/40 disabled:opacity-50"
          :disabled="bulkSuppress.isPending.value || !suppressReason.trim()"
          @click="runBulk('suppress')"
        >
          {{ $t('action.confirm') }}
        </button>
        <button
          type="button"
          class="text-xs text-slate-400 hover:text-slate-200"
          @click="() => { suppressOpen = false; suppressReason = '' }"
        >
          {{ $t('action.cancel') }}
        </button>
      </div>
      <button
        v-else
        type="button"
        class="rounded border border-yellow-800 bg-yellow-950/40 px-3 py-1 text-sm text-yellow-200 hover:bg-yellow-900/40"
        @click="suppressOpen = true"
      >
        {{ $t('findings.bulk.suppress_btn', { n: selectedIds.size }) }}
      </button>
      <button
        type="button"
        class="ml-2 text-xs text-slate-400 hover:text-slate-200"
        @click="clearSelection"
      >
        {{ $t('action.clear') }}
      </button>
    </div>
  </div>
</template>
