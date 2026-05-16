<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import { RouterLink } from 'vue-router'
import { Save, Send } from 'lucide-vue-next'

import IntegrationCard from '@/components/IntegrationCard.vue'
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

// Per-provider form state. clientSecret="" + clearSecret=true → server clears the stored secret;
// clientSecret="" + clearSecret=false → server preserves the existing value.
interface ProviderForm {
  clientId: string
  clientSecret: string
  scopes: string
  clearSecret: boolean
}

// Sensible defaults so users don't have to memorize provider scopes. Use full repo (private)
// access for GitHub so Shield can scan everything the operator can see — public-only is a
// downgrade you can pick after the fact by deleting `repo`. Slack covers signin + bot post +
// channel pick. Google covers signin + Gmail XOAUTH2 for SMTP.
const DEFAULT_SCOPES = {
  Github: 'read:user user:email repo read:org',
  Slack: 'openid email profile chat:write channels:read users.identity',
  Google: 'openid email profile https://mail.google.com/',
} as const

function emptyProviderForm(scopes = ''): ProviderForm {
  return { clientId: '', clientSecret: '', scopes, clearSecret: false }
}

const githubForm = reactive<ProviderForm>(emptyProviderForm(DEFAULT_SCOPES.Github))
const slackForm = reactive<ProviderForm>(emptyProviderForm(DEFAULT_SCOPES.Slack))
const googleForm = reactive<ProviderForm>(emptyProviderForm(DEFAULT_SCOPES.Google))

// Dynamic callback URL based on what the operator's actually hitting — same value GitHub
// rejects with redirect_uri_mismatch if it disagrees.
const callbackBase = computed(() => `${window.location.origin}/api/oauth`)

const githubConfigured = computed(() => data.value?.github?.configured ?? false)
const slackConfigured = computed(() => data.value?.slack?.configured ?? false)
const googleConfigured = computed(() => data.value?.google?.configured ?? false)

const githubSecretMasked = computed(() => data.value?.github?.clientSecretMasked ?? null)
const slackSecretMasked = computed(() => data.value?.slack?.clientSecretMasked ?? null)
const googleSecretMasked = computed(() => data.value?.google?.clientSecretMasked ?? null)

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

  githubForm.clientId = next.github?.clientId ?? ''
  githubForm.scopes = next.github?.scopes ?? DEFAULT_SCOPES.Github
  githubForm.clientSecret = ''
  githubForm.clearSecret = false

  slackForm.clientId = next.slack?.clientId ?? ''
  slackForm.scopes = next.slack?.scopes ?? DEFAULT_SCOPES.Slack
  slackForm.clientSecret = ''
  slackForm.clearSecret = false

  googleForm.clientId = next.google?.clientId ?? ''
  googleForm.scopes = next.google?.scopes ?? DEFAULT_SCOPES.Google
  googleForm.clientSecret = ''
  googleForm.clearSecret = false
}, { immediate: true })

