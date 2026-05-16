<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { Check, ChevronRight, File, FolderClosed, GitBranch, HardDrive, Loader2, Package, Search, X } from 'lucide-vue-next'

import { useFsBrowse } from '@/queries/sources'
import type { FsEntry } from '@/types/api'

interface Props {
  open: boolean
}

const props = defineProps<Props>()
const emit = defineEmits<{
  close: []
  submit: [paths: string[]]
}>()

const currentPath = ref<string | null>(null)
const selected = ref<Set<string>>(new Set())
const filter = ref('')

const { data, isLoading, isError, error } = useFsBrowse(currentPath)

const breadcrumbs = computed<{ name: string, path: string }[]>(() => {
  const path = data.value?.path
  if (!path) return []
  const isWindows = /^[A-Z]:[\\/]/i.test(path)
  const sep = isWindows ? '\\' : '/'
  const parts = path.split(/[\\/]+/).filter(Boolean)
  const crumbs: { name: string, path: string }[] = []
  let acc = ''
  parts.forEach((part, index) => {
    if (isWindows && index === 0) {
      acc = `${part}${sep}`
      crumbs.push({ name: part, path: acc })
    }
    else if (!isWindows && index === 0) {
      acc = `${sep}${part}`
      crumbs.push({ name: part, path: acc })
    }
    else {
      acc = `${acc}${sep}${part}`.replace(/[\\/]+/g, sep)
      crumbs.push({ name: part, path: acc })
    }
  })
  return crumbs
})

const filteredEntries = computed<FsEntry[]>(() => {
  const entries = data.value?.entries ?? []
  if (!filter.value.trim()) return entries
  const needle = filter.value.toLowerCase()
  return entries.filter(entry => entry.name.toLowerCase().includes(needle))
})

const selectedCount = computed(() => selected.value.size)

function navigateTo(path: string | null): void {
  currentPath.value = path
  filter.value = ''
}

function toggleSelect(path: string, event?: Event): void {
  event?.stopPropagation()
  const next = new Set(selected.value)
  if (next.has(path)) next.delete(path)
  else next.add(path)
  selected.value = next
}

function onEntryClick(entry: FsEntry): void {
  if (entry.isDirectory) navigateTo(entry.path)
}

function onSubmit(): void {
  if (!selected.value.size) return
  emit('submit', Array.from(selected.value))
}

function onClose(): void {
  emit('close')
}

