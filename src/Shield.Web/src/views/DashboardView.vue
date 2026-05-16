<script setup lang="ts">
import { computed } from 'vue'
import { RouterLink } from 'vue-router'

import SeverityBadge from '@/components/SeverityBadge.vue'
import { useDashboardQuery } from '@/queries/dashboard'
import { formatDate } from '@/lib/format'
import { Severity } from '@/types/api'

const { data, isLoading, isError } = useDashboardQuery()

interface SeverityCard {
  severity: Severity
  count: number
}

const cards = computed<SeverityCard[]>(() => {
  const counts = data.value?.openCounts ?? { low: 0, medium: 0, high: 0, critical: 0 }
  return [
    { severity: Severity.Critical, count: counts.critical },
    { severity: Severity.High, count: counts.high },
    { severity: Severity.Medium, count: counts.medium },
    { severity: Severity.Low, count: counts.low },
  ]
})

const recent = computed(() => data.value?.recentFindings ?? [])
const healthy = computed(() => data.value?.sourcesHealthy ?? 0)
const stale = computed(() => data.value?.sourcesStale ?? 0)
</script>

<template>
  <div class="space-y-6">
    <h1 class="text-2xl font-semibold">Dashboard</h1>

    <p v-if="isLoading" class="text-sm text-slate-400">Loading…</p>
    <p v-else-if="isError" class="text-sm text-red-300">Failed to load dashboard.</p>

    <template v-else>
      <div class="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <div
          v-for="card in cards"
          :key="card.severity"
          class="rounded-lg border border-slate-800 bg-slate-900 p-4"
        >
          <SeverityBadge :severity="card.severity" />
          <p class="mt-2 text-3xl font-semibold">{{ card.count }}</p>
          <p class="text-xs text-slate-500">open findings</p>
        </div>
      </div>

      <div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
        <div class="rounded-lg border border-slate-800 bg-slate-900 p-4">
          <p class="text-xs uppercase text-slate-500">Sources healthy</p>
          <p class="mt-2 text-3xl font-semibold text-green-300">{{ healthy }}</p>
        </div>
        <div class="rounded-lg border border-slate-800 bg-slate-900 p-4">
          <p class="text-xs uppercase text-slate-500">Sources stale</p>
          <p class="mt-2 text-3xl font-semibold text-yellow-300">{{ stale }}</p>
        </div>
      </div>

      <section class="rounded-lg border border-slate-800 bg-slate-900">
        <header class="flex items-center justify-between border-b border-slate-800 px-4 py-3">
          <h2 class="text-lg font-medium">Recent findings</h2>
          <RouterLink to="/findings" class="text-sm text-blue-400 hover:underline">View all</RouterLink>
        </header>
        <ul v-if="recent.length" class="divide-y divide-slate-800">
          <li
            v-for="finding in recent"
            :key="finding.id"
            class="flex items-center justify-between px-4 py-3"
          >
            <div>
              <RouterLink
                :to="`/findings/${finding.id}`"
                class="text-sm font-medium text-slate-100 hover:underline"
              >
                {{ finding.dedupKey }}
              </RouterLink>
              <p class="text-xs text-slate-500">
                Source #{{ finding.sourceId }} · first seen {{ formatDate(finding.firstSeenAt) }}
              </p>
            </div>
            <SeverityBadge :severity="finding.severity" />
          </li>
        </ul>
        <p v-else class="px-4 py-6 text-sm text-slate-500">No findings yet.</p>
      </section>
    </template>
  </div>
</template>
