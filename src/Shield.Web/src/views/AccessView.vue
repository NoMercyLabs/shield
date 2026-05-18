<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue'
import { Copy, Github, MailPlus, Search, Share2, Trash2, UserPlus, Users, XCircle } from 'lucide-vue-next'

import { useRouter } from 'vue-router'
import { Eye } from 'lucide-vue-next'
import { useI18n } from 'vue-i18n'

import {
  searchGithubUsers,
  useAccessGroupsQuery,
  useAccessUsersQuery,
  useAddGroupMemberMutation,
  useCreateGroupMutation,
  useDeleteGroupMutation,
  useGithubOrgMembersQuery,
  useGithubOrgsQuery,
  useInviteUserMutation,
  usePendingInvitesQuery,
  useRemoveGroupMemberMutation,
  useRevokeInviteMutation,
} from '@/queries/access'
import { useStartImpersonationMutation } from '@/queries/impersonation'
import { useAuth } from '@/stores/auth'
import { enumLabel } from '@/stores/enums'
import { useToasts } from '@/stores/toast'
import SortableTh from '@/components/SortableTh.vue'
import { useClientSort } from '@/composables/useClientSort'
import type { AccessRoleName, AccessUser, GithubUserSummary, PendingInvite } from '@/types/api'
import { SourceAccessLevel } from '@/types/api'

const users = useAccessUsersQuery()
const groups = useAccessGroupsQuery()
const invites = usePendingInvitesQuery()

// Two independent sort handles — invites + users tables sort separately.
const invitesRowsRef = computed<PendingInvite[]>(() => invites.data.value ?? [])
const invitesSort = useClientSort<PendingInvite>(
  invitesRowsRef,
  [
    { key: 'email', extract: row => row.email, defaultDirection: 'asc' },
    { key: 'role', extract: row => row.role, defaultDirection: 'asc' },
    { key: 'groups', extract: row => row.sourceGroupNames.join(','), defaultDirection: 'asc' },
    { key: 'invitedBy', extract: row => row.inviterLogin ?? '', defaultDirection: 'asc' },
    { key: 'expires', extract: row => row.expiresAt, defaultDirection: 'asc' },
  ],
  { storageKey: 'shield.access.invites.sort' },
)

const usersRowsRef = computed<AccessUser[]>(() => users.data.value ?? [])
const usersSort = useClientSort<AccessUser>(
  usersRowsRef,
  [
    { key: 'username', extract: row => row.username, defaultDirection: 'asc' },
    { key: 'email', extract: row => row.email ?? '', defaultDirection: 'asc' },
    { key: 'roles', extract: row => row.roles.join(','), defaultDirection: 'asc' },
    { key: 'created', extract: row => row.createdAt, defaultDirection: 'desc' },
  ],
  { storageKey: 'shield.access.users.sort' },
)
const createGroup = useCreateGroupMutation()
const deleteGroup = useDeleteGroupMutation()
const addMember = useAddGroupMemberMutation()
const removeMember = useRemoveGroupMemberMutation()
const invite = useInviteUserMutation()
const revokeInvite = useRevokeInviteMutation()
const startImpersonation = useStartImpersonationMutation()
const auth = useAuth()
const router = useRouter()
const { push } = useToasts()
const { t } = useI18n()

async function onImpersonate(userId: string, username: string): Promise<void> {
  try {
    await startImpersonation.mutateAsync(userId)
    push('success', t('access_view.impersonating_as', { username }))
    await router.push({ name: 'dashboard' })
  }
  catch (error) {
    const message = error instanceof Error ? error.message : t('access_view.impersonation_failed')
    push('error', message)
  }
}

const inviteOpen = ref(false)
const inviteTab = ref<'email' | 'github'>('email')
const inviteEmail = ref('')
const inviteRole = ref<AccessRoleName>('Maintainer')
const inviteGroupIds = ref<Set<number>>(new Set())
const lastInviteAcceptUrl = ref<string | null>(null)
const lastInviteEmailSent = ref<boolean | null>(null)
const lastInviteSkipReason = ref<string | null>(null)

// GitHub picker state. Orgs lazy-load when the tab is first opened; members + search are
// per-org / per-query so they re-fetch when the admin picks a different org or types.
const githubEnabled = computed(() => inviteOpen.value && inviteTab.value === 'github')
// Pass a getter so TanStack re-evaluates whenever the computed flips. Previous shape
// captured githubEnabled.value at component-construction time (always false at mount
// because the dialog isn't open yet) and the watch-and-refetch fallback couldn't
// rescue it because v5's refetch() is a no-op on a disabled query — which is what
// left the invite-collaborator picker empty.
const githubOrgsQ = useGithubOrgsQuery(() => githubEnabled.value)
const selectedOrg = ref<string | null>(null)
const memberPage = ref(1)
const membersQ = useGithubOrgMembersQuery(() => selectedOrg.value, () => memberPage.value)

