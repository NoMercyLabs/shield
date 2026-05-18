import { computed, type ComputedRef, ref, type Ref, watch } from 'vue'

export type SortDirection = 'asc' | 'desc'

export interface SortableColumn<TRow> {
  key: string
  // Pulls the comparable scalar out of a row. Strings sort case-insensitively below; if
  // numeric ordering matters (counts, scores) return a number directly. Dates can be
  // returned as Date or ISO string — both sort correctly.
  extract: (row: TRow) => string | number | Date | null | undefined
  // Direction applied when the user first clicks this column. Names default 'asc' (a→z),
  // counts and dates usually 'desc' (largest / newest first). Default fallback: 'asc'.
  defaultDirection?: SortDirection
}

export interface ClientSortHandle<TRow> {
  sortedRows: ComputedRef<TRow[]>
  sortKey: Ref<string | null>
  sortDir: Ref<SortDirection>
  toggleSort: (key: string) => void
}

export interface ClientSortOptions {
  // Starting sort applied when nothing is persisted yet. Pass to make the table land in
  // a sensible order on first visit; subsequent visits restore the user's last choice.
  initial?: { key: string, direction?: SortDirection }
  // localStorage key for cross-reload persistence. Each table should pass its own — sharing
  // a key would let one table's sort state leak into another's column space (different
  // column key sets) and silently no-op via the columnsByKey lookup. Omit to opt out.
  storageKey?: string
}

interface PersistedSort {
  key: string | null
  direction: SortDirection
}

// In-memory sorting for tables that have all their rows loaded client-side. Stable: rows
// with equal sort keys keep their original relative order. Nulls sort to the end so a
// table sorted "by last delivery" doesn't bury never-delivered rows at the top.
//
// When `storageKey` is provided the chosen column + direction round-trip through
// localStorage so the user's last sort survives a reload. Validation guards against:
//   - older persisted keys that no longer exist in the current column set,
//   - direction values that aren't 'asc' / 'desc',
//   - private-mode browsers that throw on localStorage access.
export function useClientSort<TRow>(
  rows: Ref<TRow[]>,
  columns: SortableColumn<TRow>[],
  options?: ClientSortOptions,
): ClientSortHandle<TRow> {
  const columnsByKey = new Map(columns.map(col => [col.key, col]))
  const restored = loadPersistedSort(options?.storageKey, columnsByKey)
  const sortKey = ref<string | null>(restored?.key ?? options?.initial?.key ?? null)
  const sortDir = ref<SortDirection>(
    restored?.direction ?? options?.initial?.direction ?? 'asc',
  )

  if (options?.storageKey) {
    watch([sortKey, sortDir], ([key, direction]) => {
      savePersistedSort(options.storageKey!, { key, direction })
    })
  }

  const sortedRows = computed<TRow[]>(() => {
    if (!sortKey.value || !columnsByKey.has(sortKey.value))
      return rows.value
    const column = columnsByKey.get(sortKey.value)!
    const direction = sortDir.value === 'asc' ? 1 : -1
    return [...rows.value]
      .map((row, index) => ({ row, index }))
      .sort((a, b) => {
        const va = column.extract(a.row)
        const vb = column.extract(b.row)
        if (va == null && vb == null) return a.index - b.index
        if (va == null) return 1
        if (vb == null) return -1
        const cmp = compareScalar(va, vb)
        if (cmp !== 0) return cmp * direction
        // Tie-break on original index so the sort stays stable.
        return a.index - b.index
      })
      .map(entry => entry.row)
  })

  function toggleSort(key: string): void {
    if (!columnsByKey.has(key)) return
    if (sortKey.value === key) {
      sortDir.value = sortDir.value === 'asc' ? 'desc' : 'asc'
      return
    }
    sortKey.value = key
    sortDir.value = columnsByKey.get(key)!.defaultDirection ?? 'asc'
  }

  return { sortedRows, sortKey, sortDir, toggleSort }
}

function compareScalar(
  a: string | number | Date,
  b: string | number | Date,
): number {
  // Dates → epoch ms so the comparison stays numeric across DST and timezone offsets.
  if (a instanceof Date) a = a.getTime()
  if (b instanceof Date) b = b.getTime()
  if (typeof a === 'string' && typeof b === 'string')
    return a.localeCompare(b, undefined, { sensitivity: 'base' })
  if (a < b) return -1
  if (a > b) return 1
  return 0
}

function loadPersistedSort<TRow>(
  storageKey: string | undefined,
  columnsByKey: Map<string, SortableColumn<TRow>>,
): PersistedSort | null {
  if (!storageKey || typeof localStorage === 'undefined')
    return null
  try {
    const raw = localStorage.getItem(storageKey)
    if (!raw) return null
    const parsed = JSON.parse(raw) as Partial<PersistedSort>
    const key = typeof parsed.key === 'string' && columnsByKey.has(parsed.key)
      ? parsed.key
      : null
    const direction = parsed.direction === 'asc' || parsed.direction === 'desc'
      ? parsed.direction
      : 'asc'
    return { key, direction }
  }
  catch {
    return null
  }
}

function savePersistedSort(storageKey: string, value: PersistedSort): void {
  if (typeof localStorage === 'undefined') return
  try {
    localStorage.setItem(storageKey, JSON.stringify(value))
  }
  catch {
    // Private-mode / quota-exceeded — best-effort.
  }
}
