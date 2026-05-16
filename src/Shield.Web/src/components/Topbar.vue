<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { LogOut } from 'lucide-vue-next'

import { type LocaleCode, LOCALE_FLAGS, LOCALE_LABELS, SUPPORTED_LOCALES, setLocale } from '@/i18n'
import { logout, useAuth } from '@/stores/auth'

const { user } = useAuth()
const router = useRouter()
const { t, locale } = useI18n()

const currentLocale = ref<LocaleCode>(locale.value as LocaleCode)

async function onLocaleChange(event: Event): Promise<void> {
  const next = (event.target as HTMLSelectElement).value as LocaleCode
  await setLocale(next)
  currentLocale.value = next
}

async function onLogout(): Promise<void> {
  await logout()
  router.push({ name: 'login' })
}
</script>

<template>
  <header class="flex items-center justify-between px-4">
    <div class="text-sm text-slate-400">
      <span v-if="user?.singleUserMode" class="rounded bg-slate-800 px-2 py-1 text-xs uppercase tracking-wide">{{ t('banner.single_user_mode') }}</span>
    </div>
    <div class="flex items-center gap-3">
      <label class="sr-only" for="shield-locale">{{ t('field.language') }}</label>
      <select
        id="shield-locale"
        :value="currentLocale"
        :aria-label="t('field.language')"
        class="rounded border border-slate-700 bg-slate-800 px-2 py-1 text-xs text-slate-200 hover:bg-slate-700 focus:border-blue-500 focus:outline-none"
        @change="onLocaleChange"
      >
        <option v-for="code in SUPPORTED_LOCALES" :key="code" :value="code">
          {{ LOCALE_FLAGS[code] }} {{ LOCALE_LABELS[code] }}
        </option>
      </select>
      <span v-if="user?.username" class="text-sm text-slate-300">{{ user.username }}</span>
      <button
        v-if="user && !user.singleUserMode"
        type="button"
        class="flex items-center gap-1 rounded px-3 py-1.5 text-sm text-slate-300 hover:bg-slate-800 hover:text-white"
        @click="onLogout"
      >
        <LogOut class="h-4 w-4" />
        <span>{{ t('nav.sign_out') }}</span>
      </button>
    </div>
  </header>
</template>
