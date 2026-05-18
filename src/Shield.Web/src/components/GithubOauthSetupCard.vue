<script setup lang="ts">
import { computed, onUnmounted, ref } from 'vue'
import { useQueryClient } from '@tanstack/vue-query'
import { useI18n } from 'vue-i18n'
import { CheckCircle2, ChevronRight, Copy, ExternalLink, Github, Loader2 } from 'lucide-vue-next'

import {
  useDisconnectOAuth,
  useGithubDevicePoll,
  useGithubDeviceStart,
  useOAuthStatus,
  useStartOAuth,
} from '@/queries/oauth'
import { useSettingsQuery, useUpdateSettings } from '@/queries/settings'
import { useToasts } from '@/stores/toast'
import type { OAuthStartResponse } from '@/types/api'

const props = withDefaults(defineProps<{
  // When true, the card auto-triggers the connect flow as soon as device flow starts (or after
  // credentials save in the Advanced auth-code path). Onboarding wants this; Settings stays manual.
  autoConnectAfterSave?: boolean
}>(), { autoConnectAfterSave: false })

const emit = defineEmits<{ connected: [] }>()

const { t } = useI18n()
const { push } = useToasts()
const queryClient = useQueryClient()

const { data: settings, isLoading: settingsLoading } = useSettingsQuery()
const update = useUpdateSettings()
const { data: oauthStatus, refetch: refetchStatus } = useOAuthStatus('Github')
const start = useStartOAuth('Github')
const deviceStart = useGithubDeviceStart()
const devicePoll = useGithubDevicePoll()
const disconnect = useDisconnectOAuth('Github')

const clientId = ref('')
const clientSecret = ref('')
const showAdvanced = ref(false)

const githubConfigured = computed(() => settings.value?.github?.configured ?? false)
const githubSecretMasked = computed(() => settings.value?.github?.clientSecretMasked ?? null)
const connected = computed(() => oauthStatus.value?.connected ?? false)
const accountLogin = computed(() => oauthStatus.value?.accountLogin ?? null)
const deviceFlowAvailable = computed(() => oauthStatus.value?.deviceFlowAvailable ?? true)

const redirectUri = computed(() => `${window.location.origin}/api/oauth/github/callback`)
const callbackSetupUrl = 'https://github.com/settings/applications/new'

const canSave = computed(() => {
  if (!githubConfigured.value)
    return clientId.value.trim().length > 0 && clientSecret.value.trim().length > 0
  return clientId.value.trim().length > 0 || clientSecret.value.trim().length > 0
})

// Device flow state.
const deviceFlowActive = ref(false)
const deviceUserCode = ref<string | null>(null)
const deviceVerificationUri = ref<string | null>(null)
const devicePollHandle = ref<number | null>(null)

// Auth-code popup state.
const popupPolling = ref(false)
const pollHandle = ref<number | null>(null)
const popupRef = ref<Window | null>(null)

onUnmounted(() => {
  stopAuthCodePolling()
  stopDeviceFlow()
})

function stopAuthCodePolling(): void {
  if (pollHandle.value !== null) {
    window.clearInterval(pollHandle.value)
    pollHandle.value = null
  }
  popupPolling.value = false
  popupRef.value = null
}

function stopDeviceFlow(): void {
  if (devicePollHandle.value !== null) {
    // Now a setTimeout id (per-tick reschedule) so the slow_down spec can bump the gap.
    window.clearTimeout(devicePollHandle.value)
    devicePollHandle.value = null
  }
  deviceFlowActive.value = false
}

function buildPatchBody() {
  const current = settings.value
  if (!current)
    return null
  return {
    singleUserMode: current.singleUserMode,
    openApiEnabled: current.openApiEnabled,
    oidcEnabled: current.oidcEnabled,
    oidcIssuer: current.oidcIssuer ?? null,
    oidcClientId: current.oidcClientId ?? null,
    oidcClientSecret: null,
    alertSeverityFloor: current.alertSeverityFloor,
    retentionDays: current.retentionDays,
    github: {
      clientId: clientId.value || (current.github?.clientId ?? null),
      clientSecret: clientSecret.value || null,
      scopes: current.github?.scopes ?? 'read:user user:email repo read:org',
    },
    slack: null,
    google: null,
  }
}

async function copyToClipboard(value: string): Promise<void> {
  try {
    await navigator.clipboard.writeText(value)
    push('success', t('screen.oauth_setup.copied_toast'))
  }
  catch {
    // Clipboard blocked (insecure context etc) — best-effort no-op.
  }
}

