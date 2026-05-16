<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'

import { type AuthProvider, fetchAuthProviders, login, oauthSignin } from '@/stores/auth'

const router = useRouter()
const route = useRoute()
const { t } = useI18n()

const username = ref('')
const password = ref('')
const totpCode = ref('')
const error = ref<string | null>(null)
const submitting = ref(false)
const providers = ref<AuthProvider[]>([])

onMounted(async () => {
  providers.value = await fetchAuthProviders()
  // Server may redirect back here with ?oauth_signin_rejected=<reason> when the callback
  // refused to create a new user. Surface that as an inline error.
  if (typeof route.query.oauth_signin_rejected === 'string')
    error.value = t('error.oauth_rejected', { reason: route.query.oauth_signin_rejected })
})

async function onSubmit(): Promise<void> {
  error.value = null
  submitting.value = true
  try {
    await login(username.value, password.value, totpCode.value || undefined)
    const redirect = typeof route.query.redirect === 'string' ? route.query.redirect : '/'
    router.replace(redirect)
  }
  catch (err) {
    error.value = err instanceof Error ? err.message : t('error.signin_failed')
  }
  finally {
    submitting.value = false
  }
}

async function onOauth(provider: string): Promise<void> {
  error.value = null
  const redirect = typeof route.query.redirect === 'string' ? route.query.redirect : '/'
  try {
    await oauthSignin(provider, redirect)
  }
  catch (err) {
    error.value = err instanceof Error ? err.message : t('error.oauth_start_failed', { provider })
  }
}
</script>

<template>
  <div class="flex min-h-screen items-center justify-center bg-slate-950 p-4 text-slate-100">
    <form
      class="w-full max-w-sm space-y-4 rounded-lg border border-slate-800 bg-slate-900 p-6 shadow-xl"
      @submit.prevent="onSubmit"
    >
      <div>
        <h1 class="text-2xl font-semibold">Shield</h1>
        <p class="text-sm text-slate-400">{{ t('screen.signin.subtitle') }}</p>
      </div>

      <div v-if="providers.length > 0" class="space-y-2">
        <button
          v-for="provider in providers"
          :key="provider.provider"
          type="button"
          class="flex w-full items-center justify-center gap-2 rounded border border-slate-700 bg-slate-800 px-3 py-2 text-sm font-medium text-slate-100 hover:bg-slate-700"
          @click="onOauth(provider.provider)"
        >
          <img :src="provider.iconUrl" :alt="provider.displayName" class="h-4 w-4" />
          {{ t('screen.signin.with_provider', { provider: provider.displayName }) }}
        </button>
        <div class="relative pt-2">
          <div class="absolute inset-0 flex items-center"><div class="h-px w-full bg-slate-800" /></div>
          <div class="relative flex justify-center"><span class="bg-slate-900 px-2 text-xs text-slate-500">{{ t('auth.or') }}</span></div>
        </div>
      </div>

      <label class="block">
        <span class="text-sm text-slate-300">{{ t('field.username') }}</span>
        <input
          v-model="username"
          type="text"
          autocomplete="username"
          required
          class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
        />
      </label>

      <label class="block">
        <span class="text-sm text-slate-300">{{ t('field.password') }}</span>
        <input
          v-model="password"
          type="password"
          autocomplete="current-password"
          required
          class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
        />
      </label>

      <label class="block">
        <span class="text-sm text-slate-300">{{ t('field.two_factor_code_optional') }}</span>
        <input
          v-model="totpCode"
          type="text"
          inputmode="numeric"
          autocomplete="one-time-code"
          class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
        />
      </label>

      <p v-if="error" class="rounded border border-red-700 bg-red-900/40 px-3 py-2 text-sm text-red-200">
        {{ error }}
      </p>

      <button
        type="submit"
        :disabled="submitting"
        class="w-full rounded bg-blue-600 px-3 py-2 text-sm font-medium text-white hover:bg-blue-500 disabled:cursor-not-allowed disabled:bg-blue-900"
      >
        {{ submitting ? t('screen.signin.submitting') : t('screen.signin.submit_btn') }}
      </button>
    </form>
  </div>
</template>