// Translate UI form to wire payload: clearSecret beats clientSecret, null preserves.
function providerPatch(form: ProviderForm) {
  return {
    clientId: form.clientId || null,
    clientSecret: form.clearSecret ? '' : (form.clientSecret || null),
    scopes: form.scopes || null,
  }
}

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
      github: providerPatch(githubForm),
      slack: providerPatch(slackForm),
      google: providerPatch(googleForm),
    })
    oidcClientSecret.value = ''
    githubForm.clientSecret = ''
    githubForm.clearSecret = false
    slackForm.clientSecret = ''
    slackForm.clearSecret = false
    googleForm.clientSecret = ''
    googleForm.clearSecret = false
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

      <section class="space-y-3 rounded-lg border border-slate-800 bg-slate-900 p-4">
        <h2 class="text-sm font-medium text-slate-300">Integrations</h2>
        <p class="text-xs text-slate-500">
          <RouterLink to="/welcome" class="text-blue-400 hover:underline">Re-run onboarding</RouterLink> to walk through the setup wizard again.
        </p>
        <p class="text-xs text-slate-500">
          Configure OAuth client credentials for repo scanning and sign-in. Create the OAuth app at the provider's developer console, then paste the credentials below. Save once you've filled in the form — connecting happens on each card after the credentials persist.
        </p>

        <IntegrationCard
          provider="Github"
          label="GitHub"
          description="Scan GitHub repos and sign in with a GitHub account."
          :configured="githubConfigured"
        />
        <details class="rounded border border-slate-800 bg-slate-950 p-3">
          <summary class="cursor-pointer text-xs text-slate-300">Configure GitHub credentials</summary>
          <p class="mt-2 text-xs text-slate-500">
            Create the OAuth App at
            <a href="https://github.com/settings/developers" target="_blank" rel="noopener" class="text-blue-400 underline">github.com/settings/developers</a>.
            Set <strong>Authorization callback URL</strong> to
            <code class="font-mono text-slate-300">{{ callbackBase }}/github/callback</code>.
            <br />
            Scopes pre-filled with everything Shield needs &mdash; <strong>do not narrow without thought:</strong>
          </p>
          <ul class="mt-1 list-disc pl-5 text-xs text-slate-500">
            <li><code>read:user</code> &mdash; profile (for sign-in)</li>
            <li><code>user:email</code> &mdash; verified email (for sign-in find-by-email)</li>
            <li><code>repo</code> &mdash; <strong>required</strong> &mdash; read every private repo's lockfiles, and open pull requests for the one-click fix flow</li>
            <li><code>read:org</code> &mdash; enumerate org memberships so the Sources UI can offer per-org bulk-add</li>
          </ul>
          <label class="mt-2 block">
            <span class="text-xs text-slate-400">Client ID</span>
            <input
              v-model="githubForm.clientId"
              name="shield-gh-client-id"
              autocomplete="off"
              data-1p-ignore="true"
              data-lpignore="true"
              placeholder="GitHub OAuth App Client ID"
              class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
            />
          </label>
          <label class="mt-2 block">
            <span class="text-xs text-slate-400">
              Client secret
              <span v-if="githubSecretMasked" class="ml-2 font-mono text-slate-500">current: {{ githubSecretMasked }}</span>
            </span>
            <input
              v-model="githubForm.clientSecret"
              type="password"
              name="shield-gh-client-secret"
              autocomplete="new-password"
              data-1p-ignore="true"
              data-lpignore="true"
              :disabled="githubForm.clearSecret"
              placeholder="Leave blank to keep current"
              class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none disabled:opacity-50"
            />
          </label>
          <label v-if="githubSecretMasked" class="mt-2 flex items-center gap-2 text-xs text-amber-300">
            <input v-model="githubForm.clearSecret" type="checkbox" class="h-3 w-3 accent-amber-500" />
            <span>Clear secret on next save</span>
          </label>
          <label class="mt-2 block">
            <span class="text-xs text-slate-400">Scopes</span>
            <input
              v-model="githubForm.scopes"
              name="shield-gh-scopes"
              autocomplete="off"
              data-1p-ignore="true"
              data-lpignore="true"
              placeholder="read:user public_repo"
              class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
            />
          </label>
        </details>

        <IntegrationCard
          provider="Slack"
          label="Slack"
          description="Deliver alerts to Slack channels and look up the channel list."
          :configured="slackConfigured"
        />
        <details class="rounded border border-slate-800 bg-slate-950 p-3">
          <summary class="cursor-pointer text-xs text-slate-300">Configure Slack credentials</summary>
          <p class="mt-2 text-xs text-slate-500">
            Create the Slack app at
            <a href="https://api.slack.com/apps" target="_blank" rel="noopener" class="text-blue-400 underline">api.slack.com/apps</a>.
            Add an <strong>OAuth Redirect URL</strong> of
            <code class="font-mono text-slate-300">{{ callbackBase }}/slack/callback</code>.
            Scopes are pre-filled for sign-in + bot posts + channel pick.
          </p>
          <label class="mt-2 block">
            <span class="text-xs text-slate-400">Client ID</span>
            <input
              v-model="slackForm.clientId"
              name="shield-sl-client-id"
              autocomplete="off"
              data-1p-ignore="true"
              data-lpignore="true"
              placeholder="Slack OAuth Client ID"
              class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
            />
          </label>
          <label class="mt-2 block">
            <span class="text-xs text-slate-400">
              Client secret
              <span v-if="slackSecretMasked" class="ml-2 font-mono text-slate-500">current: {{ slackSecretMasked }}</span>
            </span>
            <input
              v-model="slackForm.clientSecret"
              type="password"
              name="shield-sl-client-secret"
              autocomplete="new-password"
              data-1p-ignore="true"
              data-lpignore="true"
              :disabled="slackForm.clearSecret"
              placeholder="Leave blank to keep current"
              class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none disabled:opacity-50"
            />
          </label>
          <label v-if="slackSecretMasked" class="mt-2 flex items-center gap-2 text-xs text-amber-300">
            <input v-model="slackForm.clearSecret" type="checkbox" class="h-3 w-3 accent-amber-500" />
            <span>Clear secret on next save</span>
          </label>
          <label class="mt-2 block">
            <span class="text-xs text-slate-400">Scopes</span>
            <input
              v-model="slackForm.scopes"
              name="shield-sl-scopes"
              autocomplete="off"
              data-1p-ignore="true"
              data-lpignore="true"
              placeholder="chat:write channels:read"
              class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
            />
          </label>
        </details>

        <IntegrationCard
          provider="Google"
          label="Google"
          description="Sign in with a Google account and authorize Gmail SMTP."
          :configured="googleConfigured"
        />
        <details class="rounded border border-slate-800 bg-slate-950 p-3">
          <summary class="cursor-pointer text-xs text-slate-300">Configure Google credentials</summary>
          <p class="mt-2 text-xs text-slate-500">
            Create the OAuth credentials at
            <a href="https://console.cloud.google.com/apis/credentials" target="_blank" rel="noopener" class="text-blue-400 underline">console.cloud.google.com/apis/credentials</a>. Authorized redirect URI: <code class="font-mono text-slate-300">/api/oauth/google/callback</code>. Required scopes:
            <code class="font-mono text-slate-300">openid email profile</code> (sign-in) or
            <code class="font-mono text-slate-300">https://mail.google.com/</code> (SMTP).
          </p>
          <label class="mt-2 block">
            <span class="text-xs text-slate-400">Client ID</span>
            <input
              v-model="googleForm.clientId"
              name="shield-gg-client-id"
              autocomplete="off"
              data-1p-ignore="true"
              data-lpignore="true"
              placeholder="Google OAuth Client ID"
              class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
            />
          </label>
          <label class="mt-2 block">
            <span class="text-xs text-slate-400">
              Client secret
              <span v-if="googleSecretMasked" class="ml-2 font-mono text-slate-500">current: {{ googleSecretMasked }}</span>
            </span>
            <input
              v-model="googleForm.clientSecret"
              type="password"
              name="shield-gg-client-secret"
              autocomplete="new-password"
              data-1p-ignore="true"
              data-lpignore="true"
              :disabled="googleForm.clearSecret"
              placeholder="Leave blank to keep current"
              class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none disabled:opacity-50"
            />
          </label>
          <label v-if="googleSecretMasked" class="mt-2 flex items-center gap-2 text-xs text-amber-300">
            <input v-model="googleForm.clearSecret" type="checkbox" class="h-3 w-3 accent-amber-500" />
            <span>Clear secret on next save</span>
          </label>
          <label class="mt-2 block">
            <span class="text-xs text-slate-400">Scopes</span>
            <input
              v-model="googleForm.scopes"
              name="shield-gg-scopes"
              autocomplete="off"
              data-1p-ignore="true"
              data-lpignore="true"
              placeholder="openid email profile"
              class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
            />
          </label>
        </details>
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
