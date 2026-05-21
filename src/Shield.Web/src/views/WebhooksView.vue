<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { Activity, Copy, Eye, EyeOff, Plus, Trash2 } from 'lucide-vue-next'

import {
  useCreateWebhookEndpointMutation,
  useDeleteWebhookEndpointMutation,
  useWebhookEndpointsQuery,
  useWebhookEnvelopesQuery,
} from '@/queries/webhookEndpoints'
import { useToasts } from '@/stores/toast'
import { OAuthProvider, type WebhookEndpoint } from '@/types/api'

const { t } = useI18n()
const { push } = useToasts()

const list = useWebhookEndpointsQuery()
const createMutation = useCreateWebhookEndpointMutation()
const deleteMutation = useDeleteWebhookEndpointMutation()

const SUPPORTED_PROVIDERS: { value: OAuthProvider, labelKey: string }[] = [
  { value: OAuthProvider.Github, labelKey: 'screen.webhooks.provider.github' },
  { value: OAuthProvider.Gitea, labelKey: 'screen.webhooks.provider.gitea' },
  { value: OAuthProvider.Gitlab, labelKey: 'screen.webhooks.provider.gitlab' },
  { value: OAuthProvider.Forgejo, labelKey: 'screen.webhooks.provider.forgejo' },
]

const deliveriesFor = ref<string | null>(null)
const deliveries = useWebhookEnvelopesQuery(deliveriesFor)

function openDeliveries(endpoint: WebhookEndpoint): void {
  deliveriesFor.value = endpoint.id
}

function closeDeliveries(): void {
  deliveriesFor.value = null
}

const deliveriesEndpoint = computed(() =>
  list.data.value?.find(endpoint => endpoint.id === deliveriesFor.value) ?? null,
)

const showCreate = ref(false)
const form = ref<{ provider: OAuthProvider, label: string }>({
  provider: OAuthProvider.Github,
  label: '',
})
const formError = ref<string | null>(null)
const created = ref<{ endpoint: WebhookEndpoint, secret: string } | null>(null)
const revealed = ref(false)

const canSubmit = computed(() => form.value.label.trim().length > 0)

function openCreate(): void {
  form.value = { provider: OAuthProvider.Github, label: '' }
  formError.value = null
  created.value = null
  revealed.value = false
  showCreate.value = true
}

function closeCreate(): void {
  showCreate.value = false
  created.value = null
  revealed.value = false
}

async function onSubmit(): Promise<void> {
  formError.value = null
  if (!canSubmit.value) {
    formError.value = t('screen.webhooks.error.label_required')
    return
  }
  try {
    const response = await createMutation.mutateAsync({
      provider: form.value.provider,
      label: form.value.label.trim(),
    })
    created.value = response
  }
  catch (err) {
    formError.value = err instanceof Error ? err.message : t('screen.webhooks.error.create_failed')
  }
}

async function onDelete(endpoint: WebhookEndpoint): Promise<void> {
  if (!confirm(t('screen.webhooks.confirm_delete', { label: endpoint.label }))) return
  try {
    await deleteMutation.mutateAsync(endpoint.id)
    push('success', t('screen.webhooks.toast.deleted'))
  }
  catch {
    push('error', t('screen.webhooks.error.delete_failed'))
  }
}

async function copy(value: string, successKey: string): Promise<void> {
  try {
    await navigator.clipboard.writeText(value)
    push('success', t(successKey))
  }
  catch {
    push('error', t('screen.webhooks.copy_failed'))
  }
}

function providerLabel(value: OAuthProvider): string {
  const match = SUPPORTED_PROVIDERS.find(entry => entry.value === value)
  return match ? t(match.labelKey) : String(value)
}
</script>

