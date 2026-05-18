// Shield service worker — PWA shell + Web Push handler.
//
// Caching strategy: passthrough for everything except hashed assets in /assets/, which are
// safe to cache long-term because Vite content-hashes their filenames. API responses, the
// SPA shell, and dynamic SignalR traffic NEVER hit the cache — operators need fresh state.
//
// Lifecycle: skipWaiting + clients.claim so an updated SW takes over on the next page load
// without forcing a second refresh. Operators ship through Cloudflare tunnels where the
// classic "two reloads" SW dance confuses non-technical users.

const CACHE_VERSION = 'shield-v16'
const HASHED_ASSET_RE = /\/assets\/.+\.[a-zA-Z0-9]{6,}\.[a-z0-9]+$/

self.addEventListener('install', (event) => {
  event.waitUntil(self.skipWaiting())
})

self.addEventListener('activate', (event) => {
  event.waitUntil(
    (async () => {
      // Drop any stale cache versions on activate so old hashed files don't linger.
      const names = await caches.keys()
      await Promise.all(names.filter(name => name !== CACHE_VERSION).map(name => caches.delete(name)))
      await self.clients.claim()
    })(),
  )
})

self.addEventListener('fetch', (event) => {
  const request = event.request
  if (request.method !== 'GET')
    return

  const url = new URL(request.url)

  // Only cache same-origin hashed asset bundles. Everything else (API, /hubs/, index.html,
  // /api/og/*, manifest) hits the network so freshness wins.
  if (url.origin !== self.location.origin)
    return
  if (!HASHED_ASSET_RE.test(url.pathname))
    return

  event.respondWith(
    (async () => {
      const cache = await caches.open(CACHE_VERSION)
      const cached = await cache.match(request)
      if (cached)
        return cached
      const network = await fetch(request)
      if (network.ok)
        cache.put(request, network.clone()).catch(() => undefined)
      return network
    })(),
  )
})

// Web Push handler — server posts an aes128gcm payload that decrypts to the JSON below:
//   { title, body, severity, url, tag }
// Tag collapses duplicates so a flaky-network retry doesn't double-stack on the user.
self.addEventListener('push', (event) => {
  if (!event.data)
    return

  let payload = {}
  try {
    payload = event.data.json()
  }
  catch {
    payload = { title: 'Shield', body: event.data.text(), severity: 'Low', url: '/notifications', tag: 'shield' }
  }

  const title = payload.title || 'Shield'
  const options = {
    body: payload.body || '',
    icon: '/api/og/icon-192.png',
    badge: '/api/og/icon-192.png',
    tag: payload.tag || 'shield',
    renotify: true,
    data: {
      url: payload.url || '/notifications',
      severity: payload.severity || 'Low',
    },
  }

  event.waitUntil(self.registration.showNotification(title, options))
})

self.addEventListener('notificationclick', (event) => {
  event.notification.close()
  const target = event.notification.data?.url || '/notifications'

  event.waitUntil(
    (async () => {
      const allClients = await self.clients.matchAll({ type: 'window', includeUncontrolled: true })
      const origin = self.location.origin

      // If a Shield tab is already open, focus it and route in-app instead of opening a
      // fresh window. Browsers can be picky about cross-origin navigation here so we only
      // attempt the navigate when the existing tab is same-origin.
      for (const client of allClients) {
        if (!client.url.startsWith(origin))
          continue
        await client.focus()
        if ('navigate' in client) {
          try {
            await client.navigate(target)
          }
          catch {
            // Some browsers refuse navigate() on already-focused clients — fall through
            // to opening a new window if focus alone didn't cover the deep-link case.
          }
        }
        return
      }

      await self.clients.openWindow(target)
    })(),
  )
})
