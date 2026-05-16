<script setup lang="ts">
import { computed, ref, watch } from 'vue'

const props = defineProps<{ initial?: Record<string, unknown> | null }>()
const emit = defineEmits<{ change: [json: string, valid: boolean] }>()

const url = ref<string>(typeof props.initial?.url === 'string' ? props.initial.url as string : '')
const topic = ref<string>('')
const authToken = ref<string>(typeof props.initial?.authToken === 'string' ? props.initial.authToken as string : '')
const priority = ref<number>(typeof props.initial?.priority === 'number' ? props.initial.priority as number : 3)

// Final URL strategy: if the user enters Topic separately AND the URL doesn't
// already end with it, append. Lets people paste a bare server URL + the topic
// name without thinking about slashes.
const finalUrl = computed(() => {
  const base = url.value.replace(/\/+$/, '')
  if (!topic.value)
    return url.value
  if (base.endsWith(`/${topic.value}`))
    return url.value
  return `${base}/${topic.value}`
})

const isValid = computed(() => {
  if (!finalUrl.value) return false
  try {
    const parsed = new URL(finalUrl.value)
    return parsed.protocol === 'https:' || parsed.protocol === 'http:'
  }
  catch {
    return false
  }
})

function publish(): void {
  const payload: Record<string, unknown> = {
    url: finalUrl.value,
    priority: priority.value,
  }
  if (authToken.value)
    payload.authToken = authToken.value
  emit('change', JSON.stringify(payload), isValid.value)
}

watch([url, topic, authToken, priority], publish, { immediate: true })
</script>

<template>
  <div class="space-y-3">
    <label class="block">
      <span class="text-sm text-slate-300">Server URL</span>
      <input
        v-model="url"
        type="url"
        required
        placeholder="https://ntfy.sh"
        class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
      />
    </label>
    <label class="block">
      <span class="text-sm text-slate-300">Topic (optional)</span>
      <input
        v-model="topic"
        type="text"
        placeholder="shield-alerts"
        class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
      />
      <span class="mt-1 block text-xs text-slate-500">
        Appended to the URL if not already present.
      </span>
    </label>
    <label class="block">
      <span class="text-sm text-slate-300">Auth token (optional)</span>
      <input
        v-model="authToken"
        type="password"
        autocomplete="new-password"
        data-1p-ignore="true"
        data-lpignore="true"
        placeholder="tk_…"
        class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
      />
    </label>
    <label class="block">
      <span class="text-sm text-slate-300">Priority</span>
      <select
        v-model.number="priority"
        class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
      >
        <option :value="1">1 · Min</option>
        <option :value="2">2 · Low</option>
        <option :value="3">3 · Default</option>
        <option :value="4">4 · High</option>
        <option :value="5">5 · Max</option>
      </select>
    </label>
  </div>
</template>
