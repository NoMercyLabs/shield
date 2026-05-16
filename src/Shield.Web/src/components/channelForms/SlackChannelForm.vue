<script setup lang="ts">
import { computed, ref, watch } from 'vue'

import { useOAuthStatus, useSlackChannels } from '@/queries/oauth'

const props = defineProps<{ initial?: Record<string, unknown> | null }>()
const emit = defineEmits<{ change: [json: string, valid: boolean] }>()

const status = useOAuthStatus('Slack')
const oauthConnected = computed(() => status.data.value?.connected === true)

// Lazy-load the channel list only when OAuth is wired.
const slackChannels = useSlackChannels(true)
const channels = computed(() => slackChannels.data.value?.channels ?? [])

// `initial` may carry either ChannelId (OAuth) or WebhookUrl (legacy).
const channelId = ref<string>(typeof props.initial?.channelId === 'string' ? props.initial.channelId as string : '')
const webhookUrl = ref<string>(typeof props.initial?.webhookUrl === 'string' ? props.initial.webhookUrl as string : '')

// When OAuth is connected, the dropdown wins. Operators can flip back to the
// webhook URL field by toggling the radio at the top.
type Mode = 'oauth' | 'webhook'
const mode = ref<Mode>(channelId.value || oauthConnected.value ? 'oauth' : 'webhook')

watch(oauthConnected, (connected, prev) => {
  // Only flip the default on first arrival of the status; don't trample the
  // user's explicit choice on re-fetch.
  if (prev === undefined && connected && !webhookUrl.value)
    mode.value = 'oauth'
})

const isValid = computed(() => {
  if (mode.value === 'oauth')
    return !!channelId.value
  if (!webhookUrl.value)
    return false
  try {
    const parsed = new URL(webhookUrl.value)
    return parsed.protocol === 'https:' || parsed.protocol === 'http:'
  }
  catch {
    return false
  }
})

function publish(): void {
  const payload = mode.value === 'oauth'
    ? { channelId: channelId.value }
    : { webhookUrl: webhookUrl.value }
  emit('change', JSON.stringify(payload), isValid.value)
}

watch([mode, channelId, webhookUrl], publish, { immediate: true })
</script>

<template>
  <div class="space-y-3">
    <div class="flex items-center gap-3 text-sm text-slate-300">
      <label class="flex items-center gap-1.5">
        <input
          v-model="mode"
          type="radio"
          value="oauth"
          :disabled="!oauthConnected"
          class="accent-blue-500"
        />
        Workspace channel
        <span v-if="!oauthConnected" class="text-xs text-slate-500">
          (connect Slack in Settings first)
        </span>
      </label>
      <label class="flex items-center gap-1.5">
        <input
          v-model="mode"
          type="radio"
          value="webhook"
          class="accent-blue-500"
        />
        Incoming Webhook URL
      </label>
    </div>

    <template v-if="mode === 'oauth'">
      <label class="block">
        <span class="text-sm text-slate-300">Channel</span>
        <select
          v-model="channelId"
          required
          class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
        >
          <option value="">— pick a channel —</option>
          <option
            v-for="channel in channels"
            :key="channel.id"
            :value="channel.id"
          >
            {{ channel.isPrivate ? '🔒' : '#' }} {{ channel.name }}
          </option>
        </select>
        <p v-if="slackChannels.isLoading.value" class="mt-1 text-xs text-slate-500">
          Loading channel list…
        </p>
        <p v-else-if="slackChannels.isError.value" class="mt-1 text-xs text-red-300">
          Slack API call failed. Confirm the workspace is still connected in Settings.
        </p>
      </label>
    </template>

    <template v-else>
      <label class="block">
        <span class="text-sm text-slate-300">Webhook URL</span>
        <input
          v-model="webhookUrl"
          type="url"
          required
          placeholder="https://hooks.slack.com/services/…"
          class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
        />
      </label>
    </template>
  </div>
</template>
