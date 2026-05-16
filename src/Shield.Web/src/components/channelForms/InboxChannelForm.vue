<script setup lang="ts">
import { onMounted } from 'vue'

const emit = defineEmits<{ change: [json: string, valid: boolean] }>()

// Inbox has no config — emit once on mount so the parent's `valid` flag flips
// without the user having to touch anything.
onMounted(() => emit('change', '{}', true))
</script>

<template>
  <p class="text-sm text-slate-400">
    In-app inbox — no configuration required. Findings will appear in the
    Inbox view and persist until cleared.
  </p>
</template>