const searchQuery = ref('')
const searchDebounced = ref('')
const searchResults = ref<GithubUserSummary[]>([])
const searchPending = ref(false)
let searchTimer: ReturnType<typeof setTimeout> | null = null
watch(searchQuery, (next) => {
  if (searchTimer)
    clearTimeout(searchTimer)
  searchTimer = setTimeout(() => {
    searchDebounced.value = next.trim()
  }, 300)
})
watch(searchDebounced, async (next) => {
  if (!next) {
    searchResults.value = []
    return
  }
  searchPending.value = true
  try {
    const { users: result } = await searchGithubUsers(next)
    searchResults.value = result
  }
  catch {
    push('error', t('access_view.search_failed'))
    searchResults.value = []
  }
  finally {
    searchPending.value = false
  }
})

const pendingGithubInvite = ref<GithubUserSummary | null>(null)
const githubTokenInvalid = ref(false)

// The orgs query response signals 409 via axios — react once the query lands and surface
// the "reconnect" banner. The 400 "github_not_connected" path is the bare "Connect GitHub"
// banner — different copy, different CTA.
const githubNotConnected = computed(() => {
  const err = githubOrgsQ.error.value as { response?: { status?: number, data?: { error?: string } } } | undefined
  return err?.response?.status === 400 && err.response.data?.error === 'github_not_connected'
})
watch(() => githubOrgsQ.error.value, (err) => {
  const status = (err as { response?: { status?: number } } | undefined)?.response?.status
  githubTokenInvalid.value = status === 409
})

const newGroupName = ref('')
const newGroupDescription = ref('')

const addMemberGroupId = ref<number | null>(null)
const addMemberUsername = ref('')

const sortedGroups = computed(() =>
  (groups.data.value ?? []).slice().sort((a, b) => a.name.localeCompare(b.name)),
)

function toggleInviteGroup(groupId: number): void {
  const next = new Set(inviteGroupIds.value)
  if (next.has(groupId))
    next.delete(groupId)
  else
    next.add(groupId)
  inviteGroupIds.value = next
}

async function onInviteSubmit(): Promise<void> {
  try {
    const result = await invite.mutateAsync({
      email: inviteEmail.value.trim(),
      role: inviteRole.value,
      sourceGroupIds: Array.from(inviteGroupIds.value),
    })
    lastInviteAcceptUrl.value = result.acceptUrl
    lastInviteEmailSent.value = result.emailSent
    lastInviteSkipReason.value = result.emailSkipReason
    push(
      'success',
      result.emailSent
        ? t('access_view.invite_sent_email', { recipient: result.email })
        : t('access_view.invite_sent_manual', { recipient: result.email, reason: result.emailSkipReason ?? 'unknown' }),
    )
    inviteEmail.value = ''
    inviteGroupIds.value = new Set()
  }
  catch (error) {
    push('error', error instanceof Error ? error.message : t('access_view.invite_failed'))
  }
}

async function onInviteFromGithub(): Promise<void> {
  const target = pendingGithubInvite.value
  if (!target)
    return
  try {
    const result = await invite.mutateAsync({
      email: target.email ?? null,
      role: inviteRole.value,
      sourceGroupIds: Array.from(inviteGroupIds.value),
      externalIdentity: {
        provider: 'github',
        subjectId: target.githubId,
        login: target.login,
        displayName: target.name ?? null,
        avatarUrl: target.avatarUrl ?? null,
        email: target.email ?? null,
      },
    })
    lastInviteAcceptUrl.value = result.acceptUrl
    lastInviteEmailSent.value = result.emailSent
    lastInviteSkipReason.value = result.emailSkipReason
    push(
      'success',
      result.emailSent
        ? t('access_view.invite_sent_github', { login: target.login })
        : t('access_view.invite_sent_manual_github', { login: target.login }),
    )
    pendingGithubInvite.value = null
    inviteGroupIds.value = new Set()
  }
  catch (error) {
    push('error', error instanceof Error ? error.message : t('access_view.invite_failed'))
  }
}

function openGithubInviteFor(target: GithubUserSummary): void {
  pendingGithubInvite.value = target
  // The confirmation panel renders below the member list — scroll it into view so the
  // operator sees the "Send invitation" affordance immediately after clicking a row.
  void nextTick(() => {
    const el = document.getElementById('shield-github-invite-confirm')
    el?.scrollIntoView({ behavior: 'smooth', block: 'center' })
  })
}

