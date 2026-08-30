import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

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

  it('does not send the synthetic action column and uses exact operators for select filters', async () => {
    const loader = vi.fn(async () => ({
      items: [],
      total: 0,
      pageIndex: 1,
      pageSize: 20,
    }))
    const wrapper = mount(AppDataTable, {
      props: {
        tableKey: 'query-contract',
        routeKey: 'query-contract',
        userKey: 'operator',
        rows: [],
        columns: [
          { field: 'loginName', title: '登录名', filter: { kind: 'text' as const } },
          {
            field: 'status',
            title: '状态',
            filter: { kind: 'select' as const, options: [{ label: '启用', value: 'Active' }] },
          },
        ],
        loader,
      },
      slots: { actions: '<button type="button">操作</button>' },
    })

    const vm = wrapper.vm as unknown as {
      switchQueryMode: (mode: 'top' | 'header') => void
      setHeaderFilter: (field: string, value: unknown) => void
      request: () => { columns: string[]; descriptor?: { filters: Array<{ field: string; operator: string; value: unknown }> } }
    }
    vm.switchQueryMode('header')
    vm.setHeaderFilter('status', 'Active')
    await flushPromises()

    const request = vm.request()
    expect(request.columns).toEqual(['loginName', 'status'])
    expect(request.columns).not.toContain('__actions')
    expect(request.descriptor?.filters).toContainEqual({
      field: 'status',
      operator: 'eq',
      value: 'Active',
    })
  })
})
