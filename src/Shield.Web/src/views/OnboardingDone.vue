<script setup lang="ts">
import { computed } from 'vue'
import { RouterLink } from 'vue-router'
import { CheckCircle2, FileSearch, ListChecks, Settings as SettingsIcon } from 'lucide-vue-next'

import { useOnboardingStatus } from '@/queries/onboarding'

const { data: status } = useOnboardingStatus()

const sourceCount = computed(() => status.value?.sourceCount ?? 0)
const channelCount = computed(() => status.value?.channelCount ?? 0)
</script>

<template>
  <section class="space-y-6">
    <div class="flex items-center gap-3">
      <CheckCircle2 class="h-10 w-10 text-emerald-400" />
      <div>
        <h1 class="text-2xl font-semibold">You're set up</h1>
        <p class="text-sm text-slate-400">
          Shield will pick up the rest from here. Here's what's already wired in:
        </p>
      </div>
    </div>

    <div class="grid grid-cols-1 gap-3 sm:grid-cols-2">
      <div class="rounded-lg border border-slate-800 bg-slate-900 p-4">
        <p class="text-xs uppercase text-slate-500">Sources</p>
        <p class="mt-1 text-2xl font-semibold text-slate-100">{{ sourceCount }}</p>
        <p class="text-xs text-slate-500">{{ sourceCount === 0 ? 'None yet — add some from the Sources page.' : 'Watched for new advisories on a schedule.' }}</p>
      </div>
      <div class="rounded-lg border border-slate-800 bg-slate-900 p-4">
        <p class="text-xs uppercase text-slate-500">Alert channels</p>
        <p class="mt-1 text-2xl font-semibold text-slate-100">{{ channelCount }}</p>
        <p class="text-xs text-slate-500">{{ channelCount === 0 ? 'None yet — alerts will sit in the in-app inbox.' : 'Ready to receive findings.' }}</p>
      </div>
    </div>

    <div class="rounded-lg border border-blue-900/40 bg-blue-900/10 p-4">
      <p class="flex items-center gap-2 text-sm font-medium text-blue-100">
        <FileSearch class="h-4 w-4" />
        Tip: scan a source on demand
      </p>
      <p class="mt-1 text-xs text-blue-200/80">
        Scheduled scans run hourly by default. To see findings immediately, open a source from the
        Sources page and click <strong>Scan now</strong>.
      </p>
    </div>

    <div class="flex flex-wrap items-center gap-2">
      <RouterLink
        to="/sources"
        class="inline-flex items-center gap-1.5 rounded border border-slate-700 px-3 py-1.5 text-sm text-slate-100 hover:bg-slate-800"
      >
        <ListChecks class="h-3.5 w-3.5" />
        Manage sources
      </RouterLink>
      <RouterLink
        to="/settings"
        class="inline-flex items-center gap-1.5 rounded border border-slate-700 px-3 py-1.5 text-sm text-slate-100 hover:bg-slate-800"
      >
        <SettingsIcon class="h-3.5 w-3.5" />
        Open Settings
      </RouterLink>
    </div>
  </section>
</template>
