<script setup lang="ts">
import { computed } from 'vue'
import { RouterLink } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { CheckCircle2, FileSearch, ListChecks, Settings as SettingsIcon } from 'lucide-vue-next'

import { useOnboardingStatus } from '@/queries/onboarding'

const { t } = useI18n()
const { data: status } = useOnboardingStatus()

const sourceCount = computed(() => status.value?.sourceCount ?? 0)
const channelCount = computed(() => status.value?.channelCount ?? 0)
</script>

<template>
  <section class="space-y-6">
    <div class="flex items-center gap-3">
      <CheckCircle2 class="h-10 w-10 text-emerald-400" />
      <div>
        <h1 class="text-2xl font-semibold">{{ t('onboarding.done_title') }}</h1>
        <p class="text-sm text-slate-400">{{ t('onboarding.done_subtitle') }}</p>
      </div>
    </div>

    <div class="grid grid-cols-1 gap-3 sm:grid-cols-2">
      <div class="rounded-lg border border-slate-800 bg-slate-900 p-4">
        <p class="text-xs uppercase text-slate-500">{{ t('onboarding.done_sources_label') }}</p>
        <p class="mt-1 text-2xl font-semibold text-slate-100">{{ sourceCount }}</p>
        <p class="text-xs text-slate-500">{{ sourceCount === 0 ? t('onboarding.done_sources_none') : t('onboarding.done_sources_ok') }}</p>
      </div>
      <div class="rounded-lg border border-slate-800 bg-slate-900 p-4">
        <p class="text-xs uppercase text-slate-500">{{ t('onboarding.done_channels_label') }}</p>
        <p class="mt-1 text-2xl font-semibold text-slate-100">{{ channelCount }}</p>
        <p class="text-xs text-slate-500">{{ channelCount === 0 ? t('onboarding.done_channels_none') : t('onboarding.done_channels_ok') }}</p>
      </div>
    </div>

    <div class="rounded-lg border border-blue-900/40 bg-blue-900/10 p-4">
      <p class="flex items-center gap-2 text-sm font-medium text-blue-100">
        <FileSearch class="h-4 w-4" />
        {{ t('onboarding.done_tip_title') }}
      </p>
      <p class="mt-1 text-xs text-blue-200/80" v-html="t('onboarding.done_tip_body')" />
    </div>

    <div class="flex flex-wrap items-center gap-2">
      <RouterLink
        to="/sources"
        class="inline-flex items-center gap-1.5 rounded border border-slate-700 px-3 py-1.5 text-sm text-slate-100 hover:bg-slate-800"
      >
        <ListChecks class="h-3.5 w-3.5" />
        {{ t('onboarding.done_manage_sources') }}
      </RouterLink>
      <RouterLink
        to="/settings"
        class="inline-flex items-center gap-1.5 rounded border border-slate-700 px-3 py-1.5 text-sm text-slate-100 hover:bg-slate-800"
      >
        <SettingsIcon class="h-3.5 w-3.5" />
        {{ t('onboarding.done_open_settings') }}
      </RouterLink>
    </div>
  </section>
</template>
