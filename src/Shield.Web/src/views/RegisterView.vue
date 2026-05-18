<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink, useRouter } from 'vue-router'
import { ShieldCheck } from 'lucide-vue-next'

import { type AuthProvider, fetchAuthProviders, fetchRegistrationAllowed, oauthSignin, register } from '@/stores/auth'
import { useToasts } from '@/stores/toast'

const router = useRouter()
const { push } = useToasts()
const { t } = useI18n()

const username = ref('')
const password = ref('')
const confirm = ref('')
const formError = ref<string | null>(null)
const submitting = ref(false)
const allowed = ref<boolean | null>(null)
const reason = ref<string | null>(null)
const providers = ref<AuthProvider[]>([])

const headline = computed(() =>
  reason.value === 'first-user' ? t('screen.register.title') : t('screen.register.title_secondary'),
)

onMounted(async () => {
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
    formError.value = message
    push('error', message)
  }
}

async function onSubmit(): Promise<void> {
  formError.value = null
  if (password.value !== confirm.value) {
    formError.value = t('error.passwords_mismatch')
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
    formError.value = message
    push('error', message)
  }
  finally {
    submitting.value = false
  }
}
</script>

<template>
  <div class="grid min-h-screen place-items-center bg-slate-950 px-4 py-10 text-slate-100">
    <div class="w-full max-w-md">
      <div class="rounded-xl border border-slate-800 bg-slate-900/80 p-7 shadow-2xl backdrop-blur">
        <div v-if="allowed === false" class="space-y-4 text-center">
          <div class="mx-auto w-fit rounded-full bg-amber-500/10 p-3 ring-1 ring-amber-500/30">
            <ShieldCheck class="h-7 w-7 text-amber-300" />
          </div>
          <h1 class="text-xl font-semibold">{{ t('screen.register.closed_title') }}</h1>
          <p class="text-sm text-slate-400">{{ t('screen.register.closed_body') }}</p>
          <RouterLink to="/login" class="block text-sm text-blue-400 transition-colors hover:text-blue-300">
            {{ t('screen.register.back_btn') }}
          </RouterLink>
        </div>

        <div v-else-if="allowed === true" class="space-y-5">
          <header class="flex flex-col items-center gap-2 text-center">
            <div class="rounded-full bg-blue-500/10 p-3 ring-1 ring-blue-500/30">
              <ShieldCheck class="h-7 w-7 text-blue-400" />
            </div>
            <h1 class="text-xl font-semibold text-slate-100">{{ headline }}</h1>
            <p class="text-sm text-slate-400">{{ t('screen.register.subtitle') }}</p>
          </header>

          <p v-if="formError" class="rounded-md border border-red-700/50 bg-red-900/30 px-3 py-2 text-sm text-red-300">
            {{ formError }}
          </p>

          <form class="space-y-3" @submit.prevent="onSubmit">
            <label class="block">
              <span class="mb-1 block text-sm text-slate-300">{{ t('field.username') }}</span>
              <input
                v-model="username"
                type="text"
                autocomplete="username"
                required
                minlength="2"
                class="w-full rounded-md border border-slate-700 bg-slate-950/60 px-3 py-2 text-sm text-slate-100 transition-colors focus:border-blue-500 focus:outline-none focus-visible:ring-1 focus-visible:ring-blue-500"
              />
            </label>

            <label class="block">
              <span class="mb-1 block text-sm text-slate-300">{{ t('field.password') }}</span>
              <input
                v-model="password"
                type="password"
                autocomplete="new-password"
                required
                minlength="8"
                class="w-full rounded-md border border-slate-700 bg-slate-950/60 px-3 py-2 text-sm text-slate-100 transition-colors focus:border-blue-500 focus:outline-none focus-visible:ring-1 focus-visible:ring-blue-500"
              />
            </label>

            <label class="block">
              <span class="mb-1 block text-sm text-slate-300">{{ t('field.confirm_password') }}</span>
              <input
                v-model="confirm"
                type="password"
                autocomplete="new-password"
                required
                minlength="8"
                class="w-full rounded-md border border-slate-700 bg-slate-950/60 px-3 py-2 text-sm text-slate-100 transition-colors focus:border-blue-500 focus:outline-none focus-visible:ring-1 focus-visible:ring-blue-500"
              />
            </label>

            <button
              type="submit"
              :disabled="submitting"
              class="w-full rounded-md bg-blue-600 px-3 py-2 text-sm font-medium text-white transition-colors hover:bg-blue-500 focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-blue-300 disabled:cursor-not-allowed disabled:bg-blue-900/60"
            >
              {{ submitting ? t('screen.register.submitting') : t('screen.register.submit_btn') }}
            </button>
          </form>

          <div v-if="providers.length > 0" class="flex items-center gap-3">
            <span class="h-px flex-1 bg-slate-800" />
            <span class="text-xs uppercase tracking-wide text-slate-500">{{ t('auth.or') }}</span>
            <span class="h-px flex-1 bg-slate-800" />
          </div>

          <div v-if="providers.length > 0" class="space-y-2">
            <button
              v-for="provider in providers"
              :key="provider.provider"
              type="button"
              class="flex w-full items-center gap-3 rounded-md border border-slate-700 bg-slate-950/40 px-3 py-2 text-sm font-medium text-slate-100 transition-colors hover:bg-slate-800/60 focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-blue-500"
              @click="onOauth(provider.provider)"
            >
              <img :src="provider.iconUrl" :alt="provider.displayName" class="h-4 w-4 shrink-0" />
              <span class="flex-1 text-left">{{ t('screen.register.with_provider', { provider: provider.displayName }) }}</span>
            </button>
          </div>

          <p class="pt-1 text-center text-xs text-slate-500">
            {{ t('screen.register.have_account_prompt') }}
            <RouterLink to="/login" class="text-blue-400 transition-colors hover:text-blue-300">
              {{ t('nav.sign_in') }}
            </RouterLink>
          </p>
        </div>

        <p v-else class="text-center text-sm text-slate-400">{{ t('screen.register.checking') }}</p>
      </div>
    </div>
  </div>
</template>
