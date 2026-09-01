import { mount } from '@vue/test-utils'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

import PlatformCommandSearch from '@/components/shell/PlatformCommandSearch.vue'

describe('PlatformCommandSearch', () => {
  it('输入与 placeholder 使用顶栏主题,且窄屏允许父级收缩', () => {
    const source = readFileSync(
      resolve(process.cwd(), 'src/components/shell/PlatformCommandSearch.vue'),
      'utf8',
    )
    expect(source).toMatch(/\.ip-command-search\s*\{[\s\S]*?min-width:\s*0;/)
    expect(source).toMatch(
      /\.ip-command-search input\s*\{[\s\S]*?color:\s*var\(--ip-shell-topbar-text\);[\s\S]*?font-size:\s*12px;/,
    )
    expect(source).toMatch(
      /\.ip-command-search input::placeholder\s*\{[\s\S]*?color:\s*var\(--ip-shell-topbar-text-secondary\);[\s\S]*?opacity:\s*1;/,
    )
  })

  it('结果浮层使用 border-box,不会因内边距撑出视口', () => {
    const source = readFileSync(
      resolve(process.cwd(), 'src/components/shell/PlatformCommandSearch.vue'),
      'utf8',
    )
    expect(source).toMatch(
      /\.ip-command-search__results\s*\{[\s\S]*?box-sizing:\s*border-box;/,
    )
    expect(source).toContain('window.requestAnimationFrame')
    expect(source).toContain('schedulePositionResults')
  })

  it('快捷键提示不拦截输入框的鼠标/触摸事件', () => {
    const source = readFileSync(
      resolve(process.cwd(), 'src/components/shell/PlatformCommandSearch.vue'),
      'utf8',
    )
    expect(source).toMatch(/\.ip-command-search kbd\s*\{[\s\S]*?pointer-events:\s*none;/)
  })

  it('opens with Ctrl+K and only searches supplied authorized entries', async () => {
    const wrapper = mount(PlatformCommandSearch, {
      props: {
        items: [
          { id: 'users', label: '用户管理', kind: 'navigation' },
          { id: 'recent', label: '最近访问', kind: 'recent' },
        ],
      },
      attachTo: document.body,
    })
    await window.dispatchEvent(new KeyboardEvent('keydown', { key: 'k', ctrlKey: true }))
    expect(wrapper.get('input').attributes('aria-expanded')).toBe('true')
    expect(wrapper.get('input').element).toBe(document.activeElement)
    expect(wrapper.get('[data-testid="command-search-shortcut"]').text()).toBe('Ctrl+K')
    await wrapper.get('input').setValue('用户')
    expect(wrapper.text()).toContain('用户管理')
    expect(wrapper.text()).not.toContain('工单')
  })

  it('emits the selected authorized item and closes on Escape', async () => {
    const wrapper = mount(PlatformCommandSearch, {
      props: { items: [{ id: 'users', label: '用户管理', kind: 'navigation' }] },
    })
    await wrapper.get('input').trigger('focus')
    await wrapper.get('input').setValue('用户')
    await wrapper.get('[data-testid="command-search-result"]').trigger('click')
    expect(wrapper.emitted('select')?.[0]).toEqual(['users'])
    await wrapper.get('input').trigger('keydown', { key: 'Escape' })
    expect(wrapper.get('input').attributes('aria-expanded')).toBe('false')
  })

  it('reopens from a normal click after Escape closes the existing search', async () => {
    const wrapper = mount(PlatformCommandSearch, {
      props: { items: [{ id: 'users', label: '用户管理', kind: 'navigation' }] },
      attachTo: document.body,
    })

    await wrapper.get('input').trigger('click')
    await wrapper.get('input').trigger('keydown', { key: 'Escape' })
    expect(wrapper.get('input').attributes('aria-expanded')).toBe('false')

    await wrapper.get('input').trigger('click')
    expect(wrapper.get('input').attributes('aria-expanded')).toBe('true')
    expect(wrapper.get('input').element).toBe(document.activeElement)
  })

  it('deduplicates navigation and recent entries with the same stable id', async () => {
    const wrapper = mount(PlatformCommandSearch, {
      props: {
        items: [
          { id: 'identity-users', label: '用户管理', kind: 'navigation' },
          { id: 'identity-users', label: '用户管理', kind: 'recent' },
        ],
      },
    })

    await wrapper.get('input').trigger('focus')

    expect(wrapper.findAll('[data-testid="command-search-result"]')).toHaveLength(1)
  })
})
