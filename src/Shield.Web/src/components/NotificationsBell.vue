<script setup lang="ts">
import { computed, onMounted, onUnmounted } from 'vue'
import { useQueryClient } from '@tanstack/vue-query'
import { useRouter } from 'vue-router'
import { Bell } from 'lucide-vue-next'

import { getFindingsConnection } from '@/lib/signalr'
import { useNotificationsQuery } from '@/queries/notifications'

const { data } = useNotificationsQuery(false, 50)
const router = useRouter()
const queryClient = useQueryClient()

const unread = computed<number>(() => data.value?.unreadCount ?? 0)

// SignalR push: when the publisher emits, invalidate the query so the badge + inbox both
// refresh. The query cache key matches `useNotificationsQuery` so any open view bumps.
function onPush(): void {
  queryClient.invalidateQueries({ queryKey: ['notifications'] })
}

let attached = false

onMounted(() => {
  const connection = getFindingsConnection()
  if (attached) return
  attached = true
  connection.on('notifications.new', onPush)
})

onUnmounted(() => {
  if (!attached) return
  const connection = getFindingsConnection()
  connection.off('notifications.new', onPush)
  attached = false
})

function go(): void {
  router.push('/notifications')
}
</script>

<template>
  <button
    type="button"
    class="relative flex items-center gap-1 rounded px-2 py-1 text-slate-300 hover:bg-slate-800 hover:text-white"
    :aria-label="`Notifications (${unread} unread)`"
    @click="go"
  >
    <Bell class="h-4 w-4" />
    <span
      v-if="unread > 0"
      class="absolute -right-1 -top-1 inline-flex h-4 min-w-[1rem] items-center justify-center rounded-full bg-red-500 px-1 text-[10px] font-semibold text-white"
    >
      {{ unread > 99 ? '99+' : unread }}
    </span>
  </button>
</template>
