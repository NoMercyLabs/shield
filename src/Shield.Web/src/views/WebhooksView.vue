<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { Copy, Eye, EyeOff, RefreshCw, ShieldCheck } from 'lucide-vue-next'

import { useToasts } from '@/stores/toast'

const { t } = useI18n()
const { push } = useToasts()

const secret = ref<string | null>(null)
const revealed = ref(false)

const masked = computed(() => (secret.value ? '•'.repeat(secret.value.length) : ''))

function generate(): void {
  const bytes = new Uint8Array(32)
  crypto.getRandomValues(bytes)
  secret.value = Array.from(bytes, byte => byte.toString(16).padStart(2, '0')).join('')
  revealed.value = false
}

function toggleReveal(): void {
  revealed.value = !revealed.value
}

async function copy(): Promise<void> {
  if (!secret.value) return
  try {
    await navigator.clipboard.writeText(secret.value)
    push('success', t('screen.webhooks.toast.copied'))
  }
  catch {
    push('error', t('screen.webhooks.copy_failed'))
  }
}
</script>

<template>
  <div class="max-w-2xl space-y-6">
    <header>
      <h1 class="text-2xl font-semibold">{{ t('screen.webhooks.title') }}</h1>
      <p class="mt-1 text-sm text-slate-400">{{ t('screen.webhooks.subtitle') }}</p>
    </header>

    <section class="rounded-lg border border-slate-800 bg-slate-900 p-5">
      <div class="flex items-start gap-3">
        <ShieldCheck class="mt-0.5 h-5 w-5 shrink-0 text-blue-400" aria-hidden="true" />
        <div class="flex-1 space-y-1">
          <h2 class="text-base font-semibold text-slate-100">
            {{ t('screen.webhooks.secret.title') }}
          </h2>
          <p class="text-sm text-slate-400">{{ t('screen.webhooks.secret.desc') }}</p>
        </div>
      </div>

      <div v-if="secret" class="mt-4 space-y-3">
        <label class="block">
          <span class="sr-only">{{ t('screen.webhooks.secret.field_label') }}</span>
          <div class="flex items-stretch gap-1.5">
            <input
              :value="revealed ? secret : masked"
              :type="revealed ? 'text' : 'password'"
              readonly
              :aria-label="t('screen.webhooks.secret.field_label')"
              autocomplete="off"
              spellcheck="false"
              class="min-w-0 flex-1 rounded border border-slate-700 bg-slate-950 px-3 py-2 font-mono text-sm text-slate-100 focus:border-blue-500 focus:outline-none"
            />
            <button
              type="button"
              class="inline-flex shrink-0 items-center justify-center rounded border border-slate-700 px-3 text-slate-300 hover:bg-slate-800 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-400"
              :aria-label="revealed ? t('screen.webhooks.secret.hide_aria') : t('screen.webhooks.secret.show_aria')"
              :aria-pressed="revealed"
              @click="toggleReveal"
            >
              <component :is="revealed ? EyeOff : Eye" class="h-4 w-4" aria-hidden="true" />
            </button>
            <button
              type="button"
              class="inline-flex shrink-0 items-center justify-center rounded border border-slate-700 px-3 text-slate-300 hover:bg-slate-800 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-400"
              :aria-label="t('screen.webhooks.secret.copy_aria')"
              @click="copy"
            >
              <Copy class="h-4 w-4" aria-hidden="true" />
            </button>
          </div>
        </label>
        <div class="flex flex-wrap items-center justify-between gap-2">
          <p class="text-xs text-slate-500">{{ t('screen.webhooks.secret.local_note') }}</p>
          <button
            type="button"
            class="inline-flex items-center gap-1.5 rounded border border-slate-700 px-3 py-1.5 text-sm text-slate-300 hover:bg-slate-800 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-400"
            @click="generate"
          >
            <RefreshCw class="h-4 w-4" aria-hidden="true" />
            {{ t('screen.webhooks.secret.regenerate_btn') }}
          </button>
        </div>
      </div>

      <div v-else class="mt-4">
        <button
          type="button"
          class="inline-flex items-center gap-1.5 rounded bg-blue-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-blue-500 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-400"
          @click="generate"
        >
          <ShieldCheck class="h-4 w-4" aria-hidden="true" />
          {{ t('screen.webhooks.secret.generate_btn') }}
        </button>
      </div>
    </section>
  </div>
</template>
