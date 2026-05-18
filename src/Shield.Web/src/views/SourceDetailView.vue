<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import { AlertTriangle, ExternalLink, GitBranch, Pencil, Play, ShieldAlert, ShieldCheck, Sparkles, Trash2, Upload, User, UserPlus, X } from 'lucide-vue-next'
import { useI18n } from 'vue-i18n'

import InventoryTree from '@/components/InventoryTree.vue'
import {
  useApplyAllFixesMutation,
  useDeleteSourceMutation,
  usePromoteSourceToGithubMutation,
  useScanNowMutation,
  useSetIsProductionMutation,
  useSnapshotDiffQuery,
  useSnapshotItemsQuery,
  useSnapshotsListQuery,
  useSourceQuery,
  useUpdateSourceMutation,
} from '@/queries/sources'
import { useAuth } from '@/stores/auth'
import { enumName } from '@/stores/enums'
import { useToasts } from '@/stores/toast'
import { formatDate } from '@/lib/format'
import { repoUrl } from '@/lib/repo-url'
import { AnomalyFlags, SourceType } from '@/types/api'
import type { BulkApplyResponse, InventoryDiffEntry } from '@/types/api'

const SHOW_FLAT_KEY = 'shield.inventory.show-flat'
const showFlat = ref<boolean>(localStorage.getItem(SHOW_FLAT_KEY) === '1')
function toggleShowFlat(): void {
  showFlat.value = !showFlat.value
  localStorage.setItem(SHOW_FLAT_KEY, showFlat.value ? '1' : '0')
}

const props = defineProps<{ id: string }>()
const sourceId = computed(() => Number.parseInt(props.id, 10))

const { t } = useI18n()
const router = useRouter()
const { data, isLoading, isError } = useSourceQuery(sourceId)
const scan = useScanNowMutation()
const update = useUpdateSourceMutation()
const remove = useDeleteSourceMutation()
const promote = usePromoteSourceToGithubMutation()
const applyAllFixes = useApplyAllFixesMutation()
const setIsProduction = useSetIsProductionMutation()
const { push } = useToasts()
const { isAdmin } = useAuth()

const showBulkApplyModal = ref(false)
const bulkPreviewResult = ref<BulkApplyResponse | null>(null)
const bulkApplying = ref(false)
const allowMajorBumps = ref(false)
const confirmProduction = ref(false)

const canBulkApply = computed(() => source.value?.type === SourceType.GithubRepo)

// Manual cooldown surfaces from the server. Null = no cooldown. The tooltip shows the
// wall-clock so a 429 is never a surprise — Stoney's exact original bug was the scheduler
// silently bumped LastBulkApplyAt hours earlier and the first manual click hit a wall.
const cooldownUntil = computed<Date | null>(() => {
  const raw = source.value?.manualCooldownUntil
  return raw ? new Date(raw) : null
})
const cooldownActive = computed<boolean>(() =>
  cooldownUntil.value !== null && cooldownUntil.value.getTime() > Date.now(),
)
const cooldownTooltip = computed<string>(() => {
  if (!cooldownActive.value) return ''
  return t('source_detail.bulk_apply_cooldown_tooltip', {
    when: cooldownUntil.value!.toLocaleString(),
  })
})

async function onDryRunBulkApply(): Promise<void> {
  confirmProduction.value = false
  try {
    bulkPreviewResult.value = await applyAllFixes.mutateAsync({
      id: sourceId.value,
      payload: { dryRun: true, allowMajorBumps: allowMajorBumps.value },
    })
    showBulkApplyModal.value = true
  }
  catch {
    push('error', t('source_detail.bulk_apply_preview_error'))
  }
}

async function onConfirmBulkApply(force: boolean = false): Promise<void> {
  bulkApplying.value = true
  try {
    const result = await applyAllFixes.mutateAsync({
      id: sourceId.value,
      payload: {
        dryRun: false,
        force,
        allowMajorBumps: allowMajorBumps.value,
        confirmProduction: confirmProduction.value,
      },
    })
    showBulkApplyModal.value = false
    bulkPreviewResult.value = null
    if (result.pullRequestUrl) {
      push(
        'success',
        t('source_detail.bulk_apply_pr_opened_short'),
        { href: result.pullRequestUrl, label: t('source_detail.bulk_apply_open_pr_btn') },
      )
    }
    else {
      push('info', t('source_detail.bulk_apply_no_pr'))
    }
  }
  catch (error: unknown) {
    // 429 cooldown: surface the wall-clock + offer a force retry instead of a generic error.
    const status = (error as { response?: { status?: number, data?: { error?: string, retryAfter?: string } } })?.response?.status
    const code = (error as { response?: { data?: { error?: string } } })?.response?.data?.error
    if (status === 429 && code === 'bulk_cooldown') {
      const retryAfter = (error as { response?: { data?: { retryAfter?: string } } }).response?.data?.retryAfter
      const when = retryAfter ? new Date(retryAfter).toLocaleString() : '?'
      push('error', t('source_detail.bulk_apply_cooldown_error', { when }))
    }
    else {
      push('error', t('source_detail.bulk_apply_error'))
    }
  }
  finally {
    bulkApplying.value = false
  }
}

