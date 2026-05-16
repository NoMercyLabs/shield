import type { Severity } from '@/types/api'

export function formatDate(iso: string | null | undefined): string {
  if (!iso) return '—'
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return '—'
  return d.toLocaleString()
}

export function severityClass(severity: Severity): string {
  switch (severity) {
    case 'Critical': return 'bg-red-600/20 text-red-300 border-red-700/50'
    case 'High': return 'bg-orange-600/20 text-orange-300 border-orange-700/50'
    case 'Medium': return 'bg-yellow-600/20 text-yellow-300 border-yellow-700/50'
    case 'Low': return 'bg-green-600/20 text-green-300 border-green-700/50'
  }
}

export function severityRank(severity: Severity): number {
  switch (severity) {
    case 'Critical': return 4
    case 'High': return 3
    case 'Medium': return 2
    case 'Low': return 1
  }
}
