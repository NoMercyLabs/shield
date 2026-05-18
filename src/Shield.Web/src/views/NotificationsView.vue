<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { BellRing, X } from 'lucide-vue-next'

import SeverityBadge from '@/components/SeverityBadge.vue'
import { formatDate } from '@/lib/format'
import {
  cleanupStaleSubscriptionIfNeeded,
  getCurrentSubscription,
  hashEndpoint,
  listSubscriptions,
  notificationPermission,
  pushSupported,
  requestPermissionAndSubscribe,
} from '@/lib/push'
import { RouterLink } from 'vue-router'
import {
  useArchiveNotificationMutation,
  useMarkAllNotificationsReadMutation,
  useMarkNotificationReadMutation,
  useNotificationsQuery,
} from '@/queries/notifications'
import { enumLabel } from '@/stores/enums'
import { useToasts } from '@/stores/toast'
import type { Notification } from '@/types/api'

const { t } = useI18n()
const router = useRouter()
const unreadOnly = ref(false)
const hasCurrentDevicePush = ref<boolean | null>(null)
const enablingPush = ref(false)
const pushBannerDismissed = ref<boolean>(typeof localStorage !== 'undefined'
  && localStorage.getItem('shield.notifications.push_banner_dismissed') === '1')

async function refreshPushStatus(): Promise<void> {
  if (!pushSupported()) {
    hasCurrentDevicePush.value = true // hide the banner — nothing useful to offer
    return
  }
  try {
    // Drop stale local subscription if permission was revoked since last visit so the
    // banner accurately reflects "you cannot receive pushes right now".
    await cleanupStaleSubscriptionIfNeeded()
    if (notificationPermission() !== 'granted') {
      hasCurrentDevicePush.value = false
      return
    }
    const [rows, localSub] = await Promise.all([listSubscriptions(), getCurrentSubscription()])
    if (!localSub) {
      hasCurrentDevicePush.value = false
      return
    }
    const localHash = await hashEndpoint(localSub.endpoint)
    hasCurrentDevicePush.value = rows.some(row => row.endpointHash === localHash)
  }
  catch {
    hasCurrentDevicePush.value = true
  }
}

onMounted(refreshPushStatus)
void router
const { data, isLoading, isError } = useNotificationsQuery(unreadOnly, 100)
const markRead = useMarkNotificationReadMutation()
const archive = useArchiveNotificationMutation()
const markAllRead = useMarkAllNotificationsReadMutation()
const toasts = useToasts()

const items = computed<Notification[]>(() => data.value?.items ?? [])
const unreadCount = computed<number>(() => data.value?.unreadCount ?? 0)

async function onRead(notification: Notification): Promise<void> {
  if (notification.readAt) return
  try {
    await markRead.mutateAsync(notification.id)
  }
  catch {
    toasts.push('error', t('notifications_view.toast_mark_read_error'))
  }
}

async function onArchive(notification: Notification): Promise<void> {
  try {
    await archive.mutateAsync(notification.id)
  }
  catch {
    toasts.push('error', t('notifications_view.toast_archive_error'))
  }
}

async function onMarkAllRead(): Promise<void> {
  try {
    const response = await markAllRead.mutateAsync()
    toasts.push('success', t('notifications_view.toast_marked_read', { n: response.updated }))
  }
  catch {
    toasts.push('error', t('notifications_view.toast_bulk_error'))
  }
}

async function onEnablePushBanner(): Promise<void> {
  enablingPush.value = true
  try {
    const result = await requestPermissionAndSubscribe()
    if (result.ok) {
      toasts.push('success', t('push.subscribe_success_toast'))
      hasCurrentDevicePush.value = true
    }
    else {
      toasts.push('error', t('push.subscribe_failed_toast'))
    }
  }
  catch {
    toasts.push('error', t('push.subscribe_failed_toast'))
  }
  finally {
    enablingPush.value = false
  }
}

function dismissPushBanner(): void {
  pushBannerDismissed.value = true
  try { localStorage.setItem('shield.notifications.push_banner_dismissed', '1') }
  catch { /* private mode — accept session-only dismissal */ }
}

type NotificationLink =
  | { external: true, href: string }
  | { external: false, to: string }
  | null

function notificationLink(notification: Notification): NotificationLink {
  if (!notification.relatedType || !notification.relatedId) return null
  if (notification.relatedType === 'PullRequest') return { external: true, href: notification.relatedId }
  if (notification.relatedType === 'Finding') return { external: false, to: `/findings/${notification.relatedId}` }
  if (notification.relatedType === 'Source') return { external: false, to: `/sources/${notification.relatedId}` }
  if (notification.relatedType === 'Feed') return { external: false, to: '/feeds' }
  return null
}

// Template-friendly helpers that dodge the discriminated-union narrowing inside Vue
// templates (the literal `false` confuses vue-tsc).
function externalHref(notification: Notification): string | null {
  const link = notificationLink(notification)
  return link && link.external ? link.href : null
}
function internalTo(notification: Notification): string | null {
  const link = notificationLink(notification)
  return link && !link.external ? link.to : null
}

const showPushBanner = computed<boolean>(() =>
  !pushBannerDismissed.value
  && pushSupported()
  && hasCurrentDevicePush.value === false,
)
</script>

