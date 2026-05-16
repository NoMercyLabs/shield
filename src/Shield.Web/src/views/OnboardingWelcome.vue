<script setup lang="ts">
import { ShieldCheck } from 'lucide-vue-next'
</script>

<template>
  <section class="space-y-6">
    <div class="flex items-center gap-3">
      <ShieldCheck class="h-10 w-10 text-blue-400" />
      <div>
        <h1 class="text-2xl font-semibold">Welcome to Shield</h1>
        <p class="text-sm text-slate-400">
          A self-hosted, cross-ecosystem dependency vulnerability watcher for the code you ship.
        </p>
      </div>
    </div>

    <div class="rounded-lg border border-slate-800 bg-slate-900 p-5 space-y-3">
      <p class="text-sm font-medium text-slate-200">Here's what we'll set up in the next few steps:</p>
      <ul class="space-y-2 text-sm text-slate-300">
        <li class="flex items-start gap-2">
          <span class="mt-1 inline-block h-1.5 w-1.5 rounded-full bg-blue-400" />
          <span>
            <strong class="text-slate-100">A code source</strong> — a GitHub repo or a local folder
            with lockfiles. Shield watches it for new advisories.
          </span>
        </li>
        <li class="flex items-start gap-2">
          <span class="mt-1 inline-block h-1.5 w-1.5 rounded-full bg-blue-400" />
          <span>
            <strong class="text-slate-100">An alert channel</strong> — where Shield pings you when a
            new vulnerability lands. Discord works in seconds.
          </span>
        </li>
        <li class="flex items-start gap-2">
          <span class="mt-1 inline-block h-1.5 w-1.5 rounded-full bg-blue-400" />
          <span>
            <strong class="text-slate-100">The Dashboard</strong> — your at-a-glance view of every
            open finding, severity-graded and grouped by source.
          </span>
        </li>
      </ul>
    </div>

    <p class="text-xs text-slate-500">
      You can skip any step and configure it later from the Sources or Channels pages.
    </p>
  </section>
</template>
