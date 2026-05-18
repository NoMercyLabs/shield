<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink, useRoute, useRouter } from 'vue-router'
import { ChevronDown, LogOut, Menu, MonitorSmartphone, Settings, UserCog, X } from 'lucide-vue-next'

import NotificationsBell from '@/components/NotificationsBell.vue'
import { type LocaleCode, LOCALE_FLAGS, LOCALE_LABELS, SUPPORTED_LOCALES, setLocale } from '@/i18n'
import { logout, useAuth } from '@/stores/auth'

interface Props {
  drawerOpen?: boolean
}

const props = withDefaults(defineProps<Props>(), { drawerOpen: false })
const emit = defineEmits<{ (event: 'toggle-drawer'): void }>()

const { user } = useAuth()
const router = useRouter()
const route = useRoute()
const { t, locale } = useI18n()

const currentLocale = ref<LocaleCode>(locale.value as LocaleCode)
const accountOpen = ref(false)
const accountMenu = ref<HTMLDivElement | null>(null)

// Route name -> nav key (re-uses existing nav.* keys so we don't duplicate strings).
const NAV_KEY_BY_ROUTE: Record<string, string> = {
  'dashboard': 'nav.dashboard',
  'sources': 'nav.sources',
  'source-detail': 'nav.sources',
  'findings': 'nav.findings',
  'finding-detail': 'nav.findings',
  'channels': 'nav.channels',
  'notifications': 'nav.notifications',
  'feeds': 'nav.feeds',
  'access': 'nav.access',
  'settings': 'nav.settings',
  'audit': 'nav.audit',
  'account': 'nav.account',
  'account-sessions': 'nav.sessions',
  'account-security': 'nav.account',
  'account-tokens': 'nav.tokens',
}

const pageTitle = computed(() => {
  const name = String(route.name ?? '')
  const key = NAV_KEY_BY_ROUTE[name]
  return key ? t(key) : ''
})

const initials = computed(() => {
  const name = user.value?.username ?? ''
  if (!name) return '?'
  const parts = name.split(/[\s._-]+/).filter(Boolean)
  if (parts.length === 0) return name.charAt(0).toUpperCase()
  if (parts.length === 1) return parts[0].slice(0, 2).toUpperCase()
  return (parts[0].charAt(0) + parts[1].charAt(0)).toUpperCase()
})

async function onLocaleChange(event: Event): Promise<void> {
  const next = (event.target as HTMLSelectElement).value as LocaleCode
  await setLocale(next)
  currentLocale.value = next
}

async function onLogout(): Promise<void> {
  accountOpen.value = false
  await logout()
  router.push({ name: 'login' })
}

function toggleAccount(): void {
  accountOpen.value = !accountOpen.value
}

function onDocumentClick(event: MouseEvent): void {
  if (!accountOpen.value) return
  const target = event.target as Node | null
  if (accountMenu.value && target && !accountMenu.value.contains(target))
    accountOpen.value = false
}

function onEscape(event: KeyboardEvent): void {
  if (event.key === 'Escape')
    accountOpen.value = false
}

onMounted(() => {
  document.addEventListener('click', onDocumentClick)
  document.addEventListener('keydown', onEscape)
})

onUnmounted(() => {
  document.removeEventListener('click', onDocumentClick)
  document.removeEventListener('keydown', onEscape)
})
</script>

