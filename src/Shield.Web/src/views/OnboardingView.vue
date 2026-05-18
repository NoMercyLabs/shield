<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ArrowLeft, ArrowRight, ShieldCheck } from 'lucide-vue-next'
import { useI18n } from 'vue-i18n'

import OnboardingChannel from '@/views/OnboardingChannel.vue'
import OnboardingDone from '@/views/OnboardingDone.vue'
import OnboardingSource from '@/views/OnboardingSource.vue'
import OnboardingWelcome from '@/views/OnboardingWelcome.vue'
import { useDismissOnboarding } from '@/queries/onboarding'

type Step = 1 | 2 | 3 | 4

const { t } = useI18n()
const router = useRouter()
const route = useRoute()
const dismiss = useDismissOnboarding()

const step = ref<Step>(1)
const finishing = ref(false)

const steps = computed<{ id: Step, label: string }[]>(() => [
  { id: 1 as Step, label: t('onboarding.step_welcome') },
  { id: 2 as Step, label: t('onboarding.step_source') },
  { id: 3 as Step, label: t('onboarding.step_channel') },
  { id: 4 as Step, label: t('onboarding.step_done') },
])

const canBack = computed(() => step.value > 1)
const isFinal = computed(() => step.value === 4)

function goNext(): void {
  if (step.value < 4) step.value = (step.value + 1) as Step
}

function goBack(): void {
  if (step.value > 1) step.value = (step.value - 1) as Step
}

async function finish(): Promise<void> {
  finishing.value = true
  try {
    await dismiss.mutateAsync()
  }
  finally {
    finishing.value = false
    router.push('/')
  }
}

// Deep-link return from Settings → Integrations: server redirects back to /welcome?return=...
// after the operator saves OAuth credentials. Jump straight to the source step.
onMounted(() => {
  const fromSettings = route.query.from === 'settings' || route.query.step === '2'
  if (fromSettings) {
    step.value = 2
  }
})
</script>

<template>
  <div class="flex min-h-screen w-screen items-center justify-center bg-slate-950 px-4 py-10 text-slate-100">
    <div class="w-full max-w-2xl space-y-6">
      <header class="flex items-center justify-between">
        <div class="flex items-center gap-2 text-sm text-slate-300">
          <ShieldCheck class="h-5 w-5 text-blue-400" />
          <span class="font-medium">{{ t('onboarding.setup_label') }}</span>
        </div>
        <button
          type="button"
          class="text-xs text-slate-500 hover:text-slate-300 hover:underline"
          @click="finish"
        >
          {{ t('onboarding.skip_btn') }}
        </button>
      </header>

      <ol class="flex items-center gap-2">
        <li
          v-for="(item, index) in steps"
          :key="item.id"
          class="flex flex-1 items-center gap-2"
        >
          <span
            class="flex h-2.5 w-2.5 rounded-full transition-colors"
            :class="step >= item.id ? 'bg-blue-400' : 'bg-slate-700'"
          />
          <span
            class="text-xs"
            :class="step === item.id ? 'font-semibold text-slate-100' : 'text-slate-500'"
          >
            {{ item.label }}
          </span>
          <span
            v-if="index < steps.length - 1"
            class="flex-1 h-px"
            :class="step > item.id ? 'bg-blue-400/60' : 'bg-slate-800'"
          />
        </li>
      </ol>

      <div class="rounded-xl border border-slate-800 bg-slate-950/40 p-6 shadow-lg">
        <OnboardingWelcome v-if="step === 1" />
        <OnboardingSource v-else-if="step === 2" @done="goNext" />
        <OnboardingChannel v-else-if="step === 3" @done="goNext" />
        <OnboardingDone v-else-if="step === 4" />
      </div>

      <footer class="flex items-center justify-between">
        <button
          type="button"
          :disabled="!canBack"
          class="inline-flex items-center gap-1.5 rounded border border-slate-700 px-3 py-1.5 text-sm text-slate-200 hover:bg-slate-800 disabled:opacity-30"
          @click="goBack"
        >
          <ArrowLeft class="h-3.5 w-3.5" />
          Back
        </button>

        <button
          v-if="!isFinal"
          type="button"
          class="inline-flex items-center gap-1.5 rounded bg-blue-600 px-4 py-1.5 text-sm font-medium text-white hover:bg-blue-500"
          @click="goNext"
        >
          Next
          <ArrowRight class="h-3.5 w-3.5" />
        </button>
        <button
          v-else
          type="button"
          :disabled="finishing"
          class="inline-flex items-center gap-1.5 rounded bg-emerald-600 px-4 py-1.5 text-sm font-medium text-white hover:bg-emerald-500 disabled:opacity-50"
          @click="finish"
        >
          {{ finishing ? t('onboarding.finishing') : t('onboarding.take_to_dashboard') }}
        </button>
      </footer>
    </div>
  </div>
</template>