<template>
  <div class="space-y-6">
    <header class="flex items-start justify-between gap-4">
      <div>
        <h1 class="text-2xl font-semibold">{{ t('screen.webhooks.title') }}</h1>
        <p class="mt-1 max-w-3xl text-sm text-slate-400">{{ t('screen.webhooks.subtitle') }}</p>
      </div>
      <button
        type="button"
        class="inline-flex items-center gap-1.5 rounded bg-blue-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-blue-500 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-400"
        @click="openCreate"
      >
        <Plus class="h-4 w-4" aria-hidden="true" />
        {{ t('screen.webhooks.create_btn') }}
      </button>
    </header>

    <section v-if="list.isLoading.value" class="text-sm text-slate-400">
      {{ t('state.loading') }}
    </section>

    <section
      v-else-if="list.data.value && list.data.value.length > 0"
      class="space-y-2"
      :aria-label="t('screen.webhooks.list_aria')"
    >
      <article
        v-for="endpoint in list.data.value"
        :key="endpoint.id"
        class="rounded-lg border border-slate-800 bg-slate-900 p-4"
      >
        <div class="flex items-start justify-between gap-4">
          <div class="min-w-0 flex-1 space-y-1">
            <div class="flex flex-wrap items-center gap-2">
              <h2 class="text-sm font-semibold text-slate-100">{{ endpoint.label }}</h2>
              <span class="rounded-full bg-slate-800 px-2 py-0.5 text-[10px] font-medium uppercase tracking-wider text-slate-300">
                {{ providerLabel(endpoint.provider) }}
              </span>
            </div>
            <div class="flex items-center gap-2">
              <code class="break-all rounded bg-slate-950 px-2 py-1 font-mono text-xs text-slate-100">
                {{ endpoint.inboundUrl }}
              </code>
              <button
                type="button"
                class="inline-flex shrink-0 items-center gap-1 rounded border border-slate-700 px-2 py-1 text-xs text-slate-300 hover:bg-slate-800 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-400"
                :aria-label="t('screen.webhooks.copy_url_aria', { label: endpoint.label })"
                @click="copy(endpoint.inboundUrl, 'screen.webhooks.toast.url_copied')"
              >
                <Copy class="h-3 w-3" aria-hidden="true" />
                {{ t('action.copy') }}
              </button>
            </div>
            <p class="text-xs text-slate-500">
              {{ t('screen.webhooks.created_at', { date: new Date(endpoint.createdAt).toLocaleString() }) }}
              <template v-if="endpoint.lastDeliveryAt">
                · {{ t('screen.webhooks.last_delivery', { date: new Date(endpoint.lastDeliveryAt).toLocaleString() }) }}
              </template>
            </p>
          </div>
          <div class="flex shrink-0 items-center gap-1">
            <button
              type="button"
              class="inline-flex items-center gap-1 rounded border border-slate-700 px-2 py-1 text-xs text-slate-300 hover:bg-slate-800 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-400"
              :aria-label="t('screen.webhooks.deliveries_aria', { label: endpoint.label })"
              @click="openDeliveries(endpoint)"
            >
              <Activity class="h-3 w-3" aria-hidden="true" />
              {{ t('screen.webhooks.deliveries_btn') }}
            </button>
            <button
              type="button"
              class="inline-flex items-center gap-1 rounded border border-red-700 px-2 py-1 text-xs text-red-300 hover:bg-red-900/40 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-red-400"
              :aria-label="t('screen.webhooks.delete_aria', { label: endpoint.label })"
              @click="onDelete(endpoint)"
            >
              <Trash2 class="h-3 w-3" aria-hidden="true" />
              {{ t('action.delete') }}
            </button>
          </div>
        </div>
      </article>
    </section>

    <div
      v-if="deliveriesFor && deliveriesEndpoint"
      class="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4"
      role="dialog"
      aria-modal="true"
      aria-labelledby="webhook-deliveries-title"
      @click.self="closeDeliveries"
    >
      <div class="flex max-h-[80vh] w-full max-w-2xl flex-col rounded-lg border border-slate-700 bg-slate-900 shadow-xl">
        <header class="border-b border-slate-800 p-4">
          <h2 id="webhook-deliveries-title" class="text-lg font-semibold">
            {{ t('screen.webhooks.deliveries.title', { label: deliveriesEndpoint.label }) }}
          </h2>
          <p class="mt-0.5 text-xs text-slate-500">{{ t('screen.webhooks.deliveries.subtitle') }}</p>
        </header>

        <div class="flex-1 overflow-y-auto p-4">
          <p v-if="deliveries.isLoading.value" class="text-sm text-slate-400">{{ t('state.loading') }}</p>
          <p v-else-if="!deliveries.data.value || deliveries.data.value.length === 0" class="text-sm text-slate-400">
            {{ t('screen.webhooks.deliveries.empty') }}
          </p>
          <ul v-else class="space-y-2" role="list">
            <li
              v-for="envelope in deliveries.data.value"
              :key="envelope.id"
              class="rounded border border-slate-800 bg-slate-950/40 p-3"
            >
              <div class="flex items-center justify-between gap-2 text-xs">
                <div class="flex items-center gap-2">
                  <span
                    class="inline-flex h-2 w-2 rounded-full"
                    :class="envelope.signatureValid ? 'bg-emerald-400' : 'bg-red-400'"
                    :aria-label="envelope.signatureValid ? t('screen.webhooks.deliveries.ok') : t('screen.webhooks.deliveries.failed')"
                  />
                  <code class="rounded bg-slate-900 px-1.5 py-0.5 font-mono text-[11px] text-slate-200">
                    {{ envelope.eventType ?? '—' }}
                  </code>
                  <span v-if="!envelope.signatureValid && envelope.reason" class="text-red-300">
                    {{ envelope.reason }}
                  </span>
                </div>
                <span class="text-slate-500">{{ new Date(envelope.receivedAt).toLocaleString() }}</span>
              </div>
              <p v-if="envelope.deliveryId" class="mt-1 font-mono text-[10px] text-slate-600">
                {{ envelope.deliveryId }}
              </p>
            </li>
          </ul>
        </div>

        <footer class="flex justify-end gap-2 border-t border-slate-800 p-4">
          <button
            type="button"
            class="rounded border border-slate-700 px-3 py-1.5 text-sm text-slate-300 hover:bg-slate-800 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-400"
            @click="closeDeliveries"
          >
            {{ t('action.close') }}
          </button>
        </footer>
      </div>
    </div>

    <section
      v-else
      class="rounded border border-dashed border-slate-700 p-6 text-center text-sm text-slate-400"
    >
      {{ t('screen.webhooks.empty') }}
    </section>

    <div
      v-if="showCreate"
      class="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4"
      role="dialog"
      aria-modal="true"
      :aria-labelledby="created ? 'webhook-reveal-title' : 'webhook-create-title'"
      @click.self="closeCreate"
    >
      <div class="w-full max-w-lg rounded-lg border border-slate-700 bg-slate-900 p-6 shadow-xl">
        <div v-if="created" class="space-y-4">
          <h2 id="webhook-reveal-title" class="text-lg font-semibold">
            {{ t('screen.webhooks.reveal.title') }}
          </h2>
          <p class="rounded border border-yellow-700 bg-yellow-900/30 p-2 text-sm text-yellow-200">
            {{ t('screen.webhooks.reveal.warning') }}
          </p>

          <div class="space-y-2">
            <span class="block text-xs text-slate-400">{{ t('screen.webhooks.reveal.url_label') }}</span>
            <div class="flex items-stretch gap-1.5">
              <code class="min-w-0 flex-1 break-all rounded bg-slate-950 px-3 py-2 font-mono text-xs text-slate-100 select-all">
                {{ created.endpoint.inboundUrl }}
              </code>
              <button
                type="button"
                class="inline-flex shrink-0 items-center justify-center rounded border border-slate-700 px-3 text-slate-300 hover:bg-slate-800 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-400"
                :aria-label="t('screen.webhooks.reveal.copy_url_aria')"
                @click="copy(created.endpoint.inboundUrl, 'screen.webhooks.toast.url_copied')"
              >
                <Copy class="h-4 w-4" aria-hidden="true" />
              </button>
            </div>
          </div>

          <div class="space-y-2">
            <span class="block text-xs text-slate-400">{{ t('screen.webhooks.reveal.secret_label') }}</span>
            <div class="flex items-stretch gap-1.5">
              <input
                :value="revealed ? created.secret : '•'.repeat(created.secret.length)"
                :type="revealed ? 'text' : 'password'"
                readonly
                autocomplete="off"
                spellcheck="false"
                :aria-label="t('screen.webhooks.reveal.secret_field_aria')"
                class="min-w-0 flex-1 rounded border border-slate-700 bg-slate-950 px-3 py-2 font-mono text-sm text-slate-100 focus:border-blue-500 focus:outline-none"
              />
              <button
                type="button"
                class="inline-flex shrink-0 items-center justify-center rounded border border-slate-700 px-3 text-slate-300 hover:bg-slate-800 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-400"
                :aria-label="revealed ? t('screen.webhooks.reveal.hide_aria') : t('screen.webhooks.reveal.show_aria')"
                :aria-pressed="revealed"
                @click="revealed = !revealed"
              >
                <component :is="revealed ? EyeOff : Eye" class="h-4 w-4" aria-hidden="true" />
              </button>
              <button
                type="button"
                class="inline-flex shrink-0 items-center justify-center rounded border border-slate-700 px-3 text-slate-300 hover:bg-slate-800 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-400"
                :aria-label="t('screen.webhooks.reveal.copy_secret_aria')"
                @click="copy(created!.secret, 'screen.webhooks.toast.secret_copied')"
              >
                <Copy class="h-4 w-4" aria-hidden="true" />
              </button>
            </div>
          </div>

          <button
            type="button"
            class="w-full rounded bg-blue-600 px-3 py-2 text-sm font-medium text-white hover:bg-blue-500 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-400"
            @click="closeCreate"
          >
            {{ t('action.done') }}
          </button>
        </div>

        <form v-else class="space-y-4" @submit.prevent="onSubmit">
          <h2 id="webhook-create-title" class="text-lg font-semibold">
            {{ t('screen.webhooks.create.title') }}
          </h2>

          <label class="block">
            <span class="text-sm text-slate-300">{{ t('screen.webhooks.create.provider_label') }}</span>
            <select
              v-model.number="form.provider"
              class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
            >
              <option v-for="entry in SUPPORTED_PROVIDERS" :key="entry.value" :value="entry.value">
                {{ t(entry.labelKey) }}
              </option>
            </select>
          </label>

          <label class="block">
            <span class="text-sm text-slate-300">{{ t('screen.webhooks.create.label_label') }}</span>
            <input
              v-model="form.label"
              type="text"
              required
              maxlength="200"
              :placeholder="t('screen.webhooks.create.label_placeholder')"
              class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
            />
            <span class="mt-1 block text-xs text-slate-500">{{ t('screen.webhooks.create.label_hint') }}</span>
          </label>

          <p v-if="formError" class="text-sm text-red-400">{{ formError }}</p>

          <div class="flex justify-end gap-2">
            <button
              type="button"
              class="rounded border border-slate-700 px-3 py-1.5 text-sm text-slate-300 hover:bg-slate-800 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-400"
              @click="closeCreate"
            >
              {{ t('action.cancel') }}
            </button>
            <button
              type="submit"
              :disabled="!canSubmit || createMutation.isPending.value"
              class="rounded bg-blue-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-blue-500 disabled:cursor-not-allowed disabled:opacity-50 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-400"
            >
              {{ createMutation.isPending.value ? t('state.saving') : t('screen.webhooks.create.submit_btn') }}
            </button>
          </div>
        </form>
      </div>
    </div>
  </div>
</template>
