<script setup lang="ts">
import { computed } from 'vue'

import { severityClass } from '@/lib/format'
import type { Severity } from '@/types/api'

const props = defineProps<{ severity: Severity }>()
const classes = computed(() => severityClass(props.severity))
</script>

<template>
  <span class="inline-flex items-center rounded border px-2 py-0.5 text-xs font-medium" :class="classes">
    {{ severity }}
  </span>
</template>
