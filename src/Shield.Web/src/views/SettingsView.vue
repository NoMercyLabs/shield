<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink, useRoute, useRouter } from 'vue-router'
import { ExternalLink, Save, Send } from 'lucide-vue-next'

import GithubOauthSetupCard from '@/components/GithubOauthSetupCard.vue'
import IntegrationCard from '@/components/IntegrationCard.vue'
import PushNotificationsCard from '@/components/PushNotificationsCard.vue'
import {
  useRuntimeInfo,
  useSettingsQuery,
  useTestOidc,
  useUpdateSettings,
} from '@/queries/settings'
import { useSourcesQuery, useUpdateSourceAutoFixModeMutation } from '@/queries/sources'
import { enumEntries, enumName } from '@/stores/enums'
import { useToasts } from '@/stores/toast'
import { AutoFixMode, Severity, type SeverityName } from '@/types/api'

const { t } = useI18n()
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

// Dirty tracking — compared against the last loaded snapshot. The snapshot resets after
// every successful save so the section dots clear together with the toast.
const snapshot = ref({
  singleUserMode: false,
  openApiEnabled: false,
  oidcEnabled: false,
  oidcIssuer: '',
  oidcClientId: '',
  alertSeverityFloor: 'Low' as SeverityName,
  retentionDays: 90,
})

interface ProviderForm {
  clientId: string
  clientSecret: string
  scopes: string
  clearSecret: boolean
}

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

const callbackBase = computed(() => `${window.location.origin}/api/oauth`)
const autoFixModeOptions = computed(() => enumEntries('AutoFixMode'))

const slackConfigured = computed(() => data.value?.slack?.configured ?? false)
const googleConfigured = computed(() => data.value?.google?.configured ?? false)

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
  alertSeverityFloor.value = (enumName('Severity', next.alertSeverityFloor) || 'Low') as SeverityName
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

  snapshot.value = {
    singleUserMode: next.singleUserMode,
    openApiEnabled: next.openApiEnabled,
    oidcEnabled: next.oidcEnabled,
    oidcIssuer: next.oidcIssuer ?? '',
    oidcClientId: next.oidcClientId ?? '',
    alertSeverityFloor: (enumName('Severity', next.alertSeverityFloor) || 'Low') as SeverityName,
    retentionDays: next.retentionDays,
  }
}, { immediate: true })

// Per-section dirty signals — drives the unsaved-changes dot.
const accessDirty = computed(() =>
  singleUserMode.value !== snapshot.value.singleUserMode
  || oidcEnabled.value !== snapshot.value.oidcEnabled
  || oidcIssuer.value !== snapshot.value.oidcIssuer
  || oidcClientId.value !== snapshot.value.oidcClientId
  || oidcClientSecret.value.length > 0,
)
const alertsDirty = computed(() =>
  alertSeverityFloor.value !== snapshot.value.alertSeverityFloor
  || retentionDays.value !== snapshot.value.retentionDays,
)
const apiDirty = computed(() => openApiEnabled.value !== snapshot.value.openApiEnabled)
const oauthDirty = computed(() =>
  githubForm.clientId !== (data.value?.github?.clientId ?? '')
  || githubForm.scopes !== (data.value?.github?.scopes ?? DEFAULT_SCOPES.Github)
  || githubForm.clientSecret.length > 0
  || githubForm.clearSecret
  || slackForm.clientId !== (data.value?.slack?.clientId ?? '')
  || slackForm.scopes !== (data.value?.slack?.scopes ?? DEFAULT_SCOPES.Slack)
  || slackForm.clientSecret.length > 0
  || slackForm.clearSecret
  || false,
)
// google* refs stay declared (still hydrated by the response watcher) in case the v1.x
// re-enable lands quickly — they're not surfaced in the UI right now.
void googleForm
void googleConfigured
void googleSecretMasked

const anyDirty = computed(() => accessDirty.value || alertsDirty.value || apiDirty.value || oauthDirty.value)

