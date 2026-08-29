import { computed, ref } from 'vue'
import { defineStore } from 'pinia'

import {
  createDefaultLocalePreferences,
  readLocalePreferences,
  writeLocalePreferences,
  type LocalePreferencesStorage,
} from '@/localization/preferences'
import { platformI18n } from '@/localization/i18n'
import type { LocalePreferences, SupportedLocale } from '@/localization/types'

function defaultStorage(): LocalePreferencesStorage | null {
  try {
    return typeof globalThis.localStorage === 'undefined' ? null : globalThis.localStorage
  } catch {
    return null
  }
}

function applyHtmlLanguage(locale: SupportedLocale): void {
  if (typeof document !== 'undefined') document.documentElement.lang = locale
}

export const useLocalizationStore = defineStore('localization', () => {
  const preferences = ref<LocalePreferences>(createDefaultLocalePreferences())
  const locale = computed(() => preferences.value.locale)
  const initialized = ref(false)

  function applyLocale(next: SupportedLocale): void {
    // vue-i18n composition mode exposes a Ref at runtime; its public v11 type
    // is intentionally widened, so keep this cast at the integration seam.
    ;(platformI18n.global.locale as unknown as { value: SupportedLocale }).value = next
    applyHtmlLanguage(next)
  }

  function initialize(storage: LocalePreferencesStorage | null = defaultStorage()): void {
    if (initialized.value) return
    const stored = storage === null ? null : readLocalePreferences(storage)
    preferences.value = stored ?? createDefaultLocalePreferences()
    applyLocale(preferences.value.locale)
    initialized.value = true
  }

  function setLocale(next: SupportedLocale, storage: LocalePreferencesStorage | null = defaultStorage()): void {
    preferences.value = { ...preferences.value, locale: next }
    applyLocale(next)
    if (storage !== null) writeLocalePreferences(storage, preferences.value)
  }

  function setPreferences(
    patch: Partial<LocalePreferences>,
    storage: LocalePreferencesStorage | null = defaultStorage(),
  ): void {
    const next = { ...preferences.value, ...patch }
    preferences.value = next
    applyLocale(next.locale)
    if (storage !== null) writeLocalePreferences(storage, next)
  }

  initialize()

  return { preferences, locale, initialized, initialize, setLocale, setPreferences }
})
