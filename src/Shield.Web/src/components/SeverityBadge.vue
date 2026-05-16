<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'

import { severityClass, severityI18nKey } from '@/lib/format'
import type { Severity } from '@/types/api'

const props = defineProps<{ severity: Severity }>()
const { t } = useI18n()
const classes = computed(() => severityClass(props.severity))
const label = computed(() => t(severityI18nKey(props.severity)))
</script>

<template>
  <span class="inline-flex items-center rounded border px-2 py-0.5 text-xs font-medium" :class="classes">
    {{ label }}
  </span>
</template>
