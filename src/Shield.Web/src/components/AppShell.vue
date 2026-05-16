<script setup lang="ts">
import { watchEffect } from 'vue'

import Sidebar from '@/components/Sidebar.vue'
import Topbar from '@/components/Topbar.vue'
import ToastStack from '@/components/ToastStack.vue'
import { useLiveFindings } from '@/stores/liveFindings'

const live = useLiveFindings()

watchEffect(() => {
  const urgent = live.urgentCount.value
  document.title = urgent > 0 ? `(${urgent}) Shield` : 'Shield'
})
</script>

<template>
  <div class="flex h-screen w-screen overflow-hidden bg-slate-950 text-slate-100">
    <Sidebar class="w-60 shrink-0 border-r border-slate-800 bg-slate-900" />
    <div class="flex flex-1 flex-col overflow-hidden">
      <Topbar class="h-14 shrink-0 border-b border-slate-800 bg-slate-900" />
      <main class="flex-1 overflow-y-auto p-6">
        <slot />
      </main>
    </div>
    <ToastStack />
  </div>
</template>
