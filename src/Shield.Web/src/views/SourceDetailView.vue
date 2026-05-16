<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import { AlertTriangle, ExternalLink, GitBranch, Pencil, Play, Sparkles, Trash2, Upload, User, UserPlus, X } from 'lucide-vue-next'

import InventoryTree from '@/components/InventoryTree.vue'
import {
  useDeleteSourceMutation,
  usePromoteSourceToGithubMutation,
  useScanNowMutation,
  useSnapshotDiffQuery,
  useSnapshotItemsQuery,
  useSnapshotsListQuery,
  useSourceQuery,
  useUpdateSourceMutation,
} from '@/queries/sources'
import { useAuth } from '@/stores/auth'
import { useToasts } from '@/stores/toast'
import { formatDate } from '@/lib/format'
import { AnomalyFlags, EcosystemNames, SourceTypeNames } from '@/types/api'
import type { InventoryDiffEntry } from '@/types/api'

const SHOW_FLAT_KEY = 'shield.inventory.show-flat'
const showFlat = ref<boolean>(localStorage.getItem(SHOW_FLAT_KEY) === '1')
function toggleShowFlat(): void {
  showFlat.value = !showFlat.value
  localStorage.setItem(SHOW_FLAT_KEY, showFlat.value ? '1' : '0')
}

const props = defineProps<{ id: string }>()
const sourceId = computed(() => Number.parseInt(props.id, 10))

const router = useRouter()
const { data, isLoading, isError } = useSourceQuery(sourceId)
const scan = useScanNowMutation()
const update = useUpdateSourceMutation()
const remove = useDeleteSourceMutation()
const promote = usePromoteSourceToGithubMutation()
const { push } = useToasts()
const { isAdmin } = useAuth()

const source = computed(() => data.value?.source)
const snapshot = computed(() => data.value?.latestSnapshot ?? null)
const snapshotId = computed(() => snapshot.value?.id ?? null)

const inventory = useSnapshotItemsQuery(sourceId, snapshotId)
const items = computed(() => inventory.data.value?.items ?? [])

const editing = ref(false)
const editName = ref('')
const editConfigJson = ref('{}')
const editScanInterval = ref('01:00:00')
const editEnabled = ref(true)

// Reseed the edit form whenever the source loads or edit mode opens.
watch(
  [() => source.value, editing],
  ([src, isEditing]) => {
    if (src && isEditing) {
      editName.value = src.name
      editConfigJson.value = src.configJson
      editScanInterval.value = src.scanInterval
      editEnabled.value = src.enabled
    }
  },
  { immediate: true },
)

async function onScanNow(): Promise<void> {
  try {
    await scan.mutateAsync(sourceId.value)
    push('success', 'Scan queued.')
  }
  catch {
    push('error', 'Failed to queue scan.')
  }
}

async function onSave(): Promise<void> {
  try {
    await update.mutateAsync({
      id: sourceId.value,
      patch: {
        name: editName.value,
        configJson: editConfigJson.value,
        scanInterval: editScanInterval.value,
        enabled: editEnabled.value,
      },
    })
    push('success', 'Source updated.')
    editing.value = false
  }
  catch {
    push('error', 'Failed to update source.')
  }
}

async function onDelete(): Promise<void> {
  const name = source.value?.name ?? 'this source'
  if (!window.confirm(`Delete source "${name}"? Snapshots and findings for this source will be deleted too.`))
    return
  try {
    await remove.mutateAsync(sourceId.value)
    push('success', `Source "${name}" deleted.`)
    await router.push('/sources')
  }
  catch {
    push('error', 'Failed to delete source.')
  }
}

async function onPromote(): Promise<void> {
  try {
    const sibling = await promote.mutateAsync(sourceId.value)
    push('success', `Created GitHub source "${sibling.name}".`)
    await router.push(`/sources/${sibling.id}`)
  }
  catch {
    push('error', 'Failed to promote to GitHub source.')
  }
}

