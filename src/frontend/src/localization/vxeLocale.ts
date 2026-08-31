import { VxeUI } from 'vxe-table'
import vxeEnUS from 'vxe-table/lib/locale/lang/en-US'
import vxeZhCN from 'vxe-table/lib/locale/lang/zh-CN'

import type { SupportedLocale } from './types'

let installed = false

function installVxeLocales(): void {
  if (installed) return
  VxeUI.setI18n('zh-CN', vxeZhCN)
  VxeUI.setI18n('en-US', vxeEnUS)
  installed = true
}

export function setVxeLocale(locale: SupportedLocale): void {
  installVxeLocales()
  VxeUI.setLanguage(locale)
}
