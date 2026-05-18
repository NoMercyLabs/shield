<script setup lang="ts">
// Click-to-sort table header cell. Used in concert with useClientSort: the parent owns
// the sort state + the toggleSort handler, this component handles the button + indicator.
// Keep the cell `<th>` so screen readers still treat it as a header — the button is the
// interactive child, not a replacement.
defineProps<{
  columnKey: string
  activeKey: string | null
  activeDir: 'asc' | 'desc'
  cellClass?: string
}>()
defineEmits<{ toggle: [key: string] }>()
</script>

<template>
  <th :class="cellClass ?? 'px-4 py-2'">
    <button
      type="button"
      class="inline-flex items-center gap-1 uppercase hover:text-slate-200 focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-blue-500"
      :aria-sort="activeKey === columnKey
        ? (activeDir === 'asc' ? 'ascending' : 'descending')
        : 'none'"
      @click="$emit('toggle', columnKey)"
    >
      <slot />
      <span v-if="activeKey === columnKey" class="text-blue-300" aria-hidden="true">
        {{ activeDir === 'asc' ? '▲' : '▼' }}
      </span>
    </button>
  </th>
</template>
