import { ref } from 'vue'

// Web app install prompt — Chrome / Edge / Android Chrome fire `beforeinstallprompt` once
// per session when the PWA criteria are satisfied. iOS doesn't expose the event — Safari
// users still install via the Share sheet, so the prompt path is desktop / Android only.
//
// We capture the deferred event on app boot (see main.ts) and expose a `prompt()` that
// any view can call. The event can only be used once; after a successful prompt + user
// choice, the browser won't refire it for the same site for a while.

type BeforeInstallPromptEvent = Event & {
  prompt: () => Promise<void>
  userChoice: Promise<{ outcome: 'accepted' | 'dismissed' }>
}

const deferred = ref<BeforeInstallPromptEvent | null>(null)
const installed = ref<boolean>(typeof window !== 'undefined'
  && window.matchMedia?.('(display-mode: standalone)').matches === true)

export function bootstrapPwa(): void {
  if (typeof window === 'undefined')
    return

  window.addEventListener('beforeinstallprompt', (event) => {
    event.preventDefault()
    deferred.value = event as BeforeInstallPromptEvent
  })

  window.addEventListener('appinstalled', () => {
    deferred.value = null
    installed.value = true
  })
}

export function usePwaInstall() {
  async function prompt(): Promise<'accepted' | 'dismissed' | 'unavailable'> {
    const event = deferred.value
    if (!event)
      return 'unavailable'
    try {
      await event.prompt()
      const { outcome } = await event.userChoice
      // beforeinstallprompt is single-use — drop the reference whether or not the user
      // accepted, so a second click doesn't trip the "already used" error.
      deferred.value = null
      return outcome
    }
    catch {
      deferred.value = null
      return 'dismissed'
    }
  }

  return {
    canInstall: deferred,
    isInstalled: installed,
    prompt,
  }
}