function cancelGithubInvite(): void {
  pendingGithubInvite.value = null
}

function onPickOrg(org: string): void {
  selectedOrg.value = org
  memberPage.value = 1
}

function loadMoreMembers(): void {
  memberPage.value += 1
}

function reconnectGithub(): void {
  // Existing OAuth connect path lives in Settings — sending the admin there is the safest
  // call (the connect button is already there with the correct redirect handling).
  window.location.href = '/settings?tab=oauth'
}

// Share helpers. SMTP/Google delivery is intentionally out of scope for v1 — invites are
// always relayed manually via copy or native share. The Pending Invitations table reuses
// these to surface "Copy link" on every row.
const canShare = computed(() => typeof navigator !== 'undefined' && typeof navigator.share === 'function')

async function copyAcceptUrl(url: string | null): Promise<void> {
  if (!url) return
  try {
    await navigator.clipboard.writeText(url)
    push('success', t('access_view.copy_ok'))
  }
  catch {
    push('error', t('access_view.copy_error'))
  }
}

async function shareAcceptUrl(url: string | null): Promise<void> {
  if (!url || !canShare.value) return
  try {
    await navigator.share({
      title: t('access_view.invite_section_title'),
      text: t('access_view.invite_ready_label'),
      url,
    })
  }
  catch (err) {
    // User cancelled — no toast. Other failures fall back to copy.
    if ((err as { name?: string })?.name !== 'AbortError')
      await copyAcceptUrl(url)
  }
}

// Pending-list rows carry the raw token; build the accept URL from the current origin so
// the link works through whichever hostname Shield is currently being accessed on.
function buildAcceptUrlFor(token: string | null | undefined): string | null {
  if (!token) return null
  return `${window.location.origin}/accept-invite?token=${encodeURIComponent(token)}`
}

// vue-tsc occasionally fails to detect template @click usage of script-setup functions —
// these are all referenced in the template above. Keep them flagged as used.
void cancelGithubInvite
void onPickOrg
void loadMoreMembers
void reconnectGithub

async function onRevoke(inviteId: string, email: string): Promise<void> {
  if (!confirm(t('access_view.revoke_confirm', { email })))
    return
  try {
    await revokeInvite.mutateAsync(inviteId)
    push('success', t('access_view.revoked_ok', { email }))
  }
  catch {
    push('error', t('access_view.revoke_error'))
  }
}

async function onCreateGroup(): Promise<void> {
  if (!newGroupName.value.trim()) return
  try {
    await createGroup.mutateAsync({
      name: newGroupName.value.trim(),
      description: newGroupDescription.value || null,
    })
    push('success', t('access_view.create_group_ok', { name: newGroupName.value }))
    newGroupName.value = ''
    newGroupDescription.value = ''
  }
  catch {
    push('error', t('access_view.create_group_error'))
  }
}

async function onAddMember(): Promise<void> {
  if (addMemberGroupId.value === null) return
  try {
    await addMember.mutateAsync({
      groupId: addMemberGroupId.value,
      member: { username: addMemberUsername.value, email: null },
    })
    addMemberUsername.value = ''
    addMemberGroupId.value = null
    push('success', t('access_view.add_member_ok'))
  }
  catch {
    push('error', t('access_view.add_member_error'))
  }
}

async function onRemoveMember(groupId: number, userId: string): Promise<void> {
  try {
    await removeMember.mutateAsync({ groupId, userId })
  }
  catch {
    push('error', t('access_view.remove_member_error'))
  }
}

async function onDeleteGroup(groupId: number): Promise<void> {
  if (!confirm(t('access_view.delete_group_confirm')))
    return
  try {
    await deleteGroup.mutateAsync(groupId)
  }
  catch {
    push('error', t('access_view.delete_group_error'))
  }
}

function fmtDate(iso: string): string {
  try {
    return new Date(iso).toLocaleString()
  }
  catch {
    return iso
  }
}
</script>

