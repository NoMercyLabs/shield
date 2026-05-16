<script setup lang="ts">
import { useSettingsQuery } from '@/queries/settings'

const { data, isLoading, isError } = useSettingsQuery()
</script>

<template>
  <div class="space-y-6">
    <h1 class="text-2xl font-semibold">Settings</h1>

    <p v-if="isLoading" class="text-sm text-slate-400">Loading…</p>
    <p v-else-if="isError" class="text-sm text-red-300">Failed to load settings.</p>

    <div v-else-if="data" class="space-y-4">
      <section class="rounded-lg border border-slate-800 bg-slate-900 p-4">
        <h2 class="text-sm font-medium text-slate-300">Single-user mode</h2>
        <p class="mt-1 text-sm text-slate-400">
          {{ data.singleUser ? 'Enabled — login skipped, auto-authenticated as Admin.' : 'Disabled — login required.' }}
        </p>
        <p class="mt-2 text-xs text-slate-500">
          Configured via <code class="rounded bg-slate-800 px-1 py-0.5">Shield:SingleUser</code> in appsettings or env.
        </p>
      </section>

      <section class="rounded-lg border border-slate-800 bg-slate-900 p-4">
        <h2 class="text-sm font-medium text-slate-300">OIDC</h2>
        <p class="mt-1 text-sm text-slate-400">
          {{ data.oidcEnabled ? `Enabled — issuer: ${data.oidcIssuer}` : 'Disabled.' }}
        </p>
        <p class="mt-2 text-xs text-slate-500">
          Configured via <code class="rounded bg-slate-800 px-1 py-0.5">Shield:Oidc</code> section in appsettings.
        </p>
      </section>
    </div>
  </div>
</template>
