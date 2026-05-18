<script setup lang="ts">
import { computed, defineAsyncComponent, ref, watch } from 'vue'
import { Plus, Send, Trash2 } from 'lucide-vue-next'
import { useI18n } from 'vue-i18n'

import {
  useChannelsQuery,
  useCreateChannelMutation,
  useDeleteChannelMutation,
  useTestSendMutation,
  useUpdateChannelMutation,
} from '@/queries/channels'
import { enumName } from '@/stores/enums'
import { useToasts } from '@/stores/toast'
import { severityName } from '@/lib/format'
import { ChannelType, Severity } from '@/types/api'
import type { AlertChannel } from '@/types/api'

const DiscordChannelForm = defineAsyncComponent(() => import('@/components/channelForms/DiscordChannelForm.vue'))
const SlackChannelForm = defineAsyncComponent(() => import('@/components/channelForms/SlackChannelForm.vue'))
const NtfyChannelForm = defineAsyncComponent(() => import('@/components/channelForms/NtfyChannelForm.vue'))
const SmtpChannelForm = defineAsyncComponent(() => import('@/components/channelForms/SmtpChannelForm.vue'))
const WebhookChannelForm = defineAsyncComponent(() => import('@/components/channelForms/WebhookChannelForm.vue'))
const InboxChannelForm = defineAsyncComponent(() => import('@/components/channelForms/InboxChannelForm.vue'))

const FORM_FOR_TYPE: Record<ChannelType, ReturnType<typeof defineAsyncComponent>> = {
  [ChannelType.Discord]: DiscordChannelForm,
  [ChannelType.Slack]: SlackChannelForm,
  [ChannelType.Ntfy]: NtfyChannelForm,
  [ChannelType.Smtp]: SmtpChannelForm,
  [ChannelType.Webhook]: WebhookChannelForm,
  [ChannelType.Inbox]: InboxChannelForm,
}

const { t } = useI18n()
const { data, isLoading, isError } = useChannelsQuery()
const create = useCreateChannelMutation()
const update = useUpdateChannelMutation()
const remove = useDeleteChannelMutation()
const testSend = useTestSendMutation()
const { push } = useToasts()

const showForm = ref(false)
const editingId = ref<string | null>(null)
const type = ref<ChannelType>(ChannelType.Discord)
const name = ref('')
const minSeverity = ref<Severity>(Severity.High)
const enabled = ref(true)
const configJson = ref('{}')
const formValid = ref(false)
const initialForForm = ref<Record<string, unknown> | null>(null)

// `<component :is="...">` resolves through the lookup table so dropping in a
// new channel type only needs the import + map entry.
const activeFormComponent = computed(() => FORM_FOR_TYPE[type.value])

function resetForm(): void {
  editingId.value = null
  type.value = ChannelType.Discord
  name.value = ''
  minSeverity.value = Severity.High
  enabled.value = true
  configJson.value = '{}'
  formValid.value = false
  initialForForm.value = null
}

function startCreate(): void {
  resetForm()
  showForm.value = true
}

function startEdit(channel: AlertChannel): void {
  editingId.value = channel.id
  type.value = channel.type
  name.value = channel.name
  minSeverity.value = channel.minSeverity
  enabled.value = channel.enabled
  configJson.value = channel.configJson
  initialForForm.value = channel.parsedConfig ?? safeParse(channel.configJson)
  formValid.value = true
  showForm.value = true
}

function safeParse(raw: string): Record<string, unknown> | null {
  try {
    const parsed = JSON.parse(raw)
    return parsed && typeof parsed === 'object' ? parsed as Record<string, unknown> : null
  }
  catch {
    return null
  }
}

// Reset the initial-prop when the user switches type mid-create so the form
// doesn't get hydrated with another type's fields.
watch(type, (_, prev) => {
  if (prev !== undefined && editingId.value === null)
    initialForForm.value = null
})

