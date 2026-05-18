import { enumLabel, enumName } from '@/stores/enums'
import { Severity } from '@/types/api'

export function formatDate(iso: string | null | undefined): string {
  if (!iso) return '—'
  const parsed = new Date(iso)
  if (Number.isNaN(parsed.getTime())) return '—'
  return parsed.toLocaleString()
}

export function severityName(severity: Severity): string {
  return enumLabel('Severity', severity) || String(severity)
}

// i18n key for vue-i18n lookups (e.g. `severity.critical`). Falls back to "low" when the
// catalog hasn't hydrated, so the missing-key warning surfaces instead of crashing. Uses
// enumName (raw C# name) because the i18n keys downstream are lowercase forms of the raw.
export function severityI18nKey(severity: Severity): string {
  const name = enumName('Severity', severity)
  return name ? `severity.${name.toLowerCase()}` : 'severity.low'
}

export function severityClass(severity: Severity): string {
  switch (severity) {
    case Severity.Critical: return 'bg-red-600/20 text-red-300 border-red-700/50'
    case Severity.High: return 'bg-orange-600/20 text-orange-300 border-orange-700/50'
    case Severity.Medium: return 'bg-yellow-600/20 text-yellow-300 border-yellow-700/50'
    case Severity.Low: return 'bg-green-600/20 text-green-300 border-green-700/50'
    default: return 'bg-slate-700/20 text-slate-300 border-slate-700/50'
  }
}

export function severityRank(severity: Severity): number {
  switch (severity) {
    case Severity.Critical: return 4
    case Severity.High: return 3
    case Severity.Medium: return 2
    case Severity.Low: return 1
    default: return 0
  }
}

// Server stores some array fields as JSON-encoded strings (Advisory.ReferencesJson).
// Parse defensively so a malformed payload doesn't blow up the view.
export function parseJsonArray<T = unknown>(json: string | null | undefined): T[] {
  if (!json) return []
  try {
    const parsed = JSON.parse(json)
    return Array.isArray(parsed) ? parsed as T[] : []
  }
  catch {
    return []
  }
}
