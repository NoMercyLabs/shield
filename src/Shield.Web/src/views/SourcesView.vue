<script setup lang="ts">
import { computed, ref } from 'vue'
import { RouterLink } from 'vue-router'
import { FolderOpen, Github, GitBranch, Plus, RefreshCw } from 'lucide-vue-next'
import { useI18n } from 'vue-i18n'

import FolderPickerDialog from '@/components/FolderPickerDialog.vue'
import RepoPickerDialog from '@/components/RepoPickerDialog.vue'
import { useRefreshGithubAccessMutation } from '@/queries/access'
import { useOAuthStatus } from '@/queries/oauth'
import { useBulkFromGithubMutation, useBulkLocalFoldersMutation, useCreateSourceMutation, useSourcesQuery } from '@/queries/sources'
import { useAuth } from '@/stores/auth'
import { useToasts } from '@/stores/toast'
import { formatDate } from '@/lib/format'
import type { BulkSelection } from '@/types/api'
import { AutoFixMode, SourceType, SourceTypeNames } from '@/types/api'

const { t } = useI18n()
const { data, isLoading, isError } = useSourcesQuery()
const create = useCreateSourceMutation()
const bulkLocalFolders = useBulkLocalFoldersMutation()
const bulkFromGithub = useBulkFromGithubMutation()
const githubStatus = useOAuthStatus('Github')
const refreshGithubAccess = useRefreshGithubAccessMutation()
const { user, isAdmin } = useAuth()
const { push } = useToasts()

// Maintainers + Admins can re-pull their own GitHub org map; covers the case where the
// user joined a new org on GitHub and wants Shield to surface its repos without waiting
// for the 15-min cache to lapse.
const canRefreshGithubAccess = computed(() =>
  isAdmin.value || (user.value?.roles.includes('Maintainer') ?? false),
)

async function onRefreshGithubAccess(): Promise<void> {
  try {
    const result = await refreshGithubAccess.mutateAsync(undefined)
    if (!result.hasGithubLogin) {
      push('error', t('sources.refresh_access_no_login'))
      return
    }
    if (result.sourceCount === 0) {
      push('success', t('sources.refresh_access_empty', { orgs: result.orgs.join(', ') || t('sources.refresh_access_none') }))
      return
    }
    push('success', t('sources.refresh_access_ok', { n: result.sourceCount, orgs: result.orgs.join(', ') }))
  }
  catch {
    push('error', t('sources.refresh_access_error'))
  }
}

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
    push('success', t('sources.bulk_folder_ok', { created: result.created, skipped: result.skippedExisting }))
    showFolderPicker.value = false
  }
  catch {
    push('error', t('sources.bulk_folder_error'))
  }
}

async function onRepoPickerSubmit(selections: BulkSelection[]): Promise<void> {
  try {
    const result = await bulkFromGithub.mutateAsync({ selections })
    push('success', t('sources.bulk_github_ok', { created: result.created, skipped: result.skippedExisting }))
    showRepoPicker.value = false
  }
  catch {
    push('error', t('sources.bulk_github_error'))
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
    push('success', t('sources.added_toast', { name: name.value }))
    showForm.value = false
    name.value = ''
    configJson.value = '{}'
    scanInterval.value = '01:00:00'
  }
  catch {
    push('error', t('sources.add_error'))
  }
}
</script>

