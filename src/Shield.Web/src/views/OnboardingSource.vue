<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { CheckCircle2, FolderClosed, Github, Loader2 } from 'lucide-vue-next'

import FolderPickerDialog from '@/components/FolderPickerDialog.vue'
import GithubOauthSetupCard from '@/components/GithubOauthSetupCard.vue'
import RepoPickerDialog from '@/components/RepoPickerDialog.vue'
import {
  useBulkFromGithubMutation,
  useBulkLocalFoldersMutation,
} from '@/queries/sources'
import { useOAuthStatus } from '@/queries/oauth'
import { useOnboardingStatus } from '@/queries/onboarding'
import { useToasts } from '@/stores/toast'
import type { BulkSelection } from '@/types/api'

const emit = defineEmits<{ done: [] }>()

const { t } = useI18n()
const { push } = useToasts()
const { data: status, refetch, isFetching } = useOnboardingStatus()
const { data: oauthStatus } = useOAuthStatus('Github')

type Choice = 'github' | 'local' | 'skip' | null
const choice = ref<Choice>(null)

const repoPickerOpen = ref(false)
const folderPickerOpen = ref(false)
const githubSetupOpen = ref(false)

const bulkFromGithub = useBulkFromGithubMutation()
const bulkLocalFolders = useBulkLocalFoldersMutation()

// Race-safe: when status hasn't resolved we treat the answer as "unknown, don't act"
// rather than the default-false "not configured" — otherwise the first click before the
// /api/onboarding/status fetch resolves would render the wrong inline state.
const githubConfigured = computed(() => status.value?.anyOauthConfigured ?? null)
const githubConnected = computed(() => oauthStatus.value?.connected ?? status.value?.githubConnected ?? false)
const githubAccountLogin = computed(() => oauthStatus.value?.accountLogin ?? null)
const sourceCount = computed(() => status.value?.sourceCount ?? 0)
const statusLoading = computed(() => status.value === undefined)

// Once GitHub is fully wired, the card is hidden and the picker is the natural next step.
// Until then, clicking the GitHub option toggles the inline setup card open.
watch(githubConnected, (connected) => {
  if (connected) githubSetupOpen.value = false
})

async function pickGithub(): Promise<void> {
  choice.value = 'github'
  // Re-poll before deciding so the inline state reflects fresh server truth.
  if (statusLoading.value)
    await refetch()
  if (githubConnected.value) {
    repoPickerOpen.value = true
    return
  }
  // Either no creds saved, or saved but not connected — both flows live inside the card.
  githubSetupOpen.value = true
}

function pickLocal(): void {
  choice.value = 'local'
  folderPickerOpen.value = true
}

function pickSkip(): void {
  choice.value = 'skip'
  emit('done')
}

async function onGithubConnected(): Promise<void> {
  // The card just flipped to connected; pop the picker straight away so the operator
  // doesn't have to click GitHub a second time.
  await refetch()
  githubSetupOpen.value = false
  repoPickerOpen.value = true
}

async function onRepoSubmit(selections: BulkSelection[]): Promise<void> {
  repoPickerOpen.value = false
  if (!selections.length) {
    emit('done')
    return
  }
  try {
    const result = await bulkFromGithub.mutateAsync({ selections })
    push('success', t('screen.onboarding.source.github_added_toast', { n: result.created }, result.created))
    await refetch()
    emit('done')
  }
  catch (error) {
    push('error', error instanceof Error ? error.message : t('screen.onboarding.source.github_add_failed'))
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
    push('success', t('screen.onboarding.source.folders_added_toast', { n: result.created }, result.created))
    await refetch()
    emit('done')
  }
  catch (error) {
    push('error', error instanceof Error ? error.message : t('screen.onboarding.source.folders_add_failed'))
  }
}
</script>

<template>
  <section class="space-y-5">
    <div>
      <h1 class="text-2xl font-semibold">{{ t('screen.onboarding.source.title') }}</h1>
      <p class="mt-1 text-sm text-slate-400">{{ t('screen.onboarding.source.subtitle') }}</p>
    </div>

    <div class="grid grid-cols-1 gap-3">
      <div
        class="rounded-lg border border-slate-800 bg-slate-900 text-left transition"
        :class="{ 'border-blue-500 bg-slate-800/30': choice === 'github' }"
      >
        <button
          type="button"
          class="flex w-full items-start gap-3 p-4 text-left hover:bg-slate-800/50 disabled:opacity-50"
          :disabled="bulkFromGithub.isPending.value || statusLoading || isFetching"
          @click="pickGithub"
        >
          <Github class="mt-0.5 h-5 w-5 text-slate-200" />
          <div class="flex-1 space-y-1">
            <p class="flex items-center gap-2 text-sm font-medium text-slate-100">
              {{ t('screen.onboarding.source.github_btn_title') }}
              <span
                v-if="githubConnected"
                class="inline-flex items-center gap-1 rounded border border-emerald-700/50 bg-emerald-950/40 px-1.5 py-0.5 text-xs text-emerald-300"
              >
                <CheckCircle2 class="h-3 w-3" />
                {{ githubAccountLogin
                  ? t('screen.onboarding.source.github_connected_as', { login: githubAccountLogin })
                  : t('screen.oauth_setup.status_configured') }}
              </span>
            </p>
            <p v-if="githubConnected" class="text-xs text-slate-400">
              {{ t('screen.onboarding.source.github_ready_body') }}
            </p>
            <p v-else-if="githubConfigured === true" class="text-xs text-amber-300">
              {{ t('screen.onboarding.source.github_needs_connect_body') }}
            </p>
            <p v-else class="text-xs text-slate-400">
              {{ t('screen.onboarding.source.github_needs_setup_body') }}
            </p>
          </div>
        </button>

        <div v-if="githubSetupOpen && !githubConnected" class="border-t border-slate-800 px-4 pb-4 pt-3">
          <GithubOauthSetupCard
            :auto-connect-after-save="true"
            @connected="onGithubConnected"
          />
        </div>
      </div>

      <button
        type="button"
        class="flex items-start gap-3 rounded-lg border border-slate-800 bg-slate-900 p-4 text-left hover:border-blue-500 hover:bg-slate-800/50 disabled:opacity-50"
        :class="{ 'border-blue-500 bg-slate-800/30': choice === 'local' }"
        :disabled="bulkLocalFolders.isPending.value"
        @click="pickLocal"
      >
        <FolderClosed class="mt-0.5 h-5 w-5 text-slate-200" />
        <div class="flex-1 space-y-1">
          <p class="text-sm font-medium text-slate-100">{{ t('screen.onboarding.source.local_btn_title') }}</p>
          <p class="text-xs text-slate-400">{{ t('screen.onboarding.source.local_btn_body') }}</p>
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
          <p class="text-sm font-medium text-slate-300">{{ t('screen.onboarding.source.skip_btn_title') }}</p>
          <p class="text-xs text-slate-500">{{ t('screen.onboarding.source.skip_btn_body') }}</p>
        </div>
      </button>
    </div>

    <div
      v-if="sourceCount > 0"
      class="rounded border border-emerald-700/40 bg-emerald-900/20 p-3 text-xs text-emerald-200"
    >
      {{ t('screen.onboarding.source.existing_sources_note', { count: sourceCount }, sourceCount) }}
    </div>

    <div v-if="bulkFromGithub.isPending.value || bulkLocalFolders.isPending.value" class="flex items-center gap-2 text-xs text-slate-400">
      <Loader2 class="h-3 w-3 animate-spin" />
      {{ t('screen.onboarding.source.adding_source') }}
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
