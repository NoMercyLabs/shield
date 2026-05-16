<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import { FolderClosed, Github, Loader2, Settings as SettingsIcon } from 'lucide-vue-next'

import FolderPickerDialog from '@/components/FolderPickerDialog.vue'
import RepoPickerDialog from '@/components/RepoPickerDialog.vue'
import {
  useBulkFromGithubMutation,
  useBulkLocalFoldersMutation,
} from '@/queries/sources'
import { useOnboardingStatus } from '@/queries/onboarding'
import { useToasts } from '@/stores/toast'
import type { BulkSelection } from '@/types/api'

const emit = defineEmits<{ done: [] }>()

const router = useRouter()
const { push } = useToasts()
const { data: status, refetch } = useOnboardingStatus()

type Choice = 'github' | 'local' | 'skip' | null
const choice = ref<Choice>(null)

const repoPickerOpen = ref(false)
const folderPickerOpen = ref(false)

const bulkFromGithub = useBulkFromGithubMutation()
const bulkLocalFolders = useBulkLocalFoldersMutation()

const githubConfigured = computed(() => status.value?.anyOauthConfigured ?? false)
const githubConnected = computed(() => status.value?.githubConnected ?? false)
const sourceCount = computed(() => status.value?.sourceCount ?? 0)

function pickGithub(): void {
  choice.value = 'github'
  if (!githubConfigured.value) {
    // Deep-link to Settings → Integrations; the operator returns via the wizard's URL.
    router.push({ path: '/settings', query: { onboarding: 'github', return: '/welcome' } })
    return
  }
  repoPickerOpen.value = true
}

function pickLocal(): void {
  choice.value = 'local'
  folderPickerOpen.value = true
}

function pickSkip(): void {
  choice.value = 'skip'
  emit('done')
}

async function onRepoSubmit(selections: BulkSelection[]): Promise<void> {
  repoPickerOpen.value = false
  if (!selections.length) {
    emit('done')
    return
  }
  try {
    const result = await bulkFromGithub.mutateAsync({ selections })
    push('success', `Added ${result.created} GitHub source${result.created === 1 ? '' : 's'}.`)
    await refetch()
    emit('done')
  }
  catch (error) {
    push('error', error instanceof Error ? error.message : 'Failed to add GitHub sources.')
  }
}

async function onFolderSubmit(paths: string[]): Promise<void> {
  folderPickerOpen.value = false
  if (!paths.length) {
    emit('done')
    return
  }
  try {
    const result = await bulkLocalFolders.mutateAsync({ paths })
    push('success', `Added ${result.created} folder source${result.created === 1 ? '' : 's'}.`)
    await refetch()
    emit('done')
  }
  catch (error) {
    push('error', error instanceof Error ? error.message : 'Failed to add folders.')
  }
}
</script>

<template>
  <section class="space-y-5">
    <div>
      <h1 class="text-2xl font-semibold">Connect a code source</h1>
      <p class="mt-1 text-sm text-slate-400">
        Shield needs at least one project to scan. Pick a GitHub repo, a folder on disk, or skip
        and add one later.
      </p>
    </div>

    <div class="grid grid-cols-1 gap-3">
      <button
        type="button"
        class="flex items-start gap-3 rounded-lg border border-slate-800 bg-slate-900 p-4 text-left hover:border-blue-500 hover:bg-slate-800/50 disabled:opacity-50"
        :class="{ 'border-blue-500 bg-slate-800/30': choice === 'github' }"
        :disabled="bulkFromGithub.isPending.value"
        @click="pickGithub"
      >
        <Github class="mt-0.5 h-5 w-5 text-slate-200" />
        <div class="flex-1 space-y-1">
          <p class="text-sm font-medium text-slate-100">Pick a GitHub repo</p>
          <p v-if="githubConfigured && githubConnected" class="text-xs text-slate-400">
            Opens the repo picker — pick one or many, Shield will scan them on a schedule.
          </p>
          <p v-else-if="githubConfigured" class="text-xs text-amber-300">
            GitHub OAuth is configured but not connected — opens Settings to connect.
          </p>
          <p v-else class="text-xs text-slate-400">
            <SettingsIcon class="inline h-3 w-3" />
            GitHub isn't configured yet — opens Settings → Integrations to set up the OAuth client.
          </p>
        </div>
      </button>

      <button
        type="button"
        class="flex items-start gap-3 rounded-lg border border-slate-800 bg-slate-900 p-4 text-left hover:border-blue-500 hover:bg-slate-800/50 disabled:opacity-50"
        :class="{ 'border-blue-500 bg-slate-800/30': choice === 'local' }"
        :disabled="bulkLocalFolders.isPending.value"
        @click="pickLocal"
      >
        <FolderClosed class="mt-0.5 h-5 w-5 text-slate-200" />
        <div class="flex-1 space-y-1">
          <p class="text-sm font-medium text-slate-100">Pick a local folder</p>
          <p class="text-xs text-slate-400">
            Browse the filesystem and pick any folder with lockfiles — works without OAuth.
          </p>
        </div>
      </button>

      <button
        type="button"
        class="flex items-start gap-3 rounded-lg border border-slate-800 bg-slate-900 p-4 text-left hover:border-slate-700 hover:bg-slate-800/30"
        :class="{ 'border-slate-700': choice === 'skip' }"
        @click="pickSkip"
      >
        <div class="mt-0.5 h-5 w-5 rounded-full border border-slate-600" />
        <div class="flex-1 space-y-1">
          <p class="text-sm font-medium text-slate-300">Skip for now</p>
          <p class="text-xs text-slate-500">
            You can add sources later from the Sources page.
          </p>
        </div>
      </button>
    </div>

    <div
      v-if="sourceCount > 0"
      class="rounded border border-emerald-700/40 bg-emerald-900/20 p-3 text-xs text-emerald-200"
    >
      You already have {{ sourceCount }} source{{ sourceCount === 1 ? '' : 's' }} configured.
      You're free to add more or continue.
    </div>

    <div v-if="bulkFromGithub.isPending.value || bulkLocalFolders.isPending.value" class="flex items-center gap-2 text-xs text-slate-400">
      <Loader2 class="h-3 w-3 animate-spin" />
      Adding source…
    </div>

    <RepoPickerDialog
      v-if="repoPickerOpen"
      :open="repoPickerOpen"
      provider="github"
      @close="repoPickerOpen = false"
      @submit="onRepoSubmit"
    />
    <FolderPickerDialog
      v-if="folderPickerOpen"
      :open="folderPickerOpen"
      @close="folderPickerOpen = false"
      @submit="onFolderSubmit"
    />
  </section>
</template>
