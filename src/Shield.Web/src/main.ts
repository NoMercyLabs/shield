import { createApp } from 'vue'
import { VueQueryPlugin } from '@tanstack/vue-query'

import App from '@/App.vue'
import { router } from '@/router'
import { bootstrapAuth } from '@/stores/auth'

import '@/styles/main.css'

const app = createApp(App)

app.use(router)
app.use(VueQueryPlugin)

bootstrapAuth().finally(() => {
  app.mount('#app')
})