function formatSize(bytes: number | null): string {
  if (bytes === null) return ''
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`
}

watch(
  () => props.open,
  (open) => {
    if (open) {
      // Start at roots — currentPath null triggers root browse.
      currentPath.value = null
      filter.value = ''
      selected.value = new Set()
    }
  },
)
</script>

<template>
  <div
    v-if="open"
    class="fixed inset-0 z-50 flex items-center justify-center bg-black/70 p-4"
    @click.self="onClose"
  >
    <div class="flex w-full max-w-5xl flex-col rounded-lg border border-slate-800 bg-slate-950 text-slate-100 shadow-2xl" style="max-height: 80vh">
      <header class="flex items-center justify-between border-b border-slate-800 px-4 py-3">
        <div>
          <h2 class="text-lg font-semibold">Pick folder(s)</h2>
          <p class="text-xs text-slate-400">Walk the server filesystem and pick folders with lockfiles to scan.</p>
        </div>
        <button
          type="button"
          class="rounded p-1 text-slate-400 hover:bg-slate-800 hover:text-slate-100"
          aria-label="Close"
          @click="onClose"
        >
          <X class="h-5 w-5" />
        </button>
      </header>

      <div class="flex items-center gap-2 border-b border-slate-800 px-4 py-2 text-sm">
        <button
          type="button"
          class="rounded px-2 py-1 font-mono text-xs text-slate-400 hover:bg-slate-800 hover:text-slate-100"
          @click="navigateTo(null)"
        >
          Roots
        </button>
        <template v-for="(crumb, index) in breadcrumbs" :key="crumb.path">
          <ChevronRight class="h-3 w-3 flex-shrink-0 text-slate-600" />
          <button
            type="button"
            class="rounded px-2 py-1 font-mono text-xs text-slate-300 hover:bg-slate-800"
            :disabled="index === breadcrumbs.length - 1"
            @click="navigateTo(crumb.path)"
          >
            {{ crumb.name }}
          </button>
        </template>
        <div class="ml-auto flex items-center gap-2">
          <Search class="h-3.5 w-3.5 text-slate-500" />
          <input
            v-model="filter"
            placeholder="Filter…"
            class="rounded border border-slate-700 bg-slate-900 px-2 py-1 text-xs focus:border-blue-500 focus:outline-none"
          />
        </div>
      </div>

      <div class="flex min-h-0 flex-1">
        <aside class="w-48 flex-shrink-0 overflow-y-auto border-r border-slate-800 bg-slate-900/50 p-2">
          <p class="px-2 py-1 text-xs uppercase tracking-wider text-slate-500">Roots</p>
          <ul class="space-y-0.5">
            <li v-for="root in data?.roots ?? []" :key="root">
              <button
                type="button"
                class="flex w-full items-center gap-2 rounded px-2 py-1.5 text-left text-xs font-mono text-slate-300 hover:bg-slate-800"
                :class="{ 'bg-slate-800 text-slate-100': data?.path === root }"
                @click="navigateTo(root)"
              >
                <HardDrive class="h-3.5 w-3.5 text-slate-500" />
                {{ root }}
              </button>
            </li>
          </ul>
        </aside>

        <main class="flex min-h-0 flex-1 flex-col overflow-y-auto">
          <p v-if="isLoading" class="flex items-center gap-2 p-4 text-sm text-slate-400">
            <Loader2 class="h-4 w-4 animate-spin" />
            Loading…
          </p>
          <p v-else-if="isError" class="p-4 text-sm text-red-300">
            Failed to browse: {{ error?.message ?? 'unknown error' }}
          </p>
          <p v-else-if="!filteredEntries.length" class="p-4 text-sm text-slate-500">
            <span v-if="filter">No entries match "{{ filter }}".</span>
            <span v-else>Empty directory.</span>
          </p>
          <ul v-else class="divide-y divide-slate-800/60">
            <li
              v-for="entry in filteredEntries"
              :key="entry.path"
              class="group flex items-center gap-2 px-4 py-2 hover:bg-slate-900"
              :class="{ 'cursor-pointer': entry.isDirectory }"
              @click="onEntryClick(entry)"
            >
              <label
                v-if="entry.isDirectory"
                class="flex h-5 w-5 flex-shrink-0 items-center justify-center rounded border border-slate-700 bg-slate-900"
                :class="{ 'border-blue-500 bg-blue-600': selected.has(entry.path) }"
                @click.stop
              >
                <input
                  type="checkbox"
                  class="sr-only"
                  :checked="selected.has(entry.path)"
                  @change="toggleSelect(entry.path)"
                />
                <Check v-if="selected.has(entry.path)" class="h-3.5 w-3.5 text-white" />
              </label>
              <span v-else class="h-5 w-5 flex-shrink-0" />

              <component
                :is="entry.isDirectory ? FolderClosed : File"
                class="h-4 w-4 flex-shrink-0"
                :class="entry.isDirectory ? 'text-blue-400' : 'text-slate-500'"
              />

              <span class="flex-1 truncate font-mono text-sm">{{ entry.name }}</span>

              <span v-if="entry.hasLockfiles" class="inline-flex items-center gap-1 rounded border border-amber-700 bg-amber-950 px-1.5 py-0.5 text-xs text-amber-300" :title="entry.lockfileCount ? `${entry.lockfileCount} lockfile(s)` : 'lockfiles found'">
                <Package class="h-3 w-3" />
                {{ entry.lockfileCount ?? 'lockfiles' }}
              </span>
              <span v-if="entry.hasGitRepo" class="inline-flex items-center gap-1 rounded border border-emerald-700 bg-emerald-950 px-1.5 py-0.5 text-xs text-emerald-300" title="contains .git">
                <GitBranch class="h-3 w-3" />
                git
              </span>
              <span v-if="!entry.isDirectory" class="font-mono text-xs text-slate-500">
                {{ formatSize(entry.size) }}
              </span>
            </li>
          </ul>
        </main>
      </div>

      <footer class="flex items-center justify-between border-t border-slate-800 px-4 py-3">
        <p class="text-sm text-slate-400">
          Selected <span class="font-semibold text-slate-100">{{ selectedCount }}</span> folder(s)
        </p>
        <div class="flex gap-2">
          <button
            type="button"
            class="rounded border border-slate-700 px-3 py-1.5 text-sm font-medium text-slate-200 hover:bg-slate-800"
            @click="onClose"
          >
            Cancel
          </button>
          <button
            type="button"
            class="rounded bg-blue-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-blue-500 disabled:bg-slate-700 disabled:text-slate-400"
            :disabled="!selectedCount"
            @click="onSubmit"
          >
            Add selected
          </button>
        </div>
      </footer>
    </div>
  </div>
</template>
