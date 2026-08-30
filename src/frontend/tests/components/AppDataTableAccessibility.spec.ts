import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it } from 'vitest'

import AppDataTable from '@/components/management/AppDataTable.vue'
import { markVxeElementDecorative } from '@/components/management/appDataTable/vxeDomAdapter'

describe('AppDataTable accessibility semantics', () => {
  beforeEach(() => {
    localStorage.clear()
    setActivePinia(createPinia())
  })

  it('keeps the main header filter test target unique', async () => {
    const wrapper = mount(AppDataTable, {
      props: {
        tableKey: 'accessibility-users',
        routeKey: 'accessibility-users',
        userKey: 'operator',
        rows: [{ id: '1', loginName: 'e2e.admin', name: 'Admin' }],
        columns: [
          { field: 'loginName', title: '登录名', filter: { kind: 'text' } },
          { field: 'name', title: '姓名', fixed: 'right' },
        ],
      },
      slots: { actions: '<button type="button">详情</button>' },
    })

    await wrapper.get('[data-testid="app-data-table-query-toggle"]').trigger('click')
    await flushPromises()

    const filters = wrapper.findAll('[data-testid="app-data-table-header-filter-loginName"]')
    expect(filters).toHaveLength(1)
    expect(filters[0]?.element.closest('.vxe-table--main-wrapper')).not.toBeNull()
  })

  it('marks a duplicate VXE subtree as hidden and unfocusable while keeping its markup', () => {
    const duplicate = document.createElement('div')
    duplicate.innerHTML = '<span>e2e.admin</span><button>详情</button><input />'

    markVxeElementDecorative(duplicate)

    expect(duplicate.getAttribute('aria-hidden')).toBe('true')
    expect(duplicate.hasAttribute('inert')).toBe(true)
    expect(duplicate.textContent).toContain('e2e.admin')
    expect(
      Array.from(duplicate.querySelectorAll<HTMLElement>('button, input')).every(
        (element) => element.getAttribute('tabindex') === '-1',
      ),
    ).toBe(true)
  })
})
