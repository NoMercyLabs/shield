<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'

import SortableTh from '@/components/SortableTh.vue'
import { useClientSort } from '@/composables/useClientSort'
import {
  requestBan,
  updateIpNotes,
  useIpDetailQuery,
  useIpReputationsQuery,
  useSecurityEventsQuery,
  useSecurityHostsQuery,
} from '@/queries/security'
import { useLiveSecurityEvents } from '@/stores/liveSecurityEvents'
import { formatDate } from '@/lib/format'
import {
  Severity,
  SeverityNames,
  type IpReputation,
  type SecurityEvent,
  type SecurityEventFilter,
  type SecurityHost,
} from '@/types/api'

const { t } = useI18n()

type Tab = 'timeline' | 'ips' | 'hosts'

const SOURCE_FILTERS = [
  'fail2ban',
  'shield.auth',
  'shield.crawler',
  'shield.ratelimit',
  'shield.apitoken',
  'shield',
] as const

const activeTab = ref<Tab>('timeline')
const page = ref(1)
const pageSize = ref(50)
const sourceFilter = ref<string | null>(null)
const severityFilter = ref<Severity | null>(null)
const jailFilter = ref<string>('')
const ipFilter = ref<string>('')

const timelineFilter = computed<SecurityEventFilter>(() => ({
  page: page.value,
  pageSize: pageSize.value,
  minSeverity: severityFilter.value,
  source: sourceFilter.value,
  jail: jailFilter.value || null,
  ip: ipFilter.value || null,
}))

const { data: timelinePage, isLoading: timelineLoading } = useSecurityEventsQuery(timelineFilter)

const live = useLiveSecurityEvents()

// IPs tab.
const ipsPage = ref(1)
const ipBannedOnly = ref(false)
const ipSearch = ref('')
const ipsQueryParams = computed(() => ({
  page: ipsPage.value,
  pageSize: 50,
  bannedOnly: ipBannedOnly.value,
  search: ipSearch.value || null,
}))
const { data: ipsResponse, isLoading: ipsLoading } = useIpReputationsQuery(ipsQueryParams)

const selectedIp = ref<string | null>(null)
const { data: ipDetail } = useIpDetailQuery(selectedIp)

// Hosts tab.
const { data: hostsResponse, isLoading: hostsLoading } = useSecurityHostsQuery()

// Mutation state for "Request fail2ban ban" + notes.
const banJailInput = ref('')
const banReasonInput = ref('')
const banHoursInput = ref<number | null>(null)
const banRequestStatus = ref<'idle' | 'submitting' | 'submitted' | 'failed'>('idle')

const notesInput = ref('')
const notesStatus = ref<'idle' | 'saving' | 'saved' | 'failed'>('idle')

watch(ipDetail, (next) => {
  notesInput.value = next?.reputation.notes ?? ''
}, { immediate: true })

const totalTimelinePages = computed(() => {
  if (!timelinePage.value) return 1
  return Math.max(1, Math.ceil(timelinePage.value.total / pageSize.value))
})

function severityLabel(value: Severity): string {
  return SeverityNames[value] ?? 'Unknown'
}

function severityBadgeClass(value: Severity): string {
  switch (value) {
    case Severity.Critical: return 'bg-red-500/30 text-red-100 border-red-400'
    case Severity.High: return 'bg-orange-500/20 text-orange-100 border-orange-400'
    case Severity.Medium: return 'bg-yellow-500/20 text-yellow-100 border-yellow-400'
    default: return 'bg-slate-700 text-slate-200 border-slate-600'
  }
}

function selectIp(ip: string): void {
  selectedIp.value = ip
  banRequestStatus.value = 'idle'
  notesStatus.value = 'idle'
  banJailInput.value = ''
  banReasonInput.value = ''
  banHoursInput.value = null
}

async function submitBanRequest(): Promise<void> {
  if (!selectedIp.value || !banJailInput.value || !banReasonInput.value) return
  banRequestStatus.value = 'submitting'
  try {
    await requestBan(
      selectedIp.value,
      banJailInput.value,
      banReasonInput.value,
      banHoursInput.value ?? undefined,
    )
    banRequestStatus.value = 'submitted'
  }
  catch {
    banRequestStatus.value = 'failed'
  }
}

