<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { BellRing, RefreshCw, Send, Trash2 } from 'lucide-vue-next'

import {
  type PushSubscriptionRow,
  deleteSubscriptionById,
  getCurrentSubscription,
  hashEndpoint,
  listSubscriptions,
  notificationPermission,
  pushSupported,
  requestPermissionAndSubscribe,
  sendTestPush,
  unsubscribeCurrentDevice,
} from '@/lib/push'
import { useToasts } from '@/stores/toast'

const { t } = useI18n()
const toasts = useToasts()

const supported = computed(() => pushSupported())
const permission = ref<NotificationPermission>(notificationPermission())
const subscriptions = ref<PushSubscriptionRow[]>([])
const loading = ref(false)
const enabling = ref(false)

// Once a user denies the prompt, the browser silently refuses subsequent
// requestPermission() calls forever. The recovery requires flipping the per-site setting
// in browser preferences, then returning to the tab. visibilitychange + focus catch
// that moment so the SPA refreshes its view of Notification.permission without a reload.
function refreshPermission(): void {
  permission.value = notificationPermission()
}
function onVisibilityChange(): void {
  if (document.visibilityState === 'visible') refreshPermission()
}
onMounted(() => {
  document.addEventListener('visibilitychange', onVisibilityChange)
  window.addEventListener('focus', refreshPermission)
})
onUnmounted(() => {
  document.removeEventListener('visibilitychange', onVisibilityChange)
  window.removeEventListener('focus', refreshPermission)
})

