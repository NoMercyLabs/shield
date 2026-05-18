<script setup lang="ts">
import { computed } from 'vue'
import { useRouter } from 'vue-router'
import { Eye, LogOut } from 'lucide-vue-next'

import { useStopImpersonationMutation } from '@/queries/impersonation'
import { useAuth } from '@/stores/auth'
import { useToasts } from '@/stores/toast'

const auth = useAuth()
const router = useRouter()
const { push } = useToasts()
const stop = useStopImpersonationMutation()

const visible = computed(() => auth.isImpersonating.value)
const impersonatorLogin = computed(() => auth.user.value?.impersonatorLogin ?? 'admin')
const impersonatedLogin = computed(() => auth.user.value?.username ?? 'user')

async function onExit(): Promise<void> {
  try {
    await stop.mutateAsync()
    push('success', 'Exited impersonation.')
    await router.push({ name: 'access' })
  }
  catch {
    push('error', 'Failed to exit impersonation.')
  }
}
</script>

<template>
  <div
    v-if="visible"
    class="flex shrink-0 items-center gap-3 border-b border-amber-700 bg-amber-600 px-4 py-2 text-sm text-amber-50 shadow"
    role="alert"
  >
    <Eye class="h-4 w-4 shrink-0" />
    <span class="flex-1">
      <strong>{{ impersonatorLogin }}</strong> viewing as <strong>@{{ impersonatedLogin }}</strong>
    </span>
    <button
      type="button"
      :disabled="stop.isPending.value"
      class="inline-flex items-center gap-1 rounded bg-amber-900/40 px-2 py-1 text-xs font-medium text-amber-50 hover:bg-amber-900/60 disabled:opacity-60"
      @click="onExit"
    >
      <LogOut class="h-3 w-3" />
      <span v-if="stop.isPending.value">Exiting…</span>
      <span v-else>Exit impersonation</span>
    </button>
  </div>
</template>
