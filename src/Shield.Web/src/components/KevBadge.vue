<script setup lang="ts">
import { computed } from 'vue'

const props = defineProps<{
  isKev?: boolean | null
  dueDate?: string | null
}>()

const tooltip = computed(() => {
  if (!props.dueDate) return 'CISA KEV: known to be exploited in the wild'
  const due = new Date(props.dueDate)
  if (Number.isNaN(due.getTime())) return 'CISA KEV: known to be exploited in the wild'
  return `CISA KEV: patch by ${due.toISOString().slice(0, 10)}`
})
</script>

<template>
  <span
    v-if="isKev"
    class="inline-flex items-center gap-1 rounded border border-red-800 bg-red-950/50 px-2 py-0.5 text-xs font-medium text-red-200"
    :title="tooltip"
  >
    KEV exploited
  </span>
</template>
