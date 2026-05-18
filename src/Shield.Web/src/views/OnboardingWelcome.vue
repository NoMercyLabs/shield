<script setup lang="ts">
import { ShieldCheck } from 'lucide-vue-next'
import { useI18n } from 'vue-i18n'

const { t } = useI18n()
</script>

<template>
  <section class="space-y-6">
    <div class="flex items-center gap-3">
      <ShieldCheck class="h-10 w-10 text-blue-400" />
      <div>
        <h1 class="text-2xl font-semibold">{{ t('onboarding.welcome_title') }}</h1>
        <p class="text-sm text-slate-400">{{ t('onboarding.welcome_subtitle') }}</p>
      </div>
    </div>

    <div class="rounded-lg border border-slate-800 bg-slate-900 p-5 space-y-3">
      <p class="text-sm font-medium text-slate-200">{{ t('onboarding.welcome_steps_intro') }}</p>
      <ul class="space-y-2 text-sm text-slate-300">
        <li class="flex items-start gap-2">
          <span class="mt-1 inline-block h-1.5 w-1.5 rounded-full bg-blue-400" />
          <span v-html="t('onboarding.welcome_step_source')" />
        </li>
        <li class="flex items-start gap-2">
          <span class="mt-1 inline-block h-1.5 w-1.5 rounded-full bg-blue-400" />
          <span v-html="t('onboarding.welcome_step_channel')" />
        </li>
        <li class="flex items-start gap-2">
          <span class="mt-1 inline-block h-1.5 w-1.5 rounded-full bg-blue-400" />
          <span v-html="t('onboarding.welcome_step_dashboard')" />
        </li>
      </ul>
    </div>

    <p class="text-xs text-slate-500">{{ t('onboarding.welcome_skip_hint') }}</p>
  </section>
</template>
