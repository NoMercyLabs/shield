<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink, useRoute, useRouter } from 'vue-router'
import { Github, ShieldCheck } from 'lucide-vue-next'

import DeviceLoginPanel from '@/components/DeviceLoginPanel.vue'
import {
  type AuthProvider,
  fetchAuthProviders,
  fetchExternalLoginProviders,
  fetchRegistrationAllowed,
  login,
  oauthSignin,
} from '@/stores/auth'
import type { ExternalLoginIdentity, ExternalLoginProvider } from '@/types/external-login'

const router = useRouter()
const route = useRoute()
const { t } = useI18n()

const username = ref('')
const password = ref('')
const totpCode = ref('')
const formError = ref<string | null>(null)
const fieldErrors = ref<{ username?: string, password?: string }>({})
const submitting = ref(false)
const providers = ref<AuthProvider[]>([])
const externalProviders = ref<ExternalLoginProvider[]>([])
const registrationAllowed = ref(false)

const activeExternal = ref<ExternalLoginProvider | null>(null)
const needsInvite = ref<ExternalLoginIdentity | null>(null)
const devicePanel = ref<InstanceType<typeof DeviceLoginPanel> | null>(null)

const ICON_MAP: Record<string, typeof Github> = {
  github: Github,
}

const hasExternalChoice = computed(() => providers.value.length > 0 || externalProviders.value.length > 0)

onMounted(async () => {
  const [authProviders, extProviders, registration] = await Promise.all([
    fetchAuthProviders(),
    fetchExternalLoginProviders(),
    fetchRegistrationAllowed().catch(() => ({ allowed: false, reason: null })),
  ])
  // Auth-code (private OAuth App with client_secret) wins over device-flow (public baked
  // client_id) per-provider. Operator chose to register their own App = they want the
  // popup. We only fall back to device-flow when the instance has NO private client
  // configured for that provider — that's the "onboarding-ease" path for fresh self-hosted
  // installs. Net effect: exactly one "Sign in with X" button per provider.
  providers.value = authProviders
  const privateKeys = new Set(authProviders.map(provider => provider.provider.toLowerCase()))
  externalProviders.value = extProviders.filter(provider => !privateKeys.has(provider.key.toLowerCase()))
  registrationAllowed.value = registration.allowed
  if (typeof route.query.oauth_signin_rejected === 'string')
    formError.value = t('error.oauth_rejected', { reason: route.query.oauth_signin_rejected })
})

async function onSubmit(): Promise<void> {
  formError.value = null
  fieldErrors.value = {}
  if (!username.value)
    fieldErrors.value.username = t('error.signin_failed')
  if (!password.value)
    fieldErrors.value.password = t('error.signin_failed')
  if (fieldErrors.value.username || fieldErrors.value.password)
    return

  submitting.value = true
  try {
    await login(username.value, password.value, totpCode.value || undefined)
    const redirect = typeof route.query.redirect === 'string' ? route.query.redirect : '/'
    router.replace(redirect)
  }
  catch (err) {
    formError.value = err instanceof Error ? err.message : t('error.signin_failed')
  }
  finally {
    submitting.value = false
  }
}

async function onOauth(provider: string): Promise<void> {
  formError.value = null
  const redirect = typeof route.query.redirect === 'string' ? route.query.redirect : '/'
  try {
    await oauthSignin(provider, redirect)
  }
  catch (err) {
    formError.value = err instanceof Error ? err.message : t('error.oauth_start_failed', { provider })
  }
}

async function startExternal(provider: ExternalLoginProvider): Promise<void> {
  formError.value = null
  needsInvite.value = null
  activeExternal.value = provider
  await new Promise(resolve => setTimeout(resolve, 0))
  await devicePanel.value?.startFlow()
}

function onSignedIn(): void {
  const redirect = typeof route.query.redirect === 'string' ? route.query.redirect : '/'
  router.replace(redirect)
}

