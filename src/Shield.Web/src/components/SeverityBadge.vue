<script setup lang="ts">
import { computed } from 'vue'

import { severityClass, severityName } from '@/lib/format'
import type { Severity } from '@/types/api'

const props = defineProps<{ severity: Severity }>()
const classes = computed(() => severityClass(props.severity))
const label = computed(() => severityName(props.severity))
</script>

<template>
  <span class="inline-flex items-center rounded border px-2 py-0.5 text-xs font-medium" :class="classes">
    {{ label }}
  </span>
</template>
