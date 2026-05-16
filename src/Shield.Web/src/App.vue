<script setup lang="ts">
import { RouterView, useRoute } from 'vue-router'
import { computed } from 'vue'

import AppShell from '@/components/AppShell.vue'

const route = useRoute()
const isPublic = computed(() => route.meta.public === true)
</script>

<template>
  <AppShell v-if="!isPublic">
    <RouterView />
  </AppShell>
  <RouterView v-else />
</template>
