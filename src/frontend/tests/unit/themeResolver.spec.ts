/**
 * 系统模式解析与根节点外观应用测试(PF-01 §7.3)。
 */

import { describe, expect, it } from 'vitest'

import {
  ROOT_COLOR_MODE_ATTR,
  ROOT_DENSITY_ATTR,
  ROOT_MODE_ATTR,
  ROOT_PALETTE_ATTR,
  applyAppearanceToRoot,
  resolveEffectiveColorMode,
} from '@/theme'

describe('resolveEffectiveColorMode', () => {
  it('light 恒为 light,与系统偏好无关', () => {
    expect(resolveEffectiveColorMode('light', true)).toBe('light')
    expect(resolveEffectiveColorMode('light', false)).toBe('light')
  })

  it('dark 恒为 dark', () => {
    expect(resolveEffectiveColorMode('dark', true)).toBe('dark')
    expect(resolveEffectiveColorMode('dark', false)).toBe('dark')
  })

  it('system 跟随系统偏好', () => {
    expect(resolveEffectiveColorMode('system', true)).toBe('dark')
    expect(resolveEffectiveColorMode('system', false)).toBe('light')
  })
})

describe('applyAppearanceToRoot', () => {
  it('设置四个 data-ip-* 属性与 colorScheme', () => {
    const root = document.createElement('html')
    applyAppearanceToRoot(root, {
      palette: 'technology-blue',
      mode: 'system',
      effectiveColorMode: 'dark',
      density: 'compact',
    })
    expect(root.getAttribute(ROOT_PALETTE_ATTR)).toBe('technology-blue')
    expect(root.getAttribute(ROOT_MODE_ATTR)).toBe('system')
    expect(root.getAttribute(ROOT_COLOR_MODE_ATTR)).toBe('dark')
    expect(root.getAttribute(ROOT_DENSITY_ATTR)).toBe('compact')
    expect(root.style.colorScheme).toBe('dark')
  })

  it('覆盖已有属性值', () => {
    const root = document.createElement('html')
    applyAppearanceToRoot(root, {
      palette: 'industrial-cyan',
      mode: 'light',
      effectiveColorMode: 'light',
      density: 'comfortable',
    })
    expect(root.getAttribute(ROOT_PALETTE_ATTR)).toBe('industrial-cyan')
  })
})