// ---- Snapshot compare ----
const snapshotsQuery = useSnapshotsListQuery(sourceId)
const snapshotList = computed(() => snapshotsQuery.data.value ?? [])

const compareOlderId = ref<string | null>(null)
const compareNewerId = ref<string | null>(null)
const diffRequested = ref(false)

// Default the dropdowns once the snapshot list loads: newest = newer, its
// PrevSnapshotId (or the next-oldest entry) = older.
watch(snapshotList, (list) => {
  if (!list || list.length < 2) {
    compareNewerId.value = list?.[0]?.id ?? null
    compareOlderId.value = null
    return
  }
  if (!compareNewerId.value) compareNewerId.value = list[0].id
  if (!compareOlderId.value) compareOlderId.value = list[0].prevSnapshotId ?? list[1].id
}, { immediate: true })

const diffQuery = useSnapshotDiffQuery(
  sourceId,
  computed(() => (diffRequested.value ? compareOlderId.value : null)),
  computed(() => (diffRequested.value ? compareNewerId.value : null)),
)

const canCompare = computed(() =>
  !!compareOlderId.value
  && !!compareNewerId.value
  && compareOlderId.value !== compareNewerId.value,
)

function onCompare(): void {
  if (!canCompare.value) {
    push('error', 'Pick two distinct snapshots to compare.')
    return
  }
  diffRequested.value = true
}

function snapshotLabel(id: string): string {
  const entry = snapshotList.value.find(snap => snap.id === id)
  if (!entry) return id.slice(0, 8)
  return `${formatDate(entry.takenAt)} · ${entry.itemCount} items`
}

interface AnomalyBadge {
  flag: number
  label: string
  title: string
  classes: string
  icon: typeof Sparkles
}

const anomalyBadges: AnomalyBadge[] = [
  {
    flag: AnomalyFlags.Typosquat,
    label: 'typosquat',
    title: 'Name is one or two edits from a popular package — possible impersonation.',
    classes: 'border-red-700 bg-red-950/60 text-red-200',
    icon: AlertTriangle,
  },
  {
    flag: AnomalyFlags.NewMaintainerThisVersion,
    label: 'new maintainer',
    title: 'Maintainer set changed compared to a prior version of this package.',
    classes: 'border-amber-700 bg-amber-950/60 text-amber-200',
    icon: UserPlus,
  },
  {
    flag: AnomalyFlags.BrandNew,
    label: 'brand new',
    title: 'Published to the registry within the last 30 days.',
    classes: 'border-emerald-700 bg-emerald-950/60 text-emerald-200',
    icon: Sparkles,
  },
  {
    flag: AnomalyFlags.SingleMaintainer,
    label: 'single maintainer',
    title: 'Only one maintainer — single point of compromise.',
    classes: 'border-slate-700 bg-slate-800/80 text-slate-200',
    icon: User,
  },
  {
    flag: AnomalyFlags.Deprecated,
    label: 'deprecated',
    title: 'Marked deprecated upstream.',
    classes: 'border-slate-700 bg-slate-800/80 text-slate-300',
    icon: AlertTriangle,
  },
  {
    flag: AnomalyFlags.HighScopeMismatch,
    label: 'scope mismatch',
    title: 'Scoped package whose inner name impersonates an unscoped popular package.',
    classes: 'border-red-700 bg-red-950/60 text-red-200',
    icon: AlertTriangle,
  },
]

function badgesFor(entry: InventoryDiffEntry): AnomalyBadge[] {
  if (!entry.anomaly) return []
  // Bitwise check, but kept as a plain number compare for type clarity.
  return anomalyBadges.filter(badge => (entry.anomaly & badge.flag) !== 0)
}
</script>

