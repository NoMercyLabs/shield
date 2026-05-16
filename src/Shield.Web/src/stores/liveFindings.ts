import { computed, ref } from 'vue'

import { api } from '@/lib/api'
import { getFindingsConnection } from '@/lib/signalr'
import { useToasts } from '@/stores/toast'
import { Severity, type DashboardResponse } from '@/types/api'

export interface LiveFindingEvent {
  id: string
  severity: Severity
  packageName: string | null
  packageVersion: string | null
  advisorySummary: string | null
  sourceName: string | null
  receivedAt: number
}

interface CountsPayload {
  low: number
  medium: number
  high: number
  critical: number
}

const RING_BUFFER_SIZE = 20

const lowCount = ref(0)
const mediumCount = ref(0)
const highCount = ref(0)
const criticalCount = ref(0)
const recent = ref<LiveFindingEvent[]>([])
const connected = ref(false)

let started = false

function applyCounts(payload: CountsPayload): void {
  lowCount.value = payload.low
  mediumCount.value = payload.medium
  highCount.value = payload.high
  criticalCount.value = payload.critical
}

function bumpCount(severity: Severity): void {
  switch (severity) {
    case Severity.Low:
      lowCount.value += 1
      break
    case Severity.Medium:
      mediumCount.value += 1
      break
    case Severity.High:
      highCount.value += 1
      break
    case Severity.Critical:
      criticalCount.value += 1
      break
  }
}

function ingestNew(events: LiveFindingEvent[]): void {
  const toasts = useToasts()
  for (const event of events) {
    bumpCount(event.severity)
    recent.value = [event, ...recent.value].slice(0, RING_BUFFER_SIZE)
    if (event.severity === Severity.High || event.severity === Severity.Critical) {
      const label = event.severity === Severity.Critical ? 'Critical' : 'High'
      const subject = event.packageName
        ? `${event.packageName}${event.packageVersion ? `@${event.packageVersion}` : ''}`
        : event.advisorySummary ?? 'new finding'
      toasts.push('error', `${label}: ${subject}${event.sourceName ? ` (${event.sourceName})` : ''}`)
    }
  }
}

async function bootstrapCounts(): Promise<void> {
  try {
    const { data } = await api.get<DashboardResponse>('/dashboard')
    applyCounts(data.openCounts)
  }
  catch {
    // First-paint counts stay zero on failure; the next push will correct them.
  }
}

async function ensureStarted(): Promise<void> {
  if (started) return
  started = true

  await bootstrapCounts()

  const connection = getFindingsConnection()
  connection.on('findings.new', (raw: unknown) => {
    if (!Array.isArray(raw)) return
    const now = Date.now()
    const events: LiveFindingEvent[] = raw.map((entry) => {
      const obj = entry as Record<string, unknown>
      return {
        id: String(obj.id ?? ''),
        severity: (typeof obj.severity === 'number' ? obj.severity : Severity.Low) as Severity,
        packageName: (obj.packageName as string | null | undefined) ?? null,
        packageVersion: (obj.packageVersion as string | null | undefined) ?? null,
        advisorySummary: (obj.advisorySummary as string | null | undefined) ?? null,
        sourceName: (obj.sourceName as string | null | undefined) ?? null,
        receivedAt: now,
      }
    })
    ingestNew(events)
  })
  connection.on('findings.counts', (payload: CountsPayload) => {
    applyCounts(payload)
  })

  connection.onreconnected(() => {
    connected.value = true
    // Re-bootstrap counts after a reconnect so we don't drift while disconnected.
    void bootstrapCounts()
  })
  connection.onreconnecting(() => {
    connected.value = false
  })
  connection.onclose(() => {
    connected.value = false
  })

  try {
    await connection.start()
    connected.value = true
  }
  catch {
    // withAutomaticReconnect only kicks in after a successful start, so on initial
    // failure we leave `started=true` and rely on the next page-level retry. The
    // UI keeps working off REST.
    started = false
    connected.value = false
  }
}

export function useLiveFindings() {
  void ensureStarted()
  return {
    lowCount: computed(() => lowCount.value),
    mediumCount: computed(() => mediumCount.value),
    highCount: computed(() => highCount.value),
    criticalCount: computed(() => criticalCount.value),
    urgentCount: computed(() => highCount.value + criticalCount.value),
    recent: computed(() => recent.value),
    connected: computed(() => connected.value),
  }
}
