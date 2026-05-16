<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { Minus, Plus } from 'lucide-vue-next'

const props = defineProps<{ initial?: Record<string, unknown> | null }>()
const emit = defineEmits<{ change: [json: string, valid: boolean] }>()

interface HeaderRow { key: string, value: string }

function initialHeaders(): HeaderRow[] {
  const value = props.initial?.headers
  if (!value || typeof value !== 'object')
    return [{ key: '', value: '' }]
  return Object.entries(value as Record<string, unknown>).map(([key, val]) => ({
    key,
    value: typeof val === 'string' ? val : '',
  }))
}

const url = ref<string>(typeof props.initial?.url === 'string' ? props.initial.url as string : '')
const method = ref<'POST' | 'PUT' | 'PATCH'>(
  typeof props.initial?.method === 'string'
    ? ((props.initial.method as string).toUpperCase() as 'POST' | 'PUT' | 'PATCH')
    : 'POST',
)
const bodyTemplate = ref<string>(typeof props.initial?.bodyTemplate === 'string' ? props.initial.bodyTemplate as string : '')
const headers = ref<HeaderRow[]>(initialHeaders())

function addHeader(): void {
  headers.value.push({ key: '', value: '' })
}

function removeHeader(index: number): void {
  headers.value.splice(index, 1)
  if (headers.value.length === 0)
    headers.value.push({ key: '', value: '' })
}

const isValid = computed(() => {
  if (!url.value) return false
  try {
    const parsed = new URL(url.value)
    return parsed.protocol === 'https:' || parsed.protocol === 'http:'
  }
  catch {
    return false
  }
})

function publish(): void {
  const headerMap: Record<string, string> = {}
  for (const row of headers.value) {
    const key = row.key.trim()
    if (!key) continue
    headerMap[key] = row.value
  }
  const payload: Record<string, unknown> = {
    url: url.value,
    method: method.value,
  }
  if (Object.keys(headerMap).length > 0)
    payload.headers = headerMap
  if (bodyTemplate.value)
    payload.bodyTemplate = bodyTemplate.value
  emit('change', JSON.stringify(payload), isValid.value)
}

watch([url, method, bodyTemplate, headers], publish, { deep: true, immediate: true })
</script>

<template>
  <div class="space-y-3">
    <div class="grid grid-cols-4 gap-3">
      <label class="col-span-3 block">
        <span class="text-sm text-slate-300">URL</span>
        <input
          v-model="url"
          type="url"
          required
          placeholder="https://hooks.example.com/shield"
          class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
        />
      </label>
      <label class="block">
        <span class="text-sm text-slate-300">Method</span>
        <select
          v-model="method"
          class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
        >
          <option value="POST">POST</option>
          <option value="PUT">PUT</option>
          <option value="PATCH">PATCH</option>
        </select>
      </label>
    </div>

    <div>
      <div class="flex items-center justify-between">
        <span class="text-sm text-slate-300">Headers</span>
        <button
          type="button"
          class="flex items-center gap-1 rounded border border-slate-700 px-2 py-1 text-xs text-slate-200 hover:bg-slate-800"
          @click="addHeader"
        >
          <Plus class="h-3 w-3" />
          Add
        </button>
      </div>
      <div class="mt-1 space-y-1.5">
        <div
          v-for="(row, index) in headers"
          :key="index"
          class="grid grid-cols-[1fr_2fr_auto] gap-1.5"
        >
          <input
            v-model="row.key"
            type="text"
            placeholder="Header"
            class="rounded border border-slate-700 bg-slate-800 px-2 py-1.5 text-sm focus:border-blue-500 focus:outline-none"
          />
          <input
            v-model="row.value"
            type="text"
            placeholder="Value"
            class="rounded border border-slate-700 bg-slate-800 px-2 py-1.5 text-sm focus:border-blue-500 focus:outline-none"
          />
          <button
            type="button"
            class="rounded border border-slate-700 px-2 text-slate-300 hover:bg-slate-800"
            @click="removeHeader(index)"
          >
            <Minus class="h-3 w-3" />
          </button>
        </div>
      </div>
    </div>

    <div>
      <span class="text-sm text-slate-300">Body template</span>
      <p v-pre class="mt-0.5 text-xs text-slate-500">
        Placeholders:
        <code>{{severity}}</code>,
        <code>{{package}}</code>,
        <code>{{version}}</code>,
        <code>{{advisoryId}}</code>,
        <code>{{summary}}</code>,
        <code>{{findingId}}</code>,
        <code>{{sourceName}}</code>,
        <code>{{count}}</code>.
        Leave blank to send the raw finding JSON.
      </p>
      <textarea
        v-model="bodyTemplate"
        rows="4"
        class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 font-mono text-sm focus:border-blue-500 focus:outline-none"
      />
    </div>
  </div>
</template>
