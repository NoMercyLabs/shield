<script setup lang="ts">
import { RouterLink } from 'vue-router'
import {
  Bell,
  LayoutDashboard,
  Plug,
  Radio,
  Settings,
  ShieldAlert,
  Sliders,
} from 'lucide-vue-next'

interface NavItem {
  name: string
  to: string
  label: string
  icon: typeof LayoutDashboard
}

const items: NavItem[] = [
  { name: 'dashboard', to: '/', label: 'Dashboard', icon: LayoutDashboard },
  { name: 'sources', to: '/sources', label: 'Sources', icon: Plug },
  { name: 'findings', to: '/findings', label: 'Findings', icon: ShieldAlert },
  { name: 'channels', to: '/channels', label: 'Channels', icon: Bell },
  { name: 'feeds', to: '/feeds', label: 'Feeds', icon: Radio },
  { name: 'settings', to: '/settings', label: 'Settings', icon: Settings },
]
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
        <span>{{ item.label }}</span>
      </RouterLink>
    </nav>
  </aside>
</template>