async function onForceBulkApply(): Promise<void> {
  // Admin override — bypasses the 24h cooldown. Distinct from the new-findings escape hatch
  // the server applies automatically, since the operator might want to force a re-run even
  // without new findings (re-create a PR after the previous one was closed, etc.).
  await onConfirmBulkApply(true)
}

const source = computed(() => data.value?.source)
const snapshot = computed(() => data.value?.latestSnapshot ?? null)
const snapshotId = computed(() => snapshot.value?.id ?? null)

// Parse configJson into typed view-model per source kind. Renderer prefers structured rows
// (label + value + provider link); falls back to raw JSON only for unknown shapes.
interface GithubConfig { kind: 'github', owner: string, repo: string, branch: string | null, htmlUrl: string }
interface FolderConfig { kind: 'folder', path: string }
interface HostConfig { kind: 'host', host: string }
interface RawConfig { kind: 'raw', json: string }
type ParsedConfig = GithubConfig | FolderConfig | HostConfig | RawConfig

const parsedConfig = computed<ParsedConfig | null>(() => {
  const src = source.value
  if (!src) return null
  let obj: Record<string, unknown>
  try {
    obj = JSON.parse(src.configJson ?? '{}') as Record<string, unknown>
  }
  catch {
    return { kind: 'raw', json: src.configJson ?? '' }
  }
  // SourceType: 0=GithubRepo, 1=LocalFolder, 2=LinuxHost
  if (src.type === 0 && typeof obj.owner === 'string' && typeof obj.repo === 'string') {
    const branch = typeof obj.branch === 'string' ? obj.branch : null
    return {
      kind: 'github',
      owner: obj.owner,
      repo: obj.repo,
      branch,
      htmlUrl: `https://github.com/${obj.owner}/${obj.repo}${branch ? `/tree/${encodeURIComponent(branch)}` : ''}`,
    }
  }
  if (src.type === 1 && typeof obj.path === 'string')
    return { kind: 'folder', path: obj.path }
  if (src.type === 2 && typeof obj.host === 'string')
    return { kind: 'host', host: obj.host }
  return { kind: 'raw', json: JSON.stringify(obj, null, 2) }
})

const inventory = useSnapshotItemsQuery(sourceId, snapshotId)
const items = computed(() => inventory.data.value?.items ?? [])

const editing = ref(false)
const editName = ref('')
const editConfigJson = ref('{}')
const editScanInterval = ref('01:00:00')
const editEnabled = ref(true)
const editMinPackageAgeHours = ref(48)

// Reseed the edit form whenever the source loads or edit mode opens.
watch(
  [() => source.value, editing],
  ([src, isEditing]) => {
    if (src && isEditing) {
      editName.value = src.name
      editConfigJson.value = src.configJson
      editScanInterval.value = src.scanInterval
      editEnabled.value = src.enabled
      editMinPackageAgeHours.value = src.minPackageAgeHours
    }
  },
  { immediate: true },
)

async function onToggleIsProduction(): Promise<void> {
  if (!source.value) return
  try {
    await setIsProduction.mutateAsync({
      id: sourceId.value,
      payload: { isProduction: !source.value.isProduction },
    })
  }
  catch {
    push('error', t('source_detail.is_production_error'))
  }
}

async function onScanNow(): Promise<void> {
  try {
    await scan.mutateAsync(sourceId.value)
    push('success', t('source_detail.scan_queued'))
  }
  catch {
    push('error', t('source_detail.scan_error'))
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
        minPackageAgeHours: editMinPackageAgeHours.value,
      },
    })
    push('success', t('source_detail.update_ok'))
    editing.value = false
  }
  catch {
    push('error', t('source_detail.update_error'))
  }
}

