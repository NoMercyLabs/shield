<script setup lang="ts">
import { computed, ref, watch } from 'vue'

const props = defineProps<{ initial?: Record<string, unknown> | null }>()
const emit = defineEmits<{ change: [json: string, valid: boolean] }>()

function initialToString(key: string, fallback = ''): string {
  const value = props.initial?.[key]
  return typeof value === 'string' ? value : fallback
}

function initialToArrayString(key: string): string {
  const value = props.initial?.[key]
  if (Array.isArray(value))
    return (value as unknown[]).filter(item => typeof item === 'string').join('\n')
  return ''
}

const host = ref<string>(initialToString('host'))
const port = ref<number>(typeof props.initial?.port === 'number' ? props.initial.port as number : 587)
const username = ref<string>(initialToString('username'))
const password = ref<string>(initialToString('password'))
const from = ref<string>(initialToString('from'))
const toRaw = ref<string>(initialToArrayString('to'))
const useStartTls = ref<boolean>(
  typeof props.initial?.useStartTls === 'boolean' ? props.initial.useStartTls as boolean : true,
)

const toList = computed(() =>
  toRaw.value
    .split(/[\n,]/)
    .map(addr => addr.trim())
    .filter(addr => addr.length > 0),
)

const isValid = computed(() =>
  !!host.value
  && port.value > 0
  && port.value <= 65535
  && !!from.value
  && toList.value.length > 0,
)

function publish(): void {
  const payload: Record<string, unknown> = {
    host: host.value,
    port: port.value,
    useStartTls: useStartTls.value,
    from: from.value,
    to: toList.value,
  }
  if (username.value)
    payload.username = username.value
  if (password.value)
    payload.password = password.value
  emit('change', JSON.stringify(payload), isValid.value)
}

watch([host, port, username, password, from, toRaw, useStartTls], publish, { immediate: true })
</script>

<template>
  <div class="space-y-3">
    <div class="grid grid-cols-3 gap-3">
      <label class="col-span-2 block">
        <span class="text-sm text-slate-300">Host</span>
        <input
          v-model="host"
          type="text"
          required
          placeholder="smtp.example.com"
          class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
        />
      </label>
      <label class="block">
        <span class="text-sm text-slate-300">Port</span>
        <input
          v-model.number="port"
          type="number"
          min="1"
          max="65535"
          required
          class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
        />
      </label>
    </div>
    <div class="grid grid-cols-2 gap-3">
      <label class="block">
        <span class="text-sm text-slate-300">Username</span>
        <input
          v-model="username"
          type="text"
          autocomplete="username"
          class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
        />
      </label>
      <label class="block">
        <span class="text-sm text-slate-300">Password</span>
        <input
          v-model="password"
          type="password"
          autocomplete="new-password"
          data-1p-ignore="true"
          data-lpignore="true"
          class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
        />
      </label>
    </div>
    <label class="block">
      <span class="text-sm text-slate-300">From</span>
      <input
        v-model="from"
        type="email"
        required
        placeholder="shield@example.com"
        class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
      />
    </label>
    <label class="block">
      <span class="text-sm text-slate-300">To (one per line)</span>
      <textarea
        v-model="toRaw"
        rows="3"
        required
        placeholder="ops@example.com&#10;security@example.com"
        class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 font-mono text-sm focus:border-blue-500 focus:outline-none"
      />
    </label>
    <label class="flex items-center gap-2 text-sm text-slate-300">
      <input
        v-model="useStartTls"
        type="checkbox"
        class="accent-blue-500"
      />
      Use STARTTLS
    </label>
  </div>
</template>
