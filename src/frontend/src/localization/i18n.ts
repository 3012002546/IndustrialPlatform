import { createI18n, type I18n } from 'vue-i18n'

import { enUS } from '@/locales/en-US'
import { zhCN } from '@/locales/zh-CN'
import type { PlatformLocaleMessages, SupportedLocale } from './types'

export const SUPPORTED_LOCALES: readonly SupportedLocale[] = ['zh-CN', 'en-US']

export const localeMessages: Record<SupportedLocale, PlatformLocaleMessages> = {
  'zh-CN': zhCN,
  'en-US': enUS,
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
