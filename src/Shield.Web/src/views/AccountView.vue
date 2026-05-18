<script setup lang="ts">
import { ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import QRCode from 'qrcode'

import InstallPwaCard from '@/components/InstallPwaCard.vue'
import { changePassword, useAuth } from '@/stores/auth'
import { useToasts } from '@/stores/toast'
import {
  useDisableTwoFactorMutation,
  useEnrollTwoFactorMutation,
  useTwoFactorStatusQuery,
  useVerifyTwoFactorMutation,
} from '@/queries/twoFactor'
import axios from 'axios'

const { user } = useAuth()
const { push } = useToasts()
const { t } = useI18n()

const currentPassword = ref('')
const newPassword = ref('')
const confirm = ref('')
const error = ref<string | null>(null)
const submitting = ref(false)

// --- 2FA section -----------------------------------------------------------
const twoFactorStatus = useTwoFactorStatusQuery()
const enrollMutation = useEnrollTwoFactorMutation()
const verifyMutation = useVerifyTwoFactorMutation()
const disableMutation = useDisableTwoFactorMutation()

const enrollSecret = ref<string | null>(null)
const enrollUri = ref<string | null>(null)
const enrollRecoveryCodes = ref<string[] | null>(null)
const qrDataUrl = ref<string | null>(null)
const verifyCode = ref('')
const disablePassword = ref('')

watch(enrollUri, async (uri) => {
  if (!uri) {
    qrDataUrl.value = null
    return
  }
  try {
    // 256px + margin 2 keeps the cells large enough to scan reliably on phone cameras even
    // after the otpauth URI grew to spell out algorithm/period/digits explicitly. margin 1
    // is below the QR spec's quiet-zone minimum and trips some scanners.
    qrDataUrl.value = await QRCode.toDataURL(uri, {
      margin: 2,
      width: 256,
      errorCorrectionLevel: 'M',
    })
  }
  catch {
    qrDataUrl.value = null
  }
})

async function startEnroll(): Promise<void> {
  try {
    const data = await enrollMutation.mutateAsync()
    enrollSecret.value = data.sharedKey
    enrollUri.value = data.authenticatorUri
    enrollRecoveryCodes.value = data.recoveryCodes
    verifyCode.value = ''
  }
  catch {
    push('error', t('error.two_factor_enroll_failed'))
  }
}

async function confirmEnroll(): Promise<void> {
  if (verifyCode.value.length !== 6) return
  try {
    await verifyMutation.mutateAsync(verifyCode.value)
    push('success', t('toast.two_factor_enabled'))
    // Keep the recovery codes panel visible — clear the QR + secret so the user can't go back.
    enrollSecret.value = null
    enrollUri.value = null
    qrDataUrl.value = null
  }
  catch {
    push('error', t('error.two_factor_verify_failed'))
  }
}

async function disable(): Promise<void> {
  if (!disablePassword.value) return
  try {
    await disableMutation.mutateAsync(disablePassword.value)
    push('success', t('toast.two_factor_disabled'))
    disablePassword.value = ''
    enrollRecoveryCodes.value = null
  }
  catch {
    push('error', t('error.two_factor_disable_failed'))
  }
}

async function onSubmit(): Promise<void> {
  error.value = null
  if (newPassword.value !== confirm.value) {
    error.value = t('error.new_passwords_mismatch')
    return
  }
  submitting.value = true
  try {
    await changePassword(currentPassword.value, newPassword.value)
    push('success', t('toast.password_updated'))
    currentPassword.value = ''
    newPassword.value = ''
    confirm.value = ''
  }
  catch (err) {
    const message = axios.isAxiosError(err)
      ? (err.response?.data?.error ?? t('error.password_update_failed'))
      : (err instanceof Error ? err.message : t('error.password_update_failed'))
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
      <h1 class="text-2xl font-semibold">{{ t('screen.account.title') }}</h1>
      <p class="text-sm text-slate-400">{{ t('screen.account.signed_in_as', { user: user?.username ?? '—' }) }}</p>
    </header>

    <InstallPwaCard />

    <section class="max-w-md space-y-3 rounded-lg border border-slate-800 bg-slate-900 p-4">
      <h2 class="text-sm font-medium text-slate-300">{{ t('screen.account.change_password_title') }}</h2>

      <form class="space-y-3" @submit.prevent="onSubmit">
        <label class="block">
          <span class="text-sm text-slate-300">{{ t('field.current_password') }}</span>
          <input
            v-model="currentPassword"
            type="password"
            autocomplete="current-password"
            required
            class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
          />
        </label>

        <label class="block">
          <span class="text-sm text-slate-300">{{ t('field.new_password') }}</span>
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
          <span class="text-sm text-slate-300">{{ t('field.confirm_new_password') }}</span>
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
          {{ submitting ? t('state.saving') : t('screen.account.update_password_btn') }}
        </button>
      </form>
    </section>

    <section class="max-w-md space-y-3 rounded-lg border border-slate-800 bg-slate-900 p-4">
      <h2 class="text-sm font-medium text-slate-300">{{ t('screen.account.two_factor_title') }}</h2>

      <p v-if="twoFactorStatus.isLoading.value" class="text-xs text-slate-400">{{ t('state.loading') }}</p>

      <template v-else-if="twoFactorStatus.data.value?.enabled">
        <p class="text-sm text-emerald-300">{{ t('screen.account.two_factor_enabled') }}</p>
        <p v-if="twoFactorStatus.data.value.remainingRecoveryCodes !== undefined" class="text-xs text-slate-400">
          {{ t('screen.account.recovery_codes_remaining', { n: twoFactorStatus.data.value.remainingRecoveryCodes }) }}
        </p>
        <form class="space-y-2" @submit.prevent="disable">
          <label class="block">
            <span class="text-sm text-slate-300">{{ t('field.current_password') }}</span>
            <input
              v-model="disablePassword"
              type="password"
              autocomplete="current-password"
              class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 text-sm"
            />
          </label>
          <button
            type="submit"
            :disabled="!disablePassword || disableMutation.isPending.value"
            class="rounded border border-red-700 px-3 py-1.5 text-sm font-medium text-red-200 hover:bg-red-700/20 disabled:cursor-not-allowed disabled:opacity-50"
          >
            {{ t('screen.account.two_factor_disable_btn') }}
          </button>
        </form>
      </template>

      <template v-else>
        <p v-if="twoFactorStatus.data.value?.requiredByPolicy" class="text-xs text-amber-300">
          {{ t('screen.account.two_factor_required_by_policy') }}
        </p>
        <p v-else class="text-xs text-slate-400">{{ t('screen.account.two_factor_help') }}</p>

        <button
          v-if="!enrollUri"
          type="button"
          :disabled="enrollMutation.isPending.value"
          class="rounded bg-blue-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-blue-500 disabled:cursor-not-allowed disabled:opacity-50"
          @click="startEnroll"
        >
          {{ t('screen.account.two_factor_enable_btn') }}
        </button>

        <div v-else class="space-y-3">
          <img
            v-if="qrDataUrl"
            :src="qrDataUrl"
            alt="2FA QR code"
            class="rounded border border-slate-700 bg-white p-2"
          />
          <p class="break-all rounded bg-slate-800 px-2 py-1 font-mono text-xs text-slate-300">{{ enrollSecret }}</p>
          <form class="space-y-2" @submit.prevent="confirmEnroll">
            <label class="block">
              <span class="text-sm text-slate-300">{{ t('field.two_factor_code') }}</span>
              <input
                v-model="verifyCode"
                inputmode="numeric"
                pattern="[0-9]{6}"
                maxlength="6"
                autocomplete="one-time-code"
                class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 text-sm tracking-widest"
              />
            </label>
            <button
              type="submit"
              :disabled="verifyCode.length !== 6 || verifyMutation.isPending.value"
              class="rounded bg-blue-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-blue-500 disabled:cursor-not-allowed disabled:opacity-50"
            >
              {{ t('screen.account.two_factor_confirm_btn') }}
            </button>
          </form>
        </div>
      </template>

      <div v-if="enrollRecoveryCodes" class="space-y-2 rounded border border-amber-700 bg-amber-900/30 p-3">
        <p class="text-xs font-semibold text-amber-200">{{ t('screen.account.recovery_codes_title') }}</p>
        <p class="text-xs text-amber-100/80">{{ t('screen.account.recovery_codes_warning') }}</p>
        <pre class="rounded bg-slate-900 p-2 font-mono text-xs text-amber-100">{{ enrollRecoveryCodes.join('\n') }}</pre>
      </div>
    </section>
  </div>
</template>