async function onDelete(): Promise<void> {
  const name = source.value?.name ?? 'this source'
  if (!window.confirm(t('source_detail.delete_confirm', { name })))
    return
  try {
    await remove.mutateAsync(sourceId.value)
    push('success', t('source_detail.delete_ok', { name }))
    await router.push('/sources')
  }
  catch {
    push('error', t('source_detail.delete_error'))
  }
}

async function onPromote(): Promise<void> {
  try {
    const sibling = await promote.mutateAsync(sourceId.value)
    push('success', t('source_detail.promote_ok', { name: sibling.name }))
    await router.push(`/sources/${sibling.id}`)
  }
  catch {
    push('error', t('source_detail.promote_error'))
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
    push('error', t('source_detail.compare_pick_error'))
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
    <p v-if="isLoading" class="text-sm text-slate-400">{{ t('source_detail.loading') }}</p>
    <p v-else-if="isError" class="text-sm text-red-300">{{ t('source_detail.error') }}</p>

    <template v-else-if="source">
      <header class="flex items-center justify-between">
        <div>
          <div class="flex items-center gap-2">
            <h1 class="text-2xl font-semibold">{{ source.name }}</h1>
            <span
              v-if="source.isProduction"
              class="inline-flex items-center gap-1 rounded border border-red-700 bg-red-950/60 px-1.5 py-0.5 text-[10px] uppercase tracking-wide text-red-300"
              :title="t('source_detail.is_production_badge_title')"
            >
              <ShieldAlert class="h-3 w-3" />
              {{ t('source_detail.is_production_badge') }}
            </span>
          </div>
          <p class="text-sm text-slate-400">{{ enumName('SourceType', source.type) }}</p>
          <p v-if="source.detectedRemote" class="mt-1 flex items-center gap-2 text-xs text-slate-400">
            <GitBranch class="h-3.5 w-3.5" />
            <a
              :href="repoUrl(source.detectedRemote) ?? '#'"
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
              {{ t('source_detail.promote_btn') }}
            </button>
          </p>
        </div>
        <div class="flex items-center gap-2">
          <a
            v-if="repoUrl(source.detectedRemote)"
            :href="repoUrl(source.detectedRemote)!"
            target="_blank"
            rel="noopener noreferrer"
            class="flex items-center gap-1 rounded border border-slate-700 px-3 py-1.5 text-sm text-slate-200 hover:bg-slate-800"
          >
            <ExternalLink class="h-4 w-4" />
            {{ t('source_detail.open_repo_btn') }}
          </a>
          <button
            type="button"
            class="flex items-center gap-1 rounded border border-slate-700 px-3 py-1.5 text-sm text-slate-200 hover:bg-slate-800"
            @click="editing = !editing"
          >
            <component :is="editing ? X : Pencil" class="h-4 w-4" />
            {{ editing ? t('source_detail.cancel_edit') : t('source_detail.edit_btn') }}
          </button>
          <button
            type="button"
            class="flex items-center gap-1 rounded bg-blue-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-blue-500 disabled:bg-blue-900"
            :disabled="scan.isPending.value"
            @click="onScanNow"
          >
            <Play class="h-4 w-4" />
            {{ t('source_detail.scan_now_btn') }}
          </button>
          <button
            v-if="isAdmin && canBulkApply"
            type="button"
            class="flex items-center gap-1 rounded border border-emerald-800/60 px-3 py-1.5 text-sm text-emerald-300 hover:bg-emerald-950/40 disabled:opacity-50"
            :disabled="applyAllFixes.isPending.value"
            :title="cooldownTooltip"
            @click="onDryRunBulkApply"
          >
            <ShieldCheck class="h-4 w-4" />
            {{ t('source_detail.bulk_apply_btn') }}
            <span v-if="cooldownActive" class="ml-1 rounded bg-amber-900/60 px-1.5 py-0.5 text-[10px] uppercase text-amber-300">
              {{ t('source_detail.bulk_apply_cooldown_badge') }}
            </span>
          </button>
          <button
            v-if="isAdmin"
            type="button"
            class="flex items-center gap-1 rounded border px-3 py-1.5 text-sm disabled:opacity-50"
            :class="source.isProduction
              ? 'border-red-700 text-red-300 hover:bg-red-950/40'
              : 'border-slate-700 text-slate-300 hover:bg-slate-800'"
            :disabled="setIsProduction.isPending.value"
            :title="source.isProduction ? t('source_detail.unmark_production_title') : t('source_detail.mark_production_title')"
            @click="onToggleIsProduction"
          >
            <ShieldAlert class="h-4 w-4" />
            {{ source.isProduction ? t('source_detail.unmark_production_btn') : t('source_detail.mark_production_btn') }}
          </button>
          <button
            type="button"
            class="flex items-center gap-1 rounded border border-red-900/50 px-3 py-1.5 text-sm text-red-300 hover:bg-red-950/40 disabled:opacity-50"
            :disabled="remove.isPending.value"
            @click="onDelete"
          >
            <Trash2 class="h-4 w-4" />
            {{ t('source_detail.delete_btn') }}
          </button>
        </div>
      </header>

      <form
        v-if="editing"
        class="space-y-3 rounded-lg border border-slate-800 bg-slate-900 p-4"
        @submit.prevent="onSave"
      >
        <label class="block">
          <span class="text-sm text-slate-300">{{ t('source_detail.field_name') }}</span>
          <input
            v-model="editName"
            required
            class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
          />
        </label>
        <label class="block">
          <span class="text-sm text-slate-300">{{ t('source_detail.field_scan_interval') }}</span>
          <input
            v-model="editScanInterval"
            required
            pattern="^\d+:\d{2}:\d{2}$"
            placeholder="01:00:00"
            class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 font-mono text-sm focus:border-blue-500 focus:outline-none"
          />
        </label>
        <label class="block">
          <span class="text-sm text-slate-300">{{ t('source_detail.field_config_json') }}</span>
          <textarea
            v-model="editConfigJson"
            rows="6"
            class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 font-mono text-sm focus:border-blue-500 focus:outline-none"
          />
        </label>
        <label class="flex items-center gap-2 text-sm text-slate-300">
          <input v-model="editEnabled" type="checkbox" class="h-4 w-4" />
          {{ t('source_detail.field_enabled') }}
        </label>
        <label class="block">
          <span class="text-sm text-slate-300">{{ t('source_detail.field_min_package_age') }}</span>
          <span class="block text-xs text-slate-500">{{ t('source_detail.field_min_package_age_hint') }}</span>
          <input
            v-model.number="editMinPackageAgeHours"
            type="number"
            min="0"
            max="720"
            class="mt-1 w-32 rounded border border-slate-700 bg-slate-800 px-3 py-2 font-mono text-sm focus:border-blue-500 focus:outline-none"
          />
        </label>
        <div class="flex gap-2">
          <button
            type="submit"
            :disabled="update.isPending.value"
            class="rounded bg-blue-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-blue-500 disabled:bg-blue-900"
          >
            {{ update.isPending.value ? t('source_detail.saving') : t('source_detail.save_btn') }}
          </button>
          <button
            type="button"
            class="rounded border border-slate-700 px-3 py-1.5 text-sm text-slate-300 hover:bg-slate-800"
            @click="editing = false"
          >
            {{ t('source_detail.cancel_btn') }}
          </button>
        </div>
      </form>

      <dl class="grid grid-cols-1 gap-4 sm:grid-cols-2">
        <div class="rounded-lg border border-slate-800 bg-slate-900 p-4">
          <dt class="text-xs uppercase text-slate-500">{{ t('source_detail.meta_last_scanned') }}</dt>
          <dd class="mt-1 text-sm">{{ formatDate(source.lastScannedAt) }}</dd>
        </div>
        <div class="rounded-lg border border-slate-800 bg-slate-900 p-4">
          <dt class="text-xs uppercase text-slate-500">{{ t('source_detail.meta_scan_interval') }}</dt>
          <dd class="mt-1 text-sm">{{ source.scanInterval }}</dd>
        </div>
        <div v-if="snapshot" class="rounded-lg border border-slate-800 bg-slate-900 p-4">
          <dt class="text-xs uppercase text-slate-500">{{ t('source_detail.meta_last_snapshot') }}</dt>
          <dd class="mt-1 text-sm">
            {{ formatDate(snapshot.takenAt) }} · {{ t('source_detail.snapshot_items', { count: snapshot.itemCount }) }}
          </dd>
        </div>
        <div v-if="snapshot" class="rounded-lg border border-slate-800 bg-slate-900 p-4">
          <dt class="text-xs uppercase text-slate-500">{{ t('source_detail.meta_contents_sha') }}</dt>
          <dd class="mt-1 break-all font-mono text-xs text-slate-300">{{ snapshot.contentsSha }}</dd>
        </div>
        <div v-if="parsedConfig?.kind === 'github'" class="rounded-lg border border-slate-800 bg-slate-900 p-4 sm:col-span-2">
          <dt class="text-xs uppercase text-slate-500">{{ t('source_detail.meta_github_repo') }}</dt>
          <dd class="mt-2 space-y-2 text-sm">
            <div class="flex items-center gap-2">
              <a
                :href="parsedConfig.htmlUrl"
                target="_blank"
                rel="noopener"
                class="inline-flex items-center gap-1 text-slate-100 hover:text-blue-300 hover:underline"
              >
                <GitBranch class="h-3.5 w-3.5 text-slate-500" />
                <span class="font-medium">{{ parsedConfig.owner }}/{{ parsedConfig.repo }}</span>
                <ExternalLink class="h-3 w-3 text-slate-500" />
              </a>
            </div>
            <div class="flex items-center gap-2 text-xs text-slate-400">
              <span>{{ t('source_detail.meta_branch') }}</span>
              <code class="rounded bg-slate-800 px-1.5 py-0.5 font-mono text-slate-200">{{ parsedConfig.branch ?? t('source_detail.meta_branch_default') }}</code>
            </div>
          </dd>
        </div>
        <div v-else-if="parsedConfig?.kind === 'folder'" class="rounded-lg border border-slate-800 bg-slate-900 p-4 sm:col-span-2">
          <dt class="text-xs uppercase text-slate-500">{{ t('source_detail.meta_local_folder') }}</dt>
          <dd class="mt-2 break-all font-mono text-xs text-slate-200">{{ parsedConfig.path }}</dd>
        </div>
        <div v-else-if="parsedConfig?.kind === 'host'" class="rounded-lg border border-slate-800 bg-slate-900 p-4 sm:col-span-2">
          <dt class="text-xs uppercase text-slate-500">{{ t('source_detail.meta_linux_host') }}</dt>
          <dd class="mt-2 font-mono text-xs text-slate-200">{{ parsedConfig.host }}</dd>
        </div>
        <details v-else-if="parsedConfig?.kind === 'raw'" class="rounded-lg border border-slate-800 bg-slate-900 p-4 sm:col-span-2">
          <summary class="cursor-pointer text-xs uppercase text-slate-500">{{ t('source_detail.meta_raw_config') }}</summary>
          <pre class="mt-2 whitespace-pre-wrap break-all font-mono text-xs text-slate-300">{{ parsedConfig.json }}</pre>
        </details>
        <div v-if="source.lastError" class="rounded-lg border border-red-800 bg-red-950/40 p-4 sm:col-span-2">
          <dt class="text-xs uppercase text-red-300">{{ t('source_detail.meta_last_error') }}</dt>
          <dd class="mt-1 text-sm text-red-200">{{ source.lastError }}</dd>
        </div>
      </dl>

      <section v-if="snapshotList.length >= 2" class="rounded-lg border border-slate-800 bg-slate-900">
        <header class="flex items-center justify-between border-b border-slate-800 px-4 py-3">
          <div>
            <h2 class="text-sm font-semibold uppercase tracking-wide text-slate-300">{{ t('source_detail.compare_title') }}</h2>
            <p class="text-xs text-slate-500">{{ t('source_detail.compare_subtitle') }}</p>
          </div>
        </header>

        <div class="grid grid-cols-1 gap-3 px-4 py-3 sm:grid-cols-[1fr_1fr_auto]">
          <label class="block">
            <span class="text-xs uppercase tracking-wide text-slate-500">{{ t('source_detail.compare_older') }}</span>
            <select
              v-model="compareOlderId"
              class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
            >
              <option v-for="snap in snapshotList" :key="`older-${snap.id}`" :value="snap.id">
                {{ formatDate(snap.takenAt) }} — {{ t('source_detail.snapshot_items', { count: snap.itemCount }) }}
              </option>
            </select>
          </label>
          <label class="block">
            <span class="text-xs uppercase tracking-wide text-slate-500">{{ t('source_detail.compare_newer') }}</span>
            <select
              v-model="compareNewerId"
              class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
            >
              <option v-for="snap in snapshotList" :key="`newer-${snap.id}`" :value="snap.id">
                {{ formatDate(snap.takenAt) }} — {{ t('source_detail.snapshot_items', { count: snap.itemCount }) }}
              </option>
            </select>
          </label>
          <div class="flex items-end">
            <button
              type="button"
              class="w-full rounded bg-blue-600 px-3 py-2 text-sm font-medium text-white hover:bg-blue-500 disabled:cursor-not-allowed disabled:bg-blue-900 sm:w-auto"
              :disabled="!canCompare || diffQuery.isFetching.value"
              :title="canCompare ? t('source_detail.compare_btn_title') : t('source_detail.compare_btn_title_disabled')"
              @click="onCompare"
            >
              {{ diffQuery.isFetching.value ? t('source_detail.comparing') : t('source_detail.compare_btn') }}
            </button>
          </div>
        </div>

        <div v-if="diffRequested" class="border-t border-slate-800 px-4 py-3">
          <p v-if="diffQuery.isLoading.value" class="text-sm text-slate-400">{{ t('source_detail.diff_loading') }}</p>
          <p v-else-if="diffQuery.isError.value" class="text-sm text-red-300">{{ t('source_detail.diff_error') }}</p>
          <div v-else-if="diffQuery.data.value" class="space-y-4">
            <p class="text-xs text-slate-500">
              {{ snapshotLabel(diffQuery.data.value.older.id) }}
              →
              {{ snapshotLabel(diffQuery.data.value.newer.id) }}
            </p>

            <div class="grid grid-cols-1 gap-4 lg:grid-cols-3">
              <div class="rounded border border-emerald-900/60 bg-emerald-950/20 p-3">
                <h3 class="text-xs font-semibold uppercase tracking-wide text-emerald-300">{{ t('source_detail.diff_added_title', { n: diffQuery.data.value.added.length }) }}</h3>
                <p v-if="diffQuery.data.value.added.length === 0" class="mt-2 text-xs text-slate-500">{{ t('source_detail.diff_added_none') }}</p>
                <ul v-else class="mt-2 space-y-2">
                  <li v-for="entry in diffQuery.data.value.added" :key="`added-${entry.ecosystem}-${entry.name}`" class="text-sm">
                    <div class="flex flex-wrap items-baseline gap-2">
                      <span class="rounded bg-emerald-900/40 px-1.5 py-0.5 text-[10px] uppercase text-emerald-300">{{ enumName('Ecosystem', entry.ecosystem) }}</span>
                      <RouterLink
                        :to="{ path: '/findings', query: { packageName: entry.name, packageVersion: entry.version } }"
                        class="font-mono text-slate-100 hover:text-blue-300 hover:underline"
                      >
                        {{ entry.name }}
                      </RouterLink>
                      <span class="font-mono text-xs text-slate-400">{{ entry.version }}</span>
                      <span class="text-[10px] text-slate-500">{{ entry.isDirect ? t('source_detail.dep_direct') : t('source_detail.dep_transitive') }}</span>
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
                <h3 class="text-xs font-semibold uppercase tracking-wide text-red-300">{{ t('source_detail.diff_removed_title', { n: diffQuery.data.value.removed.length }) }}</h3>
                <p v-if="diffQuery.data.value.removed.length === 0" class="mt-2 text-xs text-slate-500">{{ t('source_detail.diff_removed_none') }}</p>
                <ul v-else class="mt-2 space-y-2">
                  <li v-for="entry in diffQuery.data.value.removed" :key="`removed-${entry.ecosystem}-${entry.name}`" class="text-sm">
                    <div class="flex flex-wrap items-baseline gap-2">
                      <span class="rounded bg-red-900/40 px-1.5 py-0.5 text-[10px] uppercase text-red-300">{{ enumName('Ecosystem', entry.ecosystem) }}</span>
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
                <h3 class="text-xs font-semibold uppercase tracking-wide text-amber-300">{{ t('source_detail.diff_bumped_title', { n: diffQuery.data.value.versionChanged.length }) }}</h3>
                <p v-if="diffQuery.data.value.versionChanged.length === 0" class="mt-2 text-xs text-slate-500">{{ t('source_detail.diff_bumped_none') }}</p>
                <ul v-else class="mt-2 space-y-2">
                  <li v-for="entry in diffQuery.data.value.versionChanged" :key="`bump-${entry.ecosystem}-${entry.name}`" class="text-sm">
                    <div class="flex flex-wrap items-baseline gap-2">
                      <span class="rounded bg-amber-900/40 px-1.5 py-0.5 text-[10px] uppercase text-amber-300">{{ enumName('Ecosystem', entry.ecosystem) }}</span>
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
            <h2 class="text-sm font-semibold uppercase tracking-wide text-slate-300">{{ t('source_detail.inventory_title') }}</h2>
            <p class="text-xs text-slate-500">{{ t('source_detail.inventory_subtitle', { count: snapshot.itemCount, when: formatDate(snapshot.takenAt) }) }}</p>
          </div>
          <button
            type="button"
            class="rounded border border-slate-700 px-2 py-1 text-xs text-slate-300 hover:bg-slate-800"
            @click="toggleShowFlat"
          >
            {{ showFlat ? t('source_detail.show_tree') : t('source_detail.show_flat') }}
          </button>
        </header>
        <p v-if="inventory.isLoading.value" class="px-4 py-6 text-sm text-slate-400">{{ t('source_detail.inventory_loading') }}</p>
        <p v-else-if="inventory.isError.value" class="px-4 py-6 text-sm text-red-300">{{ t('source_detail.inventory_error') }}</p>
        <p v-else-if="items.length === 0" class="px-4 py-6 text-sm text-slate-400">{{ t('source_detail.inventory_empty') }}</p>
        <table v-else-if="showFlat" class="w-full text-sm">
          <thead class="text-xs uppercase text-slate-500">
            <tr>
              <th class="px-4 py-2 text-left font-medium">{{ t('source_detail.col_ecosystem') }}</th>
              <th class="px-4 py-2 text-left font-medium">{{ t('source_detail.col_package') }}</th>
              <th class="px-4 py-2 text-left font-medium">{{ t('source_detail.col_version') }}</th>
              <th class="px-4 py-2 text-left font-medium">{{ t('source_detail.col_type') }}</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="item in items" :key="item.id" class="border-t border-slate-800">
              <td class="px-4 py-2 text-slate-300">{{ enumName('Ecosystem', item.ecosystem) }}</td>
              <td class="px-4 py-2 font-mono">
                <RouterLink
                  :to="{ path: '/findings', query: { packageName: item.name, packageVersion: item.version } }"
                  class="text-slate-100 hover:text-blue-300 hover:underline"
                >
                  {{ item.name }}
                </RouterLink>
              </td>
              <td class="px-4 py-2 font-mono text-slate-300">{{ item.version }}</td>
              <td class="px-4 py-2 text-xs text-slate-400">{{ item.isDirect ? t('source_detail.dep_direct') : t('source_detail.dep_transitive') }}</td>
            </tr>
          </tbody>
        </table>
        <InventoryTree v-else :items="items" />
      </section>
    </template>

    <!-- Bulk apply: dry-run preview modal -->
    <Teleport to="body">
      <div
        v-if="showBulkApplyModal && bulkPreviewResult"
        class="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4"
        @click.self="showBulkApplyModal = false"
      >
        <div class="w-full max-w-lg rounded-lg border border-slate-700 bg-slate-900 shadow-xl">
          <header class="flex items-center justify-between border-b border-slate-800 px-5 py-4">
            <h2 class="text-sm font-semibold text-slate-100">{{ t('source_detail.bulk_apply_modal_title') }}</h2>
            <button type="button" class="text-slate-400 hover:text-slate-100" @click="showBulkApplyModal = false">
              <X class="h-4 w-4" />
            </button>
          </header>
          <div class="max-h-96 overflow-y-auto px-5 py-4 text-sm">
            <p v-if="bulkPreviewResult.entries.length === 0 && (bulkPreviewResult.majorBumps?.length ?? 0) === 0 && bulkPreviewResult.errors.length === 0" class="text-slate-400">
              {{ t('source_detail.bulk_apply_nothing') }}
            </p>

            <!-- Patch / minor bumps -->
            <ul v-if="bulkPreviewResult.entries.length > 0" class="space-y-2">
              <li v-for="entry in bulkPreviewResult.entries" :key="entry.packageName" class="rounded border border-slate-800 bg-slate-800/50 px-3 py-2">
                <span class="font-mono font-medium text-slate-100">{{ entry.packageName }}</span>
                <span class="ml-2 font-mono text-xs text-slate-400">{{ entry.currentVersion }}</span>
                <span class="mx-1 text-emerald-400">→</span>
                <span class="font-mono text-xs text-emerald-300">{{ entry.suggestedVersion }}</span>
                <p class="mt-0.5 text-[10px] text-slate-500">{{ entry.advisoryIds.join(', ') }}</p>
              </li>
            </ul>

            <!-- Major bumps (skipped unless allowMajorBumps is set) -->
            <div v-if="(bulkPreviewResult.majorBumps?.length ?? 0) > 0" class="mt-3">
              <p class="text-xs uppercase text-amber-400">{{ t('source_detail.bulk_apply_major_bumps') }}</p>
              <ul class="mt-1 space-y-2">
                <li v-for="entry in bulkPreviewResult.majorBumps" :key="`major-${entry.packageName}`" class="rounded border border-amber-900/60 bg-amber-950/20 px-3 py-2">
                  <span class="font-mono font-medium text-amber-200">{{ entry.packageName }}</span>
                  <span class="ml-2 font-mono text-xs text-slate-400">{{ entry.currentVersion }}</span>
                  <span class="mx-1 text-amber-400">→</span>
                  <span class="font-mono text-xs text-amber-300">{{ entry.suggestedVersion }}</span>
                  <p class="mt-0.5 text-[10px] text-slate-500">{{ entry.advisoryIds.join(', ') }}</p>
                </li>
              </ul>
              <label class="mt-2 flex cursor-pointer items-center gap-2 text-xs text-amber-300">
                <input v-model="allowMajorBumps" type="checkbox" class="h-3.5 w-3.5" />
                {{ t('source_detail.bulk_apply_allow_major') }}
              </label>
            </div>

            <!-- Warnings -->
            <div v-if="(bulkPreviewResult.warnings?.length ?? 0) > 0" class="mt-3 space-y-1">
              <p class="text-xs uppercase text-amber-400">{{ t('source_detail.bulk_apply_warnings') }}</p>
              <p v-for="warn in bulkPreviewResult.warnings" :key="warn.packageName" class="text-xs text-amber-200">
                {{ warn.packageName }}: {{ warn.message }}
              </p>
            </div>

            <!-- Errors -->
            <div v-if="bulkPreviewResult.errors.length > 0" class="mt-3 space-y-1">
              <p class="text-xs uppercase text-red-400">{{ t('source_detail.bulk_apply_errors') }}</p>
              <p v-for="err in bulkPreviewResult.errors" :key="err.packageName" class="text-xs text-red-300">
                {{ err.packageName }}: {{ err.reason }}
              </p>
            </div>
          </div>
          <footer class="space-y-3 border-t border-slate-800 px-5 py-3">
            <!-- Production confirmation gate -->
            <label
              v-if="source?.isProduction"
              class="flex cursor-pointer items-start gap-2 rounded border border-red-800 bg-red-950/30 px-3 py-2 text-xs text-red-300"
            >
              <input v-model="confirmProduction" type="checkbox" class="mt-0.5 h-3.5 w-3.5 shrink-0" />
              {{ t('source_detail.bulk_apply_confirm_production') }}
            </label>
            <!-- Cooldown notice — server may still allow the click via new-findings escape hatch -->
            <p
              v-if="cooldownActive"
              class="rounded border border-amber-800/60 bg-amber-950/30 px-3 py-2 text-xs text-amber-300"
            >
              {{ t('source_detail.bulk_apply_cooldown_notice', { when: cooldownUntil!.toLocaleString() }) }}
            </p>
            <div class="flex justify-end gap-2">
              <button
                type="button"
                class="rounded border border-slate-700 px-3 py-1.5 text-sm text-slate-300 hover:bg-slate-800"
                @click="showBulkApplyModal = false"
              >
                {{ t('action.cancel') }}
              </button>
              <button
                v-if="cooldownActive"
                type="button"
                :disabled="bulkApplying || (source?.isProduction && !confirmProduction)"
                class="rounded border border-amber-700 bg-amber-900/40 px-3 py-1.5 text-sm font-medium text-amber-200 hover:bg-amber-900/60 disabled:opacity-50"
                @click="onForceBulkApply"
              >
                {{ t('source_detail.bulk_apply_force_btn') }}
              </button>
              <button
                type="button"
                :disabled="bulkApplying
                  || (bulkPreviewResult.entries.length === 0 && !(allowMajorBumps && (bulkPreviewResult.majorBumps?.length ?? 0) > 0))
                  || (source?.isProduction && !confirmProduction)"
                class="rounded bg-emerald-700 px-3 py-1.5 text-sm font-medium text-white hover:bg-emerald-600 disabled:opacity-50"
                @click="onConfirmBulkApply()"
              >
                {{ bulkApplying ? t('source_detail.bulk_apply_opening_pr') : t('source_detail.bulk_apply_confirm') }}
              </button>
            </div>
          </footer>
        </div>
      </div>
    </Teleport>
  </div>
</template>
