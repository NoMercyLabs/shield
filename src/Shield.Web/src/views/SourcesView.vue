<script setup lang="ts">
import { computed, ref } from 'vue'
import { RouterLink } from 'vue-router'
import { FolderOpen, Github, GitBranch, Plus } from 'lucide-vue-next'

import FolderPickerDialog from '@/components/FolderPickerDialog.vue'
import RepoPickerDialog from '@/components/RepoPickerDialog.vue'
import { useOAuthStatus } from '@/queries/oauth'
import { useBulkFromGithubMutation, useBulkLocalFoldersMutation, useCreateSourceMutation, useSourcesQuery } from '@/queries/sources'
import { useToasts } from '@/stores/toast'
import { formatDate } from '@/lib/format'
import type { BulkSelection } from '@/types/api'
import { SourceType, SourceTypeNames } from '@/types/api'

const { data, isLoading, isError } = useSourcesQuery()
const create = useCreateSourceMutation()
const bulkLocalFolders = useBulkLocalFoldersMutation()
const bulkFromGithub = useBulkFromGithubMutation()
const githubStatus = useOAuthStatus('Github')
const { push } = useToasts()

const showForm = ref(false)
const showFolderPicker = ref(false)
const showRepoPicker = ref(false)
const name = ref('')
const type = ref<SourceType>(SourceType.GithubRepo)
const configJson = ref('{}')
const scanInterval = ref('01:00:00')

const githubConnected = computed(() => githubStatus.data.value?.connected ?? false)

async function onFolderPickerSubmit(paths: string[]): Promise<void> {
  try {
    const result = await bulkLocalFolders.mutateAsync({ paths })
    push('success', `Created ${result.created} folder source(s), skipped ${result.skippedExisting} existing.`)
    showFolderPicker.value = false
  }
  catch {
    push('error', 'Failed to add folder sources.')
  }
}

async function onRepoPickerSubmit(selections: BulkSelection[]): Promise<void> {
  try {
    const result = await bulkFromGithub.mutateAsync({ selections })
    push('success', `Created ${result.created} sources, skipped ${result.skippedExisting} existing`)
    showRepoPicker.value = false
  }
  catch {
    push('error', 'Failed to add GitHub repo sources.')
  }
}

async function onSubmit(): Promise<void> {
  try {
    await create.mutateAsync({
      name: name.value,
      type: type.value,
      configJson: configJson.value,
      scanInterval: scanInterval.value,
      enabled: true,
    })
    push('success', `Source "${name.value}" added.`)
    showForm.value = false
    name.value = ''
    configJson.value = '{}'
    scanInterval.value = '01:00:00'
  }
  catch {
    push('error', 'Failed to add source.')
  }
}
</script>

