import { createRouter, createWebHistory, type RouteRecordRaw } from 'vue-router'

const routes: RouteRecordRaw[] = [
  {
    path: '/login',
    name: 'login',
    component: () => import('@/views/LoginView.vue'),
    meta: { public: true },
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

export { routes }
