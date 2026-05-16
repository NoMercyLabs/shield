<script setup lang="ts">
import { computed, ref } from 'vue'
import { Bell, Check, Inbox, Loader2, Send } from 'lucide-vue-next'

import { useCreateChannelMutation, useTestSendMutation } from '@/queries/channels'
import { useOnboardingStatus } from '@/queries/onboarding'
import { useToasts } from '@/stores/toast'
import { ChannelType, Severity } from '@/types/api'

const emit = defineEmits<{ done: [] }>()

const { push } = useToasts()
const { data: status, refetch } = useOnboardingStatus()
const createChannel = useCreateChannelMutation()
const testSend = useTestSendMutation()

const webhookUrl = ref('')
const savedChannelId = ref<string | null>(null)
const testState = ref<'idle' | 'sending' | 'sent' | 'failed'>('idle')

const webhookValid = computed(() =>
  webhookUrl.value.startsWith('https://discord.com/api/webhooks/'),
)
const channelCount = computed(() => status.value?.channelCount ?? 0)

async function saveDiscord(): Promise<void> {
  if (!webhookValid.value) return
  try {
    const created = await createChannel.mutateAsync({
      type: ChannelType.Discord,
      name: 'Discord (onboarding)',
      configJson: JSON.stringify({ webhookUrl: webhookUrl.value }),
      minSeverity: Severity.Low,
      enabled: true,
    })
    savedChannelId.value = created.id
    push('success', 'Discord channel saved.')
    await refetch()
  }
  catch (error) {
    push('error', error instanceof Error ? error.message : 'Failed to save Discord channel.')
  }
}

async function runTestSend(): Promise<void> {
  if (!savedChannelId.value) return
  testState.value = 'sending'
  try {
    await testSend.mutateAsync(savedChannelId.value)
    testState.value = 'sent'
    push('success', 'Test alert sent — check Discord.')
  }
  catch (error) {
    testState.value = 'failed'
    push('error', error instanceof Error ? error.message : 'Test send failed.')
  }
}

function skipChannel(): void {
  emit('done')
}

function finishChannel(): void {
  emit('done')
}
</script>

<template>
  <section class="space-y-5">
    <div>
      <h1 class="text-2xl font-semibold">Add an alert channel</h1>
      <p class="mt-1 text-sm text-slate-400">
        Where should Shield ping you when a new vulnerability lands? Discord is the fastest path —
        paste a webhook URL and test it in one click.
      </p>
    </div>

    <div class="rounded-lg border border-slate-800 bg-slate-900 p-4 space-y-3">
      <div class="flex items-center gap-2 text-sm font-medium text-slate-100">
        <Bell class="h-4 w-4 text-blue-400" />
        Discord webhook
      </div>

      <label class="block">
        <span class="text-xs text-slate-400">Webhook URL</span>
        <input
          v-model="webhookUrl"
          type="url"
          :disabled="savedChannelId !== null"
          placeholder="https://discord.com/api/webhooks/…"
          class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none disabled:opacity-60"
        />
        <span
          v-if="webhookUrl && !webhookValid"
          class="mt-1 block text-xs text-amber-300"
        >
          Must start with <code>https://discord.com/api/webhooks/</code>
        </span>
        <span class="mt-1 block text-xs text-slate-500">
          Create one in Discord under Server Settings → Integrations → Webhooks.
        </span>
      </label>

      <div class="flex flex-wrap items-center gap-2">
        <button
          v-if="!savedChannelId"
          type="button"
          :disabled="!webhookValid || createChannel.isPending.value"
          class="inline-flex items-center gap-1.5 rounded bg-blue-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-blue-500 disabled:opacity-50"
          @click="saveDiscord"
        >
          <Loader2 v-if="createChannel.isPending.value" class="h-3.5 w-3.5 animate-spin" />
          <Check v-else class="h-3.5 w-3.5" />
          Save channel
        </button>

        <button
          v-else
          type="button"
          :disabled="testState === 'sending'"
          class="inline-flex items-center gap-1.5 rounded border border-slate-700 px-3 py-1.5 text-sm text-slate-100 hover:bg-slate-800 disabled:opacity-50"
          @click="runTestSend"
        >
          <Loader2 v-if="testState === 'sending'" class="h-3.5 w-3.5 animate-spin" />
          <Send v-else class="h-3.5 w-3.5" />
          {{ testState === 'sent' ? 'Send again' : 'Test send' }}
        </button>

        <button
          v-if="savedChannelId"
          type="button"
          class="inline-flex items-center gap-1.5 rounded border border-emerald-700 bg-emerald-900/30 px-3 py-1.5 text-sm text-emerald-100 hover:bg-emerald-900/50"
          @click="finishChannel"
        >
          Continue
        </button>
      </div>

      <p v-if="testState === 'sent'" class="text-xs text-emerald-300">
        Test alert dispatched — confirm it landed in your Discord channel.
      </p>
      <p v-else-if="testState === 'failed'" class="text-xs text-red-300">
        Test send failed. The webhook may be invalid or the channel deleted.
      </p>
    </div>

    <button
      type="button"
      class="flex w-full items-start gap-3 rounded-lg border border-slate-800 bg-slate-900 p-4 text-left hover:border-slate-700 hover:bg-slate-800/30"
      @click="skipChannel"
    >
      <Inbox class="mt-0.5 h-5 w-5 text-slate-400" />
      <div class="flex-1 space-y-1">
        <p class="text-sm font-medium text-slate-200">Skip — I'll use the in-app inbox</p>
        <p class="text-xs text-slate-500">
          Shield ships with an inbox channel by default; alerts will be visible in the Findings
          page even without an external channel.
        </p>
      </div>
    </button>

    <p v-if="channelCount > 0" class="text-xs text-emerald-300">
      You already have {{ channelCount }} channel{{ channelCount === 1 ? '' : 's' }} configured.
    </p>
  </section>
</template>
