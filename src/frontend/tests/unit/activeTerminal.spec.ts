/**
 * 路由终端权威纯函数测试(PF-01 §7.11):
 * 显式路由终端优先;无显式路由时回退设备建议。
 */

import { describe, expect, it } from 'vitest'

import { resolveActiveTerminal } from '@/device/activeTerminal'

describe('resolveActiveTerminal', () => {
  it('显式路由终端优先于设备建议', () => {
    expect(resolveActiveTerminal('pda', 'pc')).toBe('pda')
    expect(resolveActiveTerminal('pda', 'mobile')).toBe('pda')
    expect(resolveActiveTerminal('mobile', 'pda')).toBe('mobile')
    expect(resolveActiveTerminal('pc', 'mobile')).toBe('pc')
  })

  it('无显式路由终端时回退设备建议', () => {
    expect(resolveActiveTerminal(undefined, 'pda')).toBe('pda')
    expect(resolveActiveTerminal(undefined, 'mobile')).toBe('mobile')
    expect(resolveActiveTerminal(undefined, 'pc')).toBe('pc')
  })

  it('设备缺省建议被显式路由覆盖(不依赖设备 Store 初始化)', () => {
    expect(resolveActiveTerminal('mobile', 'pc')).toBe('mobile')
    expect(resolveActiveTerminal('pda', 'pc')).toBe('pda')
  })
})