function onFormChange(json: string, valid: boolean): void {
  configJson.value = json
  formValid.value = valid
}

async function onSubmit(): Promise<void> {
  if (!formValid.value || !name.value) {
    push('error', t('channels.validation_error'))
    return
  }
  try {
    if (editingId.value) {
      await update.mutateAsync({
        id: editingId.value,
        name: name.value,
        configJson: configJson.value,
        minSeverity: minSeverity.value,
        enabled: enabled.value,
      })
      push('success', t('channels.updated_toast', { name: name.value }))
    }
    else {
      await create.mutateAsync({
        type: type.value,
        name: name.value,
        configJson: configJson.value,
        minSeverity: minSeverity.value,
        enabled: enabled.value,
      })
      push('success', t('channels.added_toast', { name: name.value }))
    }
    showForm.value = false
    resetForm()
  }
  catch {
    push('error', t('channels.save_error'))
  }
}

async function onTest(id: string): Promise<void> {
  try {
    await testSend.mutateAsync(id)
    push('success', t('channels.test_success'))
  }
  catch {
    push('error', t('channels.test_error'))
  }
}

async function onDelete(channel: AlertChannel): Promise<void> {
  if (!confirm(t('channels.confirm_delete', { name: channel.name })))
    return
  try {
    await remove.mutateAsync(channel.id)
    push('success', t('channels.deleted_toast', { name: channel.name }))
    if (editingId.value === channel.id) {
      showForm.value = false
      resetForm()
    }
  }
  catch {
    push('error', t('channels.delete_error'))
  }
}

// Type-specific summary text below each channel row so the list reads as
// "Slack · #shield-alerts" instead of "{...}".
function summarise(channel: AlertChannel): string {
  const config = channel.parsedConfig
  if (!config) return ''
  switch (channel.type) {
    case ChannelType.Discord:
      return stringFrom(config, 'webhookUrl') ?? ''
    case ChannelType.Slack: {
      const channelId = stringFrom(config, 'channelId')
      if (channelId) return `channel ${channelId}`
      return stringFrom(config, 'webhookUrl') ?? ''
    }
    case ChannelType.Ntfy:
      return stringFrom(config, 'url') ?? ''
    case ChannelType.Smtp: {
      const from = stringFrom(config, 'from') ?? '?'
      const to = config.to
      const first = Array.isArray(to) && typeof to[0] === 'string' ? to[0] as string : '?'
      return `${from} → ${first}`
    }
    case ChannelType.Webhook: {
      const method = stringFrom(config, 'method') ?? 'POST'
      const url = stringFrom(config, 'url') ?? ''
      return `${method} ${url}`
    }
    case ChannelType.Inbox:
      return t('channels.inbox_summary')
    default:
      return ''
  }
}

function stringFrom(config: Record<string, unknown>, key: string): string | null {
  const value = config[key]
  return typeof value === 'string' ? value : null
}
</script>

