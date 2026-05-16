<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { Check, GitFork, Loader2, Lock, Search, X } from 'lucide-vue-next'

import { useGitHubReposQuery } from '@/queries/oauth'
import type { BulkSelection, GitHubRepoEntry } from '@/types/api'

interface Props {
  open: boolean
  provider: 'github'
}

const props = defineProps<Props>()
const emit = defineEmits<{
  close: []
  submit: [selections: BulkSelection[]]
}>()

// Query is gated by props.open so we only hit /oauth/github/repos when the modal is visible.
const enabled = computed(() => props.open && props.provider === 'github')
const { data, isLoading, isError, error } = useGitHubReposQuery(enabled)

const filter = ref('')
const includePrivate = ref(true)
const includeArchived = ref(false)
const includeForks = ref(false)

// `Set<number>` keyed by GitHub repo id — stable across pagination + filter changes.
const selected = ref<Set<number>>(new Set())

const filteredRepos = computed<GitHubRepoEntry[]>(() => {
  const repos = data.value?.repos ?? []
  const needle = filter.value.trim().toLowerCase()
  return repos.filter((repo) => {
    if (!includePrivate.value && repo.private) return false
    if (!includeArchived.value && repo.archived) return false
    if (!includeForks.value && repo.fork) return false
    if (needle && !repo.fullName.toLowerCase().includes(needle)) return false
    return true
  })
})

const groupedByOwner = computed<{ owner: string, repos: GitHubRepoEntry[] }[]>(() => {
  const groups = new Map<string, GitHubRepoEntry[]>()
  for (const repo of filteredRepos.value) {
    const existing = groups.get(repo.owner)
    if (existing) existing.push(repo)
    else groups.set(repo.owner, [repo])
  }
  return Array.from(groups.entries())
    .sort((a, b) => a[0].localeCompare(b[0]))
    .map(([owner, repos]) => ({ owner, repos }))
})

const totalShown = computed(() => filteredRepos.value.length)
const selectedCount = computed(() => selected.value.size)

function toggleRepo(repo: GitHubRepoEntry): void {
  const next = new Set(selected.value)
  if (next.has(repo.id)) next.delete(repo.id)
  else next.add(repo.id)
  selected.value = next
}

function ownerAllSelected(owner: string): boolean {
  const repos = groupedByOwner.value.find(group => group.owner === owner)?.repos ?? []
  return repos.length > 0 && repos.every(repo => selected.value.has(repo.id))
}

function toggleOwnerAll(owner: string): void {
  const repos = groupedByOwner.value.find(group => group.owner === owner)?.repos ?? []
  const next = new Set(selected.value)
  const allSelected = repos.every(repo => next.has(repo.id))
  if (allSelected) repos.forEach(repo => next.delete(repo.id))
  else repos.forEach(repo => next.add(repo.id))
  selected.value = next
}

function onSubmit(): void {
  if (!selected.value.size) return
  const repos = data.value?.repos ?? []
  const selections: BulkSelection[] = repos
    .filter(repo => selected.value.has(repo.id))
    .map(repo => ({
      owner: repo.owner,
      name: repo.name,
      branch: repo.defaultBranch,
    }))
  emit('submit', selections)
}

function onClose(): void {
  emit('close')
}

// Reset state every time the modal is reopened so a previous session's selections
// don't bleed into a new picking flow.
watch(
  () => props.open,
  (open) => {
    if (open) {
      filter.value = ''
      selected.value = new Set()
    }
  },
)
</script>

