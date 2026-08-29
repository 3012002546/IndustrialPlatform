import { createI18n, type I18n } from 'vue-i18n'

import { enUS } from '@/locales/en-US'
import { zhCN } from '@/locales/zh-CN'
import type { PlatformLocaleMessages, SupportedLocale } from './types'

export const SUPPORTED_LOCALES: readonly SupportedLocale[] = ['zh-CN', 'en-US']

export const localeMessages: Record<SupportedLocale, PlatformLocaleMessages> = {
  'zh-CN': zhCN,
  'en-US': enUS,
}

export function resolveLocaleMessage(
  locale: SupportedLocale,
  key: string | undefined,
  fallback: string,
): string {
  if (key === undefined || key.length === 0) return fallback
  let value: unknown = localeMessages[locale]
  for (const part of key.split('.')) {
    if (typeof value !== 'object' || value === null || !(part in value)) return fallback
    value = (value as Record<string, unknown>)[part]
  }
  return typeof value === 'string' ? value : fallback
}

export function createPlatformI18n(locale: SupportedLocale = 'zh-CN'): I18n {
  return createI18n({
    legacy: false,
    locale,
    fallbackLocale: 'zh-CN',
    messages: localeMessages as never,
    missingWarn: false,
    fallbackWarn: false,
  })
}

export const platformI18n = createPlatformI18n()

export { type PlatformLocaleMessages, type SupportedLocale } from './types'
