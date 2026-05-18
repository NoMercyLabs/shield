<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { Download, Smartphone } from 'lucide-vue-next'

import { usePwaInstall } from '@/lib/pwa'

const { t } = useI18n()
const { canInstall, isInstalled, prompt } = usePwaInstall()

const dismissed = ref<boolean>(typeof localStorage !== 'undefined'
  && localStorage.getItem('shield.pwa.install_dismissed') === '1')

const installing = ref(false)

// iOS Safari doesn't expose beforeinstallprompt — surface a manual instruction string so
// iPhone / iPad operators know how to install. Detection is a navigator UA sniff because
// the standalone-display media query only flips AFTER install.
const isIos = computed<boolean>(() => /iPad|iPhone|iPod/.test(navigator.userAgent || ''))
const isStandalone = computed<boolean>(() =>
  isInstalled.value
  || (typeof window !== 'undefined'
    && window.matchMedia?.('(display-mode: standalone)').matches === true),
)

const visible = computed<boolean>(() => {
  if (dismissed.value || isStandalone.value) return false
  return canInstall.value !== null || isIos.value
})

async function onInstall(): Promise<void> {
  installing.value = true
  try {
    await prompt()
  }
  finally {
    installing.value = false
  }
}

function onDismiss(): void {
  dismissed.value = true
  try { localStorage.setItem('shield.pwa.install_dismissed', '1') }
  catch { /* private mode — that's fine, banner stays hidden for this session only */ }
}
</script>

<template>
  <section
    v-if="visible"
    class="flex flex-col gap-3 rounded-lg border border-blue-900/40 bg-blue-950/20 p-4 sm:flex-row sm:items-center"
  >
    <div class="grid h-10 w-10 shrink-0 place-items-center rounded-full bg-blue-500/20 text-blue-300">
      <Smartphone class="h-5 w-5" />
    </div>
    <div class="flex-1 min-w-0">
      <p class="text-sm font-medium text-slate-100">{{ t('pwa.install.title') }}</p>
      <p class="mt-0.5 text-xs text-slate-400">{{ t('pwa.install.body') }}</p>
      <p v-if="isIos && !canInstall" class="mt-1 text-xs text-blue-200/80">
        {{ t('pwa.install.ios_hint') }}
      </p>
    </div>
    <div class="flex shrink-0 items-center gap-2">
      <button
        v-if="canInstall"
        type="button"
        class="inline-flex h-11 items-center gap-1.5 rounded-md bg-blue-600 px-3 text-sm font-medium text-white transition-colors hover:bg-blue-500 focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-blue-300 disabled:cursor-not-allowed disabled:bg-blue-900/60"
        :disabled="installing"
        @click="onInstall"
      >
        <Download class="h-4 w-4" />
        {{ t('pwa.install.install_btn') }}
      </button>
      <button
        type="button"
        class="h-11 rounded-md border border-slate-700 px-3 text-sm text-slate-200 transition-colors hover:bg-slate-800 focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-blue-500"
        @click="onDismiss"
      >
        {{ t('pwa.install.dismiss_btn') }}
      </button>
    </div>
  </section>
</template>