interface SectionDef {
  id: string
  titleKey: string
  descriptionKey: string
  dirty: () => boolean
}

const sections: SectionDef[] = [
  { id: 'access', titleKey: 'screen.settings.section_access.title', descriptionKey: 'screen.settings.section_access.description', dirty: () => accessDirty.value },
  { id: 'oauth', titleKey: 'screen.settings.section_oauth.title', descriptionKey: 'screen.settings.section_oauth.description', dirty: () => oauthDirty.value },
  { id: 'alerts', titleKey: 'screen.settings.section_alerts.title', descriptionKey: 'screen.settings.section_alerts.description', dirty: () => alertsDirty.value },
  { id: 'exposure', titleKey: 'screen.settings.section_exposure.title', descriptionKey: 'screen.settings.section_exposure.description', dirty: () => false },
  { id: 'api', titleKey: 'screen.settings.section_api.title', descriptionKey: 'screen.settings.section_api.description', dirty: () => apiDirty.value },
  { id: 'auto-fix', titleKey: 'screen.settings.section_auto_fix.title', descriptionKey: 'screen.settings.section_auto_fix.description', dirty: () => false },
]

const sourcesQuery = useSourcesQuery()
const autoFixMutation = useUpdateSourceAutoFixModeMutation()

async function onAutoFixModeChange(sourceId: number, mode: AutoFixMode): Promise<void> {
  try {
    await autoFixMutation.mutateAsync({ id: sourceId, autoFixMode: mode })
  }
  catch {
    push('error', t('screen.settings.auto_fix_save_error'))
  }
}

// Tabbed nav (URL-synced via ?tab=). Only the active section mounts — no scroll-on-click,
// no off-screen forms holding focus, no laggy reactive recomputes for hidden tabs.
const settingsRoute = useRoute()
const settingsRouter = useRouter()
const activeTab = ref<string>(
  typeof settingsRoute.query.tab === 'string' && sections.some(section => section.id === settingsRoute.query.tab)
    ? (settingsRoute.query.tab as string)
    : sections[0].id,
)
function selectTab(id: string): void {
  if (activeTab.value === id) return
  activeTab.value = id
  settingsRouter.replace({ query: { ...settingsRoute.query, tab: id } })
}
watch(() => settingsRoute.query.tab, (next) => {
  if (typeof next === 'string' && sections.some(section => section.id === next) && next !== activeTab.value)
    activeTab.value = next
})

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
      // Google patch intentionally null — UI was removed in favour of copy/share invite delivery.
      google: null,
    })
    oidcClientSecret.value = ''
    githubForm.clientSecret = ''
    githubForm.clearSecret = false
    slackForm.clientSecret = ''
    slackForm.clearSecret = false
    googleForm.clientSecret = ''
    googleForm.clearSecret = false
    push('success', t('screen.settings.saved_toast'))
  }
  catch {
    push('error', t('screen.settings.save_failed_toast'))
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
      push('success', t('oidc.discovery_success'))
    else
      push('error', result.error ?? t('oidc.discovery_failed'))
  }
  catch {
    push('error', t('oidc.test_error'))
  }
}
</script>