async function saveNotes(): Promise<void> {
  if (!selectedIp.value) return
  notesStatus.value = 'saving'
  try {
    await updateIpNotes(selectedIp.value, notesInput.value || null)
    notesStatus.value = 'saved'
  }
  catch {
    notesStatus.value = 'failed'
  }
}

function clearSelection(): void {
  selectedIp.value = null
}

function timelineItems(): SecurityEvent[] {
  return timelinePage.value?.items ?? []
}

function ipsItems(): IpReputation[] {
  return ipsResponse.value?.items ?? []
}

// Three independent sort handles — the three tables in this view (timeline, IPs, hosts)
// are conceptually unrelated, so each carries its own sort state.
const timelineRowsRef = computed<SecurityEvent[]>(() => timelinePage.value?.items ?? [])
const timelineSort = useClientSort<SecurityEvent>(timelineRowsRef, [
  { key: 'when', extract: row => row.at, defaultDirection: 'desc' },
  { key: 'severity', extract: row => row.severity, defaultDirection: 'desc' },
  { key: 'source', extract: row => row.source, defaultDirection: 'asc' },
  { key: 'event', extract: row => row.event, defaultDirection: 'asc' },
  { key: 'ip', extract: row => row.ipAddress, defaultDirection: 'asc' },
  { key: 'jailHost', extract: row => `${row.jail ?? ''}/${row.host ?? ''}`, defaultDirection: 'asc' },
  { key: 'user', extract: row => row.userDisplayName ?? row.userId ?? '', defaultDirection: 'asc' },
])

const ipsRowsRef = computed<IpReputation[]>(() => ipsResponse.value?.items ?? [])
const ipsSort = useClientSort<IpReputation>(ipsRowsRef, [
  { key: 'ip', extract: row => row.ipAddress, defaultDirection: 'asc' },
  { key: 'country', extract: row => row.country ?? '', defaultDirection: 'asc' },
  { key: 'events', extract: row => row.eventCount, defaultDirection: 'desc' },
  { key: 'score', extract: row => row.reputationScore, defaultDirection: 'desc' },
  { key: 'banned', extract: row => row.bannedUntil, defaultDirection: 'desc' },
  { key: 'lastJail', extract: row => row.lastJail ?? '', defaultDirection: 'asc' },
  { key: 'lastSeen', extract: row => row.lastSeenAt, defaultDirection: 'desc' },
])

const hostsRowsRef = computed<SecurityHost[]>(() => hostsResponse.value?.items ?? [])
const hostsSort = useClientSort<SecurityHost>(hostsRowsRef, [
  { key: 'host', extract: row => row.host, defaultDirection: 'asc' },
  { key: 'lastSeen', extract: row => row.lastSeenAt, defaultDirection: 'desc' },
  { key: 'events', extract: row => row.eventCount, defaultDirection: 'desc' },
])
</script>

