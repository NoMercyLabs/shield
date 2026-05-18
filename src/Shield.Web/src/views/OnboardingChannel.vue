<script setup lang="ts">
import { computed, ref } from 'vue'
import { Bell, Check, Inbox, Loader2, Send } from 'lucide-vue-next'
import { useI18n } from 'vue-i18n'

import { useCreateChannelMutation, useTestSendMutation } from '@/queries/channels'
import { useOnboardingStatus } from '@/queries/onboarding'
import { useToasts } from '@/stores/toast'
import { ChannelType, Severity } from '@/types/api'

const emit = defineEmits<{ done: [] }>()

const { t } = useI18n()
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
      name: t('onboarding.discord_channel_name'),
      configJson: JSON.stringify({ webhookUrl: webhookUrl.value }),
      minSeverity: Severity.Low,
      enabled: true,
    })
    savedChannelId.value = created.id
    push('success', t('onboarding.discord_saved_toast'))
    await refetch()
  }
  catch (error) {
    push('error', error instanceof Error ? error.message : t('onboarding.discord_save_error'))
  }
}

async function runTestSend(): Promise<void> {
  if (!savedChannelId.value) return
  testState.value = 'sending'
  try {
    await testSend.mutateAsync(savedChannelId.value)
    testState.value = 'sent'
    push('success', t('onboarding.discord_test_sent'))
  }
  catch (error) {
    testState.value = 'failed'
    push('error', error instanceof Error ? error.message : t('onboarding.discord_test_error'))
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
      <h1 class="text-2xl font-semibold">{{ t('onboarding.channel_title') }}</h1>
      <p class="mt-1 text-sm text-slate-400">{{ t('onboarding.channel_subtitle') }}</p>
    </div>

    <div class="rounded-lg border border-slate-800 bg-slate-900 p-4 space-y-3">
      <div class="flex items-center gap-2 text-sm font-medium text-slate-100">
        <Bell class="h-4 w-4 text-blue-400" />
        {{ t('onboarding.discord_webhook_label') }}
      </div>

      <label class="block">
        <span class="text-xs text-slate-400">{{ t('onboarding.webhook_url_label') }}</span>
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
          {{ t('onboarding.webhook_url_hint') }}
        </span>
        <span class="mt-1 block text-xs text-slate-500">
          {{ t('onboarding.webhook_url_help') }}
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
          {{ t('onboarding.save_channel_btn') }}
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
          {{ testState === 'sent' ? t('onboarding.send_again_btn') : t('onboarding.test_send_btn') }}
        </button>

        <button
          v-if="savedChannelId"
          type="button"
          class="inline-flex items-center gap-1.5 rounded border border-emerald-700 bg-emerald-900/30 px-3 py-1.5 text-sm text-emerald-100 hover:bg-emerald-900/50"
          @click="finishChannel"
        >
          {{ t('action.continue') }}
        </button>
      </div>

      <p v-if="testState === 'sent'" class="text-xs text-emerald-300">{{ t('onboarding.test_sent_confirm') }}</p>
      <p v-else-if="testState === 'failed'" class="text-xs text-red-300">{{ t('onboarding.test_send_failed') }}</p>
    </div>

    <button
      type="button"
      class="flex w-full items-start gap-3 rounded-lg border border-slate-800 bg-slate-900 p-4 text-left hover:border-slate-700 hover:bg-slate-800/30"
      @click="skipChannel"
    >
      <Inbox class="mt-0.5 h-5 w-5 text-slate-400" />
      <div class="flex-1 space-y-1">
        <p class="text-sm font-medium text-slate-200">{{ t('onboarding.skip_inbox_title') }}</p>
        <p class="text-xs text-slate-500">{{ t('onboarding.skip_inbox_body') }}</p>
      </div>
    </button>

    <p v-if="channelCount > 0" class="text-xs text-emerald-300">
      {{ t('onboarding.channels_already_configured', channelCount) }}
    </p>
  </section>
</template>