<template>
  <Teleport to="body">
    <div
      v-if="open"
      class="fixed inset-0 z-50 flex items-center justify-center bg-black/70 p-4"
      @click.self="onClose"
    >
      <div class="flex w-full max-w-3xl flex-col rounded-lg border border-slate-800 bg-slate-950 text-slate-100 shadow-2xl" style="max-height: 80vh">
        <header class="flex items-center justify-between border-b border-slate-800 px-4 py-3">
          <div>
            <h2 class="text-lg font-semibold">Pick repos from GitHub</h2>
            <p class="text-xs text-slate-400">Multi-select repos the connected GitHub account can access.</p>
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

        <div class="flex flex-wrap items-center gap-3 border-b border-slate-800 px-4 py-2 text-sm">
          <div class="flex items-center gap-2">
            <Search class="h-3.5 w-3.5 text-slate-500" />
            <input
              v-model="filter"
              placeholder="Filter owner/name…"
              class="rounded border border-slate-700 bg-slate-900 px-2 py-1 text-xs focus:border-blue-500 focus:outline-none"
            />
          </div>
          <label class="flex items-center gap-1.5 text-xs text-slate-300">
            <input v-model="includePrivate" type="checkbox" class="h-3.5 w-3.5 rounded border-slate-600" />
            Private
          </label>
          <label class="flex items-center gap-1.5 text-xs text-slate-300">
            <input v-model="includeArchived" type="checkbox" class="h-3.5 w-3.5 rounded border-slate-600" />
            Archived
          </label>
          <label class="flex items-center gap-1.5 text-xs text-slate-300">
            <input v-model="includeForks" type="checkbox" class="h-3.5 w-3.5 rounded border-slate-600" />
            Forks
          </label>
          <p class="ml-auto text-xs text-slate-500">Showing {{ totalShown }} of {{ data?.total ?? 0 }}</p>
        </div>

        <div class="min-h-0 flex-1 overflow-y-auto">
          <p v-if="isLoading" class="flex items-center gap-2 p-4 text-sm text-slate-400">
            <Loader2 class="h-4 w-4 animate-spin" />
            Loading repos…
          </p>
          <p v-else-if="isError" class="p-4 text-sm text-red-300">
            Failed to load repos: {{ error?.message ?? 'unknown error' }}
          </p>
          <p v-else-if="!groupedByOwner.length" class="p-4 text-sm text-slate-500">
            <span v-if="filter">No repos match "{{ filter }}".</span>
            <span v-else>No repos returned by GitHub.</span>
          </p>
          <div v-else>
            <section
              v-for="group in groupedByOwner"
              :key="group.owner"
              class="border-b border-slate-800/60 last:border-b-0"
            >
              <header class="sticky top-0 z-10 flex items-center justify-between bg-slate-900/80 px-4 py-2 backdrop-blur">
                <div class="flex items-center gap-2">
                  <h3 class="text-sm font-semibold text-slate-200">{{ group.owner }}</h3>
                  <span class="rounded bg-slate-800 px-1.5 py-0.5 text-xs text-slate-400">{{ group.repos.length }}</span>
                </div>
                <button
                  type="button"
                  class="text-xs text-blue-400 hover:text-blue-300"
                  @click="toggleOwnerAll(group.owner)"
                >
                  {{ ownerAllSelected(group.owner) ? 'Deselect all' : 'Select all' }}
                </button>
              </header>
              <ul class="divide-y divide-slate-800/40">
                <li
                  v-for="repo in group.repos"
                  :key="repo.id"
                  class="flex cursor-pointer items-center gap-3 px-4 py-2 hover:bg-slate-900"
                  @click="toggleRepo(repo)"
                >
                  <label
                    class="flex h-5 w-5 flex-shrink-0 items-center justify-center rounded border border-slate-700 bg-slate-900"
                    :class="{ 'border-blue-500 bg-blue-600': selected.has(repo.id) }"
                    @click.stop
                  >
                    <input
                      type="checkbox"
                      class="sr-only"
                      :checked="selected.has(repo.id)"
                      @change="toggleRepo(repo)"
                    />
                    <Check v-if="selected.has(repo.id)" class="h-3.5 w-3.5 text-white" />
                  </label>
                  <div class="min-w-0 flex-1">
                    <p class="truncate font-mono text-sm">{{ repo.fullName }}</p>
                    <p v-if="repo.description" class="truncate text-xs text-slate-500">{{ repo.description }}</p>
                  </div>
                  <span v-if="repo.private" class="inline-flex items-center gap-1 rounded border border-amber-700 bg-amber-950 px-1.5 py-0.5 text-xs text-amber-300">
                    <Lock class="h-3 w-3" />
                    Private
                  </span>
                  <span v-if="repo.fork" class="inline-flex items-center gap-1 rounded border border-slate-700 bg-slate-900 px-1.5 py-0.5 text-xs text-slate-400">
                    <GitFork class="h-3 w-3" />
                    Fork
                  </span>
                  <span v-if="repo.archived" class="rounded border border-slate-700 bg-slate-900 px-1.5 py-0.5 text-xs text-slate-500">Archived</span>
                  <span v-if="repo.language" class="rounded border border-slate-800 bg-slate-900 px-1.5 py-0.5 text-xs text-slate-400">{{ repo.language }}</span>
                </li>
              </ul>
            </section>
          </div>
        </div>

        <footer class="flex items-center justify-between border-t border-slate-800 px-4 py-3">
          <p class="text-sm text-slate-400">
            Selected <span class="font-semibold text-slate-100">{{ selectedCount }}</span> of {{ totalShown }}
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
  </Teleport>
</template>
