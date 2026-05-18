<script setup lang="ts">
import { computed, ref } from 'vue'
import { RouterLink } from 'vue-router'
import { useI18n } from 'vue-i18n'

import EpssBadge from '@/components/EpssBadge.vue'
import KevBadge from '@/components/KevBadge.vue'
import SeverityBadge from '@/components/SeverityBadge.vue'
import {
  useAckFindingMutation,
  useApplyFixMutation,
  useFindingQuery,
  useResolveFindingMutation,
  useSuppressFindingMutation,
} from '@/queries/findings'
import { useToasts } from '@/stores/toast'
import { formatDate, parseJsonArray, severityName } from '@/lib/format'
import { EcosystemNames, FindingStateNames } from '@/types/api'
import type { ApplyFixStrategy } from '@/types/api'

const props = defineProps<{ id: string }>()
const id = computed(() => props.id)

const { data, isLoading, isError } = useFindingQuery(id)
const ack = useAckFindingMutation()
const suppress = useSuppressFindingMutation()
const resolve = useResolveFindingMutation()
const applyFix = useApplyFixMutation()
const { push } = useToasts()

const { t } = useI18n()
const suppressReason = ref('')

const finding = computed(() => data.value?.finding)
const advisory = computed(() => data.value?.advisory)
const item = computed(() => data.value?.item)
const fixSuggestion = computed(() => data.value?.fixSuggestion ?? null)
// Server gates triage on the per-source ACL. UI mirrors that gate to avoid the bait-and-switch
// where a Viewer clicks Resolve and only learns about the 403 via a red toast. Treat unknown
// (undefined on older responses) as allowed so we don't accidentally hide buttons from admins.
const canTriage = computed(() => data.value?.canTriage !== false)
const fixStrategy = computed<ApplyFixStrategy | null>(() => {
  const suggestion = fixSuggestion.value
  if (!suggestion) return null
  if (suggestion.prEligibility.eligible) return 'pr'
  if (suggestion.autoEligibility.eligible) return 'auto'
  return null
})

const fixIneligibleReason = computed<string | null>(() => {
  const suggestion = fixSuggestion.value
  if (!suggestion) return null
  if (fixStrategy.value !== null) return null
  return suggestion.prEligibility.reason ?? suggestion.autoEligibility.reason ?? null
})

// OSV serialises references as `[{type, url}]`; legacy rows sometimes shipped raw strings.
// Normalise both shapes into `{type, url}` so the template stays clean.
interface NormalisedReference { type: string, url: string }
const references = computed<NormalisedReference[]>(() => {
  const raw = parseJsonArray<unknown>(advisory.value?.referencesJson)
  return raw
    .map((entry): NormalisedReference | null => {
      if (typeof entry === 'string')
        return { type: 'WEB', url: entry }
      if (entry && typeof entry === 'object') {
        const obj = entry as Record<string, unknown>
        const url = typeof obj.url === 'string' ? obj.url : null
        const type = typeof obj.type === 'string' ? obj.type : 'WEB'
        if (url) return { type: type.toUpperCase(), url }
      }
      return null
    })
    .filter((entry): entry is NormalisedReference => entry !== null)
})

function refLabel(url: string): string {
  try {
    const u = new URL(url)
    const path = u.pathname.replace(/^\/+|\/+$/g, '')
    return `${u.hostname}${path ? ` / ${path}` : ''}`
  }
  catch {
    return url
  }
}

function refBadgeClass(type: string): string {
  switch (type) {
    case 'ADVISORY': return 'bg-red-500/15 text-red-300'
    case 'FIX': return 'bg-emerald-500/15 text-emerald-300'
    case 'PACKAGE': return 'bg-blue-500/15 text-blue-300'
    case 'REPORT': return 'bg-amber-500/15 text-amber-300'
    case 'EVIDENCE': return 'bg-purple-500/15 text-purple-300'
    default: return 'bg-slate-700/40 text-slate-300'
  }
}
const heading = computed(() => {
  if (item.value) return `${item.value.name}@${item.value.version}`
  if (advisory.value) return advisory.value.packageName
  return finding.value?.dedupKey ?? '—'
})