<template>
  <div class="space-y-6">
    <header class="flex items-center justify-between">
      <h1 class="text-2xl font-semibold">Sources</h1>
      <div class="flex gap-2">
        <button
          type="button"
          class="flex items-center gap-1 rounded border border-slate-700 px-3 py-1.5 text-sm font-medium text-slate-200 hover:bg-slate-800"
          @click="showFolderPicker = true"
        >
          <FolderOpen class="h-4 w-4" />
          Pick folder
        </button>
        <button
          type="button"
          class="flex items-center gap-1 rounded border border-slate-700 px-3 py-1.5 text-sm font-medium text-slate-200 hover:bg-slate-800 disabled:cursor-not-allowed disabled:opacity-50"
          :disabled="!githubConnected"
          :title="githubConnected ? 'Pick repos from your connected GitHub account' : 'Connect GitHub in Settings → Integrations first'"
          @click="showRepoPicker = true"
        >
          <Github class="h-4 w-4" />
          Pick from GitHub
        </button>
        <button
          type="button"
          class="flex items-center gap-1 rounded bg-blue-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-blue-500"
          @click="showForm = !showForm"
        >
          <Plus class="h-4 w-4" />
          Add source
        </button>
      </div>
    </header>

    <FolderPickerDialog
      :open="showFolderPicker"
      @close="showFolderPicker = false"
      @submit="onFolderPickerSubmit"
    />

    <RepoPickerDialog
      :open="showRepoPicker"
      provider="github"
      @close="showRepoPicker = false"
      @submit="onRepoPickerSubmit"
    />

    <form
      v-if="showForm"
      class="space-y-3 rounded-lg border border-slate-800 bg-slate-900 p-4"
      @submit.prevent="onSubmit"
    >
      <label class="block">
        <span class="text-sm text-slate-300">Name</span>
        <input
          v-model="name"
          required
          class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
        />
      </label>
      <label class="block">
        <span class="text-sm text-slate-300">Type</span>
        <select
          v-model.number="type"
          class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
        >
          <option :value="SourceType.GithubRepo">GitHub repo</option>
          <option :value="SourceType.LocalFolder">Local folder</option>
          <option :value="SourceType.LinuxHost">Linux host (Phase 2)</option>
        </select>
      </label>
      <label class="block">
        <span class="text-sm text-slate-300">Scan interval (hh:mm:ss)</span>
        <input
          v-model="scanInterval"
          required
          pattern="^\d+:\d{2}:\d{2}$"
          placeholder="01:00:00"
          class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 font-mono text-sm focus:border-blue-500 focus:outline-none"
        />
      </label>
      <label class="block">
        <span class="text-sm text-slate-300">Config (JSON)</span>
        <textarea
          v-model="configJson"
          rows="4"
          class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 font-mono text-sm focus:border-blue-500 focus:outline-none"
        />
      </label>
      <button
        type="submit"
        :disabled="create.isPending.value"
        class="rounded bg-blue-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-blue-500 disabled:bg-blue-900"
      >
        {{ create.isPending.value ? 'Saving…' : 'Save' }}
      </button>
    </form>

    <p v-if="isLoading" class="text-sm text-slate-400">Loading…</p>
    <p v-else-if="isError" class="text-sm text-red-300">Failed to load sources.</p>

    <div v-else-if="data && data.length" class="overflow-hidden rounded-lg border border-slate-800 bg-slate-900">
      <table class="w-full text-left text-sm">
        <thead class="border-b border-slate-800 text-xs uppercase text-slate-500">
          <tr>
            <th class="px-4 py-2">Name</th>
            <th class="px-4 py-2">Type</th>
            <th class="px-4 py-2">Last scanned</th>
            <th class="px-4 py-2">Status</th>
          </tr>
        </thead>
        <tbody class="divide-y divide-slate-800">
          <tr v-for="source in data" :key="source.id" class="hover:bg-slate-800/50">
            <td class="px-4 py-2">
              <RouterLink :to="`/sources/${source.id}`" class="text-blue-400 hover:underline">
                {{ source.name }}
              </RouterLink>
            </td>
            <td class="px-4 py-2 text-slate-400">
              <span class="inline-flex items-center gap-1">
                {{ SourceTypeNames[source.type] }}
                <span
                  v-if="source.detectedRemote"
                  class="inline-flex items-center gap-0.5 text-xs text-slate-500"
                  :title="`Detected remote: ${source.detectedRemote.host}/${source.detectedRemote.owner}/${source.detectedRemote.repo}`"
                >
                  <GitBranch class="h-3 w-3" />
                  {{ source.detectedRemote.host }}
                </span>
              </span>
            </td>
            <td class="px-4 py-2 text-slate-400">{{ formatDate(source.lastScannedAt) }}</td>
            <td class="px-4 py-2">
              <span v-if="source.lastError" class="text-red-300">Error</span>
              <span v-else-if="!source.enabled" class="text-slate-500">Disabled</span>
              <span v-else class="text-green-300">OK</span>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <p v-else class="text-sm text-slate-500">No sources yet. Add one to start scanning.</p>
  </div>
</template>
