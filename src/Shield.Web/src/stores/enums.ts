// Server-hydrated enum catalog. /api/meta/enums returns every wire-protocol enum the SPA
// renders (Feed, SourceType, OAuthProvider, Severity, Ecosystem, …) so this file is the
// only place that owns "what's the display name for value N of enum X". Adding an enum
// value server-side propagates here on next reload — no second copy in types/api.ts.
//
// enumName returns the raw C# enum name ("Npm", "GithubRepo"). enumLabel goes through
// vue-i18n with key `enum.<Type>.<Name>` for polished display strings ("npm", "GitHub").
// Components should call enumLabel for anything user-facing; reserve enumName for code
// paths that need the server-side string (URL paths, API request bodies).

import { ref } from 'vue'

import { api } from '@/lib/api'
import { i18n } from '@/i18n'

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

// Polished display label for an enum value. Looks up `enum.<EnumType>.<Name>` via vue-i18n,
// falls back to the raw enum name when the key is missing. Use this for every user-facing
// label — dropdown options, chips, badges, headers. The catalog endpoint gives us the
// authoritative name; this fn turns it into proper-cased / locale-aware display text.
export function enumLabel(enumType: string, value: number | null | undefined): string {
  const name = enumName(enumType, value)
  if (!name) return ''
  const key = `enum.${enumType}.${name}`
  const t = i18n.global.t as (key: string) => string
  const translated = t(key)
  // vue-i18n returns the key itself when no message exists. Fall back to the raw enum
  // name in that case — better than rendering "enum.Foo.Bar" to the user.
  return translated === key ? name : translated
}

// enumLabel for the entries list. Returns `{name, value, label}` pre-translated so a
// component template can render `entry.label` instead of repeating the call.
export function enumLabelEntries(
  enumType: string,
): { name: string, value: number, label: string }[] {
  return enumEntries(enumType).map(entry => ({
    ...entry,
    label: enumLabel(enumType, entry.value),
  }))
}

// Translate directly from the C# enum name (string) rather than the numeric wire value.
// Use this when you already have the name and don't want a catalog round-trip.
export function enumLabelByName(enumType: string, name: string): string {
  if (!name) return ''
  const key = `enum.${enumType}.${name}`
  const t = i18n.global.t as (key: string) => string
  const translated = t(key)
  return translated === key ? name : translated
}

export const enumsLoaded = loaded
