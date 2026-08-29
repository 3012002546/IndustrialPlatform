import { computed } from 'vue'
import { getActivePinia } from 'pinia'

import { useLocalizationStore } from '@/stores/localizationStore'
import type { SupportedLocale } from './types'

/** Shared components remain mountable in isolation while using live app locale when available. */
export function usePlatformLocale() {
  const localization = getActivePinia() === undefined ? undefined : useLocalizationStore()
  return computed<SupportedLocale>(() => localization?.locale ?? 'zh-CN')
}