// Platform-specific deep links so the user lands on the exact settings page rather than
// the browser's general settings menu. Falls back to a generic message on unknown agents.
const recoveryHint = computed<{ url: string | null, steps: string }>(() => {
  const ua = navigator.userAgent
  const origin = window.location.origin
  if (/Edg\//.test(ua))
    return { url: 'edge://settings/content/siteDetails?site=' + encodeURIComponent(origin), steps: t('push.recovery.edge') }
  if (/Firefox\//.test(ua))
    return { url: null, steps: t('push.recovery.firefox') }
  if (/Chrome\//.test(ua))
    return { url: null, steps: t('push.recovery.chrome') }
  if (/Safari\//.test(ua))
    return { url: null, steps: t('push.recovery.safari') }
  return { url: null, steps: t('push.recovery.generic') }
})

// iOS Safari requires the PWA to be installed (display-mode: standalone) before push works,
// so the card has to call this out explicitly when the user is on Mobile Safari without a
// home-screen install.
const isIos = computed<boolean>(() => /iPad|iPhone|iPod/.test(navigator.userAgent || ''))
const isStandalone = computed<boolean>(() =>
  typeof window !== 'undefined'
  && (window.matchMedia?.('(display-mode: standalone)').matches === true
    || ('standalone' in navigator && (navigator as { standalone?: boolean }).standalone === true)),
)
const iosPwaRequired = computed<boolean>(() => isIos.value && !isStandalone.value)

async function refresh(): Promise<void> {
  if (!supported.value) return
  loading.value = true
  try {
    subscriptions.value = await listSubscriptions()
    await resolveCurrentDevice()
  }
  catch {
    subscriptions.value = []
    hasCurrentDevice.value = false
  }
  finally {
    loading.value = false
  }
}

async function resolveCurrentDevice(): Promise<void> {
  if (subscriptions.value.length === 0) {
    hasCurrentDevice.value = false
    return
  }
  try {
    const localSub = await getCurrentSubscription()
    if (!localSub) {
      hasCurrentDevice.value = false
      return
    }
    const localHash = await hashEndpoint(localSub.endpoint)
    hasCurrentDevice.value = subscriptions.value.some(row => row.endpointHash === localHash)
  }
  catch {
    hasCurrentDevice.value = false
  }
}

onMounted(() => {
  refreshPermission()
  void refresh()
})

async function onEnable(): Promise<void> {
  if (!supported.value || iosPwaRequired.value) return
  enabling.value = true
  try {
    const result = await requestPermissionAndSubscribe()
    permission.value = notificationPermission()
    if (result.ok) {
      toasts.push('success', t('push.subscribe_success_toast'))
      await refresh()
    }
    else if (result.reason === 'denied') {
      toasts.push('error', t('push.permission_denied'))
    }
    else {
      toasts.push('error', t('push.subscribe_failed_toast'))
    }
  }
  catch {
    toasts.push('error', t('push.subscribe_failed_toast'))
  }
  finally {
    enabling.value = false
  }
}

async function onTest(): Promise<void> {
  try {
    const count = await sendTestPush()
    if (count === 0)
      toasts.push('error', t('push.test_no_subscriptions_toast'))
    else
      toasts.push('success', t('push.test_sent_toast', { n: count }, count))
  }
  catch {
    toasts.push('error', t('push.subscribe_failed_toast'))
  }
}

async function onDisconnect(subscription: PushSubscriptionRow): Promise<void> {
  try {
    if (subscription.isCurrentDevice)
      await unsubscribeCurrentDevice()
    await deleteSubscriptionById(subscription.id)
    toasts.push('success', t('push.unsubscribed_toast'))
    await refresh()
  }
  catch {
    toasts.push('error', t('push.unsubscribe_failed_toast'))
  }
}

function formatDate(iso: string | null): string {
  if (!iso) return t('push.last_delivered_never')
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) return t('push.last_delivered_never')
  return date.toLocaleString()
}

const hasCurrentDevice = ref<boolean>(false)
</script>

<template>
  <section class="space-y-3 rounded-lg border border-slate-800 bg-slate-900 p-4">
    <header class="flex items-start gap-2">
      <BellRing class="mt-0.5 h-4 w-4 shrink-0 text-blue-300" />
      <div class="flex-1 min-w-0">
        <h2 class="text-sm font-medium text-slate-100">{{ t('push.card_title') }}</h2>
        <p class="mt-0.5 text-xs text-slate-400">{{ t('push.card_subtitle') }}</p>
      </div>
    </header>

    <p v-if="!supported" class="text-xs text-amber-300">{{ t('push.unsupported') }}</p>
    <p v-else-if="iosPwaRequired" class="text-xs text-amber-300">{{ t('push.ios_pwa_required') }}</p>

    <!-- Permission locked-out recovery: the browser silently rejects requestPermission()
         calls after a deny, so we show the manual path + a re-check button. -->
    <div
      v-else-if="permission === 'denied'"
      class="space-y-2 rounded-md border border-amber-500/30 bg-amber-500/5 p-3"
    >
      <p class="text-xs font-medium text-amber-200">{{ t('push.permission_denied_title') }}</p>
      <p class="text-xs text-amber-100/80">{{ t('push.permission_denied_explainer') }}</p>
      <p class="text-xs text-amber-100/80 whitespace-pre-line">{{ recoveryHint.steps }}</p>
      <div class="flex flex-wrap items-center gap-2 pt-1">
        <a
          v-if="recoveryHint.url"
          :href="recoveryHint.url"
          target="_blank"
          rel="noopener"
          class="inline-flex h-9 items-center gap-1.5 rounded-md border border-amber-400/40 bg-amber-500/10 px-3 text-xs font-medium text-amber-100 transition-colors hover:bg-amber-500/20"
        >
          {{ t('push.recovery.open_settings_btn') }}
        </a>
        <button
          type="button"
          class="inline-flex h-9 items-center gap-1.5 rounded-md border border-amber-400/40 bg-transparent px-3 text-xs font-medium text-amber-100 transition-colors hover:bg-amber-500/10"
          @click="refreshPermission"
        >
          <RefreshCw class="h-3.5 w-3.5" />
          {{ t('push.recovery.recheck_btn') }}
        </button>
      </div>
    </div>

    <div v-if="supported && !iosPwaRequired && permission !== 'denied'" class="flex flex-wrap items-center gap-2">
      <button
        v-if="!hasCurrentDevice"
        type="button"
        :disabled="enabling"
        class="inline-flex h-11 items-center gap-1.5 rounded-md bg-blue-600 px-3 text-sm font-medium text-white transition-colors hover:bg-blue-500 focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-blue-300 disabled:cursor-not-allowed disabled:bg-blue-900/60"
        @click="onEnable"
      >
        <BellRing class="h-4 w-4" />
        {{ enabling ? t('push.enabling') : t('push.enable_btn') }}
      </button>
      <button
        v-if="subscriptions.length > 0"
        type="button"
        class="inline-flex h-11 items-center gap-1.5 rounded-md border border-slate-700 px-3 text-sm text-slate-200 transition-colors hover:bg-slate-800 focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-blue-500"
        @click="onTest"
      >
        <Send class="h-4 w-4" />
        {{ t('push.test_btn') }}
      </button>
    </div>

    <p v-if="loading" class="text-xs text-slate-500">{{ t('state.loading') }}</p>

    <ul v-else-if="subscriptions.length > 0" class="divide-y divide-slate-800 rounded-md border border-slate-800">
      <li
        v-for="subscription in subscriptions"
        :key="subscription.id"
        class="flex flex-col gap-2 px-3 py-3 sm:flex-row sm:items-center"
      >
        <div class="flex-1 min-w-0">
          <p class="flex flex-wrap items-center gap-2 text-sm text-slate-100">
            <span class="truncate">{{ subscription.userAgent ?? subscription.endpoint }}</span>
            <span
              v-if="subscription.isCurrentDevice"
              class="rounded bg-blue-500/20 px-2 py-0.5 text-[10px] uppercase tracking-wider text-blue-200"
            >
              {{ t('push.current_device_label') }}
            </span>
          </p>
          <p class="mt-1 text-xs text-slate-500">
            {{ t('push.added_label') }}: {{ formatDate(subscription.createdAt) }}
            <span class="mx-2 text-slate-700">·</span>
            {{ t('push.last_delivered_label') }}: {{ formatDate(subscription.lastDeliveredAt) }}
          </p>
        </div>
        <button
          type="button"
          class="inline-flex h-11 items-center gap-1.5 self-start rounded-md border border-slate-700 px-3 text-xs text-slate-300 transition-colors hover:bg-slate-800 hover:text-red-200 focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-red-500 sm:self-auto"
          @click="onDisconnect(subscription)"
        >
          <Trash2 class="h-4 w-4" />
          {{ t('push.disconnect_btn') }}
        </button>
      </li>
    </ul>

    <p v-else-if="supported" class="text-xs text-slate-500">{{ t('push.no_subscriptions') }}</p>
  </section>
</template>
