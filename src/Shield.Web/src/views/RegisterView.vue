<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'

import { fetchRegistrationAllowed, register } from '@/stores/auth'
import { useToasts } from '@/stores/toast'

const router = useRouter()
const { push } = useToasts()

const username = ref('')
const password = ref('')
const confirm = ref('')
const error = ref<string | null>(null)
const submitting = ref(false)
const allowed = ref<boolean | null>(null)
const reason = ref<string | null>(null)

const headline = computed(() =>
  reason.value === 'first-user' ? 'Create the first admin' : 'Create an account'
)

onMounted(async () => {
  try {
    const result = await fetchRegistrationAllowed()
    allowed.value = result.allowed
    reason.value = result.reason
  }
  catch {
    allowed.value = false
    reason.value = 'unavailable'
  }
})

async function onSubmit(): Promise<void> {
  error.value = null
  if (password.value !== confirm.value) {
    error.value = 'Passwords do not match.'
    return
  }
  submitting.value = true
  try {
    await register(username.value, password.value)
    push('success', 'Account created.')
    router.replace('/')
  }
  catch (err) {
    const message = err instanceof Error ? err.message : 'Registration failed.'
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
        <h1 class="text-2xl font-semibold">Registration closed</h1>
        <p class="text-sm text-slate-400">
          New accounts are disabled on this instance. Ask an admin to invite you, or sign in below.
        </p>
        <RouterLink to="/login" class="block text-sm text-blue-400 hover:text-blue-300">Back to sign in</RouterLink>
      </div>

      <form v-else-if="allowed === true" class="space-y-4" @submit.prevent="onSubmit">
        <div>
          <h1 class="text-2xl font-semibold">{{ headline }}</h1>
          <p class="text-sm text-slate-400">Set a username and password to continue.</p>
        </div>

        <label class="block">
          <span class="text-sm text-slate-300">Username</span>
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
          <span class="text-sm text-slate-300">Password</span>
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
          <span class="text-sm text-slate-300">Confirm password</span>
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
          {{ submitting ? 'Creating…' : 'Create account' }}
        </button>

        <p class="text-center text-sm text-slate-400">
          Already have an account?
          <RouterLink to="/login" class="text-blue-400 hover:text-blue-300">Sign in</RouterLink>
        </p>
      </form>

      <p v-else class="text-sm text-slate-400">Checking registration status…</p>
    </div>
  </div>
</template>
