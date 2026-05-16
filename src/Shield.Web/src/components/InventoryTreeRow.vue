<script setup lang="ts">
import { computed } from 'vue'
import { RouterLink } from 'vue-router'
import { ChevronDown, ChevronRight } from 'lucide-vue-next'

import type { TreeNode } from '@/lib/inventory-tree'

const props = defineProps<{
  node: TreeNode
  expandedIds: Set<string>
}>()

const emit = defineEmits<{ toggle: [id: string] }>()

const hasChildren = computed(() => props.node.children.length > 0)
const isExpanded = computed(() => props.expandedIds.has(props.node.id))
const indentStyle = computed(() => ({ paddingLeft: `${1 + props.node.depth * 1}rem` }))
</script>

<template>
  <li>
    <div
      class="flex cursor-default items-center gap-2 px-4 py-1 hover:bg-slate-800/30"
      :style="indentStyle"
      @click="hasChildren && emit('toggle', node.id)"
    >
      <ChevronDown v-if="hasChildren && isExpanded" class="h-3.5 w-3.5 shrink-0 text-slate-500" />
      <ChevronRight v-else-if="hasChildren" class="h-3.5 w-3.5 shrink-0 text-slate-500" />
      <span v-else class="h-3.5 w-3.5 shrink-0" />

      <span v-if="node.isSynthetic" class="font-mono text-sm italic text-slate-500">
        {{ node.name }} <span class="text-xs">(chain)</span>
      </span>
      <RouterLink
        v-else
        :to="{
          path: '/findings',
          query: {
            packageName: node.name,
            ...(node.version ? { packageVersion: node.version } : {}),
          },
        }"
        class="font-mono text-sm text-slate-100 hover:text-blue-300 hover:underline"
        @click.stop
      >
        {{ node.name }}
      </RouterLink>

      <span v-if="node.version" class="font-mono text-xs text-slate-400">@{{ node.version }}</span>
      <span
        v-if="node.isDirect"
        class="rounded bg-blue-900/40 px-1.5 py-0.5 text-[10px] uppercase text-blue-300"
      >direct</span>
    </div>

    <ul v-if="hasChildren && isExpanded">
      <InventoryTreeRow
        v-for="child in node.children"
        :key="child.id"
        :node="child"
        :expanded-ids="expandedIds"
        @toggle="(id: string) => emit('toggle', id)"
      />
    </ul>
  </li>
</template>
