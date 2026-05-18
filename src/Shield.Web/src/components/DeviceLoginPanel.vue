<script setup lang="ts">
import { onUnmounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { Copy, ExternalLink, Loader2 } from 'lucide-vue-next'

import { api } from '@/lib/api'
import { bootstrapAuth } from '@/stores/auth'
import type { ExternalLoginIdentity, ExternalLoginPollResponse, ExternalLoginStartResponse } from '@/types/external-login'

// Provider-agnostic device-code panel. Renders the user_code, opens the verification URL,
// polls the server-issued flow until the upstream auth completes, then either reloads
// /me + emits `signed-in`, or emits `needs-invite` with the captured identity so the
// caller can show the "ask the admin to invite you" screen.
const props = defineProps<{
  providerKey: string
  displayName: string
  returnPath: string
}>()

const emit = defineEmits<{
  'signed-in': []
  'needs-invite': [payload: { identity: ExternalLoginIdentity, acceptanceTicket: string }]
  'cancel': []
}>()

const { t } = useI18n()

const starting = ref(false)
const userCode = ref<string | null>(null)
const verificationUri = ref<string | null>(null)
const pollHandle = ref<number | null>(null)
const errorKey = ref<string | null>(null)

onUnmounted(() => stopPolling())

function stopPolling(): void {
  if (pollHandle.value !== null) {
    window.clearTimeout(pollHandle.value)
    pollHandle.value = null
  }
}

async function copyCode(): Promise<void> {
  if (!userCode.value)
    return
  try {
    await navigator.clipboard.writeText(userCode.value)
  }
  catch { /* clipboard blocked in insecure contexts — best-effort */ }
}

async function startFlow(): Promise<void> {
  stopPolling()
  errorKey.value = null
  starting.value = true
  try {
    const { data } = await api.post<ExternalLoginStartResponse>(
      `/auth/external/${props.providerKey}/start`,
      { returnPath: props.returnPath },
    )
    userCode.value = data.userCode
    const targetUri = data.verificationUriComplete || data.verificationUri
    verificationUri.value = targetUri
    window.open(targetUri, '_blank', 'noopener,noreferrer')
    void copyCode()

    let currentIntervalMs = Math.max(2_000, data.interval * 1_000)
    const deadline = Date.now() + data.expiresIn * 1_000
    const flowId = data.flowId

    const tick = async (): Promise<void> => {
      if (Date.now() > deadline) {
        stopPolling()
        errorKey.value = 'screen.signin.external.error_expired'
        return
      }
      try {
        const poll = await api.post<ExternalLoginPollResponse>(
          `/auth/external/${props.providerKey}/poll`,
          { flowId },
        )
        const body = poll.data
        if (body.status === 'ok') {
          stopPolling()
          if (body.needsInvite && body.identity && body.acceptanceTicket) {
            emit('needs-invite', { identity: body.identity, acceptanceTicket: body.acceptanceTicket })
            return
          }
          // Cookie is set on the response; refresh /me so the rest of the SPA sees the user.
          await bootstrapAuth()
          emit('signed-in')
          return
        }
        if (body.status === 'slow_down')
          currentIntervalMs += 5_000
        // 'pending' falls through to re-schedule
      }
      catch (err: unknown) {
        // axios error: read the status off response. 410 = expired, 403 = denied,
        // 502 = upstream transport. Anything else: keep trying.
        const status = (err as { response?: { status?: number, data?: { status?: string } } })?.response?.status
        if (status === 410) {
          stopPolling()
          errorKey.value = 'screen.signin.external.error_expired'
          return
        }
        if (status === 403) {
          stopPolling()
          errorKey.value = 'screen.signin.external.error_denied'
          return
        }
        if (status === 502) {
          stopPolling()
          errorKey.value = 'screen.signin.external.error_provider'
          return
        }
        if (status === 429) {
          // Rate-limited — back off hard then retry once.
          currentIntervalMs = Math.max(currentIntervalMs, 10_000)
        }
      }
      pollHandle.value = window.setTimeout(() => { void tick() }, currentIntervalMs)
    }

    pollHandle.value = window.setTimeout(() => { void tick() }, currentIntervalMs)
  }
  catch {
    errorKey.value = 'screen.signin.external.error_start'
  }
  finally {
    starting.value = false
  }
}

function onCancel(): void {
  stopPolling()
  userCode.value = null
  verificationUri.value = null
  errorKey.value = null
  emit('cancel')
}

defineExpose({ startFlow })
</script>

<template>
  <section class="space-y-3 rounded-lg border border-blue-700/50 bg-blue-950/30 p-3">
    <p class="text-xs text-slate-300">
      {{ t('screen.signin.external.paste_intro', { provider: displayName }) }}
    </p>
    <div class="flex items-center gap-2">
      <input
        :value="userCode ?? ''"
        readonly
        class="flex-1 rounded border border-slate-700 bg-slate-950 px-3 py-2 text-center font-mono text-lg tracking-widest text-emerald-300 focus:border-blue-500 focus:outline-none"
        @focus="(event) => (event.target as HTMLInputElement).select()"
      />
      <button
        type="button"
        class="inline-flex items-center gap-1 rounded border border-slate-700 px-2 py-2 text-xs text-slate-200 hover:bg-slate-800"
        @click="copyCode"
      >
        <Copy class="h-3 w-3" />
        {{ t('action.copy') }}
      </button>
    </div>
    <div class="flex flex-wrap items-center gap-2 text-xs">
      <a
        v-if="verificationUri"
        :href="verificationUri"
        target="_blank"
        rel="noopener"
        class="inline-flex items-center gap-1 rounded border border-blue-500/50 bg-blue-950/40 px-3 py-1.5 text-blue-200 hover:bg-blue-900/40"
      >
        <ExternalLink class="h-3 w-3" />
        {{ t('screen.signin.external.open_verification') }}
      </a>
      <span class="text-slate-400">
        <Loader2 class="mr-1 inline h-3 w-3 animate-spin" />
        {{ t('screen.signin.external.waiting') }}
      </span>
      <button
        type="button"
        class="ml-auto rounded border border-slate-700 px-2 py-1 text-slate-300 hover:bg-slate-800"
        @click="onCancel"
      >
        {{ t('action.cancel') }}
      </button>
    </div>
    <p v-if="errorKey" class="rounded border border-red-700 bg-red-900/40 px-3 py-2 text-sm text-red-200">
      {{ t(errorKey) }}
    </p>
  </section>
</template>