<template>
  <div class="space-y-6">
    <section
      v-if="showPushBanner"
      class="flex flex-col gap-3 rounded-lg border border-blue-900/40 bg-blue-950/20 p-4 sm:flex-row sm:items-center"
    >
      <div class="grid h-10 w-10 shrink-0 place-items-center rounded-full bg-blue-500/20 text-blue-300">
        <BellRing class="h-5 w-5" />
      </div>
      <div class="flex-1 min-w-0">
        <p class="text-sm font-medium text-slate-100">{{ t('push.enable_banner_title') }}</p>
        <p class="mt-0.5 text-xs text-slate-400">{{ t('push.enable_banner_body') }}</p>
      </div>
      <div class="flex shrink-0 items-center gap-2">
        <button
          type="button"
          :disabled="enablingPush"
          class="inline-flex h-11 items-center gap-1.5 rounded-md bg-blue-600 px-3 text-sm font-medium text-white transition-colors hover:bg-blue-500 focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-blue-300 disabled:cursor-not-allowed disabled:bg-blue-900/60"
          @click="onEnablePushBanner"
        >
          {{ enablingPush ? t('push.enabling') : t('push.enable_banner_btn') }}
        </button>
        <button
          type="button"
          class="grid h-11 w-11 place-items-center rounded-md text-slate-400 transition-colors hover:bg-slate-800 hover:text-slate-200 focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-blue-500"
          :aria-label="t('action.cancel')"
          @click="dismissPushBanner"
        >
          <X class="h-4 w-4" />
        </button>
      </div>
    </section>

    <header class="flex flex-wrap items-center justify-between gap-3">
      <h1 class="text-2xl font-semibold">{{ t('notifications_view.title') }}</h1>
      <div class="flex items-center gap-3 text-sm">
        <label class="flex items-center gap-2 text-slate-300">
          <input v-model="unreadOnly" type="checkbox">
          {{ t('notifications_view.unread_only') }}
        </label>
        <button
          type="button"
          class="rounded border border-slate-700 px-3 py-1 text-slate-200 hover:bg-slate-800 disabled:opacity-40"
          :disabled="unreadCount === 0 || markAllRead.isPending.value"
          @click="onMarkAllRead"
        >
          {{ t('notifications_view.mark_all_read') }}
        </button>
      </div>
    </header>

    <p v-if="isLoading" class="text-sm text-slate-400">{{ t('notifications_view.loading') }}</p>
    <p v-else-if="isError" class="text-sm text-red-300">{{ t('notifications_view.error') }}</p>

    <ul v-else-if="items.length" class="divide-y divide-slate-800 rounded-lg border border-slate-800 bg-slate-900">
      <li
        v-for="notification in items"
        :key="notification.id"
        class="flex flex-col gap-2 px-4 py-3 sm:flex-row sm:items-start sm:gap-3"
        :class="notification.readAt ? 'opacity-70' : ''"
      >
        <div class="flex items-center gap-2 sm:contents">
          <SeverityBadge :severity="notification.severity" />
          <span class="rounded bg-slate-800 px-2 py-0.5 text-[10px] uppercase tracking-wide text-slate-400 sm:hidden">
            {{ enumLabel('NotificationKind', notification.kind) }}
          </span>
        </div>
        <div class="min-w-0 flex-1">
          <p class="flex flex-wrap items-center gap-2 text-sm font-medium text-slate-100">
            <template v-if="externalHref(notification)">
              <a
                :href="externalHref(notification)!"
                target="_blank"
                rel="noopener"
                class="hover:underline"
                :aria-label="t('notifications_view.view_pr_label')"
                @click="onRead(notification)"
              >{{ notification.title }}</a>
            </template>
            <template v-else-if="internalTo(notification)">
              <RouterLink
                :to="internalTo(notification)!"
                class="hover:underline"
                @click="onRead(notification)"
              >{{ notification.title }}</RouterLink>
            </template>
            <template v-else>{{ notification.title }}</template>
            <span class="hidden rounded bg-slate-800 px-2 py-0.5 text-[10px] uppercase tracking-wide text-slate-400 sm:inline">
              {{ enumLabel('NotificationKind', notification.kind) }}
            </span>
          </p>
          <p class="mt-1 text-sm text-slate-300">{{ notification.body }}</p>
          <p class="mt-1 text-xs text-slate-500">{{ formatDate(notification.createdAt) }}</p>
          <a
            v-if="externalHref(notification)"
            :href="externalHref(notification)!"
            target="_blank"
            rel="noopener"
            class="mt-1 inline-block text-xs text-blue-400 hover:underline"
            :aria-label="t('notifications_view.view_pr_label')"
            @click="onRead(notification)"
          >{{ t('notifications_view.view_pr_btn') }}</a>
        </div>
        <div class="flex shrink-0 flex-wrap gap-2">
          <button
            v-if="!notification.readAt"
            type="button"
            class="h-11 rounded border border-slate-700 px-3 text-xs hover:bg-slate-800"
            @click="onRead(notification)"
          >
            {{ t('notifications_view.mark_read_btn') }}
          </button>
          <button
            type="button"
            class="h-11 rounded border border-slate-700 px-3 text-xs text-slate-400 hover:bg-slate-800"
            @click="onArchive(notification)"
          >
            {{ t('notifications_view.archive_btn') }}
          </button>
        </div>
      </li>
    </ul>

    <div
      v-else
      class="rounded-lg border border-dashed border-slate-700 bg-slate-900/40 px-6 py-10 text-center"
    >
      <p class="text-sm font-medium text-slate-200">
        {{ unreadOnly ? t('notifications_view.no_unread') : t('notifications_view.no_notifications') }}
      </p>
      <p class="mt-1 text-xs text-slate-500">{{ t('notifications_view.empty_body') }}</p>
    </div>
  </div>
</template>
