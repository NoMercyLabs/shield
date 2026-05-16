import { createRouter, createWebHistory, type RouteRecordRaw } from 'vue-router'

import { fetchOnboardingStatus } from '@/queries/onboarding'

const routes: RouteRecordRaw[] = [
  {
    path: '/login',
    name: 'login',
    component: () => import('@/views/LoginView.vue'),
    meta: { public: true, skipOnboardingGate: true },
  },
  {
    path: '/register',
    name: 'register',
    component: () => import('@/views/RegisterView.vue'),
    meta: { public: true, skipOnboardingGate: true },
  },
  {
    path: '/account',
    name: 'account',
    component: () => import('@/views/AccountView.vue'),
    meta: { skipOnboardingGate: true },
  },
  {
    path: '/welcome',
    name: 'onboarding',
    component: () => import('@/views/OnboardingView.vue'),
    // Wizard owns its own full-screen layout; bypass AppShell via meta.public.
    meta: { public: true, skipOnboardingGate: true },
  },
  {
    path: '/',
    name: 'dashboard',
    component: () => import('@/views/DashboardView.vue'),
  },
  {
    path: '/sources',
    name: 'sources',
    component: () => import('@/views/SourcesView.vue'),
  },
  {
    path: '/sources/:id',
    name: 'source-detail',
    component: () => import('@/views/SourceDetailView.vue'),
    props: true,
  },
  {
    path: '/findings',
    name: 'findings',
    component: () => import('@/views/FindingsView.vue'),
  },
  {
    path: '/findings/:id',
    name: 'finding-detail',
    component: () => import('@/views/FindingDetailView.vue'),
    props: true,
  },
  {
    path: '/channels',
    name: 'channels',
    component: () => import('@/views/ChannelsView.vue'),
  },
  {
    path: '/feeds',
    name: 'feeds',
    component: () => import('@/views/FeedsView.vue'),
  },
  {
    path: '/settings',
    name: 'settings',
    component: () => import('@/views/SettingsView.vue'),
    meta: { skipOnboardingGate: true },
  },
  {
    path: '/audit',
    name: 'audit',
    component: () => import('@/views/AuditView.vue'),
    meta: { adminOnly: true },
  },
  {
    path: '/:pathMatch(.*)*',
    redirect: '/',
  },
]

export const router = createRouter({
  history: createWebHistory(),
  routes,
})

// Onboarding gate: first-run installs land here before they see the empty Dashboard.
// /welcome, /login, /register, /account, /settings are recovery paths — never gated.
// The status fetch tolerates 401/404 (treat as "not first-run" so we don't trap users
// who somehow aren't authed; the per-page auth guards take care of the real auth flow).
router.beforeEach(async (to) => {
  if (to.meta.skipOnboardingGate === true) return true
  try {
    const status = await fetchOnboardingStatus()
    if (!status.completed) {
      return { path: '/welcome', query: to.fullPath !== '/' ? { from: to.fullPath } : undefined }
    }
  }
  catch {
    // Auth/network failure — let the request proceed; the page-level fetch will surface the error.
  }
  return true
})

export { routes }

