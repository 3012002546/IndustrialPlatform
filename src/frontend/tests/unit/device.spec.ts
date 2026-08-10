/**
 * 终端识别单元测试(§11.1 / §11.2):三档宽度、触控组合、四类覆盖值、优先级。
 */

import { describe, expect, it } from 'vitest'

import {
  TERMINAL_OVERRIDE_STORAGE_KEY,
  detectTerminal,
  parseTerminalOverride,
  readTerminalOverride,
  resolveTerminal,
  writeTerminalOverride,
} from '@/device'

describe('detectTerminal', () => {
  it('宽度 >=1200 一律为 PC(无论触控)', () => {
    expect(detectTerminal(1200, false)).toBe('pc')
    expect(detectTerminal(1440, true)).toBe('pc')
  })

  it('宽度 <768 一律为 Mobile(无论触控)', () => {
    expect(detectTerminal(767, false)).toBe('mobile')
    expect(detectTerminal(700, true)).toBe('mobile')
    expect(detectTerminal(0, false)).toBe('mobile')
  })

  it('768–1199 支持触控为 PDA', () => {
    expect(detectTerminal(768, true)).toBe('pda')
    expect(detectTerminal(900, true)).toBe('pda')
    expect(detectTerminal(1199, true)).toBe('pda')
  })

  it('768–1199 不支持触控为 PC', () => {
    expect(detectTerminal(768, false)).toBe('pc')
    expect(detectTerminal(1024, false)).toBe('pc')
    expect(detectTerminal(1199, false)).toBe('pc')
  })
})

describe('终端覆盖', () => {
  it('parseTerminalOverride 只接受四个合法值,其余按 auto', () => {
    expect(parseTerminalOverride('pc')).toBe('pc')
    expect(parseTerminalOverride('pda')).toBe('pda')
    expect(parseTerminalOverride('mobile')).toBe('mobile')
    expect(parseTerminalOverride('auto')).toBe('auto')
    expect(parseTerminalOverride('tablet')).toBe('auto')
    expect(parseTerminalOverride(null)).toBe('auto')
    expect(parseTerminalOverride('')).toBe('auto')
  })

  it('read/write 往返持久化', () => {
    const storage = {
      value: null as string | null,
      getItem: function (this: { value: string | null }) {
        return this.value
      },
      setItem: function (this: { value: string | null }, _key: string, raw: string) {
        this.value = raw
      },
    }
    writeTerminalOverride(storage, 'pda')
    expect(readTerminalOverride(storage)).toBe('pda')
    expect(storage.value).toBe('pda')
  })

  it('读取存储键为 industrial-platform.terminal.override.v1', () => {
    expect(TERMINAL_OVERRIDE_STORAGE_KEY).toBe('industrial-platform.terminal.override.v1')
  })

  it('resolveTerminal:auto 用自动识别,其余用显式覆盖', () => {
    expect(resolveTerminal('mobile', 'auto')).toBe('mobile')
    expect(resolveTerminal('mobile', 'pc')).toBe('pc')
    expect(resolveTerminal('mobile', 'pda')).toBe('pda')
    expect(resolveTerminal('pc', 'mobile')).toBe('mobile')
  })
})
