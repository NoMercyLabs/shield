<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import { ExternalLink, GitBranch, Pencil, Play, Trash2, Upload, X } from 'lucide-vue-next'

import InventoryTree from '@/components/InventoryTree.vue'
import {
  useDeleteSourceMutation,
  usePromoteSourceToGithubMutation,
  useScanNowMutation,
  useSnapshotItemsQuery,
  useSourceQuery,
  useUpdateSourceMutation,
} from '@/queries/sources'
import { useAuth } from '@/stores/auth'
import { useToasts } from '@/stores/toast'
import { formatDate } from '@/lib/format'
import { EcosystemNames, SourceTypeNames } from '@/types/api'

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
