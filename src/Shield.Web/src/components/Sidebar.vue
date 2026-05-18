<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink } from 'vue-router'
import {
  ArrowUpCircle,
  Bell,
  Inbox,
  KeyRound,
  LayoutDashboard,
  Lock,
  Plug,
  Radio,
  ScrollText,
  Settings,
  ShieldAlert,
  ShieldCheck,
} from 'lucide-vue-next'

import { useAuth } from '@/stores/auth'
import { useLiveFindings } from '@/stores/liveFindings'
import { useLiveSecurityEvents } from '@/stores/liveSecurityEvents'

interface NavItem {
  name: string
  to: string
  labelKey: string
  icon: typeof LayoutDashboard
  adminOnly?: boolean
  badge?: () => number
}

interface NavGroup {
  key: string
  labelKey: string
  adminOnly?: boolean
  items: NavItem[]
}

const { isAdmin } = useAuth()
const { t } = useI18n()
const live = useLiveFindings()
const liveSecurity = useLiveSecurityEvents()

const groups: NavGroup[] = [
  {
    key: 'monitor',
    labelKey: 'sidebar.group.monitor',
    items: [
      { name: 'dashboard', to: '/', labelKey: 'nav.dashboard', icon: LayoutDashboard },
      { name: 'findings', to: '/findings', labelKey: 'nav.findings', icon: ShieldAlert, badge: () => live.urgentCount.value },
      { name: 'notifications', to: '/notifications', labelKey: 'nav.notifications', icon: Inbox },
    ],
  },
  {
    key: 'sources',
    labelKey: 'sidebar.group.sources',
    items: [
      { name: 'sources', to: '/sources', labelKey: 'nav.sources', icon: Plug },
      { name: 'updates', to: '/updates', labelKey: 'nav.updates', icon: ArrowUpCircle },
    ],
  },
  {
    key: 'channels',
    labelKey: 'sidebar.group.channels',
    items: [
      { name: 'channels', to: '/channels', labelKey: 'nav.channels', icon: Bell },
      { name: 'feeds', to: '/feeds', labelKey: 'nav.feeds', icon: Radio },
    ],
  },
  {
    key: 'admin',
    labelKey: 'sidebar.group.admin',
    adminOnly: true,
    items: [
      { name: 'access', to: '/access', labelKey: 'nav.access', icon: KeyRound, adminOnly: true },
      { name: 'settings', to: '/settings', labelKey: 'nav.settings', icon: Settings },
      { name: 'security', to: '/security', labelKey: 'nav.security', icon: Lock, adminOnly: true, badge: () => liveSecurity.banCount.value },
      { name: 'audit', to: '/audit', labelKey: 'nav.audit', icon: ScrollText, adminOnly: true },
      { name: 'tokens', to: '/account/tokens', labelKey: 'nav.tokens', icon: KeyRound },
    ],
  },
]

const visibleGroups = computed(() =>
  groups
    .filter(group => !group.adminOnly || isAdmin.value)
    .map(group => ({
      ...group,
      items: group.items.filter(item => !item.adminOnly || isAdmin.value),
    }))
    .filter(group => group.items.length > 0),
)
</script>

<template>
  <aside class="flex flex-col">
    <div class="flex h-14 shrink-0 items-center gap-2 border-b border-slate-800 px-4">
      <ShieldCheck class="h-5 w-5 text-blue-400" />
      <span class="text-lg font-semibold tracking-tight">Shield</span>
    </div>
    <nav class="flex-1 space-y-4 overflow-y-auto px-2 py-3">
      <div v-for="group in visibleGroups" :key="group.key" class="space-y-0.5">
        <p class="px-3 py-1.5 text-[10px] font-semibold uppercase tracking-wider text-slate-500">
          {{ t(group.labelKey) }}
        </p>
        <RouterLink
          v-for="item in group.items"
          :key="item.name"
          :to="item.to"
          class="group flex items-center gap-3 rounded-md px-3 py-2 text-sm text-slate-300 transition-colors hover:bg-slate-800/60 hover:text-white focus-visible:bg-slate-800/60 focus-visible:text-white focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-blue-500"
          :active-class="'bg-blue-600/20 text-blue-200 font-medium [&_svg]:text-blue-300'"
        >
          <component :is="item.icon" class="h-4 w-4 shrink-0 text-slate-500 group-hover:text-slate-300 transition-colors" />
          <span class="flex-1 truncate">{{ t(item.labelKey) }}</span>
          <span
            v-if="item.badge && item.badge() > 0"
            class="inline-flex h-5 min-w-[1.25rem] items-center justify-center rounded-full bg-red-500/90 px-1.5 text-[10px] font-semibold text-white"
          >
            {{ item.badge()! > 99 ? '99+' : item.badge() }}
          </span>
        </RouterLink>
      </div>
    </nav>
  </aside>
</template>
