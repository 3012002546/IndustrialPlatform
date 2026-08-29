import { describe, expect, it } from 'vitest'

import { createPlatformI18n, localeMessages } from '@/localization/i18n'
import { APP_INFO } from '@/app/appInfo'

function flattenKeys(value: unknown, prefix = ''): string[] {
  if (typeof value !== 'object' || value === null) return prefix ? [prefix] : []
  return Object.entries(value).flatMap(([key, child]) =>
    flattenKeys(child, prefix ? `${prefix}.${key}` : key),
  )
}

describe('platform localization resources', () => {
  it('keeps zh-CN and en-US resource keys structurally identical', () => {
    const zh = flattenKeys(localeMessages['zh-CN'])
    const en = flattenKeys(localeMessages['en-US'])
    expect(en).toEqual(zh)
    expect(new Set(zh).size).toBe(zh.length)
  })

  it('falls back to zh-CN for an unknown key without throwing', () => {
    const i18n = createPlatformI18n('en-US')
    const global = i18n.global as unknown as {
      t: (key: string) => string
      locale: { value: string }
    }
    expect(global.t('common.action.search')).toBe('Search')
    expect(global.t('common.missing.key')).toBe('common.missing.key')
    expect(global.locale.value).toBe('en-US')
  })

  it('keeps the product name stable and untranslated', () => {
    expect(localeMessages['zh-CN'].common.brand.name).toBe(APP_INFO.name)
    expect(localeMessages['en-US'].common.brand.name).toBe(APP_INFO.name)
  })
})
