<script setup lang="ts">
import { RouterView, useRoute } from 'vue-router'
import { computed } from 'vue'

import AppShell from '@/components/AppShell.vue'
import ImpersonationBanner from '@/components/ImpersonationBanner.vue'

const route = useRoute()
const isPublic = computed(() => route.meta.public === true)
</script>

<template>
  <div class="flex h-screen w-screen flex-col overflow-hidden bg-slate-950 text-slate-100">
    <ImpersonationBanner />
    <AppShell v-if="!isPublic" class="min-h-0 flex-1">
      <RouterView />
    </AppShell>
    <RouterView v-else />
  </div>
</template>
