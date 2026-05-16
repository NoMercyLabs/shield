<script setup lang="ts">
import { ref } from 'vue'
import { Plus, Send } from 'lucide-vue-next'

import { useChannelsQuery, useCreateChannelMutation, useTestSendMutation } from '@/queries/channels'
import { useToasts } from '@/stores/toast'
import type { Severity } from '@/types/api'

const { data, isLoading, isError } = useChannelsQuery()
const create = useCreateChannelMutation()
const testSend = useTestSendMutation()
const { push } = useToasts()

const showForm = ref(false)
const name = ref('')
const webhookUrl = ref('')
const minSeverity = ref<Severity>('High')

async function onSubmit(): Promise<void> {
  try {
    await create.mutateAsync({
      type: 'Discord',
      name: name.value,
      minSeverity: minSeverity.value,
      enabled: true,
      configJson: JSON.stringify({ webhookUrl: webhookUrl.value }),
    })
    push('success', `Channel "${name.value}" added.`)
    showForm.value = false
    name.value = ''
    webhookUrl.value = ''
  }
  catch {
    push('error', 'Failed to add channel.')
  }
}

async function onTest(id: string): Promise<void> {
  try {
    await testSend.mutateAsync(id)
    push('success', 'Test sent.')
  }
  catch {
    push('error', 'Test send failed.')
  }
}
</script>

<template>
  <div class="space-y-6">
    <header class="flex items-center justify-between">
      <h1 class="text-2xl font-semibold">Channels</h1>
      <button
        type="button"
        class="flex items-center gap-1 rounded bg-blue-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-blue-500"
        @click="showForm = !showForm"
      >
        <Plus class="h-4 w-4" />
        Add Discord webhook
      </button>
    </header>

    <form
      v-if="showForm"
      class="space-y-3 rounded-lg border border-slate-800 bg-slate-900 p-4"
      @submit.prevent="onSubmit"
    >
      <label class="block">
        <span class="text-sm text-slate-300">Name</span>
        <input
          v-model="name"
          required
          class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
        />
      </label>
      <label class="block">
        <span class="text-sm text-slate-300">Webhook URL</span>
        <input
          v-model="webhookUrl"
          type="url"
          required
          placeholder="https://discord.com/api/webhooks/…"
          class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
        />
      </label>
      <label class="block">
        <span class="text-sm text-slate-300">Minimum severity</span>
        <select
          v-model="minSeverity"
          class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
        >
          <option value="Critical">Critical</option>
          <option value="High">High</option>
          <option value="Medium">Medium</option>
          <option value="Low">Low</option>
        </select>
      </label>
      <button
        type="submit"
        :disabled="create.isPending.value"
        class="rounded bg-blue-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-blue-500 disabled:bg-blue-900"
      >
        {{ create.isPending.value ? 'Saving…' : 'Save' }}
      </button>
    </form>

    <p v-if="isLoading" class="text-sm text-slate-400">Loading…</p>
    <p v-else-if="isError" class="text-sm text-red-300">Failed to load channels.</p>

    <div v-else-if="data && data.length" class="space-y-2">
      <article
        v-for="channel in data"
        :key="channel.id"
        class="flex items-center justify-between rounded-lg border border-slate-800 bg-slate-900 p-4"
      >
        <div>
          <p class="text-sm font-medium">{{ channel.name }}</p>
          <p class="text-xs text-slate-500">
            {{ channel.type }} · min {{ channel.minSeverity }} · {{ channel.enabled ? 'enabled' : 'disabled' }}
          </p>
        </div>
        <button
          type="button"
          class="flex items-center gap-1 rounded border border-slate-700 px-3 py-1.5 text-sm text-slate-200 hover:bg-slate-800"
          @click="onTest(channel.id)"
        >
          <Send class="h-4 w-4" />
          Test send
        </button>
      </article>
    </div>

    <p v-else class="text-sm text-slate-500">No channels yet.</p>
  </div>
</template>
