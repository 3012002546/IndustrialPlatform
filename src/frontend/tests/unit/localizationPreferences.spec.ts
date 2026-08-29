import { describe, expect, it } from 'vitest'

import {
  DEFAULT_LOCALE_PREFERENCES,
  LOCALE_PREFERENCES_STORAGE_KEY,
  parseLocalePreferences,
  readLocalePreferences,
  serializeLocalePreferences,
  writeLocalePreferences,
  type LocalePreferencesStorage,
} from '@/localization/preferences'

function storageMock(initial: Record<string, string> = {}): LocalePreferencesStorage & {
  backing: Record<string, string>
} {
  const backing = { ...initial }
  return {
    backing,
    getItem: (key) => backing[key] ?? null,
    setItem: (key, value) => {
      backing[key] = value
    },
  }
}

describe('locale preferences', () => {
  it('keeps locale, timeZone, date format, number locale, and units independent', () => {
    const preferences = {
      locale: 'en-US' as const,
      timeZone: 'America/Los_Angeles',
      dateFormat: 'MM/dd/yyyy' as const,
      numberLocale: 'zh-CN' as const,
      unitSystem: 'metric' as const,
    }
    const storage = storageMock()
    expect(writeLocalePreferences(storage, preferences)).toBe(true)
    expect(storage.backing[LOCALE_PREFERENCES_STORAGE_KEY]).toBe(
      serializeLocalePreferences(preferences),
    )
    expect(readLocalePreferences(storage)).toEqual(preferences)
    expect(readLocalePreferences(storage)?.timeZone).toBe('America/Los_Angeles')
    expect(readLocalePreferences(storage)?.numberLocale).toBe('zh-CN')
  })

  it('rejects malformed or unsupported snapshots and safely falls back', () => {
    expect(parseLocalePreferences(null)).toBeNull()
    expect(parseLocalePreferences('{"locale":"fr-FR"}')).toBeNull()
    expect(parseLocalePreferences(JSON.stringify({ ...DEFAULT_LOCALE_PREFERENCES, unitSystem: 'imperial' }))).toBeNull()
    const storage = storageMock({ [LOCALE_PREFERENCES_STORAGE_KEY]: 'invalid-json' })
    expect(readLocalePreferences(storage)).toBeNull()
  })

  it('swallows storage write failures', () => {
    const storage: LocalePreferencesStorage = {
      getItem: () => null,
      setItem: () => {
        throw new Error('QuotaExceededError')
      },
    }
    expect(writeLocalePreferences(storage, DEFAULT_LOCALE_PREFERENCES)).toBe(false)
  })
})
