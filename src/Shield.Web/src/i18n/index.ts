import { createI18n } from 'vue-i18n'

import en from './locales/en.json'

export const SUPPORTED_LOCALES = ['en', 'nl', 'de', 'fr', 'es'] as const
export type LocaleCode = (typeof SUPPORTED_LOCALES)[number]

export const LOCALE_LABELS: Record<LocaleCode, string> = {
  en: 'EN',
  nl: 'NL',
  de: 'DE',
  fr: 'FR',
  es: 'ES',
}

export const LOCALE_FLAGS: Record<LocaleCode, string> = {
  en: '🇬🇧',
  nl: '🇳🇱',
  de: '🇩🇪',
  fr: '🇫🇷',
  es: '🇪🇸',
}

// English is the canonical shape every other locale is checked against at type level.
// Other locale JSONs may be partial — vue-i18n falls back to en for missing keys.
export type MessageSchema = typeof en

export const i18n = createI18n<[MessageSchema], LocaleCode, false>({
  legacy: false,
  locale: detectLocale(),
  fallbackLocale: 'en',
  globalInjection: true,
  messages: { en } as Record<LocaleCode, MessageSchema>,
})

export async function setLocale(code: LocaleCode): Promise<void> {
  const global = i18n.global
  if (!global.availableLocales.includes(code)) {
    const messages = await loadLocale(code)
    global.setLocaleMessage(code, messages as MessageSchema)
  }
  global.locale.value = code
  localStorage.setItem('shield.locale', code)
  document.documentElement.setAttribute('lang', code)
}

async function loadLocale(code: LocaleCode): Promise<Record<string, unknown>> {
  // Dynamic import keeps the main bundle tiny — only `en` is preloaded; other locales chunk-split.
  const mod = await import(`./locales/${code}.json`)
  return mod.default as Record<string, unknown>
}

function isSupported(code: string): code is LocaleCode {
  return (SUPPORTED_LOCALES as readonly string[]).includes(code)
}

function detectLocale(): LocaleCode {
  const saved = localStorage.getItem('shield.locale')
  if (saved && isSupported(saved)) return saved
  const browser = navigator.language?.slice(0, 2).toLowerCase() ?? ''
  return isSupported(browser) ? browser : 'en'
}
