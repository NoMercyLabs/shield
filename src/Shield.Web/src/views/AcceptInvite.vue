<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { AlertCircle, Github, Loader2 } from 'lucide-vue-next'
import axios from 'axios'
import { useI18n } from 'vue-i18n'

import { acceptInvite, fetchInvitePreview } from '@/queries/access'
import { api } from '@/lib/api'
import { bootstrapAuth, oauthSignin, useAuth } from '@/stores/auth'
import type { Me, PublicInvitePreview } from '@/types/api'

const { t } = useI18n()
const route = useRoute()
const router = useRouter()

const token = computed(() => {
  const raw = route.query.token
  if (typeof raw !== 'string') return ''
  return raw.trim()
})

const { isAuthenticated, user } = useAuth()
// Synthetic single-user has no external login bound, so the auto-accept path 400s
// ("No external login bound"). Treat it as "not eligible to claim this invite" — the
// invitee must open the link in their own browser and sign in with their own code-host
// account. The GitHub button stays visible so the same admin in another browser can
// still claim if they need to.
const isSingleUserSession = computed<boolean>(() => user.value?.singleUserMode === true)
const isClaimableSession = computed<boolean>(() => isAuthenticated.value && !isSingleUserSession.value)

const preview = ref<PublicInvitePreview | null>(null)
const previewError = ref<string | null>(null)
const loading = ref(true)

const accepting = ref(false)
const acceptError = ref<string | null>(null)

async function loadPreview(): Promise<void> {
  loading.value = true
  previewError.value = null
  if (!token.value) {
    previewError.value = 'missing_token'
    loading.value = false
    return
  }
  try {
    preview.value = await fetchInvitePreview(token.value)
  }
  catch (error: unknown) {
    if (axios.isAxiosError(error)) {
      const status = error.response?.status
      if (status === 404)
        previewError.value = 'not_found'
      else if (status === 410)
        previewError.value = 'expired_or_revoked'
      else
        previewError.value = 'network'
    }
    else {
      previewError.value = 'network'
    }
  }
  finally {
    loading.value = false
  }
}

async function onAccept(): Promise<void> {
  // No ticket — backend reads the current authenticated identity from the session cookie.
  accepting.value = true
  acceptError.value = null
  try {
    await acceptInvite({ token: token.value })
    await bootstrapAuth()
    router.push('/sources')
  }
  catch (error: unknown) {
    if (axios.isAxiosError(error)) {
      const status = error.response?.status
      const code = (error.response?.data as { code?: string } | undefined)?.code
      if (status === 410)
        acceptError.value = 'expired_or_revoked'
      else if (code === 'single_user_session' || code === 'no_external_login' || code === 'no_session')
        acceptError.value = 'auth_required'
      else if (code === 'ticket_invalid')
        acceptError.value = 'missing_ticket'
      else if (status === 400 || status === 401 || status === 403)
        acceptError.value = 'auth_required'
      else
        acceptError.value = 'accept_failed'
    }
    else {
      acceptError.value = 'accept_failed'
    }
  }
  finally {
    accepting.value = false
  }
}

async function onSignInWithGithub(): Promise<void> {
  // Full-page redirect to /api/oauth/github/start?intent=signin&redirect=<return-here>.
  // GitHub round-trips back to /api/oauth/github/callback which sets the auth cookie and
  // redirects to the return URL. AcceptInvite re-renders authenticated and auto-POSTs the
  // invite acceptance.
  await oauthSignin('github', `/accept-invite?token=${encodeURIComponent(token.value)}`)
}

async function onMountedAutoAccept(): Promise<void> {
  // Returning from the OAuth round-trip: SPA has a fresh cookie. Fire accept-invite once
  // the auth bootstrap has resolved so the cookie reaches the server. If the user landed
  // here NOT-authed (first visit), wait for them to click Sign in with GitHub.
  if (!isAuthenticated.value) {
    // Refresh /me one more time in case bootstrap raced the navigation.
    try {
      const { data } = await api.get<Me>('/auth/me')
      if (data.userId) {
        await bootstrapAuth()
      }
    }
    catch { /* still anonymous — show the GitHub button */ }
  }
  // SingleUser principals can't accept (synthetic admin has no external login bound).
  // Render the GitHub button so the invitee can sign in with their own identity.
  if (isClaimableSession.value && token.value && !accepting.value && !acceptError.value)
    void onAccept()
}

onMounted(async () => {
  await loadPreview()
  await onMountedAutoAccept()
})
</script>

