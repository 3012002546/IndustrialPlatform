/**
 * 设备 Store(§11):管理生效终端、自动识别建议与手动覆盖。
 * 视口变化只更新建议终端,不强制中断当前操作并跳转(§11.2)。
 */

import { defineStore } from 'pinia'
import { ref } from 'vue'

import {
  detectTerminal,
  getViewportInfo,
  readTerminalOverride,
  resolveTerminal,
  writeTerminalOverride,
} from '@/device'
import type { TerminalOverride, TerminalType } from '@/device/types'

/** 覆盖存储(localStorage,跨会话持久)。 */
function defaultStorage(): Storage {
  return globalThis.localStorage
}

export const useDeviceStore = defineStore('device', () => {
  /** 自动识别建议终端(随视口变化)。 */
  const suggested = ref<TerminalType>('pc')
  /** 手动覆盖值(auto = 使用自动识别)。 */
  const override = ref<TerminalOverride>('auto')
  /** 生效终端 = resolve(suggested, override)。 */
  const terminal = ref<TerminalType>('pc')
  /** 是否已初始化(守卫首次导航时惰性初始化)。 */
  const ready = ref(false)

  function applyViewport(info: { width: number; hasTouch: boolean }): void {
    suggested.value = detectTerminal(info.width, info.hasTouch)
    terminal.value = resolveTerminal(suggested.value, override.value)
  }

  /** 初始化:读取持久覆盖并应用当前视口。 */
  function init(): void {
    override.value = readTerminalOverride(defaultStorage())
    applyViewport(getViewportInfo())
    ready.value = true
  }

  /** 视口变化回调:只更新建议与生效终端,不触发任何导航。 */
  function updateViewport(): void {
    applyViewport(getViewportInfo())
  }

  /** 设置覆盖:写存储并立即重算生效终端。 */
  function setOverride(value: TerminalOverride): void {
    override.value = value
    writeTerminalOverride(defaultStorage(), value)
    terminal.value = resolveTerminal(suggested.value, value)
  }

  return { suggested, override, terminal, ready, init, updateViewport, setOverride }
})
