<script lang="ts" setup>
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import type { OAuthProviderName } from '@/types/api'

interface ProviderForm {
  clientId: string
  clientSecret: string
  scopes: string
  host: string
  clearSecret: boolean
}

const props = defineProps<{
  provider: OAuthProviderName
  showHost: boolean
  secretMasked: string | null
  configured: boolean
  callbackBase: string
}>()

const modelValue = defineModel<ProviderForm>({ required: true })

const { t } = useI18n()

const providerLower = computed(() => props.provider.toLowerCase())

const summaryKey = computed(() => `settings_view.${providerLower.value}_card_summary`)
const callbackUrl = computed(() => `${props.callbackBase}/${providerLower.value}/callback`)
</script>

<template>
  <details class="rounded-md border border-slate-800 bg-slate-950/40 p-3">
    <summary class="cursor-pointer text-xs text-slate-300">
      {{ t(summaryKey) }}
    </summary>

    <div class="mt-3 space-y-2">
      <p class="text-xs text-slate-500">
        {{ t(`settings_view.${providerLower}_callback_hint`) }}
        <code class="ml-1 font-mono text-slate-300">{{ callbackUrl }}</code>
      </p>

      <label v-if="showHost" class="mt-2 block">
        <span class="text-xs text-slate-400">{{ t(`settings_view.${providerLower}_host_label`) }}</span>
        <input
          v-model="modelValue.host"
          type="url"
          :name="`shield-${providerLower}-host`"
          autocomplete="off"
          data-1p-ignore="true"
          data-lpignore="true"
          :placeholder="t(`settings_view.${providerLower}_host_placeholder`)"
          class="mt-1 w-full rounded-md border border-slate-700 bg-slate-950/60 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none focus-visible:ring-1 focus-visible:ring-blue-500"
        />
        <span class="mt-0.5 block text-xs text-slate-500">{{ t(`settings_view.${providerLower}_host_hint`) }}</span>
      </label>

      <label class="mt-2 block">
        <span class="text-xs text-slate-400">{{ t('settings_oauth.client_id_label') }}</span>
        <input
          v-model="modelValue.clientId"
          :name="`shield-${providerLower}-client-id`"
          autocomplete="off"
          data-1p-ignore="true"
          data-lpignore="true"
          :placeholder="t(`settings_view.${providerLower}_client_id_placeholder`)"
          class="mt-1 w-full rounded-md border border-slate-700 bg-slate-950/60 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none focus-visible:ring-1 focus-visible:ring-blue-500"
        />
      </label>

      <label class="mt-2 block">
        <span class="text-xs text-slate-400">
          {{ t('settings_oauth.client_secret_label') }}
          <span v-if="secretMasked" class="ml-2 font-mono text-slate-500">
            {{ t('settings_oauth.current_masked', { value: secretMasked }) }}
          </span>
        </span>
        <input
          v-model="modelValue.clientSecret"
          type="password"
          :name="`shield-${providerLower}-client-secret`"
          autocomplete="new-password"
          data-1p-ignore="true"
          data-lpignore="true"
          :disabled="modelValue.clearSecret"
          :placeholder="t('screen.settings.field.leave_blank_keep_current')"
          class="mt-1 w-full rounded-md border border-slate-700 bg-slate-950/60 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none focus-visible:ring-1 focus-visible:ring-blue-500 disabled:opacity-50"
        />
      </label>

      <label v-if="secretMasked" class="mt-2 flex items-center gap-2 text-xs text-amber-300">
        <input v-model="modelValue.clearSecret" type="checkbox" class="h-3 w-3 accent-amber-500" />
        <span>{{ t('settings_oauth.clear_secret_label') }}</span>
      </label>

      <label class="mt-2 block">
        <span class="text-xs text-slate-400">{{ t('settings_oauth.scopes_label') }}</span>
        <input
          v-model="modelValue.scopes"
          :name="`shield-${providerLower}-scopes`"
          autocomplete="off"
          data-1p-ignore="true"
          data-lpignore="true"
          class="mt-1 w-full rounded-md border border-slate-700 bg-slate-950/60 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none focus-visible:ring-1 focus-visible:ring-blue-500"
        />
      </label>
    </div>
  </details>
</template>
