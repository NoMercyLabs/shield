<script setup lang="ts">
import { ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'

import { login } from '@/stores/auth'

const router = useRouter()
const route = useRoute()

const username = ref('')
const password = ref('')
const totpCode = ref('')
const error = ref<string | null>(null)
const submitting = ref(false)

async function onSubmit(): Promise<void> {
  error.value = null
  submitting.value = true
  try {
    await login(username.value, password.value, totpCode.value || undefined)
    const redirect = typeof route.query.redirect === 'string' ? route.query.redirect : '/'
    router.replace(redirect)
  }
  catch (err) {
    const message = err instanceof Error ? err.message : 'Sign-in failed.'
    error.value = message
  }
  finally {
    submitting.value = false
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
        <p class="text-sm text-slate-400">Sign in to continue.</p>
      </div>

      <label class="block">
        <span class="text-sm text-slate-300">Username</span>
        <input
          v-model="username"
          type="text"
          autocomplete="username"
          required
          class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
        />
      </label>

      <label class="block">
        <span class="text-sm text-slate-300">Password</span>
        <input
          v-model="password"
          type="password"
          autocomplete="current-password"
          required
          class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
        />
      </label>

      <label class="block">
        <span class="text-sm text-slate-300">2FA code (optional)</span>
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
        {{ submitting ? 'Signing in…' : 'Sign in' }}
      </button>
    </form>
  </div>
</template>
