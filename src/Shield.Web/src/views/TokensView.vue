<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'

import { useApiTokensQuery, useCreateTokenMutation, useRevokeTokenMutation } from '@/queries/tokens'
import { useSourcesQuery } from '@/queries/sources'
import { useToasts } from '@/stores/toast'
import type { ApiTokenScope, CreateTokenResponse } from '@/types/api'

const { t } = useI18n()
const { push } = useToasts()

const tokensQuery = useApiTokensQuery()
const sourcesQuery = useSourcesQuery()
const createMutation = useCreateTokenMutation()
const revokeMutation = useRevokeTokenMutation()

const ALL_SCOPES: ApiTokenScope[] = ['findings:read', 'findings:write', 'sources:read', 'sbom:write']
const EXPIRY_CHOICES: { label: string, days: number | null }[] = [
  { label: '7d', days: 7 },
  { label: '30d', days: 30 },
  { label: '90d', days: 90 },
  { label: 'never', days: null },
]

const showCreate = ref(false)
const form = ref<{ name: string, scopes: ApiTokenScope[], expiresInDays: number | null, sourceIdFilter: number[] }>({
  name: '',
  scopes: ['findings:read'],
  expiresInDays: 30,
  sourceIdFilter: [],
})
const createdSecret = ref<CreateTokenResponse | null>(null)
const formError = ref<string | null>(null)

const canSubmit = computed(() => form.value.name.trim().length > 0 && form.value.scopes.length > 0)

function resetForm(): void {
  form.value = {
    name: '',
    scopes: ['findings:read'],
    expiresInDays: 30,
    sourceIdFilter: [],
  }
  formError.value = null
}

async function onSubmit(): Promise<void> {
  formError.value = null
  if (!canSubmit.value) {
    formError.value = t('screen.tokens.error.name_required')
    return
  }
  try {
    const response = await createMutation.mutateAsync({
      name: form.value.name.trim(),
      scopes: form.value.scopes,
      expiresInDays: form.value.expiresInDays,
      sourceIdFilter: form.value.sourceIdFilter,
    })
    createdSecret.value = response
    resetForm()
  }
  catch (err) {
    formError.value = err instanceof Error ? err.message : t('screen.tokens.error.create_failed')
  }
}

function closeReveal(): void {
  createdSecret.value = null
  showCreate.value = false
}

async function onRevoke(id: string): Promise<void> {
  try {
    await revokeMutation.mutateAsync(id)
    push('success', t('screen.tokens.toast.revoked'))
  }
  catch {
    push('error', t('screen.tokens.error.revoke_failed'))
  }
}

function toggleScope(scope: ApiTokenScope): void {
  const idx = form.value.scopes.indexOf(scope)
  if (idx === -1)
    form.value.scopes = [...form.value.scopes, scope]
  else
    form.value.scopes = form.value.scopes.filter(item => item !== scope)
}

function toggleSourceFilter(sourceId: number): void {
  const idx = form.value.sourceIdFilter.indexOf(sourceId)
  if (idx === -1)
    form.value.sourceIdFilter = [...form.value.sourceIdFilter, sourceId]
  else
    form.value.sourceIdFilter = form.value.sourceIdFilter.filter(id => id !== sourceId)
}

async function copySecret(): Promise<void> {
  if (!createdSecret.value) return
  try {
    await navigator.clipboard.writeText(createdSecret.value.plaintext)
    push('success', t('screen.tokens.toast.copied'))
  }
  catch {
    push('error', t('screen.tokens.error.copy_failed'))
  }
}
</script>

