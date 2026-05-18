// Server-hydrated enum catalog. /api/meta/enums returns every wire-protocol enum the SPA
// renders (Feed, SourceType, OAuthProvider, Severity, Ecosystem, …) so this file is the
// only place that owns "what's the display name for value N of enum X". Adding an enum
// value server-side propagates here on next reload — no second copy in types/api.ts.

import { ref } from 'vue'
import { api } from '@/lib/api'

type EnumDict = Record<string, number>
type Catalog = Record<string, EnumDict>

const catalog = ref<Catalog>({})
const loaded = ref(false)
let loadingPromise: Promise<void> | null = null

interface EnumCatalogResponse {
  enums: Catalog
}

export async function loadEnums(): Promise<void> {
  if (loaded.value) return
  if (loadingPromise) return loadingPromise
  loadingPromise = (async () => {
    try {
      const { data } = await api.get<EnumCatalogResponse>('/meta/enums')
      catalog.value = data.enums
      loaded.value = true
    }
    catch {
      // Network failure on bootstrap is recoverable — labels fall back to the raw numeric
      // string until the next reload or manual refresh. Keep the SPA renderable.
      loaded.value = false
    }
    finally {
      loadingPromise = null
    }
  })()
  return loadingPromise
}

// Lookup the display name for an enum value. Falls back to the raw value when the catalog
// hasn't loaded yet (or when the server doesn't know the value — most likely "client is
// newer than server", which is rare since the catalog drives BOTH sides off the server).
export function enumName(enumType: string, value: number | null | undefined): string {
  if (value == null) return ''
  const entries = catalog.value[enumType]
  if (!entries) return String(value)
  for (const [name, n] of Object.entries(entries)) {
    if (n === value) return name
  }
  return String(value)
}

// Inverse lookup — useful for code that wants to send a string label and have it resolved
// to the wire-protocol number (e.g. URL query parameters).
export function enumValue(enumType: string, name: string): number | null {
  const entries = catalog.value[enumType]
  if (!entries) return null
  return entries[name] ?? null
}

// Returns all (name, value) pairs for an enum, sorted by value. Components iterate this
// to render <option> lists / chip rows / sort indices.
export function enumEntries(enumType: string): { name: string, value: number }[] {
  const entries = catalog.value[enumType]
  if (!entries) return []
  return Object.entries(entries)
    .map(([name, value]) => ({ name, value }))
    .sort((a, b) => a.value - b.value)
}

export const enumsLoaded = loaded
