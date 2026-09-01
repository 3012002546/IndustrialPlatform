import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import AppDataTable from '@/components/management/AppDataTable.vue'

describe('AppDataTable initial header query mode', () => {
  beforeEach(() => {
    localStorage.clear()
    setActivePinia(createPinia())
  })

  it('renders actionable header filters on the first paint when restored in header mode', async () => {
    const wrapper = mount(AppDataTable, {
      props: {
        tableKey: 'initial-header-mode',
        routeKey: 'initial-header-mode',
        userKey: 'operator',
        queryMode: 'header',
        initialHeaderFilters: { name: 'alice' },
        rows: [{ id: '1', name: 'alice' }],
        columns: [{ field: 'name', title: '名称', filter: { kind: 'text' } }],
      },
    })

    await vi.waitFor(() => {
      expect(wrapper.find('[data-testid="app-data-table-header-filter-name"]').exists()).toBe(true)
    })

    const filter = wrapper.get('[data-testid="app-data-table-header-filter-name"]')
    expect(filter.isVisible()).toBe(true)
    expect((filter.element as HTMLInputElement).value).toBe('alice')
    expect(filter.attributes('tabindex')).toBe('0')
    expect(wrapper.find('.app-data-table__top-query').exists()).toBe(false)
  })

  it('does not depend on a fixed timer to attach restored header controls', async () => {
    const source = await import('@/components/management/AppDataTable.vue?raw')

    expect(source.default).not.toMatch(
      /setTimeout\(\(\) => \{[\s\S]*?syncNativeHeaderFilterRows\(\)/,
    )
  })
})
