import { createApp } from 'vue'
import { VueQueryPlugin } from '@tanstack/vue-query'

import App from '@/App.vue'
import { i18n } from '@/i18n'
import { bootstrapPwa } from '@/lib/pwa'
import { router } from '@/router'
import { bootstrapAuth } from '@/stores/auth'
import { loadEnums } from '@/stores/enums'

import '@/styles/main.css'

const app = createApp(App)

app.use(router)
app.use(VueQueryPlugin)
app.use(i18n)

// Capture beforeinstallprompt + appinstalled BEFORE the app mounts so a fast-mount Chrome
// build doesn't miss the event (it fires once per session, usually right after first paint).
bootstrapPwa()

// Auth + enum catalog hydrate in parallel — neither blocks the other, and both finish
// before mount so the first render has the labels needed for nav badges + dropdowns.
Promise.all([bootstrapAuth(), loadEnums()]).finally(() => {
  app.mount('#app')

  // Register the service worker AFTER mount so the first paint isn't blocked on it. The SW
  // file is same-origin under /service-worker.js (CSP script-src 'self' covers it). We do not
  // attempt registration when the API isn't reachable — failing here would log a noisy console
  // error but the app still works without push.
  if ('serviceWorker' in navigator) {
    window.addEventListener('load', () => {
      navigator.serviceWorker
        .register('/service-worker.js', { scope: '/' })
        .catch((error) => {
          // Don't surface this to the user — push is a progressive enhancement.
          console.warn('Service worker registration failed:', error)
        })
    })
  }
})
