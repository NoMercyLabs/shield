<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'

import { type AuthProvider, fetchAuthProviders, fetchRegistrationAllowed, oauthSignin, register } from '@/stores/auth'
import { useToasts } from '@/stores/toast'

const router = useRouter()
const { push } = useToasts()
const { t } = useI18n()

const username = ref('')
const password = ref('')
const confirm = ref('')
const error = ref<string | null>(null)
const submitting = ref(false)
const allowed = ref<boolean | null>(null)
const reason = ref<string | null>(null)
const providers = ref<AuthProvider[]>([])

const headline = computed(() =>
  reason.value === 'first-user' ? t('screen.register.title') : t('screen.register.title_secondary'),
)

onMounted(async () => {
  // Fetch providers in parallel — the OAuth row stays even when registration is closed because
  // existing users can still sign in via OAuth from the same form.
  const [registration, providersList] = await Promise.all([
    fetchRegistrationAllowed().catch(() => ({ allowed: false, reason: 'unavailable' })),
    fetchAuthProviders(),
  ])
  allowed.value = registration.allowed
  reason.value = registration.reason
  providers.value = providersList
})

async function onOauth(provider: string): Promise<void> {
  try {
    await oauthSignin(provider, '/')
  }
  catch (err) {
    const message = err instanceof Error ? err.message : t('error.oauth_start_failed', { provider })
    error.value = message
    push('error', message)
  }
}

async function onSubmit(): Promise<void> {
  error.value = null
  if (password.value !== confirm.value) {
    error.value = t('error.passwords_mismatch')
    return
  }
  submitting.value = true
  try {
    await register(username.value, password.value)
    push('success', t('toast.account_created'))
    router.replace('/')
  }
  catch (err) {
    const message = err instanceof Error ? err.message : t('error.register_failed')
    error.value = message
    push('error', message)
  }
  finally {
    submitting.value = false
  }
}
</script>

<template>
  <div class="flex min-h-screen items-center justify-center bg-slate-950 p-4 text-slate-100">
    <div class="w-full max-w-sm rounded-lg border border-slate-800 bg-slate-900 p-6 shadow-xl">
      <div v-if="allowed === false" class="space-y-3">
        <h1 class="text-2xl font-semibold">{{ t('screen.register.closed_title') }}</h1>
        <p class="text-sm text-slate-400">
          {{ t('screen.register.closed_body') }}
        </p>
        <RouterLink to="/login" class="block text-sm text-blue-400 hover:text-blue-300">{{ t('screen.register.back_btn') }}</RouterLink>
      </div>

      <form v-else-if="allowed === true" class="space-y-4" @submit.prevent="onSubmit">
        <div>
          <h1 class="text-2xl font-semibold">{{ headline }}</h1>
          <p class="text-sm text-slate-400">{{ t('screen.register.subtitle') }}</p>
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
            {{ t('screen.register.with_provider', { provider: provider.displayName }) }}
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
            minlength="2"
            class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
          />
        </label>

        <label class="block">
          <span class="text-sm text-slate-300">{{ t('field.password') }}</span>
          <input
            v-model="password"
            type="password"
            autocomplete="new-password"
            required
            minlength="8"
            class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
          />
        </label>

        <label class="block">
          <span class="text-sm text-slate-300">{{ t('field.confirm_password') }}</span>
          <input
            v-model="confirm"
            type="password"
            autocomplete="new-password"
            required
            minlength="8"
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
          {{ submitting ? t('screen.register.submitting') : t('screen.register.submit_btn') }}
        </button>

        <p class="text-center text-sm text-slate-400">
          {{ t('screen.register.have_account_prompt') }}
          <RouterLink to="/login" class="text-blue-400 hover:text-blue-300">{{ t('nav.sign_in') }}</RouterLink>
        </p>
      </form>

      <p v-else class="text-sm text-slate-400">{{ t('screen.register.checking') }}</p>
    </div>
  </div>
</template>