<template>
  <div class="space-y-6">
    <header class="flex items-center justify-between">
      <h1 class="text-2xl font-semibold">{{ t('access_view.title') }}</h1>
      <button
        type="button"
        class="flex items-center gap-1 rounded bg-blue-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-blue-500"
        @click="inviteOpen = !inviteOpen"
      >
        <UserPlus class="h-4 w-4" />
        {{ t('access_view.invite_btn') }}
      </button>
    </header>

    <section
      v-if="inviteOpen"
      class="space-y-3 rounded-lg border border-slate-800 bg-slate-900 p-4"
    >
      <h2 class="text-lg font-medium">{{ t('access_view.invite_section_title') }}</h2>

      <nav class="flex gap-1 border-b border-slate-800 text-sm">
        <button
          type="button"
          class="-mb-px border-b-2 px-3 py-1.5"
          :class="inviteTab === 'email'
            ? 'border-blue-500 text-slate-100'
            : 'border-transparent text-slate-400 hover:text-slate-200'"
          @click="inviteTab = 'email'"
        >
          <MailPlus class="mr-1 inline h-4 w-4" />
          {{ t('access_view.tab_email') }}
        </button>
        <button
          type="button"
          class="-mb-px border-b-2 px-3 py-1.5"
          :class="inviteTab === 'github'
            ? 'border-blue-500 text-slate-100'
            : 'border-transparent text-slate-400 hover:text-slate-200'"
          @click="inviteTab = 'github'"
        >
          <Github class="mr-1 inline h-4 w-4" />
          {{ t('access_view.tab_github') }}
        </button>
      </nav>

      <!-- Shared: role + source groups -->
      <div class="grid gap-3 sm:grid-cols-2">
        <label class="block">
          <span class="text-sm text-slate-300">{{ t('access_view.role_label') }}</span>
          <select
            v-model="inviteRole"
            class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
          >
            <option value="Maintainer">Maintainer</option>
            <option value="Viewer">Viewer</option>
            <option value="Admin">Admin</option>
          </select>
        </label>
      </div>
      <fieldset class="space-y-2 rounded border border-slate-800 bg-slate-950 p-3">
        <legend class="px-1 text-sm text-slate-300">{{ t('access_view.source_groups_legend') }}</legend>
        <p v-if="!sortedGroups.length" class="text-xs text-slate-500">{{ t('access_view.no_groups_hint') }}</p>
        <ul v-else class="space-y-1 text-sm">
          <li
            v-for="group in sortedGroups"
            :key="group.id"
            class="flex items-center gap-2"
          >
            <input
              :id="`invite-group-${group.id}`"
              type="checkbox"
              :checked="inviteGroupIds.has(group.id)"
              @change="toggleInviteGroup(group.id)"
            />
            <label :for="`invite-group-${group.id}`" class="flex-1 text-slate-200">{{ group.name }}</label>
            <span v-if="group.description" class="text-xs text-slate-500">{{ group.description }}</span>
          </li>
        </ul>
      </fieldset>

      <!-- Email tab -->
      <form v-if="inviteTab === 'email'" class="space-y-3" @submit.prevent="onInviteSubmit">
        <p class="text-xs text-slate-400">{{ t('access_view.email_tab_hint') }}</p>
        <label class="block">
          <span class="text-sm text-slate-300">{{ t('access_view.email_label') }}</span>
          <input
            v-model="inviteEmail"
            required
            type="email"
            :placeholder="t('access_view.email_placeholder')"
            class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
          />
        </label>
        <button
          type="submit"
          :disabled="invite.isPending.value || !inviteEmail"
          class="rounded bg-blue-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-blue-500 disabled:bg-blue-900"
        >
          <span v-if="invite.isPending.value">{{ t('access_view.sending') }}</span>
          <span v-else>{{ t('access_view.send_invite_btn') }}</span>
        </button>
      </form>

      <!-- GitHub picker tab -->
      <div v-else class="space-y-3">
        <p class="text-xs text-slate-400">{{ t('access_view.github_tab_hint') }}</p>

        <div
          v-if="githubNotConnected"
          class="rounded border border-amber-700 bg-amber-900/40 p-3 text-xs text-amber-200"
        >
          {{ t('access_view.github_not_connected') }}
          <button
            type="button"
            class="ml-2 inline-flex items-center gap-1 rounded bg-amber-700 px-2 py-1 text-amber-50 hover:bg-amber-600"
            @click="reconnectGithub"
          >
            {{ t('access_view.github_open_settings') }}
          </button>
        </div>

        <div
          v-else-if="githubTokenInvalid"
          class="rounded border border-red-700 bg-red-900/40 p-3 text-xs text-red-200"
        >
          {{ t('access_view.github_expired') }}
          <button
            type="button"
            class="ml-2 inline-flex items-center gap-1 rounded bg-red-700 px-2 py-1 text-red-50 hover:bg-red-600"
            @click="reconnectGithub"
          >
            {{ t('access_view.github_reconnect') }}
          </button>
        </div>

        <template v-else>
          <label class="block">
            <span class="text-sm text-slate-300">{{ t('access_view.org_label') }}</span>
            <select
              :value="selectedOrg ?? ''"
              class="mt-1 w-full rounded border border-slate-700 bg-slate-800 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none"
              @change="onPickOrg(($event.target as HTMLSelectElement).value)"
            >
              <option value="" disabled>
                {{ githubOrgsQ.isLoading.value ? t('access_view.org_loading') : t('access_view.org_placeholder') }}
              </option>
              <option
                v-for="org in githubOrgsQ.data.value?.orgs ?? []"
                :key="org.login"
                :value="org.login"
              >
                {{ org.login }}{{ org.name ? ` (${org.name})` : '' }}
              </option>
            </select>
            <p
              v-if="!githubOrgsQ.isLoading.value && (githubOrgsQ.data.value?.orgs.length ?? 0) === 0"
              class="mt-1 text-xs text-slate-500"
            >
              {{ t('access_view.org_none') }}
            </p>
          </label>

          <div v-if="selectedOrg" class="space-y-2">
            <p v-if="membersQ.isLoading.value" class="text-xs text-slate-500">{{ t('access_view.members_loading') }}</p>
            <p
              v-else-if="(membersQ.data.value?.members.length ?? 0) === 0"
              class="text-xs text-slate-500"
            >
              {{ t('access_view.members_none') }}
            </p>
            <ul v-else class="divide-y divide-slate-800 rounded border border-slate-800 bg-slate-950">
              <li
                v-for="member in membersQ.data.value!.members"
                :key="member.githubId"
                class="flex items-center gap-3 p-2"
              >
                <img
                  v-if="member.avatarUrl"
                  :src="member.avatarUrl"
                  :alt="member.login"
                  class="h-7 w-7 rounded-full"
                />
                <div class="flex-1">
                  <div class="text-sm text-slate-200">{{ member.login }}</div>
                  <div class="text-xs text-slate-500">
                    {{ member.name ?? '—' }}<span v-if="member.email"> · {{ member.email }}</span>
                  </div>
                </div>
                <button
                  type="button"
                  class="rounded bg-blue-600 px-2 py-1 text-xs font-medium text-white hover:bg-blue-500"
                  @click="openGithubInviteFor(member)"
                >
                  {{ t('access_view.invite_member_btn') }}
                </button>
              </li>
            </ul>
            <button
              v-if="membersQ.data.value?.hasMore"
              type="button"
              class="rounded bg-slate-800 px-2 py-1 text-xs text-slate-200 hover:bg-slate-700"
              @click="loadMoreMembers"
            >
              {{ t('access_view.load_more_btn') }}
            </button>
          </div>

          <div class="space-y-2">
            <label class="block">
              <span class="text-sm text-slate-300">{{ t('access_view.search_label') }}</span>
              <div class="relative mt-1">
                <Search class="pointer-events-none absolute left-2 top-2.5 h-4 w-4 text-slate-500" />
                <input
                  v-model="searchQuery"
                  type="search"
                  :placeholder="t('access_view.search_placeholder')"
                  class="w-full rounded border border-slate-700 bg-slate-800 py-2 pl-8 pr-3 text-sm focus:border-blue-500 focus:outline-none"
                />
              </div>
            </label>
            <p v-if="searchPending" class="text-xs text-slate-500">{{ t('access_view.searching') }}</p>
            <ul
              v-else-if="searchResults.length"
              class="divide-y divide-slate-800 rounded border border-slate-800 bg-slate-950"
            >
              <li
                v-for="result in searchResults"
                :key="result.githubId"
                class="flex items-center gap-3 p-2"
              >
                <img
                  v-if="result.avatarUrl"
                  :src="result.avatarUrl"
                  :alt="result.login"
                  class="h-7 w-7 rounded-full"
                />
                <div class="flex-1 text-sm text-slate-200">{{ result.login }}</div>
                <button
                  type="button"
                  class="rounded bg-blue-600 px-2 py-1 text-xs font-medium text-white hover:bg-blue-500"
                  @click="openGithubInviteFor(result)"
                >
                  {{ t('access_view.invite_member_btn') }}
                </button>
              </li>
            </ul>
            <p
              v-else-if="searchDebounced && !searchPending"
              class="text-xs text-slate-500"
            >
              {{ t('access_view.search_no_matches') }}
            </p>
          </div>

          <div
            v-if="pendingGithubInvite"
            id="shield-github-invite-confirm"
            class="sticky bottom-2 rounded border border-blue-700 bg-blue-900/40 p-3 text-sm text-slate-200 shadow-lg shadow-blue-950/40 backdrop-blur"
          >
            <div class="mb-2 flex items-center gap-2 font-medium">
              <img
                v-if="pendingGithubInvite.avatarUrl"
                :src="pendingGithubInvite.avatarUrl"
                :alt="pendingGithubInvite.login"
                class="h-6 w-6 rounded-full"
              />
              {{ t('access_view.invite_confirm_title', { login: pendingGithubInvite.login }) }}
            </div>
            <div class="flex flex-wrap gap-2">
              <button
                type="button"
                :disabled="invite.isPending.value"
                class="rounded bg-blue-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-blue-500 disabled:bg-blue-900"
                @click="onInviteFromGithub"
              >
                <span v-if="invite.isPending.value">{{ t('access_view.sending') }}</span>
                <span v-else>{{ t('access_view.send_invite_btn') }}</span>
              </button>
              <button
                type="button"
                class="rounded bg-slate-800 px-3 py-1.5 text-sm text-slate-200 hover:bg-slate-700"
                @click="cancelGithubInvite"
              >
                {{ t('action.cancel') }}
              </button>
            </div>
          </div>
        </template>
      </div>

      <div
        v-if="lastInviteAcceptUrl"
        class="space-y-2 rounded border border-blue-700 bg-blue-950/40 p-3 text-xs text-slate-200"
      >
        <p class="text-slate-300">{{ t('access_view.invite_ready_label') }}</p>
        <code class="block select-all break-all rounded bg-slate-950/60 px-2 py-1.5 font-mono text-[11px] text-slate-100">{{ lastInviteAcceptUrl }}</code>
        <div class="flex flex-wrap gap-2">
          <button
            type="button"
            class="inline-flex items-center gap-1.5 rounded bg-blue-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-blue-500"
            @click="copyAcceptUrl(lastInviteAcceptUrl)"
          >
            {{ t('access_view.copy_link_btn') }}
          </button>
          <button
            v-if="canShare"
            type="button"
            class="inline-flex items-center gap-1.5 rounded border border-slate-700 px-3 py-1.5 text-xs text-slate-200 hover:bg-slate-800"
            @click="shareAcceptUrl(lastInviteAcceptUrl)"
          >
            {{ t('access_view.share_btn') }}
          </button>
          <button
            type="button"
            class="inline-flex items-center gap-1.5 rounded border border-slate-700 px-3 py-1.5 text-xs text-slate-200 hover:bg-slate-800"
            @click="lastInviteAcceptUrl = null"
          >
            {{ t('access_view.dismiss_btn') }}
          </button>
        </div>
      </div>
    </section>

    <section class="overflow-x-auto rounded-lg border border-slate-800 bg-slate-900">
      <header class="flex items-center gap-2 border-b border-slate-800 p-3 text-sm font-medium text-slate-200">
        <MailPlus class="h-4 w-4" />
        {{ t('access_view.pending_section') }}
      </header>
      <p v-if="invites.isLoading.value" class="p-3 text-sm text-slate-400">{{ t('state.loading') }}</p>
      <table v-else-if="invites.data.value && invites.data.value.length" class="w-full min-w-[720px] text-left text-sm">
        <thead class="border-b border-slate-800 text-xs uppercase text-slate-500">
          <tr>
            <SortableTh column-key="email" :active-key="invitesSort.sortKey.value" :active-dir="invitesSort.sortDir.value" @toggle="invitesSort.toggleSort">
              {{ t('access_view.col_email') }}
            </SortableTh>
            <SortableTh column-key="role" :active-key="invitesSort.sortKey.value" :active-dir="invitesSort.sortDir.value" @toggle="invitesSort.toggleSort">
              {{ t('access_view.col_role') }}
            </SortableTh>
            <SortableTh column-key="groups" :active-key="invitesSort.sortKey.value" :active-dir="invitesSort.sortDir.value" @toggle="invitesSort.toggleSort">
              {{ t('access_view.col_groups') }}
            </SortableTh>
            <SortableTh column-key="invitedBy" :active-key="invitesSort.sortKey.value" :active-dir="invitesSort.sortDir.value" @toggle="invitesSort.toggleSort">
              {{ t('access_view.col_invited_by') }}
            </SortableTh>
            <SortableTh column-key="expires" :active-key="invitesSort.sortKey.value" :active-dir="invitesSort.sortDir.value" @toggle="invitesSort.toggleSort">
              {{ t('access_view.col_expires') }}
            </SortableTh>
            <th class="px-4 py-2"></th>
          </tr>
        </thead>
        <tbody class="divide-y divide-slate-800">
          <tr v-for="row in invitesSort.sortedRows.value" :key="row.id" class="hover:bg-slate-800/50">
            <td class="px-4 py-2 text-slate-200">
              <div class="flex items-center gap-2">
                <span>{{ row.email }}</span>
                <span
                  v-if="row.preBound"
                  class="inline-flex items-center gap-1 rounded bg-slate-800 px-1.5 py-0.5 text-[10px] uppercase text-slate-400"
                  :title="`Pre-bound to ${row.preBound.provider}:${row.preBound.login}`"
                >
                  <Github class="h-3 w-3" />
                  {{ row.preBound.login }}
                </span>
              </div>
            </td>
            <td class="px-4 py-2 text-slate-400">{{ row.role }}</td>
            <td class="px-4 py-2 text-slate-400">
              <span v-if="row.sourceGroupNames.length">{{ row.sourceGroupNames.join(', ') }}</span>
              <span v-else class="text-slate-600">{{ t('access_view.no_groups_cell') }}</span>
            </td>
            <td class="px-4 py-2 text-slate-400">{{ row.inviterLogin ?? '—' }}</td>
            <td class="px-4 py-2 text-slate-400">{{ fmtDate(row.expiresAt) }}</td>
            <td class="px-4 py-2">
              <div class="flex items-center justify-end gap-1">
                <button
                  type="button"
                  class="flex items-center gap-1 rounded p-1 text-xs text-slate-400 hover:bg-slate-800 hover:text-blue-300"
                  :title="t('access_view.copy_invite_title', { email: row.email })"
                  @click="copyAcceptUrl(buildAcceptUrlFor(row.token))"
                >
                  <Copy class="h-4 w-4" />
                  {{ t('access_view.copy_link') }}
                </button>
                <button
                  v-if="canShare"
                  type="button"
                  class="flex items-center gap-1 rounded p-1 text-xs text-slate-400 hover:bg-slate-800 hover:text-blue-300"
                  :title="t('access_view.share_invite_title', { email: row.email })"
                  @click="shareAcceptUrl(buildAcceptUrlFor(row.token))"
                >
                  <Share2 class="h-4 w-4" />
                  {{ t('access_view.share') }}
                </button>
                <button
                  type="button"
                  class="flex items-center gap-1 rounded p-1 text-xs text-slate-400 hover:bg-slate-800 hover:text-red-300"
                  :title="t('access_view.revoke_invite_title', { email: row.email })"
                  :disabled="revokeInvite.isPending.value"
                  @click="onRevoke(row.id, row.email)"
                >
                  <XCircle class="h-4 w-4" />
                  {{ t('access_view.revoke_btn') }}
                </button>
              </div>
            </td>
          </tr>
        </tbody>
      </table>
      <p v-else class="p-3 text-sm text-slate-500">{{ t('access_view.no_pending') }}</p>
    </section>

    <section class="overflow-x-auto rounded-lg border border-slate-800 bg-slate-900">
      <header class="border-b border-slate-800 p-3 text-sm font-medium text-slate-200">
        {{ t('access_view.users_section') }}
      </header>
      <p v-if="users.isLoading.value" class="p-3 text-sm text-slate-400">{{ t('state.loading') }}</p>
      <table v-else-if="users.data.value && users.data.value.length" class="w-full min-w-[720px] text-left text-sm">
        <thead class="border-b border-slate-800 text-xs uppercase text-slate-500">
          <tr>
            <SortableTh column-key="username" :active-key="usersSort.sortKey.value" :active-dir="usersSort.sortDir.value" @toggle="usersSort.toggleSort">
              {{ t('access_view.col_username') }}
            </SortableTh>
            <SortableTh column-key="email" :active-key="usersSort.sortKey.value" :active-dir="usersSort.sortDir.value" @toggle="usersSort.toggleSort">
              {{ t('access_view.col_email_addr') }}
            </SortableTh>
            <SortableTh column-key="roles" :active-key="usersSort.sortKey.value" :active-dir="usersSort.sortDir.value" @toggle="usersSort.toggleSort">
              {{ t('access_view.col_roles') }}
            </SortableTh>
            <SortableTh column-key="created" :active-key="usersSort.sortKey.value" :active-dir="usersSort.sortDir.value" @toggle="usersSort.toggleSort">
              {{ t('access_view.col_created') }}
            </SortableTh>
            <th class="px-4 py-2"></th>
          </tr>
        </thead>
        <tbody class="divide-y divide-slate-800">
          <tr v-for="user in usersSort.sortedRows.value" :key="user.id" class="hover:bg-slate-800/50">
            <td class="px-4 py-2 text-slate-200">{{ user.username }}</td>
            <td class="px-4 py-2 text-slate-400">{{ user.email ?? '—' }}</td>
            <td class="px-4 py-2 text-slate-400">{{ user.roles.join(', ') }}</td>
            <td class="px-4 py-2 text-slate-400">{{ user.createdAt }}</td>
            <td class="px-4 py-2 text-right">
              <button
                v-if="auth.isAdmin.value && user.id !== auth.user.value?.userId && !user.roles.includes('Admin')"
                type="button"
                class="inline-flex items-center gap-1 rounded p-1 text-xs text-slate-400 hover:bg-slate-800 hover:text-amber-300"
                :title="t('access_view.impersonate_title', { username: user.username })"
                :disabled="startImpersonation.isPending.value"
                @click="onImpersonate(user.id, user.username)"
              >
                <Eye class="h-4 w-4" />
                {{ t('access_view.impersonate_btn') }}
              </button>
            </td>
          </tr>
        </tbody>
      </table>
      <p v-else class="p-3 text-sm text-slate-500">{{ t('access_view.no_users') }}</p>
    </section>

    <section class="space-y-3 rounded-lg border border-slate-800 bg-slate-900 p-4">
      <header class="flex items-center gap-2 text-sm font-medium text-slate-200">
        <Users class="h-4 w-4" />
        {{ t('access_view.groups_section') }}
      </header>
      <form class="flex flex-wrap gap-2 text-sm" @submit.prevent="onCreateGroup">
        <input
          v-model="newGroupName"
          :placeholder="t('access_view.group_name_placeholder')"
          required
          class="flex-1 min-w-[200px] rounded border border-slate-700 bg-slate-800 px-3 py-1.5"
        />
        <input
          v-model="newGroupDescription"
          :placeholder="t('access_view.group_desc_placeholder')"
          class="flex-1 min-w-[200px] rounded border border-slate-700 bg-slate-800 px-3 py-1.5"
        />
        <button
          type="submit"
          :disabled="createGroup.isPending.value"
          class="rounded bg-slate-700 px-3 py-1.5 text-sm font-medium text-slate-100 hover:bg-slate-600"
        >
          {{ t('access_view.create_group_btn') }}
        </button>
      </form>

      <p v-if="groups.isLoading.value" class="text-sm text-slate-400">{{ t('access_view.groups_loading') }}</p>
      <ul v-else-if="groups.data.value && groups.data.value.length" class="space-y-3">
        <li
          v-for="group in groups.data.value"
          :key="group.id"
          class="rounded border border-slate-800 bg-slate-950 p-3"
        >
          <header class="flex items-center justify-between">
            <div>
              <div class="text-sm font-medium text-slate-100">{{ group.name }}</div>
              <div v-if="group.description" class="text-xs text-slate-400">{{ group.description }}</div>
            </div>
            <button
              type="button"
              class="rounded p-1 text-slate-400 hover:bg-slate-800 hover:text-red-300"
              :title="t('access_view.delete_group_title', { name: group.name })"
              @click="onDeleteGroup(group.id)"
            >
              <Trash2 class="h-4 w-4" />
            </button>
          </header>
          <ul v-if="group.members.length" class="mt-2 space-y-1 text-sm">
            <li
              v-for="member in group.members"
              :key="member.userId"
              class="flex items-center justify-between rounded bg-slate-900 px-2 py-1"
            >
              <span class="text-slate-300">{{ member.username }}</span>
              <button
                type="button"
                class="text-xs text-slate-500 hover:text-red-300"
                @click="onRemoveMember(group.id, member.userId)"
              >
                {{ t('access_view.remove_member_btn') }}
              </button>
            </li>
          </ul>
          <p v-else class="mt-2 text-xs text-slate-500">{{ t('access_view.no_members') }}</p>
          <form
            class="mt-2 flex gap-2 text-sm"
            @submit.prevent="
              () => {
                addMemberGroupId = group.id
                onAddMember()
              }
            "
          >
            <input
              v-model="addMemberUsername"
              :placeholder="t('access_view.username_placeholder')"
              class="flex-1 rounded border border-slate-700 bg-slate-800 px-2 py-1"
              @focus="addMemberGroupId = group.id"
            />
            <button
              type="submit"
              class="rounded bg-slate-700 px-2 py-1 text-xs text-slate-100 hover:bg-slate-600"
            >
              {{ t('access_view.add_member_btn') }}
            </button>
          </form>
        </li>
      </ul>
      <p v-else class="text-sm text-slate-500">{{ t('access_view.no_groups') }}</p>
    </section>

    <section class="rounded-lg border border-slate-800 bg-slate-900 p-4 text-sm text-slate-400">
      <p>{{ t('access_view.grants_notice') }}</p>
      <p class="mt-2 text-xs">
        {{ t('access_view.access_levels_label') }}
        {{ t('access_view.access_levels_detail', {
          read: enumLabel('SourceAccessLevel', SourceAccessLevel.Read),
          triage: enumLabel('SourceAccessLevel', SourceAccessLevel.Triage),
        }) }}
      </p>
    </section>
  </div>
</template>