async function onDeviceConnect(): Promise<void> {
  // Device flow uses the baked-in Shield OAuth App — no per-install registration required.
  stopDeviceFlow()
  try {
    const response = await deviceStart.mutateAsync()
    deviceUserCode.value = response.userCode
    // Prefer verification_uri_complete (pre-fills the code on github.com so the user just
    // clicks Authorize) and fall back to the bare URL if GitHub didn't return the complete
    // form for this App configuration.
    const targetUri = response.verificationUriComplete || response.verificationUri
    deviceVerificationUri.value = targetUri
    deviceFlowActive.value = true

    window.open(targetUri, '_blank', 'noopener,noreferrer')
    // Belt-and-braces: still copy the code in case the user lands on a stale tab without
    // the prefill, or wants to verify what's about to be submitted.
    await copyToClipboard(response.userCode)

    // Per RFC 8628: when GitHub returns `slow_down` we MUST bump the polling interval by
    // +5s in addition to the server-advertised interval. Failing to do this keeps GitHub
    // in throttle mode forever — it has the token ready but refuses to hand it over until
    // we slow down. setTimeout-with-self-reschedule lets us adjust the gap each tick.
    let currentIntervalMs = Math.max(2_000, response.interval * 1_000)
    const deadline = Date.now() + response.expiresIn * 1_000

    const tick = async (): Promise<void> => {
      if (Date.now() > deadline) {
        stopDeviceFlow()
        push('error', t('screen.oauth_setup.device_expired'))
        return
      }
      try {
        const poll = await devicePoll.mutateAsync(response.flowId)
        if (poll.status === 'ok') {
          stopDeviceFlow()
          await refetchStatus()
          await queryClient.invalidateQueries({ queryKey: ['onboarding', 'status'] })
          await queryClient.invalidateQueries({ queryKey: ['oauth', 'github', 'repos'] })
          push('success', t('screen.oauth_setup.connected_toast'))
          emit('connected')
          return
        }
        if (poll.status === 'expired') {
          stopDeviceFlow()
          push('error', t('screen.oauth_setup.device_expired'))
          return
        }
        if (poll.status === 'denied') {
          stopDeviceFlow()
          push('error', t('screen.oauth_setup.device_denied'))
          return
        }
        if (poll.status === 'slow_down')
          currentIntervalMs += 5_000
      }
      catch { /* transient, keep polling */ }

      // Re-schedule under the (possibly updated) interval. devicePollHandle stores the
      // timeout id so stopDeviceFlow() can cancel a pending tick.
      devicePollHandle.value = window.setTimeout(() => { void tick() }, currentIntervalMs)
    }

    devicePollHandle.value = window.setTimeout(() => { void tick() }, currentIntervalMs)
  }
  catch {
    stopDeviceFlow()
    push('error', t('screen.oauth_setup.connect_failed'))
  }
}

async function onSave(): Promise<void> {
  const body = buildPatchBody()
  if (!body)
    return
  try {
    await update.mutateAsync(body)
    clientSecret.value = ''
    push('success', t('screen.oauth_setup.saved_toast'))
    await queryClient.invalidateQueries({ queryKey: ['onboarding', 'status'] })
    if (props.autoConnectAfterSave && !connected.value)
      void onAuthCodeConnect()
  }
  catch {
    push('error', t('screen.oauth_setup.save_failed'))
  }
}

async function onAuthCodeConnect(): Promise<void> {
  if (!githubConfigured.value)
    return
  try {
    const response: OAuthStartResponse = await start.mutateAsync()
    const popup = window.open(
      response.authorizationUrl,
      'shield-github-oauth',
      'width=600,height=720,menubar=no,toolbar=no,location=yes,status=no',
    )
    if (!popup) {
      push('error', t('screen.oauth_setup.popup_blocked'))
      return
    }
    popupRef.value = popup
    popupPolling.value = true
    let elapsed = 0
    const maxMs = 60_000
    const tickMs = 1_000
    pollHandle.value = window.setInterval(async () => {
      elapsed += tickMs
      const popupClosed = popup.closed
      try {
        const fresh = await refetchStatus()
        if (fresh.data?.connected) {
          stopAuthCodePolling()
          try { popup.close() } catch { /* cross-origin during GitHub round-trip is expected */ }
          await queryClient.invalidateQueries({ queryKey: ['onboarding', 'status'] })
          await queryClient.invalidateQueries({ queryKey: ['oauth', 'github', 'repos'] })
          push('success', t('screen.oauth_setup.connected_toast'))
          emit('connected')
          return
        }
      }
      catch { /* transient — keep polling */ }
      if (popupClosed && elapsed >= 3_000) {
        stopAuthCodePolling()
        return
      }
      if (elapsed >= maxMs) {
        stopAuthCodePolling()
        try { popup.close() } catch { /* ignore */ }
        push('error', t('screen.oauth_setup.connect_timeout'))
      }
    }, tickMs)
  }
  catch {
    push('error', t('screen.oauth_setup.connect_failed'))
  }
}

