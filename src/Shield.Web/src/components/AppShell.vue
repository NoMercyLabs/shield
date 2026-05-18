<script setup lang="ts">
import { onMounted, onUnmounted, ref, watch, watchEffect } from 'vue'
import { useRoute } from 'vue-router'

import Sidebar from '@/components/Sidebar.vue'
import Topbar from '@/components/Topbar.vue'
import ToastStack from '@/components/ToastStack.vue'
import { useLiveFindings } from '@/stores/liveFindings'

const live = useLiveFindings()
const route = useRoute()
const drawerOpen = ref(false)

watchEffect(() => {
  const urgent = live.urgentCount.value
  document.title = urgent > 0 ? `(${urgent}) Shield` : 'Shield'
})

// Auto-close the drawer when the route changes — clicking a link inside the drawer should
// transition the user to the page AND dismiss the overlay. The watch on route.fullPath is
// the only reliable signal; SPA route changes don't fire any DOM event we could use here.
watch(() => route.fullPath, () => {
  if (drawerOpen.value)
    drawerOpen.value = false
})

function onEscape(event: KeyboardEvent): void {
  if (event.key === 'Escape' && drawerOpen.value)
    drawerOpen.value = false
}

onMounted(() => {
  document.addEventListener('keydown', onEscape)
})

onUnmounted(() => {
  document.removeEventListener('keydown', onEscape)
})

function toggleDrawer(): void {
  drawerOpen.value = !drawerOpen.value
}

function closeDrawer(): void {
  drawerOpen.value = false
}
</script>

<template>
  <div class="flex h-full w-full overflow-hidden">
    <!-- Desktop sidebar — always visible at md+. Width fixed at 15rem (60). -->
    <Sidebar class="hidden w-60 shrink-0 border-r border-slate-800 bg-slate-900 md:flex" />

    <!-- Mobile drawer — slides in from the left under md, backed by a translucent scrim that
         dismisses on tap. The drawer itself is the same Sidebar component so we don't fork
         the nav into two trees. -->
    <Transition
      enter-active-class="transition-opacity duration-150"
      leave-active-class="transition-opacity duration-150"
      enter-from-class="opacity-0"
      leave-to-class="opacity-0"
    >
      <div
        v-if="drawerOpen"
        class="fixed inset-0 z-40 bg-slate-950/70 backdrop-blur-sm md:hidden"
        aria-hidden="true"
        @click="closeDrawer"
      />
    </Transition>
    <Transition
      enter-active-class="transition-transform duration-200"
      leave-active-class="transition-transform duration-200"
      enter-from-class="-translate-x-full"
      leave-to-class="-translate-x-full"
    >
      <Sidebar
        v-if="drawerOpen"
        class="fixed inset-y-0 left-0 z-50 flex w-72 max-w-[80vw] border-r border-slate-800 bg-slate-900 shadow-2xl md:hidden"
        role="dialog"
        aria-modal="true"
      />
    </Transition>

    <div class="flex flex-1 flex-col overflow-hidden">
      <Topbar
        class="h-14 shrink-0 border-b border-slate-800 bg-slate-900"
        :drawer-open="drawerOpen"
        @toggle-drawer="toggleDrawer"
      />
      <main class="flex-1 overflow-y-auto px-4 pb-[max(env(safe-area-inset-bottom),1rem)] pt-4 md:px-6 md:pt-6">
        <slot />
      </main>
    </div>
    <ToastStack />
  </div>
</template>
