import type { LocalePreferences, SupportedLocale } from './types'

export const LOCALE_PREFERENCES_STORAGE_KEY = 'industrial-platform.locale.preferences.v1'

export interface LocalePreferencesStorage {
  getItem(key: string): string | null
  setItem(key: string, value: string): void
}

function defaultTimeZone(): string {
  try {
    return Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC'
  } catch {
    return 'UTC'
  }
}

export const DEFAULT_LOCALE_PREFERENCES: LocalePreferences = {
  locale: 'zh-CN',
  timeZone: defaultTimeZone(),
  dateFormat: 'yyyy-MM-dd',
  numberLocale: 'zh-CN',
  unitSystem: 'metric',
}

function isSupportedLocale(value: unknown): value is SupportedLocale {
  return value === 'zh-CN' || value === 'en-US'
}

function isDateFormat(value: unknown): value is LocalePreferences['dateFormat'] {
  return value === 'yyyy-MM-dd' || value === 'MM/dd/yyyy'
}

export function parseLocalePreferences(raw: string | null): LocalePreferences | null {
  if (raw === null) return null
  try {
    const value: unknown = JSON.parse(raw)
    if (typeof value !== 'object' || value === null) return null
    const record = value as Record<string, unknown>
    if (
      !isSupportedLocale(record.locale) ||
      typeof record.timeZone !== 'string' ||
      record.timeZone.trim() === '' ||
      !isDateFormat(record.dateFormat) ||
      !isSupportedLocale(record.numberLocale) ||
      record.unitSystem !== 'metric'
    ) {
      return null
    }
    return {
      locale: record.locale,
      timeZone: record.timeZone,
      dateFormat: record.dateFormat,
      numberLocale: record.numberLocale,
      unitSystem: 'metric',
    }
  } catch {
    return null
  }
}

export function serializeLocalePreferences(value: LocalePreferences): string {
  return JSON.stringify(value)
}

export function readLocalePreferences(
  storage: LocalePreferencesStorage,
): LocalePreferences | null {
  try {
    return parseLocalePreferences(storage.getItem(LOCALE_PREFERENCES_STORAGE_KEY))
  } catch {
    return null
  }
}

export function writeLocalePreferences(
  storage: LocalePreferencesStorage,
  value: LocalePreferences,
): boolean {
  try {
    storage.setItem(LOCALE_PREFERENCES_STORAGE_KEY, serializeLocalePreferences(value))
    return true
  } catch {
    return false
  }
}

export function createDefaultLocalePreferences(): LocalePreferences {
  return { ...DEFAULT_LOCALE_PREFERENCES, timeZone: defaultTimeZone() }
}