async function run(verb: 'ack' | 'suppress' | 'resolve'): Promise<void> {
  try {
    const mutation = verb === 'ack' ? ack : verb === 'suppress' ? suppress : resolve
    const reason = verb === 'suppress' ? suppressReason.value : undefined
    await mutation.mutateAsync({ id: id.value, reason })
    push('success', t('finding_detail.triage_toast', { verb }))
    if (verb === 'suppress') suppressReason.value = ''
  }
  catch {
    push('error', t('finding_detail.triage_error', { verb }))
  }
}

async function applyBestEffortFix(): Promise<void> {
  const strategy = fixStrategy.value
  if (!strategy) {
    push('error', t('finding_detail.fix_source_unsupported'))
    return
  }
  try {
    const result = await applyFix.mutateAsync({ id: id.value, strategy })
    if (result.pullRequestUrl) {
      push(
        'success',
        t('finding_detail.fix_pr_opened'),
        { href: result.pullRequestUrl, label: t('finding_detail.fix_open_pr') },
      )
    }
    else {
      const files = result.changedFiles.length
      const followUp = result.followUpCommand ? t('finding_detail.fix_follow_up', { cmd: result.followUpCommand }) : ''
      push('success', t('finding_detail.fix_files_changed', files) + followUp)
    }
  }
  catch (error) {
    const message =
      (error as { response?: { data?: { reason?: string } } })?.response?.data?.reason
      ?? t('state.error')
    push('error', message)
  }
}
</script>

