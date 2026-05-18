import axios from 'axios'

import { router } from '@/router'

export const api = axios.create({
  baseURL: '/api',
  withCredentials: true,
  headers: {
    'Content-Type': 'application/json',
  },
  // Disable Axios's built-in XSRF auto-attach. By default it reads the XSRF-TOKEN cookie
  // and ships it as X-XSRF-TOKEN — but for ASP.NET antiforgery, that cookie holds the
  // *cookie token* (server validates against it), NOT the *request token* (what the
  // header must carry). Shipping the cookie value as the header makes them equal, which
  // antiforgery treats as invalid. Our request interceptor below attaches the proper
  // request token cached from /api/auth/xsrf instead.
  xsrfCookieName: '',
  xsrfHeaderName: '',
})

// Belt-and-braces: also stomp the global axios defaults in case any other module-scoped
// axios instance falls through to them. Axios merges config + defaults, so `undefined`
// inherits from defaults — only an explicit falsy/empty value disables the auto-attach.
axios.defaults.xsrfCookieName = ''
axios.defaults.xsrfHeaderName = ''

// Routes that handle their own 401 — never auto-redirect from these.
// Matched against window.location.pathname because router.currentRoute may not have
// resolved yet during the initial bootstrapAuth() call (fired before app.mount).
const PUBLIC_PATH_PREFIXES = ['/login', '/register', '/accept-invite', '/welcome']

function isPublicPath(): boolean {
  const path = window.location.pathname
  if (PUBLIC_PATH_PREFIXES.some(prefix => path === prefix || path.startsWith(`${prefix}?`) || path.startsWith(`${prefix}/`)))
    return true
  // After router boot: meta.public is the source of truth.
  const current = router.currentRoute.value
  return current.meta?.public === true
}

// XSRF: ASP.NET antiforgery validates by comparing the X-XSRF-TOKEN header (the *request*
// token, opaque to the client) against the antiforgery cookie (the *cookie* token, set by
// the server). They're different values — the cookie cannot be ship as the header. The
// request token is returned in the body of /api/auth/xsrf; we cache it in memory and
// attach on every state-changing request. The response interceptor below refetches it on
// 'csrf_token_invalid' and retries the request transparently.
const SAFE_METHODS = new Set(['GET', 'HEAD', 'OPTIONS', 'TRACE'])

let cachedRequestToken: string | null = null

export function setXsrfRequestToken(token: string | null): void {
  cachedRequestToken = token
}

async function fetchXsrfRequestToken(): Promise<string | null> {
  try {
    const { data } = await api.get<{ token: string }>('/auth/xsrf')
    cachedRequestToken = data?.token ?? null
    return cachedRequestToken
  }
  catch {
    return null
  }
}

api.interceptors.request.use(async (config) => {
  const method = (config.method ?? 'get').toUpperCase()
  if (SAFE_METHODS.has(method)) return config
  // Don't recurse on the xsrf endpoint itself.
  if ((config.url ?? '').includes('/auth/xsrf')) return config

  // Lazy-fetch the request token on the first state-changing request if bootstrap didn't
  // get to /auth/xsrf yet (or if the cached token was wiped).
  if (!cachedRequestToken)
    await fetchXsrfRequestToken()

  if (cachedRequestToken) {
    config.headers = config.headers ?? {}
    config.headers['X-XSRF-TOKEN'] = cachedRequestToken
  }
  return config
})

api.interceptors.response.use(
  response => response,
  async (error) => {
    const status = error?.response?.status
    const code = (error?.response?.data as { code?: string, error?: string } | undefined)
    const errorCode = code?.code ?? code?.error
    const config = error?.config

    // CSRF retry: server rejected the token. Re-fetch /api/auth/xsrf to refresh the
    // XSRF-TOKEN cookie, then retry the original request once. Guarded with a sentinel
    // flag so we don't loop on a persistent failure.
    if (
      status === 400
      && errorCode === 'csrf_token_invalid'
      && config
      && !config.__xsrfRetried
      && !(config.url ?? '').includes('/auth/xsrf')
    ) {
      config.__xsrfRetried = true
      const fresh = await fetchXsrfRequestToken()
      if (fresh) {
        config.headers = config.headers ?? {}
        config.headers['X-XSRF-TOKEN'] = fresh
      }
      return api.request(config)
    }

    if (status === 401) {
      const current = router.currentRoute.value
      if (current.name !== 'login' && !isPublicPath()) {
        router.push({ name: 'login', query: { redirect: current.fullPath } })
      }
    }
    return Promise.reject(error)
  },
)
