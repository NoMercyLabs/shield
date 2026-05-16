<script setup lang="ts">
import { computed, ref } from 'vue'

import SeverityBadge from '@/components/SeverityBadge.vue'
import {
  useAckFindingMutation,
  useFindingQuery,
  useResolveFindingMutation,
  useSuppressFindingMutation,
} from '@/queries/findings'
import { useToasts } from '@/stores/toast'
import { formatDate, parseJsonArray, severityName } from '@/lib/format'
import { EcosystemNames, FindingStateNames } from '@/types/api'

const props = defineProps<{ id: string }>()
const id = computed(() => props.id)

const { data, isLoading, isError } = useFindingQuery(id)
const ack = useAckFindingMutation()
const suppress = useSuppressFindingMutation()
const resolve = useResolveFindingMutation()
const { push } = useToasts()

const suppressReason = ref('')

const finding = computed(() => data.value?.finding)
const advisory = computed(() => data.value?.advisory)
const item = computed(() => data.value?.item)

const references = computed(() => parseJsonArray<string>(advisory.value?.referencesJson))
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
    push('success', `Finding ${verb}ed.`)
    if (verb === 'suppress') suppressReason.value = ''
  }
  catch {
    push('error', `Failed to ${verb}.`)
  }
}
</script>

<template>
  <div class="space-y-6">
    <p v-if="isLoading" class="text-sm text-slate-400">Loading…</p>
    <p v-else-if="isError" class="text-sm text-red-300">Failed to load finding.</p>

    <template v-else-if="finding">
      <header class="flex items-start justify-between gap-4">
        <div>
          <h1 class="text-2xl font-semibold">{{ heading }}</h1>
          <p class="text-sm text-slate-400">
            <span v-if="item">{{ EcosystemNames[item.ecosystem] }}</span>
            <span v-else-if="advisory">{{ EcosystemNames[advisory.ecosystem] }}</span>
            <span v-else>Unknown ecosystem</span>
            · source #{{ finding.sourceId }}
          </p>
        </div>
        <SeverityBadge :severity="finding.severity" />
      </header>

      <section v-if="advisory" class="rounded-lg border border-slate-800 bg-slate-900 p-4">
        <h2 class="text-sm font-medium text-slate-300">Advisory</h2>
        <p class="mt-2 text-sm text-slate-200">{{ advisory.summary }}</p>
        <p class="mt-2 text-xs text-slate-500">
          {{ advisory.externalId }} · CVSS {{ advisory.cvss ?? '—' }} · {{ severityName(advisory.severity) }}
        </p>
        <ul v-if="references.length" class="mt-3 list-disc space-y-1 pl-5 text-sm">
          <li v-for="ref in references" :key="ref">
            <a :href="ref" class="text-blue-400 hover:underline" target="_blank" rel="noopener">{{ ref }}</a>
          </li>
        </ul>
      </section>
      <section v-else class="rounded-lg border border-slate-800 bg-slate-900 p-4 text-sm text-slate-500">
        Advisory record not available.
      </section>

      <section class="rounded-lg border border-slate-800 bg-slate-900 p-4">
        <h2 class="text-sm font-medium text-slate-300">Timeline</h2>
        <dl class="mt-2 grid grid-cols-2 gap-2 text-sm">
          <dt class="text-slate-500">First seen</dt>
          <dd>{{ formatDate(finding.firstSeenAt) }}</dd>
          <dt class="text-slate-500">Last seen</dt>
          <dd>{{ formatDate(finding.lastSeenAt) }}</dd>
          <dt class="text-slate-500">State</dt>
          <dd>{{ FindingStateNames[finding.state] }}</dd>
          <dt v-if="finding.notes" class="text-slate-500">Notes</dt>
          <dd v-if="finding.notes" class="whitespace-pre-wrap">{{ finding.notes }}</dd>
        </dl>
      </section>

      <section class="rounded-lg border border-slate-800 bg-slate-900 p-4">
        <h2 class="text-sm font-medium text-slate-300">Actions</h2>
        <div class="mt-3 flex flex-wrap gap-2">
          <button
            type="button"
            class="rounded bg-yellow-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-yellow-500 disabled:opacity-50"
            :disabled="ack.isPending.value"
            @click="run('ack')"
          >
            Acknowledge
          </button>
          <button
            type="button"
            class="rounded bg-green-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-green-500 disabled:opacity-50"
            :disabled="resolve.isPending.value"
            @click="run('resolve')"
          >
            Resolve
          </button>
        </div>
        <div class="mt-4 space-y-2">
          <label class="block text-sm">
            <span class="text-slate-300">Suppress reason</span>
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
            Suppress
          </button>
        </div>
      </section>
    </template>
  </div>
</template>