<template>
  <div class="space-y-6">
    <header class="flex items-center justify-between">
      <h1 class="text-2xl font-semibold">{{ t('sources.title') }}</h1>
      <div class="flex gap-2">
        <button
          v-if="canRefreshGithubAccess"
          type="button"
          class="flex items-center gap-1 rounded border border-slate-700 px-3 py-1.5 text-sm font-medium text-slate-200 hover:bg-slate-800 disabled:cursor-not-allowed disabled:opacity-50"
          :disabled="refreshGithubAccess.isPending.value"
          @click="onRefreshGithubAccess"
        >
          <RefreshCw class="h-4 w-4" :class="refreshGithubAccess.isPending.value ? 'animate-spin' : ''" />
          {{ refreshGithubAccess.isPending.value ? t('sources.refreshing') : t('sources.refresh_access_btn') }}
        </button>
        <template v-if="isAdmin">
          <button
            type="button"
            class="flex items-center gap-1 rounded border border-slate-700 px-3 py-1.5 text-sm font-medium text-slate-200 hover:bg-slate-800"
            @click="showFolderPicker = true"
          >
            <FolderOpen class="h-4 w-4" />
            {{ t('sources.pick_folder_btn') }}
          </button>
          <button
            type="button"
            class="flex items-center gap-1 rounded border border-slate-700 px-3 py-1.5 text-sm font-medium text-slate-200 hover:bg-slate-800 disabled:cursor-not-allowed disabled:opacity-50"
            :disabled="!githubConnected"
            @click="showRepoPicker = true"
          >
            <Github class="h-4 w-4" />
            {{ t('sources.pick_github_btn') }}
          </button>
          <button
            type="button"
            class="flex items-center gap-1 rounded bg-blue-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-blue-500"
            @click="showForm = !showForm"
          >
            <Plus class="h-4 w-4" />
            {{ t('sources.add_btn') }}
          </button>
        </template>
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
        <span class="text-sm text-slate-300">{{ t('sources.form_name') }}</span>
        <input
          v-model="name"
          required
          class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
        />
      </label>
      <label class="block">
        <span class="text-sm text-slate-300">{{ t('sources.form_type') }}</span>
        <select
          v-model.number="type"
          class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
        >
          <option :value="SourceType.GithubRepo">{{ t('sources.type_github') }}</option>
          <option :value="SourceType.LocalFolder">{{ t('sources.type_folder') }}</option>
          <option :value="SourceType.LinuxHost">{{ t('sources.type_host') }}</option>
        </select>
      </label>
      <label class="block">
        <span class="text-sm text-slate-300">{{ t('sources.form_scan_interval') }}</span>
        <input
          v-model="scanInterval"
          required
          pattern="^\d+:\d{2}:\d{2}$"
          placeholder="01:00:00"
          class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 font-mono text-sm focus:border-blue-500 focus:outline-none"
        />
      </label>
      <label class="block">
        <span class="text-sm text-slate-300">{{ t('sources.form_config_json') }}</span>
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
        {{ create.isPending.value ? t('sources.saving') : t('sources.save_btn') }}
      </button>
    </form>

    <p
      v-if="!isAdmin && data && data.length"
      class="rounded border border-slate-800 bg-slate-900 px-3 py-2 text-xs text-slate-400"
    >
      {{ t('sources.non_admin_notice', { n: data.length }) }}
    </p>

    <p v-if="isLoading" class="text-sm text-slate-400">{{ t('sources.loading') }}</p>
    <p v-else-if="isError" class="text-sm text-red-300">{{ t('sources.error') }}</p>

    <div v-else-if="data && data.length" class="overflow-x-auto rounded-lg border border-slate-800 bg-slate-900">
      <table class="w-full min-w-[640px] text-left text-sm">
        <thead class="border-b border-slate-800 text-xs uppercase text-slate-500">
          <tr>
            <th class="px-4 py-2">{{ t('sources.col_name') }}</th>
            <th class="px-4 py-2">{{ t('sources.col_type') }}</th>
            <th class="px-4 py-2">{{ t('sources.col_last_scanned') }}</th>
            <th class="px-4 py-2">{{ t('sources.col_status') }}</th>
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
              <div class="flex flex-wrap items-center gap-1">
                <span
                  v-if="source.lastError"
                  class="inline-flex rounded-full bg-red-950/40 px-2 py-0.5 text-xs text-red-200"
                  :title="source.lastError"
                >{{ t('sources.status_error') }}</span>
                <span
                  v-else-if="!source.enabled"
                  class="inline-flex rounded-full bg-slate-800 px-2 py-0.5 text-xs text-slate-400"
                >{{ t('sources.status_disabled') }}</span>
                <span
                  v-else
                  class="inline-flex rounded-full bg-emerald-950/40 px-2 py-0.5 text-xs text-emerald-200"
                >{{ t('sources.status_ok') }}</span>
                <span
                  v-if="source.autoFixMode === AutoFixMode.WeeklyDigest"
                  class="inline-flex rounded-full border border-blue-900/60 bg-blue-950/40 px-2 py-0.5 text-xs text-blue-300"
                  :title="t('sources.auto_fix_weekly_title')"
                >{{ t('sources.auto_fix_weekly') }}</span>
                <span
                  v-else-if="source.autoFixMode === AutoFixMode.OnEveryScan"
                  class="inline-flex rounded-full border border-violet-900/60 bg-violet-950/40 px-2 py-0.5 text-xs text-violet-300"
                  :title="t('sources.auto_fix_scan_title')"
                >{{ t('sources.auto_fix_scan') }}</span>
              </div>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <div
      v-else
      class="rounded-lg border border-dashed border-slate-700 bg-slate-900/40 px-6 py-10 text-center"
    >
      <p class="text-sm font-medium text-slate-200">{{ t('sources.empty_title') }}</p>
      <p class="mt-1 text-xs text-slate-500">{{ t('sources.empty_body') }}</p>
      <div v-if="isAdmin" class="mt-4 flex flex-wrap justify-center gap-2">
        <button
          type="button"
          class="inline-flex items-center gap-1 rounded border border-slate-700 px-3 py-1.5 text-xs font-medium text-slate-200 hover:bg-slate-800"
          @click="showFolderPicker = true"
        >
          <FolderOpen class="h-3.5 w-3.5" />
          {{ t('sources.pick_folder_btn') }}
        </button>
        <button
          type="button"
          class="inline-flex items-center gap-1 rounded border border-slate-700 px-3 py-1.5 text-xs font-medium text-slate-200 hover:bg-slate-800 disabled:cursor-not-allowed disabled:opacity-50"
          :disabled="!githubConnected"
          @click="showRepoPicker = true"
        >
          <Github class="h-3.5 w-3.5" />
          {{ t('sources.pick_github_btn') }}
        </button>
      </div>
    </div>
  </div>
</template>