<template>
  <div class="space-y-6">
    <header class="flex items-start justify-between gap-4">
      <div>
        <h1 class="text-2xl font-semibold">{{ t('screen.tokens.title') }}</h1>
        <p class="text-sm text-slate-400">{{ t('screen.tokens.subtitle') }}</p>
      </div>
      <button
        type="button"
        class="rounded bg-blue-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-blue-500"
        @click="showCreate = true"
      >
        {{ t('screen.tokens.create_btn') }}
      </button>
    </header>

    <section v-if="tokensQuery.isLoading.value" class="text-sm text-slate-400">
      {{ t('state.loading') }}
    </section>

    <section v-else-if="tokensQuery.data.value && tokensQuery.data.value.length > 0" class="space-y-2">
      <div
        v-for="token in tokensQuery.data.value"
        :key="token.id"
        class="rounded-lg border border-slate-800 bg-slate-900 p-4"
      >
        <div class="flex items-start justify-between gap-4">
          <div class="space-y-1">
            <p class="font-medium text-slate-100">{{ token.name }}</p>
            <p class="font-mono text-xs text-slate-400">shld_{{ token.prefix }}…</p>
            <div class="flex flex-wrap gap-1">
              <span
                v-for="scope in token.scopes"
                :key="scope"
                class="rounded bg-slate-800 px-2 py-0.5 font-mono text-xs text-slate-300"
              >
                {{ scope }}
              </span>
            </div>
            <p v-if="token.sourceIdFilter.length > 0" class="text-xs text-slate-500">
              {{ t('screen.tokens.field.source_filter') }}: {{ token.sourceIdFilter.join(', ') }}
            </p>
          </div>
          <div class="text-right text-xs text-slate-500">
            <p v-if="token.revokedAt" class="text-red-400">
              {{ t('screen.tokens.badge.revoked') }} {{ new Date(token.revokedAt).toLocaleString() }}
            </p>
            <p v-else-if="token.lastUsedAt">
              {{ t('screen.tokens.badge.last_used') }}: {{ new Date(token.lastUsedAt).toLocaleString() }}
            </p>
            <p v-else>{{ t('screen.tokens.badge.never_used') }}</p>
            <p v-if="token.expiresAt">
              {{ t('screen.tokens.badge.expires') }}: {{ new Date(token.expiresAt).toLocaleDateString() }}
            </p>
            <button
              v-if="!token.revokedAt"
              type="button"
              class="mt-2 rounded border border-red-700 px-2 py-0.5 text-xs text-red-300 hover:bg-red-900/40"
              @click="onRevoke(token.id)"
            >
              {{ t('screen.tokens.revoke_btn') }}
            </button>
          </div>
        </div>
      </div>
    </section>

    <section v-else class="rounded border border-dashed border-slate-700 p-6 text-center text-sm text-slate-400">
      {{ t('screen.tokens.empty') }}
    </section>

    <!-- Create modal -->
    <div
      v-if="showCreate"
      class="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4"
      @click.self="closeReveal"
    >
      <div class="w-full max-w-lg rounded-lg border border-slate-700 bg-slate-900 p-6 shadow-xl">
        <!-- Post-create reveal -->
        <div v-if="createdSecret" class="space-y-4">
          <h2 class="text-lg font-semibold">{{ t('screen.tokens.reveal.title') }}</h2>
          <p class="rounded border border-yellow-700 bg-yellow-900/30 p-2 text-sm text-yellow-200">
            {{ t('screen.tokens.reveal.warning') }}
          </p>
          <div class="space-y-1">
            <code
              class="block break-all rounded bg-slate-950 p-3 font-mono text-sm text-slate-100 select-all"
            >{{ createdSecret.plaintext }}</code>
            <button
              type="button"
              class="rounded bg-slate-800 px-2 py-1 text-xs text-slate-300 hover:bg-slate-700"
              @click="copySecret"
            >
              {{ t('screen.tokens.reveal.copy_btn') }}
            </button>
          </div>
          <button
            type="button"
            class="w-full rounded bg-blue-600 px-3 py-2 text-sm font-medium text-white hover:bg-blue-500"
            @click="closeReveal"
          >
            {{ t('action.done') }}
          </button>
        </div>

        <!-- Create form -->
        <form v-else class="space-y-4" @submit.prevent="onSubmit">
          <h2 class="text-lg font-semibold">{{ t('screen.tokens.create.title') }}</h2>

          <label class="block">
            <span class="text-sm text-slate-300">{{ t('screen.tokens.field.name') }}</span>
            <input
              v-model="form.name"
              type="text"
              required
              maxlength="200"
              placeholder="CI: nomercy-app-web"
              class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
            />
          </label>

          <div>
            <span class="text-sm text-slate-300">{{ t('screen.tokens.field.scopes') }}</span>
            <div class="mt-1 space-y-1">
              <label v-for="scope in ALL_SCOPES" :key="scope" class="flex items-center gap-2">
                <input
                  type="checkbox"
                  :checked="form.scopes.includes(scope)"
                  @change="toggleScope(scope)"
                />
                <code class="text-xs">{{ scope }}</code>
              </label>
            </div>
          </div>

          <div>
            <span class="text-sm text-slate-300">{{ t('screen.tokens.field.expiry') }}</span>
            <div class="mt-1 flex gap-2">
              <button
                v-for="choice in EXPIRY_CHOICES"
                :key="choice.label"
                type="button"
                class="rounded border px-2 py-1 text-xs"
                :class="form.expiresInDays === choice.days
                  ? 'border-blue-500 bg-blue-600 text-white'
                  : 'border-slate-700 text-slate-300 hover:bg-slate-800'"
                @click="form.expiresInDays = choice.days"
              >
                {{ choice.label }}
              </button>
            </div>
          </div>

          <div v-if="sourcesQuery.data.value && sourcesQuery.data.value.length > 0">
            <span class="text-sm text-slate-300">{{ t('screen.tokens.field.source_filter_label') }}</span>
            <p class="text-xs text-slate-500">{{ t('screen.tokens.field.source_filter_hint') }}</p>
            <div class="mt-1 max-h-32 space-y-1 overflow-y-auto rounded border border-slate-800 p-2">
              <label v-for="source in sourcesQuery.data.value" :key="source.id" class="flex items-center gap-2">
                <input
                  type="checkbox"
                  :checked="form.sourceIdFilter.includes(source.id)"
                  @change="toggleSourceFilter(source.id)"
                />
                <span class="text-xs text-slate-300">{{ source.name }}</span>
              </label>
            </div>
          </div>

          <p v-if="formError" class="rounded border border-red-700 bg-red-900/40 px-3 py-2 text-sm text-red-200">
            {{ formError }}
          </p>

          <div class="flex justify-end gap-2">
            <button
              type="button"
              class="rounded border border-slate-700 px-3 py-1.5 text-sm text-slate-300 hover:bg-slate-800"
              @click="closeReveal"
            >
              {{ t('action.cancel') }}
            </button>
            <button
              type="submit"
              :disabled="!canSubmit || createMutation.isPending.value"
              class="rounded bg-blue-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-blue-500 disabled:cursor-not-allowed disabled:bg-blue-900"
            >
              {{ createMutation.isPending.value ? t('state.saving') : t('screen.tokens.create.submit_btn') }}
            </button>
          </div>
        </form>
      </div>
    </div>
  </div>
</template>