async function onDisconnect(): Promise<void> {
  if (!window.confirm(t('screen.oauth_setup.disconnect_confirm')))
    return
  try {
    await disconnect.mutateAsync()
    await queryClient.invalidateQueries({ queryKey: ['onboarding', 'status'] })
    push('success', t('screen.oauth_setup.disconnected_toast'))
  }
  catch {
    push('error', t('screen.oauth_setup.disconnect_failed'))
  }
}

defineExpose({ onDeviceConnect, onAuthCodeConnect })
</script>

<template>
  <article class="space-y-3 rounded-lg border border-slate-800 bg-slate-900 p-4">
    <header class="flex items-start gap-3">
      <Github class="mt-0.5 h-5 w-5 text-slate-200" />
      <div class="min-w-0 flex-1">
        <h3 class="text-sm font-semibold text-slate-100">{{ t('screen.oauth_setup.github_title') }}</h3>
        <p class="mt-0.5 text-xs text-slate-400">{{ t('screen.oauth_setup.github_intro_device') }}</p>
      </div>
      <span
        v-if="connected"
        class="inline-flex items-center gap-1 rounded border border-emerald-700/60 bg-emerald-950/40 px-2 py-0.5 text-xs text-emerald-300"
      >
        <CheckCircle2 class="h-3 w-3" />
        {{ accountLogin ? t('screen.oauth_setup.status_connected', { login: accountLogin }) : t('screen.oauth_setup.status_configured') }}
      </span>
    </header>

    <div v-if="!connected && deviceFlowAvailable && !deviceFlowActive" class="flex flex-wrap items-center gap-2">
      <button
        type="button"
        :disabled="deviceStart.isPending.value"
        class="inline-flex items-center gap-2 rounded bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-500 disabled:bg-blue-900 disabled:text-slate-300"
        @click="onDeviceConnect"
      >
        <Loader2 v-if="deviceStart.isPending.value" class="h-4 w-4 animate-spin" />
        <Github v-else class="h-4 w-4" />
        {{ t('screen.oauth_setup.device_connect_btn') }}
      </button>
      <span class="text-xs text-slate-500">{{ t('screen.oauth_setup.device_connect_hint') }}</span>
    </div>

    <div
      v-if="deviceFlowActive && !connected"
      class="space-y-3 rounded border border-blue-700/50 bg-blue-950/30 p-3"
    >
      <p class="text-xs text-slate-300">{{ t('screen.oauth_setup.device_paste_intro') }}</p>
      <div class="flex items-center gap-2">
        <input
          :value="deviceUserCode ?? ''"
          readonly
          class="flex-1 rounded border border-slate-700 bg-slate-950 px-3 py-2 text-center font-mono text-lg tracking-widest text-emerald-300 focus:border-blue-500 focus:outline-none"
          @focus="(event) => (event.target as HTMLInputElement).select()"
        />
        <button
          type="button"
          class="inline-flex items-center gap-1 rounded border border-slate-700 px-2 py-2 text-xs text-slate-200 hover:bg-slate-800"
          @click="deviceUserCode && copyToClipboard(deviceUserCode)"
        >
          <Copy class="h-3 w-3" />
          {{ t('screen.oauth_setup.copy_btn') }}
        </button>
      </div>
      <div class="flex flex-wrap items-center gap-2 text-xs">
        <a
          v-if="deviceVerificationUri"
          :href="deviceVerificationUri"
          target="_blank"
          rel="noopener"
          class="inline-flex items-center gap-1 rounded border border-blue-500/50 bg-blue-950/40 px-3 py-1.5 text-blue-200 hover:bg-blue-900/40"
        >
          <ExternalLink class="h-3 w-3" />
          {{ t('screen.oauth_setup.device_open_verification') }}
        </a>
        <span class="text-slate-400">
          <Loader2 class="mr-1 inline h-3 w-3 animate-spin" />
          {{ t('screen.oauth_setup.device_waiting') }}
        </span>
        <button
          type="button"
          class="ml-auto rounded border border-slate-700 px-2 py-1 text-slate-300 hover:bg-slate-800"
          @click="stopDeviceFlow"
        >
          {{ t('action.cancel') }}
        </button>
      </div>
    </div>

    <button
      v-if="connected"
      type="button"
      :disabled="disconnect.isPending.value"
      class="inline-flex items-center gap-1 rounded border border-red-900/50 px-3 py-1.5 text-sm text-red-300 hover:bg-red-950/40 disabled:opacity-50"
      @click="onDisconnect"
    >
      {{ disconnect.isPending.value ? '…' : t('screen.oauth_setup.disconnect_btn') }}
    </button>

    <details
      v-if="!connected"
      class="border-t border-slate-800 pt-3"
      :open="showAdvanced || githubConfigured"
      @toggle="(event) => (showAdvanced = (event.target as HTMLDetailsElement).open)"
    >
      <summary class="flex cursor-pointer items-center gap-1 text-xs font-medium text-slate-400 hover:text-slate-200">
        <ChevronRight class="h-3 w-3 transition-transform" :class="{ 'rotate-90': showAdvanced || githubConfigured }" />
        {{ t('screen.oauth_setup.advanced_summary') }}
      </summary>

      <div class="mt-3 space-y-3">
        <p class="text-xs text-slate-500">{{ t('screen.oauth_setup.advanced_intro') }}</p>

        <a
          :href="callbackSetupUrl"
          target="_blank"
          rel="noopener"
          class="inline-flex items-center gap-1 text-xs text-blue-400 hover:text-blue-300 hover:underline"
        >
          <ExternalLink class="h-3 w-3" />
          {{ t('screen.oauth_setup.create_app_link') }}
        </a>

        <label class="block">
          <span class="text-xs text-slate-400">{{ t('screen.oauth_setup.redirect_uri_label') }}</span>
          <div class="mt-1 flex items-stretch gap-2">
            <input
              id="shield-github-redirect-uri"
              :value="redirectUri"
              readonly
              class="flex-1 rounded border border-slate-700 bg-slate-950 px-3 py-2 font-mono text-xs text-slate-200 focus:border-blue-500 focus:outline-none"
              @focus="(event) => (event.target as HTMLInputElement).select()"
            />
            <button
              type="button"
              class="inline-flex items-center gap-1 rounded border border-slate-700 px-2 py-2 text-xs text-slate-200 hover:bg-slate-800"
              @click="copyToClipboard(redirectUri)"
            >
              <Copy class="h-3 w-3" />
              {{ t('screen.oauth_setup.copy_btn') }}
            </button>
          </div>
          <p class="mt-1 text-[11px] text-slate-500">{{ t('screen.oauth_setup.redirect_uri_hint') }}</p>
        </label>

        <label class="block">
          <span class="text-xs text-slate-400">{{ t('screen.oauth_setup.client_id_label') }}</span>
          <input
            v-model="clientId"
            name="shield-gh-client-id-inline"
            autocomplete="off"
            data-1p-ignore="true"
            data-lpignore="true"
            :placeholder="settings?.github?.clientId || t('screen.oauth_setup.client_id_placeholder')"
            :disabled="settingsLoading || update.isPending.value"
            class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none disabled:opacity-50"
          />
        </label>

        <label class="block">
          <span class="text-xs text-slate-400">
            {{ t('screen.oauth_setup.client_secret_label') }}
            <span v-if="githubSecretMasked" class="ml-2 font-mono text-slate-500">
              {{ t('screen.oauth_setup.client_secret_masked_prefix') }}: {{ githubSecretMasked }}
            </span>
          </span>
          <input
            v-model="clientSecret"
            type="password"
            name="shield-gh-client-secret-inline"
            autocomplete="new-password"
            data-1p-ignore="true"
            data-lpignore="true"
            :placeholder="githubSecretMasked
              ? t('screen.oauth_setup.client_secret_placeholder')
              : t('screen.oauth_setup.client_secret_required_placeholder')"
            :disabled="settingsLoading || update.isPending.value"
            class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none disabled:opacity-50"
          />
        </label>

        <div class="flex flex-wrap items-center gap-2">
          <button
            type="button"
            :disabled="!canSave || update.isPending.value || settingsLoading"
            class="inline-flex items-center gap-1 rounded bg-slate-700 px-3 py-1.5 text-sm font-medium text-white hover:bg-slate-600 disabled:bg-slate-800 disabled:text-slate-500"
            @click="onSave"
          >
            <Loader2 v-if="update.isPending.value" class="h-3.5 w-3.5 animate-spin" />
            {{
              update.isPending.value
                ? t('screen.oauth_setup.saving')
                : (githubConfigured ? t('screen.oauth_setup.save_btn') : t('screen.oauth_setup.save_and_connect_btn'))
            }}
          </button>

          <button
            v-if="githubConfigured"
            type="button"
            :disabled="start.isPending.value || popupPolling"
            class="inline-flex items-center gap-1 rounded border border-blue-500/50 bg-blue-950/40 px-3 py-1.5 text-sm font-medium text-blue-200 hover:bg-blue-900/40 disabled:opacity-50"
            @click="onAuthCodeConnect"
          >
            <Loader2 v-if="start.isPending.value || popupPolling" class="h-3.5 w-3.5 animate-spin" />
            {{
              start.isPending.value
                ? t('screen.oauth_setup.connecting')
                : popupPolling
                  ? t('screen.oauth_setup.waiting_for_connect')
                  : t('screen.oauth_setup.connect_btn')
            }}
          </button>
        </div>
      </div>
    </details>
  </article>
</template>
