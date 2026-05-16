<script setup lang="ts">
import { computed } from 'vue'
import { RouterLink } from 'vue-router'

import SeverityBadge from '@/components/SeverityBadge.vue'
import { useDashboardQuery } from '@/queries/dashboard'
import { formatDate } from '@/lib/format'
import type { Severity } from '@/types/api'

const { data, isLoading, isError } = useDashboardQuery()

const severityOrder: Severity[] = ['Critical', 'High', 'Medium', 'Low']
const counts = computed(() => data.value?.countsBySeverity ?? { Critical: 0, High: 0, Medium: 0, Low: 0 })
const recent = computed(() => data.value?.recentFindings ?? [])
</script>

<template>
  <div class="space-y-6">
    <h1 class="text-2xl font-semibold">Dashboard</h1>

    <p v-if="isLoading" class="text-sm text-slate-400">Loading…</p>
    <p v-else-if="isError" class="text-sm text-red-300">Failed to load dashboard.</p>

    <div v-else class="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
      <div
        v-for="severity in severityOrder"
        :key="severity"
        class="rounded-lg border border-slate-800 bg-slate-900 p-4"
      >
        <SeverityBadge :severity="severity" />
        <p class="mt-2 text-3xl font-semibold">{{ counts[severity] ?? 0 }}</p>
        <p class="text-xs text-slate-500">open findings</p>
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
              {{ finding.packageName }}@{{ finding.packageVersion }}
            </RouterLink>
            <p class="text-xs text-slate-500">{{ finding.sourceName }} · {{ formatDate(finding.firstSeenAt) }}</p>
          </div>
          <SeverityBadge :severity="finding.severity" />
        </li>
      </ul>
      <p v-else class="px-4 py-6 text-sm text-slate-500">No findings yet.</p>
    </section>
  </div>
</template>