function onNeedsInvite(payload: { identity: ExternalLoginIdentity, acceptanceTicket: string }): void {
  // LoginView surfaces "ask the admin to invite you" — it doesn't auto-accept (no token in
  // this context). The acceptanceTicket is discarded here; the dedicated accept-invite
  // page consumes it. If the operator later clicks the invite link, DeviceLoginPanel runs
  // a fresh flow with a fresh ticket.
  activeExternal.value = null
  needsInvite.value = payload.identity
}

function onCancel(): void {
  activeExternal.value = null
}

function resetInviteState(): void {
  needsInvite.value = null
}

function iconFor(key: string): typeof Github | null {
  return ICON_MAP[key] ?? null
}
</script>

<template>
  <div class="grid min-h-screen place-items-center bg-slate-950 px-4 py-10 text-slate-100">
    <div class="w-full max-w-md">
      <article
        v-if="needsInvite"
        class="space-y-3 rounded-xl border border-amber-700/60 bg-amber-950/30 p-6 shadow-2xl"
      >
        <h1 class="text-xl font-semibold text-amber-100">{{ t('screen.signin.needs_invite.title') }}</h1>
        <p class="text-sm text-amber-100/80">
          {{ t('screen.signin.needs_invite.body', { provider: needsInvite.provider, login: needsInvite.login }) }}
        </p>
        <dl class="space-y-1 rounded border border-amber-700/40 bg-amber-950/40 p-3 text-xs">
          <div class="flex justify-between gap-2">
            <dt class="text-amber-200/70">{{ t('screen.signin.needs_invite.field_provider') }}</dt>
            <dd class="font-mono text-amber-100">{{ needsInvite.provider }}</dd>
          </div>
          <div class="flex justify-between gap-2">
            <dt class="text-amber-200/70">{{ t('screen.signin.needs_invite.field_login') }}</dt>
            <dd class="font-mono text-amber-100">{{ needsInvite.login }}</dd>
          </div>
          <div v-if="needsInvite.email" class="flex justify-between gap-2">
            <dt class="text-amber-200/70">{{ t('screen.signin.needs_invite.field_email') }}</dt>
            <dd class="font-mono text-amber-100">{{ needsInvite.email }}</dd>
          </div>
        </dl>
        <button
          type="button"
          class="w-full rounded-md border border-amber-700 px-3 py-2 text-sm text-amber-100 transition-colors hover:bg-amber-900/40 focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-amber-500"
          @click="resetInviteState"
        >
          {{ t('screen.signin.needs_invite.back_btn') }}
        </button>
      </article>

      <div
        v-else
        class="space-y-5 rounded-xl border border-slate-800 bg-slate-900/80 p-7 shadow-2xl backdrop-blur"
      >
        <header class="flex flex-col items-center gap-2 text-center">
          <div class="rounded-full bg-blue-500/10 p-3 ring-1 ring-blue-500/30">
            <ShieldCheck class="h-7 w-7 text-blue-400" />
          </div>
          <h1 class="text-xl font-semibold text-slate-100">{{ t('screen.signin.title') }}</h1>
          <p class="text-sm text-slate-400">{{ t('screen.signin.subtitle') }}</p>
        </header>

        <p v-if="formError" class="rounded-md border border-red-700/50 bg-red-900/30 px-3 py-2 text-sm text-red-300">
          {{ formError }}
        </p>

        <form v-if="!activeExternal" class="space-y-3" @submit.prevent="onSubmit">
          <label class="block">
            <span class="mb-1 block text-sm text-slate-300">{{ t('field.username') }}</span>
            <input
              v-model="username"
              type="text"
              autocomplete="username"
              required
              class="w-full rounded-md border border-slate-700 bg-slate-950/60 px-3 py-2 text-sm text-slate-100 transition-colors focus:border-blue-500 focus:outline-none focus-visible:ring-1 focus-visible:ring-blue-500"
              :class="{ 'border-red-500/70': fieldErrors.username }"
            />
            <span v-if="fieldErrors.username" class="mt-1 block text-xs text-red-300">{{ fieldErrors.username }}</span>
          </label>

          <label class="block">
            <span class="mb-1 block text-sm text-slate-300">{{ t('field.password') }}</span>
            <input
              v-model="password"
              type="password"
              autocomplete="current-password"
              required
              class="w-full rounded-md border border-slate-700 bg-slate-950/60 px-3 py-2 text-sm text-slate-100 transition-colors focus:border-blue-500 focus:outline-none focus-visible:ring-1 focus-visible:ring-blue-500"
              :class="{ 'border-red-500/70': fieldErrors.password }"
            />
            <span v-if="fieldErrors.password" class="mt-1 block text-xs text-red-300">{{ fieldErrors.password }}</span>
          </label>

          <label class="block">
            <span class="mb-1 block text-sm text-slate-300">{{ t('field.two_factor_code_optional') }}</span>
            <input
              v-model="totpCode"
              type="text"
              inputmode="numeric"
              autocomplete="one-time-code"
              class="w-full rounded-md border border-slate-700 bg-slate-950/60 px-3 py-2 text-sm text-slate-100 transition-colors focus:border-blue-500 focus:outline-none focus-visible:ring-1 focus-visible:ring-blue-500"
            />
          </label>

          <button
            type="submit"
            :disabled="submitting"
            class="w-full rounded-md bg-blue-600 px-3 py-2 text-sm font-medium text-white transition-colors hover:bg-blue-500 focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-blue-300 disabled:cursor-not-allowed disabled:bg-blue-900/60"
          >
            {{ submitting ? t('screen.signin.submitting') : t('screen.signin.submit_btn') }}
          </button>
        </form>

        <div v-if="hasExternalChoice && !activeExternal" class="flex items-center gap-3">
          <span class="h-px flex-1 bg-slate-800" />
          <span class="text-xs uppercase tracking-wide text-slate-500">{{ t('screen.signin.or_divider') }}</span>
          <span class="h-px flex-1 bg-slate-800" />
        </div>

        <div v-if="!activeExternal && hasExternalChoice" class="space-y-2">
          <button
            v-for="provider in providers"
            :key="`oauth-${provider.provider}`"
            type="button"
            class="flex w-full items-center gap-3 rounded-md border border-slate-700 bg-slate-950/40 px-3 py-2 text-sm font-medium text-slate-100 transition-colors hover:bg-slate-800/60 focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-blue-500"
            @click="onOauth(provider.provider)"
          >
            <img :src="provider.iconUrl" :alt="provider.displayName" class="h-4 w-4 shrink-0" />
            <span class="flex-1 text-left">{{ t('screen.signin.with_provider', { provider: provider.displayName }) }}</span>
          </button>
          <button
            v-for="provider in externalProviders"
            :key="`ext-${provider.key}`"
            type="button"
            class="flex w-full items-center gap-3 rounded-md border border-slate-700 bg-slate-950/40 px-3 py-2 text-sm font-medium text-slate-100 transition-colors hover:bg-slate-800/60 focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-blue-500"
            @click="startExternal(provider)"
          >
            <component :is="iconFor(provider.iconKey)" v-if="iconFor(provider.iconKey)" class="h-4 w-4 shrink-0" />
            <span v-else class="h-4 w-4 shrink-0" />
            <span class="flex-1 text-left">{{ t('screen.signin.with_provider', { provider: provider.displayName }) }}</span>
          </button>
        </div>

        <DeviceLoginPanel
          v-if="activeExternal"
          ref="devicePanel"
          :provider-key="activeExternal.key"
          :display-name="activeExternal.displayName"
          :return-path="typeof route.query.redirect === 'string' ? route.query.redirect : '/'"
          @signed-in="onSignedIn"
          @needs-invite="onNeedsInvite"
          @cancel="onCancel"
        />

        <p class="pt-1 text-center text-xs text-slate-500">
          <template v-if="registrationAllowed">
            {{ t('screen.signin.no_account_prompt') }}
            <RouterLink to="/register" class="text-blue-400 transition-colors hover:text-blue-300">
              {{ t('screen.signin.register_link') }}
            </RouterLink>
          </template>
          <template v-else>
            {{ t('screen.signin.invite_only') }}
          </template>
        </p>
      </div>
    </div>
  </div>
</template>
