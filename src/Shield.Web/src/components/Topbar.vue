<script setup lang="ts">
import { useRouter } from 'vue-router'
import { LogOut } from 'lucide-vue-next'

import { logout, useAuth } from '@/stores/auth'

const { user } = useAuth()
const router = useRouter()

async function onLogout(): Promise<void> {
  await logout()
  router.push({ name: 'login' })
}
</script>

<template>
  <header class="flex items-center justify-between px-4">
    <div class="text-sm text-slate-400">
      <span v-if="user?.singleUser" class="rounded bg-slate-800 px-2 py-1 text-xs uppercase tracking-wide">Single-user mode</span>
    </div>
    <div class="flex items-center gap-3">
      <span v-if="user" class="text-sm text-slate-300">{{ user.username }}</span>
      <button
        v-if="user && !user.singleUser"
        type="button"
        class="flex items-center gap-1 rounded px-3 py-1.5 text-sm text-slate-300 hover:bg-slate-800 hover:text-white"
        @click="onLogout"
      >
        <LogOut class="h-4 w-4" />
        <span>Logout</span>
      </button>
    </div>
  </header>
</template>
