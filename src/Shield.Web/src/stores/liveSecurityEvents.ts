import { computed, ref } from 'vue'

import { getFindingsConnection } from '@/lib/signalr'
import type { SecurityEvent } from '@/types/api'

// Parallels useLiveFindings — listens for the `security.event` SignalR frame the server
// emits from ISecurityEventLogger.LogAsync. Ring buffer of recent events powers the
// Timeline view's auto-scroll without re-querying the REST endpoint on every push.

const RING_BUFFER_SIZE = 50

const recent = ref<SecurityEvent[]>([])
const banCount = ref(0)
const connected = ref(false)

let started = false

function ingest(event: SecurityEvent): void {
  recent.value = [event, ...recent.value].slice(0, RING_BUFFER_SIZE)
  if (event.eventType === 'fail2ban.ban')
    banCount.value += 1
}

async function ensureStarted(): Promise<void> {
  if (started) return
  started = true

  const connection = getFindingsConnection()
  connection.on('security.event', (payload: unknown) => {
    if (!payload || typeof payload !== 'object') return
    const event = payload as SecurityEvent
    if (!event.id || !event.source || !event.eventType) return
    ingest(event)
  })

  connection.onreconnected(() => { connected.value = true })
  connection.onreconnecting(() => { connected.value = false })
  connection.onclose(() => { connected.value = false })

  try {
    if (connection.state === 'Disconnected')
      await connection.start()
    connected.value = true
  }
  catch {
    started = false
    connected.value = false
  }
}

export function useLiveSecurityEvents() {
  void ensureStarted()
  return {
    recent: computed(() => recent.value),
    banCount: computed(() => banCount.value),
    connected: computed(() => connected.value),
  }
}