<template>
  <div class="min-h-screen bg-slate-950 px-4 py-12 text-slate-100">
    <div class="mx-auto max-w-md space-y-4">
      <header class="space-y-1">
        <h1 class="text-2xl font-semibold">{{ t('accept_invite_view.title') }}</h1>
        <p class="text-sm text-slate-400">{{ t('accept_invite_view.subtitle') }}</p>
      </header>

      <div
        v-if="loading"
        class="flex items-center gap-2 rounded border border-slate-800 bg-slate-900 p-4 text-sm text-slate-300"
      >
        <Loader2 class="h-4 w-4 animate-spin" />
        {{ t('accept_invite_view.loading') }}
      </div>

      <div
        v-else-if="previewError"
        class="space-y-2 rounded border border-amber-700 bg-amber-900/40 p-4 text-sm text-amber-100"
      >
        <div class="flex items-center gap-2 font-medium">
          <AlertCircle class="h-4 w-4" />
          {{ t('accept_invite_view.invalid_title') }}
        </div>
        <p class="text-xs text-amber-200">
          <template v-if="previewError === 'not_found'">{{ t('accept_invite_view.error_not_found') }}</template>
          <template v-else-if="previewError === 'expired_or_revoked'">{{ t('accept_invite_view.error_expired') }}</template>
          <template v-else-if="previewError === 'missing_token'">{{ t('accept_invite_view.error_missing_token') }}</template>
          <template v-else>{{ t('accept_invite_view.error_network') }}</template>
        </p>
        <button
          type="button"
          class="rounded bg-amber-800 px-3 py-1 text-xs font-medium text-amber-50 hover:bg-amber-700"
          @click="router.push('/login')"
        >
          {{ t('accept_invite_view.go_signin') }}
        </button>
      </div>

      <div
        v-else-if="preview"
        class="space-y-4 rounded border border-slate-800 bg-slate-900 p-4"
      >
        <dl class="space-y-2 text-sm">
          <div class="flex justify-between">
            <dt class="text-slate-400">{{ t('accept_invite_view.invited_by') }}</dt>
            <dd class="text-slate-100">{{ preview.inviterLogin }}</dd>
          </div>
          <div class="flex justify-between">
            <dt class="text-slate-400">{{ t('accept_invite_view.role_label') }}</dt>
            <dd class="text-slate-100">{{ preview.role }}</dd>
          </div>
          <div class="flex justify-between">
            <dt class="text-slate-400">{{ t('accept_invite_view.source_groups') }}</dt>
            <dd class="text-slate-100">
              <span v-if="preview.sourceGroupNames.length">{{ preview.sourceGroupNames.join(', ') }}</span>
              <span v-else class="text-slate-500">{{ t('accept_invite_view.no_groups') }}</span>
            </dd>
          </div>
        </dl>

        <div class="border-t border-slate-800 pt-3">
          <p class="mb-3 text-xs text-slate-400">{{ t('accept_invite_view.signin_hint') }}</p>

          <button
            v-if="(!isAuthenticated || isSingleUserSession) && !accepting"
            type="button"
            class="flex w-full items-center justify-center gap-2 rounded bg-slate-100 px-3 py-2 text-sm font-medium text-slate-900 hover:bg-white"
            @click="onSignInWithGithub"
          >
            <Github class="h-4 w-4" />
            {{ t('accept_invite_view.signin_github_btn') }}
          </button>

          <div
            v-if="accepting"
            class="flex items-center gap-2 text-sm text-slate-300"
          >
            <Loader2 class="h-4 w-4 animate-spin" />
            {{ t('accept_invite_view.confirming') }}
          </div>

          <div v-if="acceptError" class="mt-2 space-y-2 text-xs text-red-300">
            <template v-if="acceptError === 'expired_or_revoked'">{{ t('accept_invite_view.accept_error_expired') }}</template>
            <template v-else-if="acceptError === 'auth_required'">{{ t('accept_invite_view.accept_error_auth') }}</template>
            <template v-else>{{ t('accept_invite_view.accept_error_generic') }}</template>
            <button
              v-if="acceptError === 'auth_required'"
              type="button"
              class="rounded border border-slate-700 px-2 py-1 text-slate-200 hover:bg-slate-800"
              @click="onSignInWithGithub"
            >
              {{ t('accept_invite_view.signin_github_btn') }}
            </button>
          </div>
        </div>

        <p
          v-if="acceptError"
          class="rounded border border-red-700 bg-red-900/40 p-2 text-xs text-red-200"
        >
          <template v-if="acceptError === 'missing_ticket'">{{ t('accept_invite_view.accept_error_ticket') }}</template>
          <template v-else-if="acceptError === 'expired_or_revoked'">{{ t('accept_invite_view.accept_error_revoked') }}</template>
          <template v-else>{{ t('accept_invite_view.accept_error_retry') }}</template>
        </p>
      </div>
    </div>
  </div>
</template>