<template>
  <div class="space-y-6">
    <header class="flex flex-wrap items-end justify-between gap-3">
      <div>
        <h1 class="text-2xl font-semibold text-slate-100">{{ t('screen.settings.title') }}</h1>
        <p class="text-sm text-slate-400">{{ t('screen.settings.subtitle') }}</p>
      </div>
      <button
        type="button"
        :disabled="update.isPending.value || isLoading || !anyDirty"
        class="flex items-center gap-1.5 rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-blue-500 focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-blue-300 disabled:cursor-not-allowed disabled:bg-blue-900/60"
        @click="onSave"
      >
        <Save class="h-4 w-4" />
        {{ update.isPending.value ? t('screen.settings.saving') : t('screen.settings.save_btn') }}
      </button>
    </header>

    <p v-if="isLoading" class="text-sm text-slate-400">{{ t('screen.settings.loading') }}</p>
    <p v-else-if="isError" class="text-sm text-red-300">{{ t('screen.settings.load_failed') }}</p>

    <template v-if="data">
      <div class="grid gap-8 md:grid-cols-[12rem_minmax(0,1fr)]">
        <!-- Left rail: anchored nav (sticky on md+) -->
        <aside>
          <nav class="space-y-1 md:sticky md:top-2" role="tablist">
            <button
              v-for="section in sections"
              :key="section.id"
              type="button"
              role="tab"
              :aria-selected="activeTab === section.id"
              :class="[
                'flex w-full items-center justify-between gap-2 rounded-md px-3 py-2 text-sm transition-colors focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-blue-500',
                activeTab === section.id
                  ? 'bg-blue-600/20 text-blue-200'
                  : 'text-slate-400 hover:bg-slate-800/60 hover:text-slate-100',
              ]"
              @click="selectTab(section.id)"
            >
              <span>{{ t(section.titleKey) }}</span>
              <span
                v-if="section.dirty()"
                class="h-2 w-2 rounded-full bg-amber-400"
                :aria-label="t('screen.settings.unsaved_dot_label')"
              />
            </button>
          </nav>
        </aside>

        <div class="space-y-8">
          <!-- Section 1: Account & access -->
          <section v-if="activeTab === 'access'" id="access" role="tabpanel">
            <header class="mb-4 flex items-start gap-2">
              <div class="flex-1">
                <h2 class="flex items-center gap-2 text-base font-semibold text-slate-100">
                  <span>{{ t('screen.settings.section_access.title') }}</span>
                  <span
                    v-if="accessDirty"
                    class="h-2 w-2 rounded-full bg-amber-400"
                    :aria-label="t('screen.settings.unsaved_dot_label')"
                  />
                </h2>
                <p class="mt-1 text-sm text-slate-400">{{ t('screen.settings.section_access.description') }}</p>
              </div>
            </header>

            <div class="space-y-4">
              <label class="flex items-start justify-between gap-3">
                <span class="flex-1">
                  <span class="block text-sm text-slate-200">{{ t('screen.settings.section_access.single_user_label') }}</span>
                  <span class="mt-0.5 block text-xs text-slate-500">
                    {{ singleUserMode ? t('screen.settings.section_access.single_user_hint_on') : t('screen.settings.section_access.single_user_hint_off') }}
                  </span>
                </span>
                <input v-model="singleUserMode" type="checkbox" class="mt-1 h-4 w-4 accent-blue-500" />
              </label>

              <div class="space-y-3 rounded-md border border-slate-800 bg-slate-900/40 p-4">
                <label class="flex items-center justify-between gap-3 text-sm text-slate-200">
                  <span>{{ t('screen.settings.section_access.oidc_enabled_label') }}</span>
                  <input v-model="oidcEnabled" type="checkbox" class="h-4 w-4 accent-blue-500" />
                </label>

                <label class="block">
                  <span class="text-sm text-slate-300">{{ t('screen.settings.section_access.oidc_issuer_label') }}</span>
                  <input
                    v-model="oidcIssuer"
                    type="url"
                    name="shield-oidc-issuer"
                    autocomplete="off"
                    data-1p-ignore="true"
                    data-lpignore="true"
                    placeholder="https://issuer.example.com/realms/shield"
                    class="mt-1 w-full rounded-md border border-slate-700 bg-slate-950/60 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none focus-visible:ring-1 focus-visible:ring-blue-500"
                  />
                </label>

                <label class="block">
                  <span class="text-sm text-slate-300">{{ t('screen.settings.section_access.oidc_client_id_label') }}</span>
                  <input
                    v-model="oidcClientId"
                    name="shield-oidc-client-id"
                    autocomplete="off"
                    data-1p-ignore="true"
                    data-lpignore="true"
                    class="mt-1 w-full rounded-md border border-slate-700 bg-slate-950/60 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none focus-visible:ring-1 focus-visible:ring-blue-500"
                  />
                </label>

                <label class="block">
                  <span class="text-sm text-slate-300">
                    {{ t('screen.settings.section_access.oidc_client_secret_label') }}
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
                    :placeholder="t('screen.settings.field.leave_blank_keep_current')"
                    class="mt-1 w-full rounded-md border border-slate-700 bg-slate-950/60 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none focus-visible:ring-1 focus-visible:ring-blue-500"
                  />
                </label>

                <button
                  type="button"
                  :disabled="testOidc.isPending.value || !oidcIssuer"
                  class="flex items-center gap-1.5 rounded-md border border-slate-700 px-3 py-1.5 text-sm text-slate-200 transition-colors hover:bg-slate-800 focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-blue-500 disabled:opacity-50"
                  @click="onTestOidc"
                >
                  <Send class="h-4 w-4" />
                  {{ testOidc.isPending.value ? t('screen.settings.section_access.oidc_testing') : t('screen.settings.section_access.oidc_test_btn') }}
                </button>
              </div>
            </div>
          </section>

          <!-- Section 2: Code-host OAuth -->
          <section v-if="activeTab === 'oauth'" id="oauth" role="tabpanel">
            <header class="mb-4">
              <h2 class="flex items-center gap-2 text-base font-semibold text-slate-100">
                <span>{{ t('screen.settings.section_oauth.title') }}</span>
                <span
                  v-if="oauthDirty"
                  class="h-2 w-2 rounded-full bg-amber-400"
                  :aria-label="t('screen.settings.unsaved_dot_label')"
                />
              </h2>
              <p class="mt-1 text-sm text-slate-400">{{ t('screen.settings.section_oauth.description') }}</p>
              <p class="mt-2 text-xs text-slate-500">
                <RouterLink to="/welcome" class="text-blue-400 transition-colors hover:text-blue-300 hover:underline">
                  {{ t('settings_oauth.rerun_onboarding') }}
                </RouterLink>
              </p>
            </header>

            <div class="space-y-4">
              <GithubOauthSetupCard />
              <details class="rounded-md border border-slate-800 bg-slate-950/40 p-3">
                <summary class="cursor-pointer text-xs text-slate-300">{{ t('settings_oauth.github_scopes_summary') }}</summary>
                <p class="mt-2 text-xs text-slate-500">
                  Scopes pre-filled with everything Shield needs &mdash; <strong>do not narrow without thought:</strong>
                </p>
                <ul class="mt-1 list-disc pl-5 text-xs text-slate-500">
                  <li><code>read:user</code> &mdash; profile (for sign-in)</li>
                  <li><code>user:email</code> &mdash; verified email (for sign-in find-by-email)</li>
                  <li><code>repo</code> &mdash; <strong>required</strong> &mdash; read every private repo's lockfiles, and open pull requests for the one-click fix flow</li>
                  <li><code>read:org</code> &mdash; enumerate org memberships so the Sources UI can offer per-org bulk-add</li>
                </ul>
                <label class="mt-2 block">
                  <span class="text-xs text-slate-400">{{ t('settings_oauth.scopes_label') }}</span>
                  <input
                    v-model="githubForm.scopes"
                    name="shield-gh-scopes"
                    autocomplete="off"
                    data-1p-ignore="true"
                    data-lpignore="true"
                    placeholder="read:user public_repo"
                    class="mt-1 w-full rounded-md border border-slate-700 bg-slate-950/60 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none focus-visible:ring-1 focus-visible:ring-blue-500"
                  />
                </label>
              </details>

              <IntegrationCard
                provider="Slack"
                label="Slack"
                description="Deliver alerts to Slack channels and look up the channel list."
                :configured="slackConfigured"
              />
              <details class="rounded-md border border-slate-800 bg-slate-950/40 p-3">
                <summary class="cursor-pointer text-xs text-slate-300">{{ t('settings_oauth.slack_configure_summary') }}</summary>
                <p class="mt-2 text-xs text-slate-500">
                  Create the Slack app at
                  <a href="https://api.slack.com/apps" target="_blank" rel="noopener" class="text-blue-400 underline">api.slack.com/apps</a>.
                  Add an <strong>OAuth Redirect URL</strong> of
                  <code class="font-mono text-slate-300">{{ callbackBase }}/slack/callback</code>.
                </p>
                <label class="mt-2 block">
                  <span class="text-xs text-slate-400">{{ t('settings_oauth.client_id_label') }}</span>
                  <input
                    v-model="slackForm.clientId"
                    name="shield-sl-client-id"
                    autocomplete="off"
                    data-1p-ignore="true"
                    data-lpignore="true"
                    placeholder="Slack OAuth Client ID"
                    class="mt-1 w-full rounded-md border border-slate-700 bg-slate-950/60 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none focus-visible:ring-1 focus-visible:ring-blue-500"
                  />
                </label>
                <label class="mt-2 block">
                  <span class="text-xs text-slate-400">
                    {{ t('settings_oauth.client_secret_label') }}
                    <span v-if="slackSecretMasked" class="ml-2 font-mono text-slate-500">{{ t('settings_oauth.current_masked', { value: slackSecretMasked }) }}</span>
                  </span>
                  <input
                    v-model="slackForm.clientSecret"
                    type="password"
                    name="shield-sl-client-secret"
                    autocomplete="new-password"
                    data-1p-ignore="true"
                    data-lpignore="true"
                    :disabled="slackForm.clearSecret"
                    :placeholder="t('screen.settings.field.leave_blank_keep_current')"
                    class="mt-1 w-full rounded-md border border-slate-700 bg-slate-950/60 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none focus-visible:ring-1 focus-visible:ring-blue-500 disabled:opacity-50"
                  />
                </label>
                <label v-if="slackSecretMasked" class="mt-2 flex items-center gap-2 text-xs text-amber-300">
                  <input v-model="slackForm.clearSecret" type="checkbox" class="h-3 w-3 accent-amber-500" />
                  <span>{{ t('settings_oauth.clear_secret_label') }}</span>
                </label>
                <label class="mt-2 block">
                  <span class="text-xs text-slate-400">{{ t('settings_oauth.scopes_label') }}</span>
                  <input
                    v-model="slackForm.scopes"
                    name="shield-sl-scopes"
                    autocomplete="off"
                    data-1p-ignore="true"
                    data-lpignore="true"
                    placeholder="chat:write channels:read"
                    class="mt-1 w-full rounded-md border border-slate-700 bg-slate-950/60 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none focus-visible:ring-1 focus-visible:ring-blue-500"
                  />
                </label>
              </details>

              <!-- Google OAuth intentionally hidden — invitation delivery uses copy/share, not
                   email. The backend adapter stays registered for a future re-enable; the form
                   just isn't exposed in v1. -->
            </div>
          </section>

          <!-- Section 3: Alerts -->
          <section v-if="activeTab === 'alerts'" id="alerts" role="tabpanel">
            <header class="mb-4">
              <h2 class="flex items-center gap-2 text-base font-semibold text-slate-100">
                <span>{{ t('screen.settings.section_alerts.title') }}</span>
                <span
                  v-if="alertsDirty"
                  class="h-2 w-2 rounded-full bg-amber-400"
                  :aria-label="t('screen.settings.unsaved_dot_label')"
                />
              </h2>
              <p class="mt-1 text-sm text-slate-400">{{ t('screen.settings.section_alerts.description') }}</p>
            </header>

            <div class="space-y-4">
              <label class="block">
                <span class="text-sm text-slate-300">{{ t('screen.settings.section_alerts.severity_floor_label') }}</span>
                <select
                  v-model="alertSeverityFloor"
                  class="mt-1 w-full rounded-md border border-slate-700 bg-slate-950/60 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none focus-visible:ring-1 focus-visible:ring-blue-500"
                >
                  <option value="Low">{{ t('severity.low') }}</option>
                  <option value="Medium">{{ t('severity.medium') }}</option>
                  <option value="High">{{ t('severity.high') }}</option>
                  <option value="Critical">{{ t('severity.critical') }}</option>
                </select>
                <span class="mt-1 block text-xs text-slate-500">{{ t('screen.settings.section_alerts.severity_floor_hint') }}</span>
              </label>

              <label class="block">
                <span class="text-sm text-slate-300">{{ t('screen.settings.section_alerts.retention_label') }}</span>
                <div class="mt-1 flex items-center gap-2">
                  <input
                    v-model.number="retentionDays"
                    type="number"
                    min="1"
                    max="3650"
                    inputmode="numeric"
                    class="w-32 rounded-md border border-slate-700 bg-slate-950/60 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none focus-visible:ring-1 focus-visible:ring-blue-500"
                  />
                  <span class="text-xs text-slate-500">{{ t('screen.settings.field.retention_days_suffix') }}</span>
                </div>
                <span class="mt-1 block text-xs text-slate-500">{{ t('screen.settings.section_alerts.retention_hint') }}</span>
              </label>

              <PushNotificationsCard />
            </div>
          </section>

          <!-- Section 4: Public exposure (read-only env-driven) -->
          <section v-if="activeTab === 'exposure'" id="exposure" role="tabpanel">
            <header class="mb-4">
              <h2 class="text-base font-semibold text-slate-100">{{ t('screen.settings.section_exposure.title') }}</h2>
              <p class="mt-1 text-sm text-slate-400">{{ t('screen.settings.section_exposure.description') }}</p>
            </header>

            <dl class="space-y-2 rounded-md border border-slate-800 bg-slate-950/40 p-4 text-sm">
              <div class="flex items-center justify-between gap-3">
                <dt class="text-slate-400">{{ t('screen.settings.section_exposure.public_label') }}</dt>
                <dd class="font-mono text-xs text-slate-300">{{ runtime?.environment ?? t('screen.settings.section_exposure.value_unset') }}</dd>
              </div>
              <div class="flex items-center justify-between gap-3">
                <dt class="text-slate-400">{{ t('screen.settings.section_exposure.require_https_label') }}</dt>
                <dd class="font-mono text-xs text-slate-300">{{ t('screen.settings.section_exposure.value_unset') }}</dd>
              </div>
              <div class="flex items-center justify-between gap-3">
                <dt class="text-slate-400">{{ t('screen.settings.section_exposure.cookie_domain_label') }}</dt>
                <dd class="font-mono text-xs text-slate-300">{{ t('screen.settings.section_exposure.value_unset') }}</dd>
              </div>
            </dl>
            <p class="mt-3 text-xs">
              <a
                href="https://github.com/NoMercyLabs/shield/blob/master/docs/exposure.md"
                target="_blank"
                rel="noopener"
                class="inline-flex items-center gap-1 text-blue-400 transition-colors hover:text-blue-300"
              >
                <ExternalLink class="h-3 w-3" />
                {{ t('screen.settings.section_exposure.docs_link') }}
              </a>
            </p>
          </section>

          <!-- Section 5: API -->
          <section v-if="activeTab === 'api'" id="api" role="tabpanel">
            <header class="mb-4">
              <h2 class="flex items-center gap-2 text-base font-semibold text-slate-100">
                <span>{{ t('screen.settings.section_api.title') }}</span>
                <span
                  v-if="apiDirty"
                  class="h-2 w-2 rounded-full bg-amber-400"
                  :aria-label="t('screen.settings.unsaved_dot_label')"
                />
              </h2>
              <p class="mt-1 text-sm text-slate-400">{{ t('screen.settings.section_api.description') }}</p>
            </header>

            <div class="space-y-3">
              <label class="flex items-start justify-between gap-3">
                <span class="flex-1">
                  <span class="block text-sm text-slate-200">{{ t('screen.settings.section_api.openapi_label') }}</span>
                  <span class="mt-0.5 block text-xs text-slate-500">{{ t('screen.settings.section_api.openapi_hint') }}</span>
                </span>
                <input v-model="openApiEnabled" type="checkbox" class="mt-1 h-4 w-4 accent-blue-500" />
              </label>
              <p v-if="openApiEnabled" class="text-xs">
                <a
                  href="/swagger"
                  target="_blank"
                  rel="noopener"
                  class="inline-flex items-center gap-1 text-blue-400 transition-colors hover:text-blue-300"
                >
                  <ExternalLink class="h-3 w-3" />
                  {{ t('screen.settings.section_api.swagger_link') }}
                </a>
              </p>
            </div>
          </section>

          <!-- Runtime (always shown, no section header decoration) -->
          <section v-if="runtime && activeTab === 'exposure'" class="mt-8 border-t border-slate-800 pt-6">
            <h2 class="text-base font-semibold text-slate-100">Runtime</h2>
            <dl class="mt-3 space-y-1 text-xs">
              <div class="flex justify-between gap-3">
                <dt class="text-slate-400">Version</dt>
                <dd class="font-mono text-slate-300">{{ runtime.version }}</dd>
              </div>
              <div class="flex justify-between gap-3">
                <dt class="text-slate-400">Environment</dt>
                <dd class="font-mono text-slate-300">{{ runtime.environment }}</dd>
              </div>
              <div class="flex justify-between gap-3">
                <dt class="text-slate-400">Content root</dt>
                <dd class="font-mono text-slate-300">{{ runtime.contentRoot }}</dd>
              </div>
              <div class="flex justify-between gap-3">
                <dt class="text-slate-400">Web root</dt>
                <dd class="font-mono text-slate-300">{{ runtime.webRoot || t('screen.settings.section_exposure.value_unset') }}</dd>
              </div>
            </dl>
          </section>

          <section v-if="activeTab === 'auto-fix'" id="auto-fix" role="tabpanel">
            <h2 class="text-base font-semibold text-slate-100">{{ t('screen.settings.section_auto_fix.title') }}</h2>
            <p class="mt-1 text-sm text-slate-400">{{ t('screen.settings.section_auto_fix.description') }}</p>
            <p v-if="sourcesQuery.isLoading.value" class="mt-4 text-sm text-slate-400">{{ t('state.loading') }}</p>
            <p v-else-if="sourcesQuery.isError.value" class="mt-4 text-sm text-red-300">{{ t('state.error') }}</p>
            <table v-else-if="sourcesQuery.data.value && sourcesQuery.data.value.length" class="mt-4 w-full text-sm">
              <thead class="text-xs uppercase text-slate-500">
                <tr>
                  <th class="py-1 pr-4 text-left">{{ t('screen.settings.section_auto_fix.col_source') }}</th>
                  <th class="py-1 text-left">{{ t('screen.settings.section_auto_fix.col_mode') }}</th>
                </tr>
              </thead>
              <tbody class="divide-y divide-slate-800">
                <tr v-for="src in sourcesQuery.data.value" :key="src.id">
                  <td class="py-2 pr-4 font-medium text-slate-100">{{ src.name }}</td>
                  <td class="py-2">
                    <select
                      :value="src.autoFixMode"
                      class="rounded border border-slate-700 bg-slate-800 px-2 py-1 text-xs text-slate-200 focus:border-blue-500 focus:outline-none"
                      @change="onAutoFixModeChange(src.id, Number(($event.target as HTMLSelectElement).value) as AutoFixMode)"
                    >
                      <option v-for="entry in autoFixModeOptions" :key="entry.value" :value="entry.value">
                        {{ t(`screen.settings.auto_fix_mode.${entry.name}`) }}
                      </option>
                    </select>
                  </td>
                </tr>
              </tbody>
            </table>
            <p v-else class="mt-4 text-sm text-slate-500">{{ t('screen.settings.section_auto_fix.no_sources') }}</p>
          </section>
        </div>
      </div>
    </template>
  </div>
</template>
