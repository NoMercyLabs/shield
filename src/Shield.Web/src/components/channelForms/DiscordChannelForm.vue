<script setup lang="ts">
import { computed, ref, watch } from 'vue'

const props = defineProps<{ initial?: Record<string, unknown> | null }>()
const emit = defineEmits<{ change: [json: string, valid: boolean] }>()

const webhookUrl = ref<string>(typeof props.initial?.webhookUrl === 'string' ? props.initial.webhookUrl as string : '')

const isValid = computed(() =>
  webhookUrl.value.startsWith('https://discord.com/api/webhooks/'),
)

function publish(): void {
  emit('change', JSON.stringify({ webhookUrl: webhookUrl.value }), isValid.value)
}

watch(webhookUrl, publish, { immediate: true })
</script>

<template>
  <div class="space-y-3">
    <label class="block">
      <span class="text-sm text-slate-300">Webhook URL</span>
      <input
        v-model="webhookUrl"
        type="url"
        required
        placeholder="https://discord.com/api/webhooks/…"
        class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
      />
      <span
        v-if="webhookUrl && !isValid"
        class="mt-1 block text-xs text-amber-300"
      >
        Must start with <code>https://discord.com/api/webhooks/</code>
      </span>
    </label>
  </div>
</template>