<template>
  <div class="space-y-6">
    <p v-if="isLoading" class="text-sm text-slate-400">Loading…</p>
    <p v-else-if="isError" class="text-sm text-red-300">Failed to load source.</p>

    <template v-else-if="source">
      <header class="flex items-center justify-between">
        <div>
          <h1 class="text-2xl font-semibold">{{ source.name }}</h1>
          <p class="text-sm text-slate-400">{{ SourceTypeNames[source.type] }}</p>
          <p v-if="source.detectedRemote" class="mt-1 flex items-center gap-2 text-xs text-slate-400">
            <GitBranch class="h-3.5 w-3.5" />
            <a
              :href="`https://${source.detectedRemote.host}/${source.detectedRemote.owner}/${source.detectedRemote.repo}`"
              target="_blank"
              rel="noopener noreferrer"
              class="inline-flex items-center gap-1 hover:text-blue-300 hover:underline"
            >
              {{ source.detectedRemote.host }}/{{ source.detectedRemote.owner }}/{{ source.detectedRemote.repo }}
              <ExternalLink class="h-3 w-3" />
            </a>
            <button
              v-if="isAdmin && source.detectedRemote.host === 'github.com'"
              type="button"
              class="inline-flex items-center gap-1 rounded border border-slate-700 px-2 py-0.5 text-xs text-slate-200 hover:bg-slate-800 disabled:opacity-50"
              :disabled="promote.isPending.value"
              @click="onPromote"
            >
              <Upload class="h-3 w-3" />
              Promote to GitHub source
            </button>
          </p>
        </div>
        <div class="flex items-center gap-2">
          <button
            type="button"
            class="flex items-center gap-1 rounded border border-slate-700 px-3 py-1.5 text-sm text-slate-200 hover:bg-slate-800"
            @click="editing = !editing"
          >
            <component :is="editing ? X : Pencil" class="h-4 w-4" />
            {{ editing ? 'Cancel' : 'Edit' }}
          </button>
          <button
            type="button"
            class="flex items-center gap-1 rounded bg-blue-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-blue-500 disabled:bg-blue-900"
            :disabled="scan.isPending.value"
            @click="onScanNow"
          >
            <Play class="h-4 w-4" />
            Scan now
          </button>
          <button
            type="button"
            class="flex items-center gap-1 rounded border border-red-900/50 px-3 py-1.5 text-sm text-red-300 hover:bg-red-950/40 disabled:opacity-50"
            :disabled="remove.isPending.value"
            @click="onDelete"
          >
            <Trash2 class="h-4 w-4" />
            Delete
          </button>
        </div>
      </header>

      <form
        v-if="editing"
        class="space-y-3 rounded-lg border border-slate-800 bg-slate-900 p-4"
        @submit.prevent="onSave"
      >
        <label class="block">
          <span class="text-sm text-slate-300">Name</span>
          <input
            v-model="editName"
            required
            class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
          />
        </label>
        <label class="block">
          <span class="text-sm text-slate-300">Scan interval (hh:mm:ss)</span>
          <input
            v-model="editScanInterval"
            required
            pattern="^\d+:\d{2}:\d{2}$"
            placeholder="01:00:00"
            class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 font-mono text-sm focus:border-blue-500 focus:outline-none"
          />
        </label>
        <label class="block">
          <span class="text-sm text-slate-300">Config (JSON)</span>
          <textarea
            v-model="editConfigJson"
            rows="6"
            class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 font-mono text-sm focus:border-blue-500 focus:outline-none"
          />
        </label>
        <label class="flex items-center gap-2 text-sm text-slate-300">
          <input v-model="editEnabled" type="checkbox" class="h-4 w-4" />
          Enabled
        </label>
        <div class="flex gap-2">
          <button
            type="submit"
            :disabled="update.isPending.value"
            class="rounded bg-blue-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-blue-500 disabled:bg-blue-900"
          >
            {{ update.isPending.value ? 'Saving…' : 'Save' }}
          </button>
          <button
            type="button"
            class="rounded border border-slate-700 px-3 py-1.5 text-sm text-slate-300 hover:bg-slate-800"
            @click="editing = false"
          >
            Cancel
          </button>
        </div>
      </form>

      <dl class="grid grid-cols-1 gap-4 sm:grid-cols-2">
        <div class="rounded-lg border border-slate-800 bg-slate-900 p-4">
          <dt class="text-xs uppercase text-slate-500">Last scanned</dt>
          <dd class="mt-1 text-sm">{{ formatDate(source.lastScannedAt) }}</dd>
        </div>
        <div class="rounded-lg border border-slate-800 bg-slate-900 p-4">
          <dt class="text-xs uppercase text-slate-500">Scan interval</dt>
          <dd class="mt-1 text-sm">{{ source.scanInterval }}</dd>
        </div>
        <div v-if="snapshot" class="rounded-lg border border-slate-800 bg-slate-900 p-4">
          <dt class="text-xs uppercase text-slate-500">Last snapshot</dt>
          <dd class="mt-1 text-sm">
            {{ formatDate(snapshot.takenAt) }} · {{ snapshot.itemCount }} items
          </dd>
        </div>
        <div v-if="snapshot" class="rounded-lg border border-slate-800 bg-slate-900 p-4">
          <dt class="text-xs uppercase text-slate-500">Contents SHA</dt>
          <dd class="mt-1 break-all font-mono text-xs text-slate-300">{{ snapshot.contentsSha }}</dd>
        </div>
        <div class="rounded-lg border border-slate-800 bg-slate-900 p-4 sm:col-span-2">
          <dt class="text-xs uppercase text-slate-500">Config</dt>
          <dd class="mt-1 whitespace-pre-wrap break-all font-mono text-xs text-slate-300">{{ source.configJson }}</dd>
        </div>
        <div v-if="source.lastError" class="rounded-lg border border-red-800 bg-red-950/40 p-4 sm:col-span-2">
          <dt class="text-xs uppercase text-red-300">Last error</dt>
          <dd class="mt-1 text-sm text-red-200">{{ source.lastError }}</dd>
        </div>
      </dl>

      <section v-if="snapshotList.length >= 2" class="rounded-lg border border-slate-800 bg-slate-900">
        <header class="flex items-center justify-between border-b border-slate-800 px-4 py-3">
          <div>
            <h2 class="text-sm font-semibold uppercase tracking-wide text-slate-300">Compare snapshots</h2>
            <p class="text-xs text-slate-500">Diff two snapshots to surface adds, removes, version bumps, and supply-chain anomalies.</p>
          </div>
        </header>

        <div class="grid grid-cols-1 gap-3 px-4 py-3 sm:grid-cols-[1fr_1fr_auto]">
          <label class="block">
            <span class="text-xs uppercase tracking-wide text-slate-500">Older</span>
            <select
              v-model="compareOlderId"
              class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
            >
              <option v-for="snap in snapshotList" :key="`older-${snap.id}`" :value="snap.id">
                {{ formatDate(snap.takenAt) }} — {{ snap.itemCount }} items
              </option>
            </select>
          </label>
          <label class="block">
            <span class="text-xs uppercase tracking-wide text-slate-500">Newer</span>
            <select
              v-model="compareNewerId"
              class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
            >
              <option v-for="snap in snapshotList" :key="`newer-${snap.id}`" :value="snap.id">
                {{ formatDate(snap.takenAt) }} — {{ snap.itemCount }} items
              </option>
            </select>
          </label>
          <div class="flex items-end">
            <button
              type="button"
              class="w-full rounded bg-blue-600 px-3 py-2 text-sm font-medium text-white hover:bg-blue-500 disabled:cursor-not-allowed disabled:bg-blue-900 sm:w-auto"
              :disabled="!canCompare || diffQuery.isFetching.value"
              :title="canCompare ? 'Run diff' : 'Pick two distinct snapshots'"
              @click="onCompare"
            >
              {{ diffQuery.isFetching.value ? 'Comparing…' : 'Compare' }}
            </button>
          </div>
        </div>

        <div v-if="diffRequested" class="border-t border-slate-800 px-4 py-3">
          <p v-if="diffQuery.isLoading.value" class="text-sm text-slate-400">Loading diff…</p>
          <p v-else-if="diffQuery.isError.value" class="text-sm text-red-300">Failed to load diff.</p>
          <div v-else-if="diffQuery.data.value" class="space-y-4">
            <p class="text-xs text-slate-500">
              {{ snapshotLabel(diffQuery.data.value.older.id) }}
              →
              {{ snapshotLabel(diffQuery.data.value.newer.id) }}
            </p>

            <div class="grid grid-cols-1 gap-4 lg:grid-cols-3">
              <div class="rounded border border-emerald-900/60 bg-emerald-950/20 p-3">
                <h3 class="text-xs font-semibold uppercase tracking-wide text-emerald-300">Added ({{ diffQuery.data.value.added.length }})</h3>
                <p v-if="diffQuery.data.value.added.length === 0" class="mt-2 text-xs text-slate-500">No new dependencies.</p>
                <ul v-else class="mt-2 space-y-2">
                  <li v-for="entry in diffQuery.data.value.added" :key="`added-${entry.ecosystem}-${entry.name}`" class="text-sm">
                    <div class="flex flex-wrap items-baseline gap-2">
                      <span class="rounded bg-emerald-900/40 px-1.5 py-0.5 text-[10px] uppercase text-emerald-300">{{ EcosystemNames[entry.ecosystem] }}</span>
                      <RouterLink
                        :to="{ path: '/findings', query: { packageName: entry.name, packageVersion: entry.version } }"
                        class="font-mono text-slate-100 hover:text-blue-300 hover:underline"
                      >
                        {{ entry.name }}
                      </RouterLink>
                      <span class="font-mono text-xs text-slate-400">{{ entry.version }}</span>
                      <span class="text-[10px] text-slate-500">{{ entry.isDirect ? 'direct' : 'transitive' }}</span>
                    </div>
                    <div v-if="badgesFor(entry).length > 0" class="mt-1 flex flex-wrap gap-1">
                      <span
                        v-for="badge in badgesFor(entry)"
                        :key="`${entry.name}-${badge.flag}`"
                        :class="['inline-flex items-center gap-1 rounded border px-1.5 py-0.5 text-[10px] uppercase tracking-wide', badge.classes]"
                        :title="badge.title"
                      >
                        <component :is="badge.icon" class="h-3 w-3" />
                        {{ badge.label }}
                      </span>
                    </div>
                  </li>
                </ul>
              </div>

              <div class="rounded border border-red-900/60 bg-red-950/20 p-3">
                <h3 class="text-xs font-semibold uppercase tracking-wide text-red-300">Removed ({{ diffQuery.data.value.removed.length }})</h3>
                <p v-if="diffQuery.data.value.removed.length === 0" class="mt-2 text-xs text-slate-500">No removals.</p>
                <ul v-else class="mt-2 space-y-2">
                  <li v-for="entry in diffQuery.data.value.removed" :key="`removed-${entry.ecosystem}-${entry.name}`" class="text-sm">
                    <div class="flex flex-wrap items-baseline gap-2">
                      <span class="rounded bg-red-900/40 px-1.5 py-0.5 text-[10px] uppercase text-red-300">{{ EcosystemNames[entry.ecosystem] }}</span>
                      <RouterLink
                        :to="{ path: '/findings', query: { packageName: entry.name, packageVersion: entry.version } }"
                        class="font-mono text-slate-300 line-through hover:text-blue-300 hover:no-underline"
                      >
                        {{ entry.name }}
                      </RouterLink>
                      <span class="font-mono text-xs text-slate-500 line-through">{{ entry.version }}</span>
                    </div>
                  </li>
                </ul>
              </div>

              <div class="rounded border border-amber-900/60 bg-amber-950/20 p-3">
                <h3 class="text-xs font-semibold uppercase tracking-wide text-amber-300">Version changed ({{ diffQuery.data.value.versionChanged.length }})</h3>
                <p v-if="diffQuery.data.value.versionChanged.length === 0" class="mt-2 text-xs text-slate-500">No bumps.</p>
                <ul v-else class="mt-2 space-y-2">
                  <li v-for="entry in diffQuery.data.value.versionChanged" :key="`bump-${entry.ecosystem}-${entry.name}`" class="text-sm">
                    <div class="flex flex-wrap items-baseline gap-2">
                      <span class="rounded bg-amber-900/40 px-1.5 py-0.5 text-[10px] uppercase text-amber-300">{{ EcosystemNames[entry.ecosystem] }}</span>
                      <RouterLink
                        :to="{ path: '/findings', query: { packageName: entry.name, packageVersion: entry.toVersion } }"
                        class="font-mono text-slate-100 hover:text-blue-300 hover:underline"
                      >
                        {{ entry.name }}
                      </RouterLink>
                      <span class="font-mono text-xs text-slate-400">{{ entry.fromVersion }} <span class="text-amber-300">→</span> {{ entry.toVersion }}</span>
                    </div>
                  </li>
                </ul>
              </div>
            </div>
          </div>
        </div>
      </section>

      <section v-if="snapshot" class="rounded-lg border border-slate-800 bg-slate-900">
        <header class="flex items-center justify-between border-b border-slate-800 px-4 py-3">
          <div>
            <h2 class="text-sm font-semibold uppercase tracking-wide text-slate-300">Inventory</h2>
            <p class="text-xs text-slate-500">{{ snapshot.itemCount }} packages parsed at {{ formatDate(snapshot.takenAt) }}</p>
          </div>
          <button
            type="button"
            class="rounded border border-slate-700 px-2 py-1 text-xs text-slate-300 hover:bg-slate-800"
            @click="toggleShowFlat"
          >
            {{ showFlat ? 'Show tree' : 'Show flat list' }}
          </button>
        </header>
        <p v-if="inventory.isLoading.value" class="px-4 py-6 text-sm text-slate-400">Loading inventory…</p>
        <p v-else-if="inventory.isError.value" class="px-4 py-6 text-sm text-red-300">Failed to load inventory.</p>
        <p v-else-if="items.length === 0" class="px-4 py-6 text-sm text-slate-400">No packages.</p>
        <table v-else-if="showFlat" class="w-full text-sm">
          <thead class="text-xs uppercase text-slate-500">
            <tr>
              <th class="px-4 py-2 text-left font-medium">Ecosystem</th>
              <th class="px-4 py-2 text-left font-medium">Package</th>
              <th class="px-4 py-2 text-left font-medium">Version</th>
              <th class="px-4 py-2 text-left font-medium">Type</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="item in items" :key="item.id" class="border-t border-slate-800">
              <td class="px-4 py-2 text-slate-300">{{ EcosystemNames[item.ecosystem] }}</td>
              <td class="px-4 py-2 font-mono">
                <RouterLink
                  :to="{ path: '/findings', query: { packageName: item.name, packageVersion: item.version } }"
                  class="text-slate-100 hover:text-blue-300 hover:underline"
                >
                  {{ item.name }}
                </RouterLink>
              </td>
              <td class="px-4 py-2 font-mono text-slate-300">{{ item.version }}</td>
              <td class="px-4 py-2 text-xs text-slate-400">{{ item.isDirect ? 'direct' : 'transitive' }}</td>
            </tr>
          </tbody>
        </table>
        <InventoryTree v-else :items="items" />
      </section>
    </template>
  </div>
</template>
