<script setup lang="ts">
import { ref, watch } from 'vue'
import { Save, Send } from 'lucide-vue-next'

import {
  useRuntimeInfo,
  useSettingsQuery,
  useTestOidc,
  useUpdateSettings,
} from '@/queries/settings'
import { useToasts } from '@/stores/toast'
import { Severity, SeverityNames, type SeverityName } from '@/types/api'

const { data, isLoading, isError } = useSettingsQuery()
const { data: runtime } = useRuntimeInfo()
const update = useUpdateSettings()
const testOidc = useTestOidc()
const { push } = useToasts()

const singleUserMode = ref(false)
const openApiEnabled = ref(false)
const oidcEnabled = ref(false)
const oidcIssuer = ref('')
const oidcClientId = ref('')
const oidcClientSecret = ref('')
const oidcSecretMasked = ref<string | null>(null)
const alertSeverityFloor = ref<SeverityName>('Low')
const retentionDays = ref(90)

watch(data, (next) => {
  if (!next)
    return
  singleUserMode.value = next.singleUserMode
  openApiEnabled.value = next.openApiEnabled
  oidcEnabled.value = next.oidcEnabled
  oidcIssuer.value = next.oidcIssuer ?? ''
  oidcClientId.value = next.oidcClientId ?? ''
  oidcClientSecret.value = ''
  oidcSecretMasked.value = next.oidcClientSecretMasked
  alertSeverityFloor.value = SeverityNames[next.alertSeverityFloor] ?? 'Low'
  retentionDays.value = next.retentionDays
}, { immediate: true })

async function onSave(): Promise<void> {
  try {
    await update.mutateAsync({
      singleUserMode: singleUserMode.value,
      openApiEnabled: openApiEnabled.value,
      oidcEnabled: oidcEnabled.value,
      oidcIssuer: oidcIssuer.value || null,
      oidcClientId: oidcClientId.value || null,
      oidcClientSecret: oidcClientSecret.value || null,
      alertSeverityFloor: Severity[alertSeverityFloor.value],
      retentionDays: retentionDays.value,
    })
    oidcClientSecret.value = ''
    push('success', 'Settings saved.')
  }
  catch {
    push('error', 'Failed to save settings.')
  }
}

async function onTestOidc(): Promise<void> {
  try {
    const result = await testOidc.mutateAsync({
      issuer: oidcIssuer.value,
      clientId: oidcClientId.value,
      clientSecret: oidcClientSecret.value || null,
    })
    if (result.ok)
      push('success', 'OIDC discovery succeeded.')
    else
      push('error', result.error ?? 'OIDC discovery failed.')
  }
  catch {
    push('error', 'OIDC test request failed.')
  }
}
</script>

<template>
  <div class="space-y-6">
    <header class="flex items-center justify-between">
      <h1 class="text-2xl font-semibold">Settings</h1>
      <button
        type="button"
        :disabled="update.isPending.value || isLoading"
        class="flex items-center gap-1 rounded bg-blue-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-blue-500 disabled:bg-blue-900"
        @click="onSave"
      >
        <Save class="h-4 w-4" />
        {{ update.isPending.value ? 'Saving…' : 'Save' }}
      </button>
    </header>

    <p v-if="isLoading" class="text-sm text-slate-400">Loading…</p>
    <p v-else-if="isError" class="text-sm text-red-300">Failed to load settings.</p>

    <template v-if="data">
      <section class="space-y-3 rounded-lg border border-slate-800 bg-slate-900 p-4">
        <h2 class="text-sm font-medium text-slate-300">General</h2>

        <label class="flex items-center justify-between gap-3 text-sm text-slate-200">
          <span>Single-user mode</span>
          <input v-model="singleUserMode" type="checkbox" class="h-4 w-4 accent-blue-500" />
        </label>

        <label class="flex items-center justify-between gap-3 text-sm text-slate-200">
          <span>OpenAPI / Swagger enabled</span>
          <input v-model="openApiEnabled" type="checkbox" class="h-4 w-4 accent-blue-500" />
        </label>

        <label class="block">
          <span class="text-sm text-slate-300">Alert severity floor</span>
          <select
            v-model="alertSeverityFloor"
            class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
          >
            <option value="Low">Low</option>
            <option value="Medium">Medium</option>
            <option value="High">High</option>
            <option value="Critical">Critical</option>
          </select>
        </label>

        <label class="block">
          <span class="text-sm text-slate-300">Retention (days)</span>
          <input
            v-model.number="retentionDays"
            type="number"
            min="1"
            max="3650"
            class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
          />
        </label>
      </section>

      <section class="space-y-3 rounded-lg border border-slate-800 bg-slate-900 p-4">
        <h2 class="text-sm font-medium text-slate-300">OIDC</h2>

        <label class="flex items-center justify-between gap-3 text-sm text-slate-200">
          <span>OIDC enabled</span>
          <input v-model="oidcEnabled" type="checkbox" class="h-4 w-4 accent-blue-500" />
        </label>

        <label class="block">
          <span class="text-sm text-slate-300">Issuer URL</span>
          <input
            v-model="oidcIssuer"
            type="url"
            name="shield-oidc-issuer"
            autocomplete="off"
            data-1p-ignore="true"
            data-lpignore="true"
            placeholder="https://issuer.example.com/realms/shield"
            class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
          />
        </label>

        <label class="block">
          <span class="text-sm text-slate-300">Client ID</span>
          <input
            v-model="oidcClientId"
            name="shield-oidc-client-id"
            autocomplete="off"
            data-1p-ignore="true"
            data-lpignore="true"
            class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
          />
        </label>

        <label class="block">
          <span class="text-sm text-slate-300">
            Client secret
            <span v-if="oidcSecretMasked" class="ml-2 font-mono text-xs text-slate-500">
              current: {{ oidcSecretMasked }}
            </span>
          </span>
          <input
            v-model="oidcClientSecret"
            type="password"
            name="shield-oidc-client-secret"
            autocomplete="new-password"
            data-1p-ignore="true"
            data-lpignore="true"
            placeholder="Leave blank to keep current"
            class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
          />
        </label>

        <button
          type="button"
          :disabled="testOidc.isPending.value || !oidcIssuer"
          class="flex items-center gap-1 rounded border border-slate-700 px-3 py-1.5 text-sm text-slate-200 hover:bg-slate-800 disabled:opacity-50"
          @click="onTestOidc"
        >
          <Send class="h-4 w-4" />
          {{ testOidc.isPending.value ? 'Testing…' : 'Test connection' }}
        </button>
      </section>

      <section v-if="runtime" class="space-y-1 rounded-lg border border-slate-800 bg-slate-900 p-4 text-xs text-slate-400">
        <h2 class="text-sm font-medium text-slate-300">Runtime</h2>
        <p>Version: <span class="font-mono text-slate-300">{{ runtime.version }}</span></p>
        <p>Environment: <span class="font-mono text-slate-300">{{ runtime.environment }}</span></p>
        <p>Content root: <span class="font-mono text-slate-300">{{ runtime.contentRoot }}</span></p>
        <p>Web root: <span class="font-mono text-slate-300">{{ runtime.webRoot || '(unset)' }}</span></p>
      </section>
    </template>
  </div>
</template>
