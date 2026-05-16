<script setup lang="ts">
import { ref } from 'vue'

import { changePassword, useAuth } from '@/stores/auth'
import { useToasts } from '@/stores/toast'
import axios from 'axios'

const { user } = useAuth()
const { push } = useToasts()

const currentPassword = ref('')
const newPassword = ref('')
const confirm = ref('')
const error = ref<string | null>(null)
const submitting = ref(false)

async function onSubmit(): Promise<void> {
  error.value = null
  if (newPassword.value !== confirm.value) {
    error.value = 'New passwords do not match.'
    return
  }
  submitting.value = true
  try {
    await changePassword(currentPassword.value, newPassword.value)
    push('success', 'Password updated.')
    currentPassword.value = ''
    newPassword.value = ''
    confirm.value = ''
  }
  catch (err) {
    const message = axios.isAxiosError(err)
      ? (err.response?.data?.error ?? 'Failed to change password.')
      : (err instanceof Error ? err.message : 'Failed to change password.')
    error.value = message
    push('error', message)
  }
  finally {
    submitting.value = false
  }
}
</script>

<template>
  <div class="space-y-6">
    <header>
      <h1 class="text-2xl font-semibold">Account</h1>
      <p class="text-sm text-slate-400">Signed in as <span class="font-mono">{{ user?.username ?? '—' }}</span></p>
    </header>

    <section class="max-w-md space-y-3 rounded-lg border border-slate-800 bg-slate-900 p-4">
      <h2 class="text-sm font-medium text-slate-300">Change password</h2>

      <form class="space-y-3" @submit.prevent="onSubmit">
        <label class="block">
          <span class="text-sm text-slate-300">Current password</span>
          <input
            v-model="currentPassword"
            type="password"
            autocomplete="current-password"
            required
            class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
          />
        </label>

        <label class="block">
          <span class="text-sm text-slate-300">New password</span>
          <input
            v-model="newPassword"
            type="password"
            autocomplete="new-password"
            required
            minlength="8"
            class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
          />
        </label>

        <label class="block">
          <span class="text-sm text-slate-300">Confirm new password</span>
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
          class="rounded bg-blue-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-blue-500 disabled:cursor-not-allowed disabled:bg-blue-900"
        >
          {{ submitting ? 'Saving…' : 'Update password' }}
        </button>
      </form>
    </section>
  </div>
</template>