<template>
  <div class="space-y-6">
    <div class="flex items-end justify-between">
      <div>
        <h1 class="text-2xl font-semibold">{{ t('security.title') }}</h1>
        <p class="text-sm text-slate-400">{{ t('security.subtitle') }}</p>
      </div>
      <div class="flex items-center gap-2 text-xs text-slate-500">
        <span
          class="inline-flex h-2 w-2 rounded-full"
          :class="live.connected.value ? 'bg-green-400' : 'bg-slate-600'"
        />
        {{ live.connected.value ? t('security.live_label') : t('security.offline_label') }}
        <span v-if="live.banCount.value > 0" class="ml-3 rounded-full bg-red-500/30 px-2 py-0.5 text-red-100">
          {{ t('security.ban_count', live.banCount.value) }}
        </span>
      </div>
    </div>

    <div class="flex gap-2 border-b border-slate-800">
      <button
        v-for="tab in (['timeline', 'ips', 'hosts'] as Tab[])"
        :key="tab"
        type="button"
        class="px-4 py-2 text-sm border-b-2 -mb-px transition-colors"
        :class="activeTab === tab
          ? 'border-blue-400 text-blue-100'
          : 'border-transparent text-slate-400 hover:text-slate-200'"
        @click="activeTab = tab"
      >
        {{ tab === 'timeline' ? t('security.tab_timeline') : tab === 'ips' ? t('security.tab_ips') : t('security.tab_hosts') }}
      </button>
    </div>

    <!-- Timeline -->
    <div v-if="activeTab === 'timeline'" class="space-y-4">
      <div class="space-y-3 rounded-lg border border-slate-800 bg-slate-900/40 p-4">
        <div class="flex flex-wrap gap-2">
          <button
            v-for="source in SOURCE_FILTERS"
            :key="source"
            type="button"
            class="rounded-full border px-3 py-1 text-xs"
            :class="sourceFilter === source
              ? 'border-blue-400 bg-blue-500/20 text-blue-100'
              : 'border-slate-700 text-slate-300 hover:bg-slate-800'"
            @click="sourceFilter = sourceFilter === source ? null : source; page = 1"
          >
            {{ source }}
          </button>
        </div>
        <div class="flex flex-wrap items-end gap-3">
          <label class="flex flex-col text-xs text-slate-400">
            {{ t('security.filter_min_severity') }}
            <select
              v-model.number="severityFilter"
              class="mt-1 rounded border border-slate-700 bg-slate-900 px-2 py-1 text-sm text-slate-100"
              @change="page = 1"
            >
              <option :value="null">{{ t('security.filter_severity_any') }}</option>
              <option :value="Severity.Low">Low+</option>
              <option :value="Severity.Medium">Medium+</option>
              <option :value="Severity.High">High+</option>
              <option :value="Severity.Critical">Critical only</option>
            </select>
          </label>
          <label class="flex flex-col text-xs text-slate-400">
            {{ t('security.filter_jail') }}
            <input
              v-model="jailFilter"
              type="text"
              :placeholder="t('security.filter_jail_placeholder')"
              class="mt-1 rounded border border-slate-700 bg-slate-900 px-2 py-1 text-sm text-slate-100"
              @change="page = 1"
            >
          </label>
          <label class="flex flex-col text-xs text-slate-400">
            {{ t('security.filter_remote_ip') }}
            <input
              v-model="ipFilter"
              type="text"
              :placeholder="t('security.filter_ip_placeholder')"
              class="mt-1 rounded border border-slate-700 bg-slate-900 px-2 py-1 text-sm text-slate-100"
              @change="page = 1"
            >
          </label>
        </div>
      </div>

      <p v-if="timelineLoading" class="text-sm text-slate-400">{{ t('security.loading_events') }}</p>
      <p
        v-else-if="!timelinePage || timelinePage.items.length === 0"
        class="text-sm text-slate-500"
      >
        {{ t('security.no_events') }}
      </p>

      <div v-else class="overflow-x-auto rounded-lg border border-slate-800 bg-slate-900">
        <table class="w-full min-w-[900px] text-left text-sm">
          <thead class="border-b border-slate-800 text-xs uppercase text-slate-500">
            <tr>
              <SortableTh column-key="when" :active-key="timelineSort.sortKey.value" :active-dir="timelineSort.sortDir.value" @toggle="timelineSort.toggleSort">
                {{ t('security.col_when') }}
              </SortableTh>
              <SortableTh column-key="severity" :active-key="timelineSort.sortKey.value" :active-dir="timelineSort.sortDir.value" @toggle="timelineSort.toggleSort">
                {{ t('security.col_severity') }}
              </SortableTh>
              <SortableTh column-key="source" :active-key="timelineSort.sortKey.value" :active-dir="timelineSort.sortDir.value" @toggle="timelineSort.toggleSort">
                {{ t('security.col_source') }}
              </SortableTh>
              <SortableTh column-key="event" :active-key="timelineSort.sortKey.value" :active-dir="timelineSort.sortDir.value" @toggle="timelineSort.toggleSort">
                {{ t('security.col_event') }}
              </SortableTh>
              <SortableTh column-key="ip" :active-key="timelineSort.sortKey.value" :active-dir="timelineSort.sortDir.value" @toggle="timelineSort.toggleSort">
                {{ t('security.col_ip') }}
              </SortableTh>
              <SortableTh column-key="jailHost" :active-key="timelineSort.sortKey.value" :active-dir="timelineSort.sortDir.value" @toggle="timelineSort.toggleSort">
                {{ t('security.col_jail_host') }}
              </SortableTh>
              <SortableTh column-key="user" :active-key="timelineSort.sortKey.value" :active-dir="timelineSort.sortDir.value" @toggle="timelineSort.toggleSort">
                {{ t('security.col_user') }}
              </SortableTh>
            </tr>
          </thead>
          <tbody class="divide-y divide-slate-800">
            <tr v-for="event in timelineSort.sortedRows.value" :key="event.id" class="hover:bg-slate-800/50">
              <td class="whitespace-nowrap px-4 py-2 text-slate-400">{{ formatDate(event.at) }}</td>
              <td class="px-4 py-2">
                <span
                  class="rounded border px-2 py-0.5 text-xs"
                  :class="severityBadgeClass(event.severity)"
                >
                  {{ severityLabel(event.severity) }}
                </span>
              </td>
              <td class="px-4 py-2 text-slate-300">{{ event.source }}</td>
              <td class="px-4 py-2 font-mono text-xs text-blue-200">{{ event.eventType }}</td>
              <td class="px-4 py-2 text-slate-300">
                <button
                  v-if="event.remoteIp"
                  type="button"
                  class="text-blue-300 hover:underline"
                  @click="activeTab = 'ips'; selectIp(event.remoteIp)"
                >
                  {{ event.remoteIp }}
                </button>
                <span v-else class="text-slate-600">—</span>
              </td>
              <td class="px-4 py-2 text-xs text-slate-400">
                <span v-if="event.jail">{{ event.jail }}</span>
                <span v-if="event.host" class="ml-1 text-slate-500">@{{ event.host }}</span>
                <span v-if="!event.jail && !event.host" class="text-slate-600">—</span>
              </td>
              <td class="px-4 py-2 text-slate-300">{{ event.userName ?? '—' }}</td>
            </tr>
          </tbody>
        </table>
      </div>

      <div v-if="timelinePage && timelinePage.total > pageSize" class="flex items-center justify-between text-sm">
        <span class="text-slate-500">
          {{ t('security.page_of_total', { page, total: totalTimelinePages, n: timelinePage.total }) }}
        </span>
        <div class="flex gap-2">
          <button
            type="button"
            class="rounded border border-slate-700 px-3 py-1 text-slate-200 hover:bg-slate-800 disabled:opacity-40"
            :disabled="page <= 1"
            @click="page = Math.max(1, page - 1)"
          >
            {{ t('security.prev') }}
          </button>
          <button
            type="button"
            class="rounded border border-slate-700 px-3 py-1 text-slate-200 hover:bg-slate-800 disabled:opacity-40"
            :disabled="page >= totalTimelinePages"
            @click="page = Math.min(totalTimelinePages, page + 1)"
          >
            {{ t('security.next') }}
          </button>
        </div>
      </div>
    </div>

    <!-- IP reputation -->
    <div v-if="activeTab === 'ips'" class="space-y-4">
      <div class="flex flex-wrap items-end gap-3 rounded-lg border border-slate-800 bg-slate-900/40 p-4">
        <label class="flex items-center gap-2 text-sm text-slate-300">
          <input v-model="ipBannedOnly" type="checkbox" class="accent-blue-500">
          {{ t('security.banned_only') }}
        </label>
        <label class="flex flex-col text-xs text-slate-400">
          {{ t('security.ip_search') }}
          <input
            v-model="ipSearch"
            type="text"
            placeholder="IP substring"
            class="mt-1 rounded border border-slate-700 bg-slate-900 px-2 py-1 text-sm text-slate-100"
          >
        </label>
      </div>

      <p v-if="ipsLoading" class="text-sm text-slate-400">{{ t('security.loading_ips') }}</p>
      <p
        v-else-if="!ipsResponse || ipsResponse.items.length === 0"
        class="text-sm text-slate-500"
      >
        {{ t('security.no_ips') }}
      </p>

      <div v-else class="overflow-x-auto rounded-lg border border-slate-800 bg-slate-900">
        <table class="w-full min-w-[700px] text-left text-sm">
          <thead class="border-b border-slate-800 text-xs uppercase text-slate-500">
            <tr>
              <SortableTh column-key="ip" :active-key="ipsSort.sortKey.value" :active-dir="ipsSort.sortDir.value" @toggle="ipsSort.toggleSort">
                {{ t('security.col_ip') }}
              </SortableTh>
              <SortableTh column-key="country" :active-key="ipsSort.sortKey.value" :active-dir="ipsSort.sortDir.value" @toggle="ipsSort.toggleSort">
                {{ t('security.col_country') }}
              </SortableTh>
              <SortableTh column-key="events" :active-key="ipsSort.sortKey.value" :active-dir="ipsSort.sortDir.value" @toggle="ipsSort.toggleSort">
                {{ t('security.col_events') }}
              </SortableTh>
              <SortableTh column-key="score" :active-key="ipsSort.sortKey.value" :active-dir="ipsSort.sortDir.value" @toggle="ipsSort.toggleSort">
                {{ t('security.col_score') }}
              </SortableTh>
              <SortableTh column-key="banned" :active-key="ipsSort.sortKey.value" :active-dir="ipsSort.sortDir.value" @toggle="ipsSort.toggleSort">
                {{ t('security.col_banned') }}
              </SortableTh>
              <SortableTh column-key="lastJail" :active-key="ipsSort.sortKey.value" :active-dir="ipsSort.sortDir.value" @toggle="ipsSort.toggleSort">
                {{ t('security.col_last_jail') }}
              </SortableTh>
              <SortableTh column-key="lastSeen" :active-key="ipsSort.sortKey.value" :active-dir="ipsSort.sortDir.value" @toggle="ipsSort.toggleSort">
                {{ t('security.col_last_seen') }}
              </SortableTh>
            </tr>
          </thead>
          <tbody class="divide-y divide-slate-800">
            <tr
              v-for="row in ipsSort.sortedRows.value"
              :key="row.id"
              class="hover:bg-slate-800/50 cursor-pointer"
              @click="selectIp(row.ip)"
            >
              <td class="px-4 py-2 font-mono text-blue-200">{{ row.ip }}</td>
              <td class="px-4 py-2 text-slate-400">{{ row.country ?? '—' }}</td>
              <td class="px-4 py-2 text-slate-300">{{ row.eventCount }}</td>
              <td class="px-4 py-2 text-slate-300">{{ row.score }}</td>
              <td class="px-4 py-2">
                <span
                  v-if="row.currentlyBanned"
                  class="rounded-full bg-red-500/30 px-2 py-0.5 text-xs text-red-100"
                >
                  {{ t('security.banned_label') }}
                </span>
                <span v-else class="text-xs text-slate-500">{{ t('security.clean_label') }}</span>
              </td>
              <td class="px-4 py-2 text-slate-400">{{ row.lastJail ?? '—' }}</td>
              <td class="px-4 py-2 text-slate-400">{{ formatDate(row.lastSeenAt) }}</td>
            </tr>
          </tbody>
        </table>
      </div>

      <!-- Detail drawer -->
      <div
        v-if="selectedIp && ipDetail"
        class="rounded-lg border border-blue-500/30 bg-slate-900 p-4 space-y-4"
      >
        <div class="flex items-start justify-between">
          <div>
            <h2 class="text-lg font-semibold text-slate-100">{{ ipDetail.reputation.ip }}</h2>
            <p class="text-xs text-slate-500">
              {{ t('security.ip_first_seen', { when: formatDate(ipDetail.reputation.firstSeenAt), n: ipDetail.reputation.eventCount, score: ipDetail.reputation.score }) }}
            </p>
          </div>
          <button
            type="button"
            class="text-sm text-slate-400 hover:text-slate-200"
            @click="clearSelection"
          >
            {{ t('security.close_btn') }}
          </button>
        </div>

        <div class="grid gap-4 md:grid-cols-2">
          <div class="space-y-2">
            <h3 class="text-xs uppercase tracking-wide text-slate-500">{{ t('security.notes_section') }}</h3>
            <textarea
              v-model="notesInput"
              rows="3"
              class="w-full rounded border border-slate-700 bg-slate-950 px-2 py-1 text-sm text-slate-100"
              :placeholder="t('security.notes_placeholder')"
            />
            <button
              type="button"
              class="rounded border border-slate-700 px-3 py-1 text-xs text-slate-200 hover:bg-slate-800"
              :disabled="notesStatus === 'saving'"
              @click="saveNotes"
            >
              {{ notesStatus === 'saving' ? t('security.saving_notes') : notesStatus === 'saved' ? t('security.saved_notes') : t('security.save_notes_btn') }}
            </button>
          </div>

          <div class="space-y-2">
            <h3 class="text-xs uppercase tracking-wide text-slate-500">{{ t('security.ban_section') }}</h3>
            <input
              v-model="banJailInput"
              type="text"
              :placeholder="t('security.jail_placeholder')"
              class="w-full rounded border border-slate-700 bg-slate-950 px-2 py-1 text-sm text-slate-100"
            >
            <input
              v-model="banReasonInput"
              type="text"
              :placeholder="t('security.reason_placeholder')"
              class="w-full rounded border border-slate-700 bg-slate-950 px-2 py-1 text-sm text-slate-100"
            >
            <input
              v-model.number="banHoursInput"
              type="number"
              min="1"
              :placeholder="t('security.hours_placeholder')"
              class="w-full rounded border border-slate-700 bg-slate-950 px-2 py-1 text-sm text-slate-100"
            >
            <button
              type="button"
              class="rounded border border-red-500/40 bg-red-500/10 px-3 py-1 text-xs text-red-100 hover:bg-red-500/20 disabled:opacity-40"
              :disabled="!banJailInput || !banReasonInput || banRequestStatus === 'submitting'"
              @click="submitBanRequest"
            >
              {{ banRequestStatus === 'submitting' ? t('security.submitting_ban')
                : banRequestStatus === 'submitted' ? t('security.awaiting_ban')
                  : banRequestStatus === 'failed' ? t('security.ban_failed')
                    : t('security.request_ban_btn') }}
            </button>
          </div>
        </div>

        <div>
          <h3 class="mb-2 text-xs uppercase tracking-wide text-slate-500">{{ t('security.recent_events_title', { n: ipDetail.recentEvents.length }) }}</h3>
          <div class="max-h-80 space-y-1 overflow-y-auto rounded border border-slate-800 bg-slate-950 p-2">
            <div
              v-for="event in ipDetail.recentEvents"
              :key="event.id"
              class="flex items-center gap-3 px-2 py-1 text-xs"
            >
              <span class="w-40 shrink-0 text-slate-500">{{ formatDate(event.at) }}</span>
              <span
                class="rounded border px-1 py-0.5"
                :class="severityBadgeClass(event.severity)"
              >
                {{ severityLabel(event.severity) }}
              </span>
              <span class="font-mono text-blue-200">{{ event.eventType }}</span>
              <span class="text-slate-500">{{ event.jail ?? event.host ?? event.source }}</span>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- Hosts -->
    <div v-if="activeTab === 'hosts'" class="space-y-4">
      <p v-if="hostsLoading" class="text-sm text-slate-400">{{ t('security.loading_hosts') }}</p>
      <p
        v-else-if="!hostsResponse || hostsResponse.items.length === 0"
        class="text-sm text-slate-500"
      >
        {{ t('security.no_hosts') }}
      </p>

      <div v-else class="overflow-hidden rounded-lg border border-slate-800 bg-slate-900">
        <table class="w-full text-left text-sm">
          <thead class="border-b border-slate-800 text-xs uppercase text-slate-500">
            <tr>
              <SortableTh column-key="host" :active-key="hostsSort.sortKey.value" :active-dir="hostsSort.sortDir.value" @toggle="hostsSort.toggleSort">
                {{ t('security.tab_hosts') }}
              </SortableTh>
              <SortableTh column-key="lastSeen" :active-key="hostsSort.sortKey.value" :active-dir="hostsSort.sortDir.value" @toggle="hostsSort.toggleSort">
                {{ t('security.col_last_seen') }}
              </SortableTh>
              <SortableTh column-key="events" :active-key="hostsSort.sortKey.value" :active-dir="hostsSort.sortDir.value" @toggle="hostsSort.toggleSort">
                {{ t('security.col_events') }}
              </SortableTh>
            </tr>
          </thead>
          <tbody class="divide-y divide-slate-800">
            <tr v-for="host in hostsSort.sortedRows.value" :key="host.host" class="hover:bg-slate-800/50">
              <td class="px-4 py-2 text-slate-200">{{ host.host }}</td>
              <td class="px-4 py-2 text-slate-400">{{ formatDate(host.lastSeenAt) }}</td>
              <td class="px-4 py-2 text-slate-300">{{ host.eventCount }}</td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>
  </div>
</template>
