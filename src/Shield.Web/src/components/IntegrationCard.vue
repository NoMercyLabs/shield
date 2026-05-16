<script setup lang="ts">
import { computed } from 'vue'

import { useDisconnectOAuth, useOAuthStatus, useStartOAuth } from '@/queries/oauth'
import type { OAuthProviderName } from '@/types/api'
import { useToasts } from '@/stores/toast'

const props = defineProps<{
  provider: OAuthProviderName
  label: string
  description: string
  configured: boolean
}>()

const { push } = useToasts()
const { data, isLoading } = useOAuthStatus(props.provider)
const start = useStartOAuth(props.provider)
const disconnect = useDisconnectOAuth(props.provider)

const connected = computed(() => data.value?.connected ?? false)
const accountLogin = computed(() => data.value?.accountLogin ?? null)
const scopes = computed(() => data.value?.scopes ?? null)

async function onConnect(): Promise<void> {
  if (!props.configured) {
    push('error', `Set ${props.label} client id and secret first.`)
    return
  }
  try {
    const response = await start.mutateAsync()
    window.location.href = response.authorizationUrl
  }
  catch {
    push('error', `Failed to start ${props.label} OAuth flow.`)
  }
}

async function onDisconnect(): Promise<void> {
  if (!window.confirm(`Disconnect ${props.label}? Future scans/alerts will fall back to legacy config.`))
    return
  try {
    await disconnect.mutateAsync()
    push('success', `${props.label} disconnected.`)
  }
  catch {
    push('error', `Failed to disconnect ${props.label}.`)
  }
}
</script>

<template>
  <article class="space-y-2 rounded-lg border border-slate-800 bg-slate-900 p-4">
    <header class="flex items-center justify-between gap-3">
      <div class="min-w-0">
        <h3 class="text-sm font-medium text-slate-200">{{ label }}</h3>
        <p class="text-xs text-slate-500">{{ description }}</p>
      </div>
      <div class="flex items-center gap-2">
        <button
          v-if="connected"
          type="button"
          class="rounded border border-red-900/50 px-3 py-1.5 text-sm text-red-300 hover:bg-red-950/40 disabled:opacity-50"
          :disabled="disconnect.isPending.value"
          @click="onDisconnect"
        >
          {{ disconnect.isPending.value ? 'Disconnecting…' : 'Disconnect' }}
        </button>
        <button
          v-else
          type="button"
          class="rounded bg-blue-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-blue-500 disabled:bg-blue-900"
          :disabled="start.isPending.value || isLoading || !configured"
          @click="onConnect"
        >
          {{ start.isPending.value ? 'Redirecting…' : `Connect ${label}` }}
        </button>
      </div>
    </header>
    <div v-if="!configured" class="text-xs text-amber-300">
      Client ID/Secret not yet configured. Fill them in below before connecting.
    </div>
    <dl v-if="connected" class="grid grid-cols-2 gap-2 text-xs text-slate-400">
      <dt>Account</dt>
      <dd class="font-mono text-slate-200">{{ accountLogin || '(unknown)' }}</dd>
      <dt v-if="scopes">Scopes</dt>
      <dd v-if="scopes" class="break-all font-mono text-slate-300">{{ scopes }}</dd>
    </dl>
  </article>
</template>
