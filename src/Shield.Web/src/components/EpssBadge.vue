<script setup lang="ts">
import { computed } from 'vue'

const props = defineProps<{
  score?: number | null
  percentile?: number | null
}>()

const tone = computed(() => {
  const score = props.score
  if (score == null) return ''
  if (score >= 0.5) return 'border-orange-800 bg-orange-950/40 text-orange-200'
  if (score >= 0.1) return 'border-yellow-800 bg-yellow-950/40 text-yellow-200'
  return 'border-slate-700 bg-slate-800/60 text-slate-300'
})

const label = computed(() => {
  const score = props.score
  if (score == null) return ''
  return `EPSS ${score.toFixed(2)}`
})

const tooltip = computed(() => {
  const score = props.score
  const percentile = props.percentile
  if (score == null) return ''
  const pct = percentile == null ? '' : ` (${Math.round(percentile * 100)}th pct)`
  return `Exploit Prediction Scoring System: ${(score * 100).toFixed(1)}% probability of exploitation in next 30 days${pct}`
})
</script>

<template>
  <span
    v-if="score != null"
    class="inline-flex items-center rounded border px-2 py-0.5 text-xs font-medium"
    :class="tone"
    :title="tooltip"
  >
    {{ label }}
  </span>
</template>
