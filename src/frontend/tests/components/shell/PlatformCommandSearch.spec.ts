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
    })
    await window.dispatchEvent(new KeyboardEvent('keydown', { key: 'k', ctrlKey: true }))
    expect(wrapper.get('input').attributes('aria-expanded')).toBe('true')
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
})
