<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { Copy, ExternalLink, GitBranch, Github, RefreshCw, ShieldCheck } from 'lucide-vue-next'

import { useToasts } from '@/stores/toast'

const { t } = useI18n()
const { push } = useToasts()

interface ProviderRow {
  id: 'github' | 'gitea' | 'gitlab' | 'forgejo'
  name: string
  icon: typeof Github
  envVar: string
  docsUrl: string
}

const providers: ProviderRow[] = [
  {
    id: 'github',
    name: 'GitHub',
    icon: Github,
    envVar: 'Shield__Webhooks__GitHub__Secret',
    docsUrl: 'https://docs.github.com/en/webhooks/about-webhooks',
  },
  {
    id: 'gitea',
    name: 'Gitea',
    icon: GitBranch,
    envVar: 'Shield__Webhooks__Gitea__Secret',
    docsUrl: 'https://docs.gitea.com/usage/webhooks',
  },
  {
    id: 'gitlab',
    name: 'GitLab',
    icon: GitBranch,
    envVar: 'Shield__Webhooks__GitLab__Secret',
    docsUrl: 'https://docs.gitlab.com/user/project/integrations/webhooks/',
  },
  {
    id: 'forgejo',
    name: 'Forgejo',
    icon: GitBranch,
    envVar: 'Shield__Webhooks__Forgejo__Secret',
    docsUrl: 'https://forgejo.org/docs/latest/user/webhooks/',
  },
]

const origin = computed(() => (typeof window !== 'undefined' ? window.location.origin : ''))
function urlFor(provider: ProviderRow): string {
  return `${origin.value}/api/webhooks/${provider.id}`
}

const secret = ref<string | null>(null)

function generateSecret(): void {
  const bytes = new Uint8Array(32)
  crypto.getRandomValues(bytes)
  secret.value = Array.from(bytes, byte => byte.toString(16).padStart(2, '0')).join('')
}

async function copy(value: string, successKey: string): Promise<void> {
  try {
    await navigator.clipboard.writeText(value)
    push('success', t(successKey))
  }
  catch {
    push('error', t('screen.webhooks.copy_failed'))
  }
}
</script>

