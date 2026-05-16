<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink } from 'vue-router'
import {
  Bell,
  LayoutDashboard,
  Plug,
  Radio,
  ScrollText,
  Settings,
  ShieldAlert,
  Sliders,
} from 'lucide-vue-next'

import { useAuth } from '@/stores/auth'

interface NavItem {
  name: string
  to: string
  labelKey: string
  icon: typeof LayoutDashboard
  adminOnly?: boolean
}

const { isAdmin } = useAuth()
const { t } = useI18n()

const allItems: NavItem[] = [
  { name: 'dashboard', to: '/', labelKey: 'nav.dashboard', icon: LayoutDashboard },
  { name: 'sources', to: '/sources', labelKey: 'nav.sources', icon: Plug },
  { name: 'findings', to: '/findings', labelKey: 'nav.findings', icon: ShieldAlert },
  { name: 'channels', to: '/channels', labelKey: 'nav.channels', icon: Bell },
  { name: 'feeds', to: '/feeds', labelKey: 'nav.feeds', icon: Radio },
  { name: 'settings', to: '/settings', labelKey: 'nav.settings', icon: Settings },
  { name: 'audit', to: '/audit', labelKey: 'nav.audit', icon: ScrollText, adminOnly: true },
]

const items = computed(() =>
  allItems.filter(item => !item.adminOnly || isAdmin.value),
)
</script>

<template>
  <aside class="flex flex-col">
    <div class="flex h-14 items-center gap-2 border-b border-slate-800 px-4">
      <Sliders class="h-5 w-5 text-blue-400" />
      <span class="text-lg font-semibold">Shield</span>
    </div>
    <nav class="flex-1 overflow-y-auto p-2">
      <RouterLink
        v-for="item in items"
        :key="item.name"
        :to="item.to"
        class="mb-1 flex items-center gap-2 rounded px-3 py-2 text-sm text-slate-300 hover:bg-slate-800 hover:text-white"
        active-class="bg-slate-800 text-white"
      >
        <component :is="item.icon" class="h-4 w-4" />
        <span>{{ t(item.labelKey) }}</span>
      </RouterLink>
    </nav>
  </aside>
</template>
