import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'

import PlatformCommandSearch from '@/components/shell/PlatformCommandSearch.vue'

describe('PlatformCommandSearch', () => {
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
