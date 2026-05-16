<script setup lang="ts">
import { ref } from 'vue'
import { RouterLink } from 'vue-router'
import { Plus } from 'lucide-vue-next'

import { useCreateSourceMutation, useSourcesQuery } from '@/queries/sources'
import { useToasts } from '@/stores/toast'
import { formatDate } from '@/lib/format'
import type { SourceType } from '@/types/api'

const { data, isLoading, isError } = useSourcesQuery()
const create = useCreateSourceMutation()
const { push } = useToasts()

const showForm = ref(false)
const name = ref('')
const type = ref<SourceType>('GithubRepo')
const configJson = ref('{}')

async function onSubmit(): Promise<void> {
  try {
    await create.mutateAsync({ name: name.value, type: type.value, configJson: configJson.value })
    push('success', `Source "${name.value}" added.`)
    showForm.value = false
    name.value = ''
    configJson.value = '{}'
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
      <button
        type="button"
        class="flex items-center gap-1 rounded bg-blue-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-blue-500"
        @click="showForm = !showForm"
      >
        <Plus class="h-4 w-4" />
        Add source
      </button>
    </header>

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
          v-model="type"
          class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
        >
          <option value="GithubRepo">GitHub repo</option>
          <option value="LocalFolder">Local folder</option>
          <option value="LinuxHost">Linux host (Phase 2)</option>
        </select>
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
            <td class="px-4 py-2 text-slate-400">{{ source.type }}</td>
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
