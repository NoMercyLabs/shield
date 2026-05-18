<script setup lang="ts">
import { computed, ref } from 'vue'
import { Bookmark, X } from 'lucide-vue-next'
import { useI18n } from 'vue-i18n'

import {
  useCreateSavedFilterMutation,
  useDeleteSavedFilterMutation,
  useSavedFiltersQuery,
} from '@/queries/savedFilters'
import { useToasts } from '@/stores/toast'
import type { SavedFilter } from '@/types/api'

const props = defineProps<{
  /** JSON string representing the current filter selection — passed through unchanged. */
  currentQueryJson: string
  kind?: string
}>()

const emit = defineEmits<{
  apply: [filter: SavedFilter]
}>()

const { t } = useI18n()
const kind = computed<string>(() => props.kind ?? 'findings')
const { data } = useSavedFiltersQuery(kind.value)
const createFilter = useCreateSavedFilterMutation()
const deleteFilter = useDeleteSavedFilterMutation()
const toasts = useToasts()

const saveOpen = ref(false)
const newName = ref('')

const filters = computed<SavedFilter[]>(() => data.value ?? [])

async function onSave(): Promise<void> {
  const name = newName.value.trim()
  if (!name) return
  try {
    await createFilter.mutateAsync({
      name,
      kind: kind.value,
      queryJson: props.currentQueryJson,
    })
    toasts.push('success', t('saved_filters.saved_toast', { name }))
    newName.value = ''
    saveOpen.value = false
  }
  catch {
    toasts.push('error', t('saved_filters.save_error'))
  }
}

async function onDelete(filter: SavedFilter): Promise<void> {
  try {
    await deleteFilter.mutateAsync(filter.id)
  }
  catch {
    toasts.push('error', t('saved_filters.delete_error'))
  }
}

function onApply(filter: SavedFilter): void {
  emit('apply', filter)
}
</script>

<template>
  <div class="flex flex-wrap items-center gap-2 text-sm">
    <span class="flex items-center gap-1 text-xs uppercase text-slate-500">
      <Bookmark class="h-3 w-3" />
      {{ t('saved_filters.saved_label') }}
    </span>

    <span
      v-for="filter in filters"
      :key="filter.id"
      class="inline-flex items-center gap-1 rounded-full border border-slate-700 bg-slate-800 px-3 py-1 text-xs text-slate-200 hover:border-slate-500"
    >
      <button
        type="button"
        class="text-slate-100 hover:text-white"
        @click="onApply(filter)"
      >
        {{ filter.name }}
      </button>
      <button
        type="button"
        class="rounded text-slate-500 hover:text-slate-300"
        :aria-label="t('saved_filters.delete_aria', { name: filter.name })"
        @click="onDelete(filter)"
      >
        <X class="h-3 w-3" />
      </button>
    </span>

    <span v-if="filters.length === 0" class="text-xs text-slate-600">{{ t('saved_filters.no_saved') }}</span>

    <div class="relative ml-2">
      <button
        type="button"
        class="rounded border border-dashed border-slate-600 px-3 py-1 text-xs text-slate-300 hover:border-slate-500 hover:bg-slate-800"
        @click="saveOpen = !saveOpen"
      >
        {{ t('saved_filters.save_as_btn') }}
      </button>
      <div
        v-if="saveOpen"
        class="absolute z-20 mt-2 flex w-72 items-center gap-2 rounded-lg border border-slate-700 bg-slate-900 p-3 shadow-xl"
      >
        <input
          v-model="newName"
          type="text"
          :placeholder="t('saved_filters.name_placeholder')"
          class="flex-1 rounded border border-slate-700 bg-slate-800 px-2 py-1 text-xs focus:border-blue-500 focus:outline-none"
          @keydown.enter="onSave"
        >
        <button
          type="button"
          class="rounded border border-blue-700 bg-blue-950/40 px-2 py-1 text-xs text-blue-200 hover:bg-blue-900/40 disabled:opacity-40"
          :disabled="!newName.trim() || createFilter.isPending.value"
          @click="onSave"
        >
          {{ t('saved_filters.save_btn') }}
        </button>
      </div>
    </div>
  </div>
</template>