<template>
  <div class="space-y-6">
    <header>
      <h1 class="text-2xl font-semibold">{{ t('screen.webhooks.title') }}</h1>
      <p class="mt-1 max-w-3xl text-sm text-slate-400">{{ t('screen.webhooks.subtitle') }}</p>
    </header>

    <section
      class="rounded-lg border border-slate-800 bg-slate-900 p-5"
      :aria-labelledby="'webhooks-secret-heading'"
    >
      <div class="flex items-start gap-3">
        <ShieldCheck class="mt-0.5 h-5 w-5 shrink-0 text-blue-400" aria-hidden="true" />
        <div class="flex-1 space-y-1">
          <h2 id="webhooks-secret-heading" class="text-base font-semibold text-slate-100">
            {{ t('screen.webhooks.secret.title') }}
          </h2>
          <p class="text-sm text-slate-400">{{ t('screen.webhooks.secret.desc') }}</p>
        </div>
      </div>

      <div v-if="secret" class="mt-4 space-y-3">
        <code
          class="block break-all rounded bg-slate-950 p-3 font-mono text-sm text-slate-100 select-all"
          :aria-label="t('screen.webhooks.secret.aria_value')"
        >{{ secret }}</code>
        <div class="flex flex-wrap gap-2">
          <button
            type="button"
            class="inline-flex items-center gap-1.5 rounded bg-blue-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-blue-500 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-400"
            :aria-label="t('screen.webhooks.secret.copy_aria')"
            @click="copy(secret, 'screen.webhooks.toast.secret_copied')"
          >
            <Copy class="h-4 w-4" aria-hidden="true" />
            {{ t('action.copy') }}
          </button>
          <button
            type="button"
            class="inline-flex items-center gap-1.5 rounded border border-slate-700 px-3 py-1.5 text-sm text-slate-300 hover:bg-slate-800 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-400"
            @click="generateSecret"
          >
            <RefreshCw class="h-4 w-4" aria-hidden="true" />
            {{ t('screen.webhooks.secret.regenerate_btn') }}
          </button>
        </div>
        <p class="text-xs text-slate-500">{{ t('screen.webhooks.secret.local_note') }}</p>
      </div>

      <div v-else class="mt-4">
        <button
          type="button"
          class="inline-flex items-center gap-1.5 rounded bg-blue-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-blue-500 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-400"
          @click="generateSecret"
        >
          <ShieldCheck class="h-4 w-4" aria-hidden="true" />
          {{ t('screen.webhooks.secret.generate_btn') }}
        </button>
      </div>
    </section>

    <section :aria-labelledby="'webhooks-providers-heading'">
      <div class="mb-3">
        <h2 id="webhooks-providers-heading" class="text-base font-semibold text-slate-100">
          {{ t('screen.webhooks.providers.title') }}
        </h2>
        <p class="text-sm text-slate-400">{{ t('screen.webhooks.providers.desc') }}</p>
      </div>

      <ul class="space-y-3" role="list">
        <li
          v-for="provider in providers"
          :key="provider.id"
          class="rounded-lg border border-slate-800 bg-slate-900 p-4"
        >
          <div class="flex items-start justify-between gap-4">
            <div class="flex items-center gap-2">
              <component :is="provider.icon" class="h-5 w-5 text-slate-300" aria-hidden="true" />
              <h3 class="text-sm font-semibold text-slate-100">{{ provider.name }}</h3>
              <span
                class="rounded-full bg-amber-500/10 px-2 py-0.5 text-[10px] font-medium uppercase tracking-wider text-amber-300"
              >
                {{ t('screen.webhooks.provider.status_pending') }}
              </span>
            </div>
            <a
              :href="provider.docsUrl"
              target="_blank"
              rel="noopener noreferrer"
              class="inline-flex items-center gap-1 text-xs text-slate-400 hover:text-slate-200 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-400 focus-visible:rounded"
            >
              {{ t('screen.webhooks.provider.docs_link') }}
              <ExternalLink class="h-3 w-3" aria-hidden="true" />
            </a>
          </div>

          <dl class="mt-3 grid gap-3 text-sm sm:grid-cols-[max-content_1fr]">
            <dt class="text-slate-500">{{ t('screen.webhooks.provider.url_label') }}</dt>
            <dd class="flex items-center gap-2">
              <code class="break-all rounded bg-slate-950 px-2 py-1 font-mono text-xs text-slate-100">
                {{ urlFor(provider) }}
              </code>
              <button
                type="button"
                class="inline-flex shrink-0 items-center gap-1 rounded border border-slate-700 px-2 py-1 text-xs text-slate-300 hover:bg-slate-800 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-400"
                :aria-label="t('screen.webhooks.provider.copy_url_aria', { provider: provider.name })"
                @click="copy(urlFor(provider), 'screen.webhooks.toast.url_copied')"
              >
                <Copy class="h-3 w-3" aria-hidden="true" />
                {{ t('action.copy') }}
              </button>
            </dd>

            <dt class="text-slate-500">{{ t('screen.webhooks.provider.content_type_label') }}</dt>
            <dd>
              <code class="rounded bg-slate-950 px-2 py-1 font-mono text-xs text-slate-100">
                application/json
              </code>
            </dd>

            <dt class="text-slate-500">{{ t('screen.webhooks.provider.secret_env_label') }}</dt>
            <dd>
              <code class="rounded bg-slate-950 px-2 py-1 font-mono text-xs text-slate-100">
                {{ provider.envVar }}
              </code>
            </dd>

            <dt class="text-slate-500">{{ t('screen.webhooks.provider.events_label') }}</dt>
            <dd class="text-slate-300">{{ t('screen.webhooks.provider.events_value') }}</dd>
          </dl>
        </li>
      </ul>
    </section>
  </div>
</template>