<template>
  <div class="space-y-6">
    <header class="flex items-center justify-between">
      <h1 class="text-2xl font-semibold">{{ t('channels.title') }}</h1>
      <button
        type="button"
        class="flex items-center gap-1 rounded bg-blue-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-blue-500"
        @click="startCreate"
      >
        <Plus class="h-4 w-4" />
        {{ t('channels.add_btn') }}
      </button>
    </header>

    <form
      v-if="showForm"
      class="space-y-4 rounded-lg border border-slate-800 bg-slate-900 p-4"
      @submit.prevent="onSubmit"
    >
      <div class="grid grid-cols-2 gap-3">
        <label class="block">
          <span class="text-sm text-slate-300">{{ t('channels.form_name') }}</span>
          <input
            v-model="name"
            required
            class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
          />
        </label>
        <label class="block">
          <span class="text-sm text-slate-300">{{ t('channels.form_type') }}</span>
          <select
            v-model.number="type"
            :disabled="editingId !== null"
            class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none disabled:opacity-60"
          >
            <option :value="ChannelType.Discord">Discord</option>
            <option :value="ChannelType.Slack">Slack</option>
            <option :value="ChannelType.Ntfy">Ntfy</option>
            <option :value="ChannelType.Smtp">SMTP</option>
            <option :value="ChannelType.Webhook">Webhook</option>
            <option :value="ChannelType.Inbox">Inbox</option>
          </select>
        </label>
      </div>
      <div class="grid grid-cols-2 gap-3">
        <label class="block">
          <span class="text-sm text-slate-300">{{ t('channels.form_min_severity') }}</span>
          <select
            v-model.number="minSeverity"
            class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
          >
            <option :value="Severity.Critical">{{ t('severity.critical') }}</option>
            <option :value="Severity.High">{{ t('severity.high') }}</option>
            <option :value="Severity.Medium">{{ t('severity.medium') }}</option>
            <option :value="Severity.Low">{{ t('severity.low') }}</option>
          </select>
        </label>
        <label class="flex items-end gap-2 pb-2 text-sm text-slate-300">
          <input
            v-model="enabled"
            type="checkbox"
            class="accent-blue-500"
          />
          {{ t('channels.form_enabled') }}
        </label>
      </div>

      <div class="rounded border border-slate-800 bg-slate-950/60 p-3">
        <component
          :is="activeFormComponent"
          :initial="initialForForm"
          @change="onFormChange"
        />
      </div>

      <div class="flex items-center gap-2">
        <button
          type="submit"
          :disabled="!formValid || !name || create.isPending.value || update.isPending.value"
          class="rounded bg-blue-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-blue-500 disabled:bg-blue-900"
        >
          {{ create.isPending.value || update.isPending.value ? t('channels.saving') : (editingId ? t('channels.update_btn') : t('channels.save_btn')) }}
        </button>
        <button
          type="button"
          class="rounded border border-slate-700 px-3 py-1.5 text-sm text-slate-200 hover:bg-slate-800"
          @click="(showForm = false), resetForm()"
        >
          {{ t('channels.cancel_btn') }}
        </button>
      </div>
    </form>

    <p v-if="isLoading" class="text-sm text-slate-400">{{ t('channels.loading') }}</p>
    <p v-else-if="isError" class="text-sm text-red-300">{{ t('channels.error') }}</p>

    <div v-else-if="data && data.length" class="space-y-2">
      <article
        v-for="channel in data"
        :key="channel.id"
        class="flex items-start justify-between rounded-lg border border-slate-800 bg-slate-900 p-4"
      >
        <button
          type="button"
          class="flex-1 text-left"
          @click="startEdit(channel)"
        >
          <p class="text-sm font-medium">{{ channel.name }}</p>
          <p class="text-xs text-slate-500">
            {{ enumName('ChannelType', channel.type) }} · min {{ severityName(channel.minSeverity) }} · {{ channel.enabled ? t('channels.channel_enabled') : t('channels.channel_disabled') }}
          </p>
          <p v-if="summarise(channel)" class="mt-0.5 truncate text-xs text-slate-400">
            {{ summarise(channel) }}
          </p>
        </button>
        <div class="flex items-center gap-2">
          <button
            type="button"
            class="flex items-center gap-1 rounded border border-slate-700 px-3 py-1.5 text-sm text-slate-200 hover:bg-slate-800"
            @click="onTest(channel.id)"
          >
            <Send class="h-4 w-4" />
            {{ t('channels.test_btn') }}
          </button>
          <button
            type="button"
            class="flex items-center gap-1 rounded border border-red-800/60 px-3 py-1.5 text-sm text-red-300 hover:bg-red-950/40"
            @click="onDelete(channel)"
          >
            <Trash2 class="h-4 w-4" />
          </button>
        </div>
      </article>
    </div>

    <p v-else class="text-sm text-slate-500">{{ t('channels.empty') }}</p>
  </div>
</template>
