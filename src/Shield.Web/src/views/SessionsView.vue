<script setup lang="ts">
import { computed } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { LogOut, ShieldAlert, Trash2 } from 'lucide-vue-next'

import {
  useRevokeAllSessionsMutation,
  useRevokeOtherSessionsMutation,
  useRevokeSessionMutation,
  useSessionsQuery,
} from '@/queries/sessions'
import { useAuth } from '@/stores/auth'
import { useToasts } from '@/stores/toast'
import type { SessionInfo } from '@/types/api'

const { t } = useI18n()
const { push } = useToasts()
const router = useRouter()
const auth = useAuth()

const { data, isLoading, isError, refetch } = useSessionsQuery(false)
const revoke = useRevokeSessionMutation()
const revokeOthers = useRevokeOtherSessionsMutation()
const revokeAll = useRevokeAllSessionsMutation()

const sessions = computed<SessionInfo[]>(() => data.value ?? [])
const hasOthers = computed(() => sessions.value.some(session => !session.isCurrent))
const hasAny = computed(() => sessions.value.length > 0)

function formatRelative(iso: string): string {
  const then = new Date(iso).getTime()
  const seconds = Math.max(0, Math.round((Date.now() - then) / 1000))
  if (seconds < 60) return t('screen.sessions.just_now')
  const minutes = Math.round(seconds / 60)
  if (minutes < 60) return t('screen.sessions.minutes_ago', { n: minutes })
  const hours = Math.round(minutes / 60)
  if (hours < 24) return t('screen.sessions.hours_ago', { n: hours })
  const days = Math.round(hours / 24)
  return t('screen.sessions.days_ago', { n: days })
}

function deviceLabel(ua: string | null): string {
  if (!ua) return t('screen.sessions.unknown_device')
  // Cheap UA sniff — server doesn't parse for us. Just enough so a user can tell their laptop
  // apart from their phone. No need for a real ua-parser lib here.
  const lower = ua.toLowerCase()
  let os = t('screen.sessions.unknown_os')
  if (lower.includes('windows')) os = 'Windows'
  else if (lower.includes('mac os')) os = 'macOS'
  else if (lower.includes('android')) os = 'Android'
  else if (lower.includes('iphone') || lower.includes('ipad')) os = 'iOS'
  else if (lower.includes('linux')) os = 'Linux'
  let browser = t('screen.sessions.unknown_browser')
  if (lower.includes('edg/')) browser = 'Edge'
  else if (lower.includes('chrome/')) browser = 'Chrome'
  else if (lower.includes('firefox/')) browser = 'Firefox'
  else if (lower.includes('safari/')) browser = 'Safari'
  return `${browser} · ${os}`
}

async function onRevoke(id: string): Promise<void> {
  try {
    await revoke.mutateAsync(id)
    push('success', t('toast.session_revoked'))
  }
  catch {
    push('error', t('error.session_revoke_failed'))
  }
}

async function onRevokeOthers(): Promise<void> {
  try {
    const count = await revokeOthers.mutateAsync()
    push('success', t('toast.sessions_revoked_others', { n: count }))
    await refetch()
  }
  catch {
    push('error', t('error.session_revoke_failed'))
  }
}

async function onRevokeAll(): Promise<void> {
  // Panic button — confirm because it kills the caller's session too.
  if (!window.confirm(t('screen.sessions.revoke_all_confirm')))
    return
  try {
    const count = await revokeAll.mutateAsync()
    push('success', t('toast.sessions_revoked_all', { n: count }))
    // Server signed us out + bumped the SecurityStamp; clear local state and bounce to login.
    auth.setUser(null)
    await router.replace({ name: 'login' })
  }
  catch {
    push('error', t('error.session_revoke_failed'))
  }
}
</script>

<template>
  <div class="space-y-6">
    <header class="flex items-end justify-between gap-4">
      <div>
        <h1 class="text-2xl font-semibold">{{ t('screen.sessions.title') }}</h1>
        <p class="text-sm text-slate-400">{{ t('screen.sessions.subtitle') }}</p>
      </div>
      <div class="flex items-center gap-2">
        <button
          type="button"
          :disabled="!hasOthers || revokeOthers.isPending.value"
          class="inline-flex items-center gap-2 rounded bg-slate-800 px-3 py-1.5 text-sm font-medium text-slate-200 hover:bg-slate-700 disabled:cursor-not-allowed disabled:opacity-50"
          @click="onRevokeOthers"
        >
          <LogOut class="h-4 w-4" />
          {{ t('screen.sessions.revoke_others_btn') }}
        </button>
        <button
          type="button"
          :disabled="!hasAny || revokeAll.isPending.value"
          class="inline-flex items-center gap-2 rounded border border-red-700/60 bg-red-900/30 px-3 py-1.5 text-sm font-medium text-red-200 hover:bg-red-900/50 disabled:cursor-not-allowed disabled:opacity-50"
          @click="onRevokeAll"
        >
          <ShieldAlert class="h-4 w-4" />
          {{ t('screen.sessions.revoke_all_btn') }}
        </button>
      </div>
    </header>

    <p v-if="isLoading" class="text-sm text-slate-400">{{ t('state.loading') }}</p>
    <p v-else-if="isError" class="text-sm text-red-300">{{ t('state.error') }}</p>

    <ul v-else class="divide-y divide-slate-800 rounded-lg border border-slate-800 bg-slate-900">
      <li
        v-for="session in sessions"
        :key="session.id"
        class="flex items-center justify-between gap-4 px-4 py-3"
      >
        <div class="min-w-0">
          <div class="flex items-center gap-2 text-sm">
            <span class="font-medium text-slate-100">{{ deviceLabel(session.userAgent) }}</span>
            <span
              v-if="session.isCurrent"
              class="rounded-full bg-emerald-700/40 px-2 py-0.5 text-xs text-emerald-200"
            >{{ t('screen.sessions.current_badge') }}</span>
          </div>
          <p class="truncate text-xs text-slate-400">
            {{ session.remoteIp ?? t('screen.sessions.unknown_ip') }}
            · {{ t('screen.sessions.last_active', { when: formatRelative(session.lastActiveAt) }) }}
          </p>
        </div>
        <button
          v-if="!session.isCurrent"
          type="button"
          :disabled="revoke.isPending.value"
          class="inline-flex items-center gap-1 rounded border border-slate-700 px-2 py-1 text-xs text-slate-300 hover:border-red-700 hover:text-red-300 disabled:cursor-not-allowed disabled:opacity-50"
          @click="onRevoke(session.id)"
        >
          <Trash2 class="h-3 w-3" />
          {{ t('screen.sessions.revoke_btn') }}
        </button>
      </li>
      <li v-if="sessions.length === 0" class="px-4 py-6 text-center text-sm text-slate-400">
        {{ t('screen.sessions.empty') }}
      </li>
    </ul>
  </div>
</template>