<template>
  <div class="space-y-6">
    <p v-if="isLoading" class="text-sm text-slate-400">{{ t('finding_detail.loading') }}</p>
    <p v-else-if="isError" class="text-sm text-red-300">{{ t('finding_detail.error') }}</p>

    <template v-else-if="finding">
      <header class="flex items-start justify-between gap-4">
        <div class="min-w-0">
          <h1 class="break-all text-2xl font-semibold">{{ heading }}</h1>
          <p class="text-sm text-slate-400">
            <span v-if="item">{{ EcosystemNames[item.ecosystem] }}</span>
            <span v-else-if="advisory">{{ EcosystemNames[advisory.ecosystem] }}</span>
            <span v-else>{{ t('finding_detail.unknown_ecosystem') }}</span>
            ·
            <RouterLink
              :to="`/sources/${finding.sourceId}`"
              class="text-blue-300 hover:underline"
            >
              {{ finding.sourceName ?? `Source #${finding.sourceId}` }}
            </RouterLink>
          </p>
          <div
            v-if="finding.isKev || finding.epssScore != null"
            class="mt-2 flex flex-wrap items-center gap-1.5"
          >
            <KevBadge :is-kev="finding.isKev" :due-date="finding.kevDueDate" />
            <EpssBadge :score="finding.epssScore" :percentile="finding.epssPercentile" />
            <div
              v-if="finding.epssPercentile != null"
              class="ml-1 flex h-1.5 w-32 items-center overflow-hidden rounded-full bg-slate-800"
              :title="`EPSS percentile: ${Math.round(finding.epssPercentile * 100)}th`"
            >
              <div
                class="h-full rounded-full bg-orange-500"
                :style="{ width: `${Math.round(finding.epssPercentile * 100)}%` }"
              />
            </div>
          </div>
        </div>
        <SeverityBadge :severity="finding.severity" />
      </header>

      <section v-if="advisory" class="rounded-lg border border-slate-800 bg-slate-900 p-4">
        <h2 class="text-sm font-medium text-slate-300">{{ t('finding_detail.advisory_section') }}</h2>
        <p class="mt-2 text-sm text-slate-200">{{ advisory.summary }}</p>
        <p class="mt-2 text-xs text-slate-500">
          {{ advisory.externalId }} · CVSS {{ advisory.cvss ?? '—' }} · {{ severityName(advisory.severity) }}
        </p>
        <ul v-if="references.length" class="mt-3 space-y-1.5 text-sm">
          <li v-for="ref in references" :key="ref.url" class="flex items-start gap-2">
            <span :class="['inline-flex items-center rounded px-1.5 py-0.5 text-[10px] font-medium uppercase tracking-wide', refBadgeClass(ref.type)]">
              {{ ref.type }}
            </span>
            <a :href="ref.url" target="_blank" rel="noopener" class="min-w-0 truncate text-blue-300 hover:text-blue-200 hover:underline">
              {{ refLabel(ref.url) }}
            </a>
          </li>
        </ul>
      </section>
      <section v-else class="rounded-lg border border-slate-800 bg-slate-900 p-4 text-sm text-slate-500">
        {{ t('finding_detail.advisory_not_available') }}
      </section>

      <section class="rounded-lg border border-slate-800 bg-slate-900 p-4">
        <h2 class="text-sm font-medium text-slate-300">{{ t('finding_detail.timeline_section') }}</h2>
        <dl class="mt-2 grid grid-cols-2 gap-2 text-sm">
          <dt class="text-slate-500">{{ t('finding_detail.field_first_seen') }}</dt>
          <dd>{{ formatDate(finding.firstSeenAt) }}</dd>
          <dt class="text-slate-500">{{ t('finding_detail.field_last_seen') }}</dt>
          <dd>{{ formatDate(finding.lastSeenAt) }}</dd>
          <dt class="text-slate-500">{{ t('finding_detail.field_state') }}</dt>
          <dd>{{ FindingStateNames[finding.state] }}</dd>
          <dt v-if="finding.notes" class="text-slate-500">{{ t('finding_detail.field_notes') }}</dt>
          <dd v-if="finding.notes" class="whitespace-pre-wrap">{{ finding.notes }}</dd>
        </dl>
      </section>

      <section
        v-if="fixSuggestion"
        class="rounded-lg border border-emerald-700/60 bg-emerald-950/40 p-4"
      >
        <h2 class="text-sm font-medium text-emerald-200">{{ t('finding_detail.fix_section') }}</h2>
        <p class="mt-1 text-sm text-emerald-100">
          bump {{ fixSuggestion.packageName }}
          <span class="font-mono">{{ fixSuggestion.currentVersion }}</span>
          →
          <span class="font-mono">{{ fixSuggestion.suggestedVersion }}</span>
        </p>
        <p v-if="fixSuggestion.notes" class="mt-1 text-xs text-emerald-300/80">
          Notes: {{ fixSuggestion.notes }}
        </p>
        <p v-if="fixIneligibleReason" class="mt-2 text-xs text-emerald-300/80">
          {{ fixIneligibleReason }}
        </p>
        <div v-if="fixStrategy" class="mt-3">
          <button
            type="button"
            class="rounded bg-emerald-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-emerald-500 disabled:opacity-50"
            :disabled="applyFix.isPending.value"
            @click="applyBestEffortFix()"
          >
            {{ fixStrategy === 'pr' ? t('finding_detail.fix_open_pr') : t('finding_detail.fix_apply') }}
          </button>
        </div>
      </section>
      <section
        v-else-if="advisory"
        class="rounded-lg border border-slate-800 bg-slate-900 p-4 text-sm text-slate-500"
      >
        {{ t('finding_detail.fix_not_available') }}
      </section>

      <section v-if="canTriage" class="rounded-lg border border-slate-800 bg-slate-900 p-4">
        <h2 class="text-sm font-medium text-slate-300">{{ t('finding_detail.actions_section') }}</h2>
        <div class="mt-3 flex flex-wrap gap-2">
          <button
            type="button"
            class="rounded bg-yellow-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-yellow-500 disabled:opacity-50"
            :disabled="ack.isPending.value"
            @click="run('ack')"
          >
            {{ t('finding_detail.ack_btn') }}
          </button>
          <button
            type="button"
            class="rounded bg-green-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-green-500 disabled:opacity-50"
            :disabled="resolve.isPending.value"
            @click="run('resolve')"
          >
            {{ t('finding_detail.resolve_btn') }}
          </button>
        </div>
        <div class="mt-4 space-y-2">
          <label class="block text-sm">
            <span class="text-slate-300">{{ t('finding_detail.suppress_reason_label') }}</span>
            <input
              v-model="suppressReason"
              type="text"
              class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
            />
          </label>
          <button
            type="button"
            class="rounded bg-slate-700 px-3 py-1.5 text-sm font-medium text-white hover:bg-slate-600 disabled:opacity-50"
            :disabled="suppress.isPending.value || !suppressReason"
            @click="run('suppress')"
          >
            {{ t('finding_detail.suppress_btn') }}
          </button>
        </div>
      </section>
      <section v-else class="rounded-lg border border-slate-800 bg-slate-900 p-4 text-sm text-slate-500">
        {{ t('finding_detail.readonly_notice') }}
      </section>
    </template>
  </div>
</template>