<template>
  <header class="flex items-center justify-between gap-2 px-3 md:gap-3 md:px-5">
    <div class="flex min-w-0 items-center gap-2 md:gap-3">
      <!-- Hamburger — visible below md only, swaps to an X while the drawer is open so the
           same button doubles as a close affordance. 44x44 hit area satisfies tap-target rule. -->
      <button
        type="button"
        class="grid h-11 w-11 shrink-0 place-items-center rounded-md text-slate-300 transition-colors hover:bg-slate-800/60 hover:text-white focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-blue-500 md:hidden"
        :aria-label="props.drawerOpen ? t('topbar.close_menu') : t('topbar.open_menu')"
        :aria-expanded="props.drawerOpen"
        aria-controls="shield-mobile-drawer"
        @click="emit('toggle-drawer')"
      >
        <X v-if="props.drawerOpen" class="h-5 w-5" />
        <Menu v-else class="h-5 w-5" />
      </button>

      <h1 v-if="pageTitle" class="hidden truncate text-base font-semibold text-slate-100 md:block">{{ pageTitle }}</h1>
      <span v-if="user?.singleUserMode" class="hidden rounded bg-slate-800 px-2 py-0.5 text-[10px] font-medium uppercase tracking-wider text-slate-300 md:inline">
        {{ t('banner.single_user_mode') }}
      </span>
    </div>

    <div class="flex items-center gap-1.5 md:gap-2">
      <label class="sr-only" for="shield-locale">{{ t('field.language') }}</label>
      <select
        id="shield-locale"
        :value="currentLocale"
        :aria-label="t('field.language')"
        class="hidden rounded-md border border-slate-700 bg-slate-800 px-2 py-1 text-xs text-slate-200 transition-colors hover:bg-slate-700 focus:border-blue-500 focus:outline-none focus-visible:ring-1 focus-visible:ring-blue-500 sm:block"
        @change="onLocaleChange"
      >
        <option v-for="code in SUPPORTED_LOCALES" :key="code" :value="code">
          {{ LOCALE_FLAGS[code] }} {{ LOCALE_LABELS[code] }}
        </option>
      </select>

      <span class="hidden h-5 w-px bg-slate-800 sm:inline" />

      <NotificationsBell v-if="user" />

      <template v-if="user">
        <span class="hidden h-5 w-px bg-slate-800 md:inline" />

        <div ref="accountMenu" class="relative">
          <button
            type="button"
            class="flex h-11 items-center gap-2 rounded-md px-2 text-sm text-slate-300 transition-colors hover:bg-slate-800/60 hover:text-white focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-blue-500"
            :aria-label="t('topbar.account.menu_label')"
            :aria-expanded="accountOpen"
            @click="toggleAccount"
          >
            <img
              v-if="user.avatarUrl"
              :src="user.avatarUrl"
              :alt="user.displayName ?? user.username ?? ''"
              loading="lazy"
              referrerpolicy="no-referrer"
              class="h-7 w-7 rounded-full ring-1 ring-blue-500/30 object-cover"
            />
            <span v-else class="grid h-7 w-7 place-items-center rounded-full bg-blue-500/20 text-xs font-semibold text-blue-200 ring-1 ring-blue-500/30">
              {{ initials }}
            </span>
            <span class="hidden max-w-[10rem] truncate text-sm text-slate-200 sm:inline">{{ user.displayName ?? user.username }}</span>
            <ChevronDown class="h-3 w-3 text-slate-500 transition-transform" :class="{ 'rotate-180': accountOpen }" />
          </button>

          <div
            v-if="accountOpen"
            class="absolute right-0 z-50 mt-2 w-56 overflow-hidden rounded-md border border-slate-800 bg-slate-900 shadow-xl"
            role="menu"
          >
            <RouterLink
              to="/account"
              class="flex items-center gap-2 px-3 py-2 text-sm text-slate-200 transition-colors hover:bg-slate-800/80"
              role="menuitem"
              @click="accountOpen = false"
            >
              <UserCog class="h-4 w-4 text-slate-400" />
              <span>{{ t('topbar.account.settings') }}</span>
            </RouterLink>
            <RouterLink
              to="/account/sessions"
              class="flex items-center gap-2 px-3 py-2 text-sm text-slate-200 transition-colors hover:bg-slate-800/80"
              role="menuitem"
              @click="accountOpen = false"
            >
              <MonitorSmartphone class="h-4 w-4 text-slate-400" />
              <span>{{ t('topbar.account.sessions') }}</span>
            </RouterLink>
            <RouterLink
              to="/account/tokens"
              class="flex items-center gap-2 px-3 py-2 text-sm text-slate-200 transition-colors hover:bg-slate-800/80"
              role="menuitem"
              @click="accountOpen = false"
            >
              <Settings class="h-4 w-4 text-slate-400" />
              <span>{{ t('topbar.account.tokens') }}</span>
            </RouterLink>
            <template v-if="!user?.singleUserMode">
              <div class="border-t border-slate-800" />
              <button
                type="button"
                class="flex w-full items-center gap-2 px-3 py-2 text-left text-sm text-slate-200 transition-colors hover:bg-slate-800/80"
                role="menuitem"
                @click="onLogout"
              >
                <LogOut class="h-4 w-4 text-slate-400" />
                <span>{{ t('topbar.account.sign_out') }}</span>
              </button>
            </template>
          </div>
        </div>
      </template>
    </div>
  </header>
</template>
