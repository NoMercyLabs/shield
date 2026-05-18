<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { ChevronDown, ChevronRight } from 'lucide-vue-next'

import InventoryTreeRow from '@/components/InventoryTreeRow.vue'
import { buildInventoryTree } from '@/lib/inventory-tree'
import type { EcosystemGroup } from '@/lib/inventory-tree'
import { enumName } from '@/stores/enums'
import type { Ecosystem, InventoryItemResponse } from '@/types/api'

const props = defineProps<{ items: InventoryItemResponse[] }>()

const COLLAPSED_KEY = 'shield.inventory-tree.collapsed-ecosystems'
const AUTO_COLLAPSE_THRESHOLD = 100

const groups = computed<EcosystemGroup[]>(() => buildInventoryTree(props.items))

function loadCollapsedEcosystems(): Set<Ecosystem> {
  try {
    const raw = localStorage.getItem(COLLAPSED_KEY)
    if (!raw) return new Set()
    const parsed = JSON.parse(raw) as unknown
    if (!Array.isArray(parsed)) return new Set()
    return new Set(parsed.filter((value): value is number => typeof value === 'number') as Ecosystem[])
  }
  catch {
    return new Set()
  }
}

const collapsedEcosystems = ref<Set<Ecosystem>>(loadCollapsedEcosystems())
const initialised = ref(false)

// First populated render auto-collapses noisy ecosystems unless the user has a stored preference.
watch(groups, (nextGroups) => {
  if (initialised.value || nextGroups.length === 0) return
  const stored = localStorage.getItem(COLLAPSED_KEY)
  if (stored === null) {
    const collapsed = new Set<Ecosystem>()
    for (const group of nextGroups) {
      if (group.totalCount > AUTO_COLLAPSE_THRESHOLD) collapsed.add(group.ecosystem)
    }
    collapsedEcosystems.value = collapsed
  }
  initialised.value = true
}, { immediate: true })

function persistCollapsed(): void {
  localStorage.setItem(COLLAPSED_KEY, JSON.stringify([...collapsedEcosystems.value]))
}

function toggleEcosystem(ecosystem: Ecosystem): void {
  const next = new Set(collapsedEcosystems.value)
  if (next.has(ecosystem)) next.delete(ecosystem)
  else next.add(ecosystem)
  collapsedEcosystems.value = next
  persistCollapsed()
}

const expandedIds = ref<Set<string>>(new Set())

function toggleNode(id: string): void {
  const next = new Set(expandedIds.value)
  if (next.has(id)) next.delete(id)
  else next.add(id)
  expandedIds.value = next
}
</script>

<template>
  <div class="divide-y divide-slate-800">
    <section v-for="group in groups" :key="group.ecosystem">
      <button
        type="button"
        class="flex w-full items-center justify-between px-4 py-3 text-left hover:bg-slate-800/40"
        @click="toggleEcosystem(group.ecosystem)"
      >
        <span class="flex items-center gap-2 text-sm font-medium text-slate-200">
          <ChevronRight v-if="collapsedEcosystems.has(group.ecosystem)" class="h-4 w-4 text-slate-500" />
          <ChevronDown v-else class="h-4 w-4 text-slate-500" />
          {{ enumName('Ecosystem', group.ecosystem) }}
          <span class="rounded bg-slate-800 px-1.5 py-0.5 font-mono text-xs text-slate-400">
            {{ group.totalCount }}
          </span>
        </span>
      </button>

      <ul v-if="!collapsedEcosystems.has(group.ecosystem)" class="pb-2">
        <InventoryTreeRow
          v-for="node in group.directRoots"
          :key="node.id"
          :node="node"
          :expanded-ids="expandedIds"
          @toggle="toggleNode"
        />
        <li v-if="group.orphanRoots.length" class="mt-2 px-4 py-1 text-xs uppercase text-slate-500">
          (unattributed)
        </li>
        <InventoryTreeRow
          v-for="node in group.orphanRoots"
          :key="node.id"
          :node="node"
          :expanded-ids="expandedIds"
          @toggle="toggleNode"
        />
      </ul>
    </section>
  </div>
</template>
