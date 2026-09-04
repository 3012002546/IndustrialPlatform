import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ElPagination } from 'element-plus'
import { VxeColumn, VxeTable, VxeToolbar } from 'vxe-table'
import { h } from 'vue'

import AppDataTable from '@/components/management/AppDataTable.vue'
import { useLocalizationStore } from '@/stores/localizationStore'

describe('AppDataTable', () => {
  beforeEach(() => {
    localStorage.clear()
    setActivePinia(createPinia())
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('switches query modes by clearing the inactive mode and sends filters to the server loader', async () => {
    const loader = vi.fn(async () => ({
      items: [{ id: '1', name: 'one' }],
      total: 1,
      pageIndex: 1,
      pageSize: 20,
    }))
    const wrapper = mount(AppDataTable, {
      props: {
        tableKey: 'users',
        routeKey: 'identity-users',
        userKey: 'operator',
        rows: [],
        total: 0,
        columns: [{ field: 'name', title: '名称', filter: { kind: 'text' } }],
        queryMode: 'top',
        loader,
      },
    })

    await wrapper.get('[data-testid="app-data-table-query-toggle"]').trigger('click')
    await flushPromises()
    await wrapper.get('[data-testid="app-data-table-header-filter-name"]').setValue('alice')
    await wrapper.get('[data-testid="app-data-table-header-filter-name"]').trigger('keyup.enter')
    await flushPromises()

    expect(loader).toHaveBeenLastCalledWith(
      expect.objectContaining({
        queryMode: 'header',
        filters: { name: 'alice' },
      }),
    )
    await wrapper.get('[data-testid="app-data-table-query-toggle"]').trigger('click')
    await flushPromises()
    expect((wrapper.vm as { headerFilters: Record<string, unknown> }).headerFilters).toEqual({})
  })

  it('shows only the active query slot and clears its inactive values when switching modes', async () => {
    const wrapper = mount(AppDataTable, {
      props: {
        tableKey: 'query-mode',
        routeKey: 'query-mode',
        userKey: 'operator',
        rows: [{ id: '1', name: 'one' }],
        columns: [{ field: 'name', title: '名称', filter: { kind: 'text' as const } }],
      },
      slots: { toolbar: '<div data-testid="top-query-slot">顶部查询</div>' },
    })

    expect(wrapper.find('[data-testid="top-query-slot"]').exists()).toBe(true)
    ;(wrapper.vm as { setTopQuery: (query: Record<string, unknown>) => void }).setTopQuery({
      name: 'alice',
    })
    await wrapper.get('[data-testid="app-data-table-query-toggle"]').trigger('click')
    await flushPromises()
    expect(wrapper.find('[data-testid="top-query-slot"]').exists()).toBe(false)
    expect((wrapper.vm as { topQuery: Record<string, unknown> }).topQuery).toEqual({})
    expect(wrapper.emitted('query-mode-change')).toEqual([['header']])

    ;(wrapper.vm as { setHeaderFilter: (field: string, value: unknown) => void }).setHeaderFilter(
      'name',
      'bob',
    )
    await wrapper.get('[data-testid="app-data-table-query-toggle"]').trigger('click')
    expect(wrapper.find('[data-testid="top-query-slot"]').exists()).toBe(true)
    expect((wrapper.vm as { headerFilters: Record<string, unknown> }).headerFilters).toEqual({})
  })

  it('ends loading after a successful empty server response', async () => {
    const loader = vi.fn(async () => ({
      items: [],
      total: 0,
      pageIndex: 1,
      pageSize: 20,
    }))
    const wrapper = mount(AppDataTable, {
      props: {
        tableKey: 'empty-server',
        routeKey: 'empty-server',
        userKey: 'operator',
        rows: [],
        columns: [{ field: 'name', title: '名称' }],
        loader,
      },
    })

    await wrapper.get('[data-testid="app-data-table-refresh"]').trigger('click')
    await flushPromises()

    expect(loader).toHaveBeenCalledTimes(1)
    expect(wrapper.findComponent(VxeTable).vm.$attrs.loading).toBeUndefined()
  })

  it('passes localized empty text and header overflow behavior to the shared VXE table', async () => {
    useLocalizationStore().setLocale('en-US', null)
    const wrapper = mount(AppDataTable, {
      props: {
        tableKey: 'localized-empty',
        rows: [],
        columns: [{ field: 'mustChangePassword', title: 'Password change required', width: 80 }],
      },
    })

    const table = wrapper.findComponent(VxeTable)
    const tableProps = (table.vm as unknown as { $props: Record<string, unknown> }).$props
    expect(tableProps.emptyText).toBe('No data')
    expect(tableProps.showHeaderOverflow).toBe('title')

    useLocalizationStore().setLocale('zh-CN', null)
    await wrapper.vm.$nextTick()
    expect(
      (wrapper.findComponent(VxeTable).vm as unknown as { $props: Record<string, unknown> }).$props
        .emptyText,
    ).toBe('暂无数据')
  })

  it('uses native VXE tree expansion and keeps ancestors when searching descendants', async () => {
    const wrapper = mount(AppDataTable, {
      props: {
        tableKey: 'navigation-tree',
        mode: 'tree',
        rowKey: 'id',
        tree: { childrenField: 'children' },
        rows: [
          {
            id: 'root',
            label: 'Root',
            children: [{ id: 'child', label: 'Target', children: [] }],
          },
        ],
        columns: [
          { field: 'label', title: '名称' },
          { field: 'id', title: '标识' },
        ],
      },
    })

    const columns = wrapper.findAllComponents(VxeColumn)
    expect(columns[0]?.props('treeNode')).toBe(true)
    expect(columns[1]?.props('treeNode')).toBe(false)
    expect(wrapper.find('[data-testid="app-data-table-tree-expand-all"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="app-data-table-tree-collapse-all"]').exists()).toBe(true)

    await wrapper.get('[data-testid="app-data-table-quick-search"]').setValue('Target')
    await flushPromises()
    const data = wrapper.findComponent(VxeTable).props('data') as Array<Record<string, unknown>>
    expect(data).toHaveLength(1)
    expect(data[0]?.id).toBe('root')
    expect(data[0]?.children).toEqual([{ id: 'child', label: 'Target', children: [] }])
  })

  it('keeps the platform header height at the approved 38px despite VXE inline sizing', async () => {
    const source = await import('@/components/management/AppDataTable.vue?raw')
    expect(source.default).toMatch(
      /\.vxe-table--header-wrapper \.vxe-header--column[\s\S]*height:\s*38px !important/,
    )
  })

  it('uses a platform loading overlay without passing loading to VxeTable', () => {
    const wrapper = mount(AppDataTable, {
      props: {
        tableKey: 'platform-loading',
        rows: [{ id: '1', name: 'one' }],
        loading: true,
        columns: [{ field: 'name', title: '名称' }],
      },
    })

    expect(wrapper.findComponent(VxeTable).vm.$attrs.loading).toBeUndefined()
    expect(wrapper.get('[data-testid="app-data-table-loading"]').attributes('role')).toBe('status')
  })

  it('ends loading and emits an error after a failed server response', async () => {
    const loader = vi.fn(async () => {
      throw new Error('load failed')
    })
    const wrapper = mount(AppDataTable, {
      props: {
        tableKey: 'failed-server',
        routeKey: 'failed-server',
        userKey: 'operator',
        rows: [],
        columns: [{ field: 'name', title: '名称' }],
        loader,
      },
    })

    await wrapper.get('[data-testid="app-data-table-refresh"]').trigger('click')
    await flushPromises()

    expect(wrapper.findComponent(VxeTable).vm.$attrs.loading).toBeUndefined()
    expect(wrapper.emitted('load-error')?.[0]?.[0]).toBeInstanceOf(Error)
  })

  it('shows a retryable error surface after a loader failure and clears it after retry', async () => {
    const loader = vi
      .fn()
      .mockRejectedValueOnce(new Error('load failed'))
      .mockResolvedValueOnce({ items: [], total: 0, pageIndex: 1, pageSize: 25 })
    const wrapper = mount(AppDataTable, {
      props: {
        tableKey: 'retryable-error',
        routeKey: 'retryable-error',
        userKey: 'operator',
        rows: [],
        columns: [{ field: 'name', title: '名称' }],
        loader,
      },
    })

    await wrapper.get('[data-testid="app-data-table-refresh"]').trigger('click')
    await flushPromises()

    expect(wrapper.get('[data-testid="app-data-table-load-error"]').attributes('role')).toBe(
      'alert',
    )
    expect(wrapper.get('[data-testid="app-data-table-load-error"]').text()).toContain('加载失败')
    expect(wrapper.find('[data-testid="app-data-table-load-retry"]').exists()).toBe(true)

    await wrapper.get('[data-testid="app-data-table-load-retry"]').trigger('click')
    await flushPromises()

    expect(loader).toHaveBeenCalledTimes(2)
    expect(wrapper.find('[data-testid="app-data-table-load-error"]').exists()).toBe(false)
  })

  it('syncs the internal pager when a parent resets initialPageIndex without reloading', async () => {
    const loader = vi.fn(async (request: { pageIndex: number; pageSize: number }) => ({
      items: [{ id: String(request.pageIndex), name: `page-${request.pageIndex}` }],
      total: 50,
      pageIndex: request.pageIndex,
      pageSize: request.pageSize,
    }))
    const wrapper = mount(AppDataTable, {
      props: {
        tableKey: 'controlled-page-reset',
        routeKey: 'controlled-page-reset',
        userKey: 'operator',
        rows: [],
        columns: [{ field: 'name', title: '名称' }],
        initialPageIndex: 1,
        loader,
      },
      global: { components: { 'el-pagination': ElPagination } },
    })
    const pagination = wrapper.findComponent(ElPagination)

    await pagination.vm.$emit('current-change', 2)
    await flushPromises()
    await wrapper.setProps({ initialPageIndex: 2 })
    await flushPromises()
    const callsAfterPageChange = loader.mock.calls.length
    expect(pagination.props('currentPage')).toBe(2)

    await wrapper.setProps({ initialPageIndex: 1 })
    await flushPromises()

    expect(pagination.props('currentPage')).toBe(1)
    expect(loader).toHaveBeenCalledTimes(callsAfterPageChange)
  })

  it('provides one grouped export menu and keeps Excel export server-scoped', async () => {
    const exporter = vi.fn(async () => undefined)
    vi.stubGlobal(
      'confirm',
      vi.fn(() => true),
    )
    const wrapper = mount(AppDataTable, {
      props: {
        tableKey: 'users',
        routeKey: 'identity-users',
        userKey: 'operator',
        rows: [{ id: '1', name: 'one' }],
        total: 1,
        columns: [{ field: 'name', title: '名称' }],
        exporter,
      },
    })

    await wrapper.get('[data-testid="app-data-table-column-settings"]').trigger('click')
    await flushPromises()
    expect(wrapper.find('.vxe-table-custom-wrapper').exists()).toBe(true)
    await wrapper.get('[data-testid="app-data-table-table-settings"]').trigger('click')
    await flushPromises()
    await wrapper.get('[data-testid="app-data-table-density-compact"]').trigger('click')
    await wrapper.get('[data-testid="app-data-table-export"]').trigger('click')
    const exportMenu = wrapper.get('[data-testid="app-data-table-export-menu"]')
    expect(exportMenu.text()).toContain('CSV')
    expect(wrapper.get('[data-testid="app-data-table-export-menu"]').text()).toContain(
      '当前页（已加载数据）',
    )
    expect(exportMenu.find('.app-data-table__export-formats').exists()).toBe(false)
    expect(
      exportMenu
        .get('[data-testid="app-data-table-export-type"]')
        .findAll('option')
        .map((option) => option.element.getAttribute('value')),
    ).toEqual(['xlsx', 'csv', 'html', 'xml', 'txt'])
    await wrapper.get('[data-testid="app-data-table-export-scope"]').setValue('custom')
    expect(wrapper.find('[data-testid="app-data-table-export-custom-quantity"]').exists()).toBe(
      true,
    )
    await wrapper.get('[data-testid="app-data-table-export-custom-quantity"]').setValue('321')
    await wrapper.get('[data-testid="app-data-table-export-confirm"]').trigger('click')
    expect(exporter).toHaveBeenLastCalledWith(
      expect.objectContaining({ quantity: 321, filename: 'users' }),
    )
    await wrapper.get('[data-testid="app-data-table-export"]').trigger('click')
    await wrapper.get('[data-testid="app-data-table-export-scope"]').setValue('all')
    await wrapper.get('[data-testid="app-data-table-export-confirm"]').trigger('click')
    await flushPromises()

    expect(exporter).toHaveBeenCalledWith(
      expect.objectContaining({ quantity: 'all', rows: undefined }),
    )
    expect(wrapper.find('[data-testid="app-data-table-export-confirm"]').exists()).toBe(false)
    expect(
      localStorage.getItem(
        'industrial-platform.table-preferences.v1:operator:identity-users:users',
      ),
    ).toContain('compact')
  })

  it('filters, sorts and paginates a complete local dataset and omits unavailable Excel', async () => {
    const wrapper = mount(AppDataTable, {
      props: {
        tableKey: 'local-users',
        routeKey: 'local-users',
        userKey: 'operator',
        rows: [
          { id: '1', name: 'Charlie' },
          { id: '2', name: 'Alice' },
          { id: '3', name: 'Bob' },
        ],
        columns: [
          {
            field: 'name',
            title: '名称',
            sortable: true,
            filter: { kind: 'text' },
          },
        ],
        pageSize: 2,
      },
    })

    expect(wrapper.findComponent(VxeTable).props('data')).toHaveLength(2)
    await wrapper.get('[data-testid="app-data-table-export"]').trigger('click')
    const exportType = wrapper.get('[data-testid="app-data-table-export-type"]')
    expect(exportType.find('option[value="xlsx"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="app-data-table-export-confirm"]').exists()).toBe(true)
    await wrapper.get('[data-testid="app-data-table-query-toggle"]').trigger('click')
    await flushPromises()
    await wrapper.get('[data-testid="app-data-table-header-filter-name"]').setValue('ali')
    await wrapper.get('[data-testid="app-data-table-header-filter-name"]').trigger('keyup.enter')
    await flushPromises()

    expect(wrapper.findComponent(VxeTable).props('data')).toEqual([{ id: '2', name: 'Alice' }])
    expect(wrapper.findComponent(VxeTable).props('data')).not.toContainEqual({
      id: '1',
      name: 'Charlie',
    })
  })

  it('matches select filters exactly in local mode', async () => {
    const wrapper = mount(AppDataTable, {
      props: {
        tableKey: 'local-status',
        routeKey: 'local-status',
        userKey: 'operator',
        rows: [
          { id: '1', status: 'Active' },
          { id: '2', status: 'Inactive' },
          { id: '3', status: 'Disabled' },
        ],
        columns: [
          {
            field: 'status',
            title: '状态',
            filter: {
              kind: 'select' as const,
              options: [{ label: '启用', value: 'Active' }],
            },
          },
        ],
        pageSize: 10,
      },
    })

    await wrapper.get('[data-testid="app-data-table-query-toggle"]').trigger('click')
    await flushPromises()
    await wrapper.get('[data-testid="app-data-table-header-filter-status"]').setValue('Active')
    await flushPromises()

    expect(wrapper.findComponent(VxeTable).props('data')).toEqual([{ id: '1', status: 'Active' }])
  })

  it('renders header filters inside the vxe header and persists resized widths', async () => {
    const wrapper = mount(AppDataTable, {
      props: {
        tableKey: 'header-layout',
        routeKey: 'header-layout',
        userKey: 'operator',
        rows: [{ id: '1', name: 'one' }],
        columns: [
          { field: 'name', title: '名称', width: 160 },
          { field: 'internal', title: '内部', filter: false },
        ],
      },
    })

    await wrapper.get('[data-testid="app-data-table-query-toggle"]').trigger('click')
    await flushPromises()
    const filterRow = wrapper.get('tr.app-data-table__header-filter-row')
    expect(filterRow.element.parentElement?.tagName).toBe('THEAD')
    expect(filterRow.find('[data-testid="app-data-table-header-filter-name"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="app-data-table-header-filter-internal"]').exists()).toBe(
      false,
    )
    expect(wrapper.find('.app-data-table__header-filters').exists()).toBe(false)

    wrapper.findComponent(VxeTable).vm.$emit('resizable-change', {
      resizeColumn: { field: 'name' },
      resizeWidth: 260,
    })
    expect(
      localStorage.getItem(
        'industrial-platform.table-preferences.v1:operator:header-layout:header-layout',
      ),
    ).toContain('"name":260')
  })

  it('resets persisted column widths and immediately restores declared defaults', async () => {
    const wrapper = mount(AppDataTable, {
      props: {
        tableKey: 'reset-widths',
        routeKey: 'reset-widths',
        userKey: 'operator',
        rows: [{ id: '1', name: 'one' }],
        columns: [{ field: 'name', title: '名称', width: 160 }],
      },
    })
    const table = wrapper.findComponent(VxeTable)

    table.vm.$emit('resizable-change', {
      resizeColumn: { field: 'name' },
      resizeWidth: 260,
    })
    await flushPromises()
    expect(
      wrapper
        .findAllComponents(VxeColumn)
        .find((column) => column.props('field') === 'name')
        ?.props('width'),
    ).toBe(260)

    await wrapper.get('[data-testid="app-data-table-column-settings"]').trigger('click')
    await flushPromises()
    await wrapper.get('[data-testid="app-data-table-native-reset-widths"]').trigger('click')
    await flushPromises()

    expect(
      wrapper
        .findAllComponents(VxeColumn)
        .find((column) => column.props('field') === 'name')
        ?.props('width'),
    ).toBe(160)
    expect(
      localStorage.getItem(
        'industrial-platform.table-preferences.v1:operator:reset-widths:reset-widths',
      ),
    ).not.toContain('"name":260')
  })

  it('integrates business actions into the table card above the table tools', async () => {
    const wrapper = mount(AppDataTable, {
      props: {
        tableKey: 'actions',
        routeKey: 'actions',
        userKey: 'operator',
        rows: [{ id: '1', name: 'one' }],
        columns: [{ field: 'name', title: '名称' }],
      },
      slots: {
        'toolbar-actions': '<button data-testid="business-create">新增</button>',
      },
    })

    const actions = wrapper.get('[data-testid="business-create"]').element.parentElement
    expect(actions?.className).toContain('app-data-table__business-actions')
    expect(actions?.parentElement?.className).toContain('app-data-table__card')
    expect(
      wrapper.get('.app-data-table__card').find('[data-testid="business-create"]').exists(),
    ).toBe(true)
    expect(wrapper.find('.app-data-table__business-actions-card').exists()).toBe(false)
    expect(actions?.nextElementSibling?.className).toContain('app-data-table__toolbar')
    await wrapper.get('[data-testid="app-data-table-fullscreen"]').trigger('click')
    expect(wrapper.get('[data-testid="app-data-table"]').classes()).toContain(
      'app-data-table--fullscreen',
    )
  })

  it('keeps the complete table surface inside one card and uses icon-only tool buttons', async () => {
    const wrapper = mount(AppDataTable, {
      props: {
        tableKey: 'card-surface',
        routeKey: 'card-surface',
        userKey: 'operator',
        rows: [{ id: '1', name: 'one' }],
        columns: [{ field: 'name', title: '名称' }],
      },
    })

    const card = wrapper.get('.app-data-table__card')
    expect(card.find('.app-data-table__toolbar').exists()).toBe(true)
    expect(card.findComponent(VxeTable).exists()).toBe(true)
    expect(card.find('.app-data-table__footer').exists()).toBe(true)
    await wrapper.get('[data-testid="app-data-table-table-settings"]').trigger('click')
    await flushPromises()
    expect(wrapper.get('.app-data-table__card').find('.app-data-table__settings').exists()).toBe(
      true,
    )
    for (const testId of [
      'app-data-table-query-toggle',
      'app-data-table-clear',
      'app-data-table-refresh',
      'app-data-table-fullscreen',
      'app-data-table-column-settings',
      'app-data-table-export',
    ]) {
      expect(wrapper.get(`[data-testid="${testId}"]`).text()).toBe('')
    }
  })

  it('exposes component-level style hooks for tool icons and business actions', () => {
    const wrapper = mount(AppDataTable, {
      props: {
        tableKey: 'style-contract',
        rows: [{ id: '1', name: 'one' }],
        columns: [{ field: 'name', title: '名称' }],
      },
      slots: {
        'toolbar-actions': '<button data-testid="business-create">新增</button>',
      },
    })

    expect(wrapper.get('.app-data-table__toolbar-actions').classes()).toContain(
      'app-data-table__toolbar-actions--styled',
    )
    for (const testId of [
      'app-data-table-query-toggle',
      'app-data-table-clear',
      'app-data-table-refresh',
      'app-data-table-fullscreen',
      'app-data-table-column-settings',
      'app-data-table-export',
    ]) {
      expect(wrapper.get(`[data-testid="${testId}"]`).classes()).toContain(
        'app-data-table__icon-button',
      )
      expect(wrapper.get(`[data-testid="${testId}"]`).find('svg').exists()).toBe(true)
    }
    expect(
      wrapper
        .find('[data-testid="app-data-table-query-toggle"] .app-data-table__query-filter-icon')
        .exists(),
    ).toBe(true)
  })

  it('uses the native vxe custom toolbar and keeps only platform settings outside it', async () => {
    const wrapper = mount(AppDataTable, {
      props: {
        tableKey: 'native-custom',
        routeKey: 'native-custom',
        userKey: 'operator',
        rows: [{ id: '1', name: 'one' }],
        columns: [
          { field: 'name', title: '名称', fixed: 'left' },
          { field: 'status', title: '状态' },
        ],
      },
    })

    const toolbar = wrapper.findComponent(VxeToolbar)
    expect(toolbar.exists()).toBe(true)
    expect(toolbar.props('custom')).toBe(true)
    const table = wrapper.findComponent(VxeTable)
    expect(table.props('id')).toContain('native-custom')
    expect(table.props('customConfig')).toEqual(
      expect.objectContaining({
        storage: true,
        allowVisible: true,
        allowSort: true,
        allowFixed: true,
        allowResizable: true,
      }),
    )

    await wrapper.get('[data-testid="app-data-table-column-settings"]').trigger('click')
    expect(wrapper.find('[data-testid="app-data-table-settings"]').exists()).toBe(false)
    expect(wrapper.find('.vxe-table-custom-wrapper').exists()).toBe(true)
    await wrapper.get('[data-testid="app-data-table-table-settings"]').trigger('click')
    await flushPromises()
    await new Promise((resolve) => setTimeout(resolve, 350))
    const settings = wrapper.get('[data-testid="app-data-table-settings"]')
    expect(settings.text()).toContain('序号')
    expect(settings.text()).toContain('边框')
    expect(settings.text()).toContain('紧凑')
    const customPanel = wrapper.find('.vxe-table-custom-wrapper')
    expect(customPanel.exists()).toBe(true)
    expect(customPanel.classes()).not.toContain('is--active')
    expect(window.getComputedStyle(customPanel.element).display).toBe('none')
  })

  it('keeps one compact column list in a taller native settings panel', async () => {
    const wrapper = mount(AppDataTable, {
      props: {
        tableKey: 'column-settings-viewport',
        rows: [{ id: '1', name: 'one' }],
        columns: Array.from({ length: 12 }, (_, index) => ({
          field: `field-${index}`,
          title: `字段${index + 1}`,
        })),
      },
    })

    await wrapper.get('[data-testid="app-data-table-column-settings"]').trigger('click')
    await flushPromises()

    const body = wrapper.get('.vxe-table-custom--body')
    const customPanel = wrapper.get('.vxe-table-custom-wrapper')
    expect(customPanel.attributes('data-app-data-table-column-layout')).toBe('single')
    expect(window.getComputedStyle(customPanel.element).width).toContain('420px')
    expect((customPanel.element as HTMLElement).style.height).toContain('550px')
    expect((customPanel.element as HTMLElement).style.height).toContain('240px')
    expect((body.element as HTMLElement).style.maxHeight).toBe('none')
    expect((body.element as HTMLElement).style.flexGrow).toBe('1')
    expect(window.getComputedStyle(body.element).overflowY).toBe('auto')
    expect(customPanel.attributes('data-font-size')).toBe('12px')
  })

  it('anchors native column settings to the trigger and flips within the viewport', async () => {
    const wrapper = mount(AppDataTable, {
      props: {
        tableKey: 'column-settings-anchor',
        rows: [{ id: '1', name: 'one' }],
        columns: Array.from({ length: 12 }, (_, index) => ({
          field: `field-${index}`,
          title: `字段${index + 1}`,
          sortable: true,
        })),
      },
    })
    const trigger = wrapper.get('[data-testid="app-data-table-column-settings"]')
    const triggerRect = {
      left: 400,
      right: 760,
      top: 60,
      bottom: 100,
      width: 360,
      height: 40,
      x: 400,
      y: 60,
      toJSON: () => ({}),
    }
    Object.defineProperty(trigger.element, 'getBoundingClientRect', {
      configurable: true,
      value: () => triggerRect,
    })

    await trigger.trigger('click')
    await flushPromises()
    const customPanel = wrapper.get('.vxe-table-custom-wrapper')
    const customPanelElement = customPanel.element as HTMLElement
    Object.defineProperty(customPanelElement, 'getBoundingClientRect', {
      configurable: true,
      value: () => ({
        ...triggerRect,
        width: 420,
        height: 360,
        right: 820,
        bottom: 420,
      }),
    })
    window.dispatchEvent(new Event('resize'))
    await flushPromises()
    expect(customPanel.attributes('data-app-data-table-placement')).toBe('bottom')
    expect(customPanelElement.style.position).toBe('fixed')
    expect(customPanelElement.style.top).toBe('108px')
    expect(customPanelElement.style.left).toBe('340px')

    Object.assign(triggerRect, { top: 600, bottom: 640 })
    window.dispatchEvent(new Event('resize'))
    await flushPromises()
    expect(customPanel.attributes('data-app-data-table-placement')).toBe('top')
    expect(customPanelElement.style.top).toBe('232px')
    expect(customPanelElement.style.bottom).toBe('auto')
  })

  it('keeps tall sort and group panels inside the viewport near the page bottom', async () => {
    const wrapper = mount(AppDataTable, {
      props: {
        tableKey: 'toolbar-panel-viewport',
        rows: [{ id: '1', name: 'one' }],
        columns: [{ field: 'name', title: '名称', sortable: true }],
      },
    })
    const triggerRect = {
      left: 100,
      right: 136,
      top: 400,
      bottom: 440,
      width: 36,
      height: 40,
      x: 100,
      y: 400,
      toJSON: () => ({}),
    }

    for (const [triggerId, panelId] of [
      ['app-data-table-sort', 'app-data-table-sort-panel'],
      ['app-data-table-group', 'app-data-table-group-panel'],
    ]) {
      const trigger = wrapper.get(`[data-testid="${triggerId}"]`)
      Object.defineProperty(trigger.element, 'getBoundingClientRect', {
        configurable: true,
        value: () => triggerRect,
      })
      await trigger.trigger('click')
      await flushPromises()
      const panel = wrapper.get(`[data-testid="${panelId}"]`)
      const panelElement = panel.element as HTMLElement
      Object.defineProperty(panelElement, 'getBoundingClientRect', {
        configurable: true,
        value: () => ({ ...triggerRect, width: 236, height: 480, right: 336, bottom: 880 }),
      })
      window.dispatchEvent(new Event('resize'))
      await flushPromises()
      expect(panelElement.style.position).toBe('fixed')
      expect(panelElement.style.top).toBe('276px')
      expect(panelElement.style.left).toBe('100px')
    }
  })

  it('keeps every native column row draggable, visible and fixed-capable in the grid', async () => {
    const wrapper = mount(AppDataTable, {
      props: {
        tableKey: 'column-settings-controls',
        rows: [{ id: '1', name: 'one' }],
        columns: Array.from({ length: 12 }, (_, index) => ({
          field: `field-${index}`,
          title: `字段${index + 1}`,
          sortable: true,
        })),
      },
    })

    await wrapper.get('[data-testid="app-data-table-column-settings"]').trigger('click')
    await flushPromises()
    const options = wrapper.find('.vxe-table-custom--body').findAll('.vxe-table-custom--option')
    expect(options).toHaveLength(12)
    options.forEach((option) => {
      expect(option.find('.vxe-table-custom--checkbox-option').exists()).toBe(true)
      expect(option.find('.vxe-table-custom--name-option').exists()).toBe(true)
      expect(option.find('.vxe-table-custom--sort-option').exists()).toBe(true)
      expect(option.find('.vxe-table-custom--fixed-option').exists()).toBe(true)
    })
  })

  it('exposes all real export scopes and keeps server-only ranges explicit', async () => {
    const exporter = vi.fn(async () => undefined)
    const wrapper = mount(AppDataTable, {
      props: {
        tableKey: 'export-scopes',
        rows: [{ id: '1', name: 'one' }],
        columns: [{ field: 'name', title: '名称' }],
        exporter,
      },
    })

    await wrapper.get('[data-testid="app-data-table-export"]').trigger('click')
    const menu = wrapper.get('[data-testid="app-data-table-export-menu"]')
    const scope = menu.get('[data-testid="app-data-table-export-scope"]')
    expect(scope.findAll('option').map((option) => option.element.getAttribute('value'))).toEqual([
      'current',
      'selected',
      'all',
      'custom',
    ])
    expect(menu.find('.app-data-table__export-parameters').exists()).toBe(false)

    await scope.setValue('all')
    expect(menu.find('[data-testid="app-data-table-export-custom-quantity"]').exists()).toBe(false)
    expect(
      (
        menu.get('[data-testid="app-data-table-export-type"] option[value="csv"]')
          .element as HTMLOptionElement
      ).disabled,
    ).toBe(true)
    expect(
      (
        menu.get('[data-testid="app-data-table-export-type"] option[value="xlsx"]')
          .element as HTMLOptionElement
      ).disabled,
    ).toBe(false)

    await scope.setValue('custom')
    expect(menu.find('[data-testid="app-data-table-export-custom-quantity"]').exists()).toBe(true)
    await menu.get('[data-testid="app-data-table-export-custom-quantity"]').setValue('321')
    await menu.get('[data-testid="app-data-table-export-confirm"]').trigger('click')
    expect(exporter).toHaveBeenCalledWith(expect.objectContaining({ quantity: 321 }))
  })

  it('uses a vertical, lightly themed native vxe sort wrapper beside each title', async () => {
    const wrapper = mount(AppDataTable, {
      props: {
        tableKey: 'horizontal-sort',
        rows: [{ id: '1', name: 'one' }],
        columns: [{ field: 'name', title: '名称', sortable: true }],
      },
    })

    expect(wrapper.findComponent(VxeTable).props('sortConfig')).toEqual(
      expect.objectContaining({ iconLayout: 'vertical' }),
    )
    await flushPromises()
    expect(wrapper.find('.vxe-cell--sort-vertical-layout').exists()).toBe(true)
    expect(wrapper.find('.vxe-cell--sort-horizontal-layout').exists()).toBe(false)
    expect(wrapper.find('.app-data-table .vxe-sort--asc-btn').exists()).toBe(true)
  })

  it('offers explicit sort directions and a clear action in the sort panel', async () => {
    const wrapper = mount(AppDataTable, {
      props: {
        tableKey: 'explicit-sort',
        rows: [{ id: '1', name: 'one' }],
        columns: [
          { field: 'name', title: '名称', sortable: true },
          { field: 'status', title: '状态', sortable: true },
        ],
      },
    })

    await wrapper.get('[data-testid="app-data-table-sort"]').trigger('click')
    expect(wrapper.get('[data-testid="app-data-table-sort-panel"]').text()).toContain('升序')
    expect(wrapper.get('[data-testid="app-data-table-sort-panel"]').text()).toContain('降序')
    expect(
      (wrapper.get('[data-testid="app-data-table-sort-clear"]').element as HTMLButtonElement)
        .disabled,
    ).toBe(true)

    await wrapper.get('[data-testid="app-data-table-sort-name-desc"]').trigger('click')
    await flushPromises()
    expect((wrapper.vm as { request: () => { sort?: unknown } }).request().sort).toEqual({
      field: 'name',
      order: 'desc',
    })
    expect(
      (wrapper.get('[data-testid="app-data-table-sort-clear"]').element as HTMLButtonElement)
        .disabled,
    ).toBe(false)

    await wrapper.get('[data-testid="app-data-table-sort-clear"]').trigger('click')
    expect((wrapper.vm as { request: () => { sort?: unknown } }).request().sort).toBeUndefined()
  })

  it('keeps group options left aligned with a stable order marker like sort options', async () => {
    const wrapper = mount(AppDataTable, {
      props: {
        tableKey: 'group-panel-layout',
        rows: [{ id: '1', name: 'one' }],
        columns: [{ field: 'name', title: '名称', sortable: true }],
      },
    })

    await wrapper.get('[data-testid="app-data-table-sort"]').trigger('click')
    const sortOption = wrapper.get('.app-data-table__sort-option')
    await wrapper.get('[data-testid="app-data-table-group"]').trigger('click')
    const groupOption = wrapper.get('[data-testid="app-data-table-group-field-name"]')

    expect(groupOption.find('.app-data-table__group-order').exists()).toBe(true)
    expect(groupOption.find('.app-data-table__group-label').exists()).toBe(true)
    expect(groupOption.classes()).toContain('app-data-table__group-option')
    expect(sortOption.classes()).toContain('app-data-table__sort-option')
  })

  it('cycles sorting from the whole sortable title while an arrow click emits once', async () => {
    const wrapper = mount(AppDataTable, {
      props: {
        tableKey: 'header-sort-cycle',
        rows: [{ id: '1', name: 'one' }],
        columns: [{ field: 'name', title: '名称', sortable: true }],
      },
    })
    await flushPromises()
    const title = wrapper.get('.app-data-table__header-title')

    await title.trigger('click')
    await flushPromises()
    expect((wrapper.vm as { request: () => { sort?: unknown } }).request().sort).toEqual({
      field: 'name',
      order: 'asc',
    })
    await title.trigger('click')
    await flushPromises()
    expect((wrapper.vm as { request: () => { sort?: unknown } }).request().sort).toEqual({
      field: 'name',
      order: 'desc',
    })
    await title.trigger('click')
    await flushPromises()
    expect((wrapper.vm as { request: () => { sort?: unknown } }).request().sort).toBeUndefined()

    const arrow = wrapper.get('.vxe-sort--asc-btn')
    const emittedBeforeArrow = wrapper.emitted('query-change')?.length ?? 0
    await arrow.trigger('click')
    await flushPromises()
    expect(wrapper.emitted('query-change')?.length).toBe(emittedBeforeArrow + 1)
    expect((wrapper.vm as { request: () => { sort?: unknown } }).request().sort).toEqual({
      field: 'name',
      order: 'asc',
    })
  })

  it('closes platform settings and download panels on outside click or Escape', async () => {
    const wrapper = mount(AppDataTable, {
      props: {
        tableKey: 'panel-dismiss',
        rows: [{ id: '1', name: 'one' }],
        columns: [{ field: 'name', title: '名称' }],
      },
    })

    await wrapper.get('[data-testid="app-data-table-table-settings"]').trigger('click')
    await flushPromises()
    expect(wrapper.find('[data-testid="app-data-table-settings"]').exists()).toBe(true)
    await wrapper.get('.app-data-table__surface').trigger('mousedown')
    await wrapper.vm.$nextTick()
    expect(wrapper.find('[data-testid="app-data-table-settings"]').exists()).toBe(false)

    await wrapper.get('[data-testid="app-data-table-table-settings"]').trigger('click')
    await flushPromises()
    expect(wrapper.find('[data-testid="app-data-table-settings"]').exists()).toBe(true)
    document.body.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }))
    await wrapper.vm.$nextTick()
    expect(wrapper.find('[data-testid="app-data-table-settings"]').exists()).toBe(false)

    await wrapper.get('[data-testid="app-data-table-export"]').trigger('click')
    expect(wrapper.find('[data-testid="app-data-table-export-menu"]').exists()).toBe(true)
    expect(wrapper.find('.app-data-table__dialog-backdrop').exists()).toBe(false)
    expect(
      wrapper.get('[data-testid="app-data-table-export-menu"]').attributes('aria-modal'),
    ).toBeUndefined()
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))
    await wrapper.vm.$nextTick()
    expect(wrapper.find('[data-testid="app-data-table-export-menu"]').exists()).toBe(false)

    await wrapper.get('[data-testid="app-data-table-print"]').trigger('click')
    expect(wrapper.find('[data-testid="app-data-table-print-dialog"]').exists()).toBe(true)
    expect(wrapper.find('.app-data-table__dialog-backdrop').exists()).toBe(false)
    expect(
      wrapper.get('[data-testid="app-data-table-print-dialog"]').attributes('aria-modal'),
    ).toBeUndefined()
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))
    await wrapper.vm.$nextTick()
    expect(wrapper.find('[data-testid="app-data-table-print-dialog"]').exists()).toBe(false)
  })

  it('uses the approved pager contract and exposes print beside download', () => {
    const wrapper = mount(AppDataTable, {
      props: {
        tableKey: 'pager-contract',
        rows: [{ id: '1', name: 'one' }],
        columns: [{ field: 'name', title: '名称' }],
      },
      global: { components: { 'el-pagination': ElPagination } },
    })

    const pager = wrapper.findComponent({ name: 'ElPagination' })
    expect(pager.props('pageSize')).toBe(25)
    expect(pager.props('pageSizes')).toEqual([10, 25, 50, 100, 150, 200])
    expect(pager.props('pagerCount')).toBe(5)
    expect(pager.props('layout')).toBe('total, sizes, prev, pager, next, jumper')
    const tools = wrapper.get('.app-data-table__toolbar').element
    expect(tools.textContent).toContain('')
    expect(wrapper.find('[data-testid="app-data-table-print"]').exists()).toBe(true)
    expect(
      Array.from(tools.querySelectorAll('[data-testid]')).map((element) =>
        element.getAttribute('data-testid'),
      ),
    ).toEqual([
      'app-data-table-query-toggle',
      'app-data-table-sort',
      'app-data-table-group',
      'app-data-table-quick-search',
      'app-data-table-export',
      'app-data-table-print',
      'app-data-table-clear',
      'app-data-table-refresh',
      'app-data-table-fullscreen',
      'app-data-table-column-settings',
      'app-data-table-table-settings',
    ])
  })

  it('opens a platform download dialog with range, fields and Excel parameters', async () => {
    const exporter = vi.fn(async () => undefined)
    const wrapper = mount(AppDataTable, {
      props: {
        tableKey: 'download-dialog',
        rows: [{ id: '1', name: 'one' }],
        columns: [{ field: 'name', title: '名称' }],
        exporter,
      },
    })

    await wrapper.get('[data-testid="app-data-table-export"]').trigger('click')
    expect(wrapper.get('[data-testid="app-data-table-export-menu"]').classes()).toContain(
      'app-data-table__popover',
    )
    expect(wrapper.get('[data-testid="app-data-table-export-menu"]').text()).toContain('文件名')
    expect(wrapper.get('[data-testid="app-data-table-export-menu"]').text()).toContain('保存类型')
    expect(wrapper.get('[data-testid="app-data-table-export-menu"]').text()).toContain('选择字段')
    expect(wrapper.find('.app-data-table__export-parameters').exists()).toBe(false)
    expect(
      (wrapper.get('[data-testid="app-data-table-export-filename"]').element as HTMLInputElement)
        .value,
    ).toBe('download-dialog')
    expect(wrapper.get('[data-testid="app-data-table-export-confirm"]')).toBeTruthy()
  })

  it('uses one anchored compact form layout for download and print panels', async () => {
    const exporter = vi.fn(async () => undefined)
    const wrapper = mount(AppDataTable, {
      props: {
        tableKey: 'shared-form-panels',
        rows: [{ id: '1', name: 'one' }],
        columns: Array.from({ length: 6 }, (_, index) => ({
          field: `field-${index}`,
          title: `字段${index + 1}`,
        })),
        exporter,
      },
    })

    await wrapper.get('[data-testid="app-data-table-export"]').trigger('click')
    const download = wrapper.get('[data-testid="app-data-table-export-menu"]')
    expect(download.classes()).toContain('app-data-table__form-panel')
    expect(download.classes()).toContain('app-data-table__form-panel--shared')
    expect(download.classes()).toContain('app-data-table__form-panel--dense')
    expect(download.classes()).toContain('app-data-table__popover--below-trigger')
    expect(download.attributes('data-font-size')).toBe('12px')
    expect(
      (download.element as HTMLElement).style.getPropertyValue('--app-data-table-form-panel-width'),
    ).toBe('520px')
    expect(download.element.parentElement?.classList).toContain('app-data-table__export')
    expect(window.getComputedStyle(download.element).overflowY).toBe('hidden')
    expect(download.find('.app-data-table__form-grid').exists()).toBe(true)
    expect(
      download.find('.app-data-table__form-grid').findAll('.app-data-table__form-row'),
    ).toHaveLength(3)
    expect(
      download.find('[data-testid="app-data-table-export-type"] option[value="csv"]').exists(),
    ).toBe(true)
    expect(download.find('[data-testid="app-data-table-export-scope"]').exists()).toBe(true)
    expect(download.find('[data-testid="app-data-table-export-scope-hint"]').exists()).toBe(true)
    expect(download.find('.app-data-table__form-hint').exists()).toBe(false)
    expect(
      download
        .get('[data-testid="app-data-table-export-scope"]')
        .findAll('option')
        .map((option) => option.element.getAttribute('value')),
    ).toEqual(['current', 'selected', 'all', 'custom'])
    expect(download.find('.app-data-table__export-parameters').exists()).toBe(false)
    expect(download.find('[data-testid="app-data-table-export-custom-quantity"]').exists()).toBe(
      false,
    )
    await download.get('[data-testid="app-data-table-export-scope"]').setValue('custom')
    expect(download.find('[data-testid="app-data-table-export-custom-quantity"]').exists()).toBe(
      true,
    )
    await download.get('[data-testid="app-data-table-export-scope"]').setValue('all')
    expect(download.find('[data-testid="app-data-table-export-custom-quantity"]').exists()).toBe(
      false,
    )
    expect(download.find('.app-data-table__field-picker--single-column').exists()).toBe(true)
    expect(download.find('.app-data-table__export-formats').exists()).toBe(false)
    expect(download.find('.app-data-table__dialog-actions').exists()).toBe(true)

    await wrapper.get('[data-testid="app-data-table-print"]').trigger('click')
    const print = wrapper.get('[data-testid="app-data-table-print-dialog"]')
    expect(print.classes()).toContain('app-data-table__form-panel')
    expect(print.classes()).toContain('app-data-table__form-panel--shared')
    expect(print.classes()).toContain('app-data-table__form-panel--dense')
    expect(print.classes()).toContain('app-data-table__popover--below-trigger')
    expect(print.attributes('data-font-size')).toBe('12px')
    expect(
      (print.element as HTMLElement).style.getPropertyValue('--app-data-table-form-panel-width'),
    ).toBe('520px')
    expect(print.element.parentElement?.classList).toContain('app-data-table__toolbar-popover')
    expect(print.find('.app-data-table__form-grid').exists()).toBe(true)
    expect(print.findAll('.app-data-table__form-row')).toHaveLength(3)
    expect(print.find('[data-testid="app-data-table-print-scope"]').exists()).toBe(true)
    expect(print.find('[data-testid="app-data-table-print-width"]').exists()).toBe(true)
    expect(print.find('.app-data-table__field-picker--single-column').exists()).toBe(true)
    expect(print.find('.app-data-table__dialog-actions').exists()).toBe(true)
  })

  it('adds shared column controls to the native vxe settings header', async () => {
    const wrapper = mount(AppDataTable, {
      props: {
        tableKey: 'native-column-header',
        rows: [{ id: '1', name: 'one' }],
        columns: [{ field: 'name', title: '名称' }],
      },
    })

    await wrapper.get('[data-testid="app-data-table-column-settings"]').trigger('click')
    await flushPromises()
    await new Promise((resolve) => setTimeout(resolve, 350))

    const panel = wrapper.get('.vxe-table-custom-wrapper')
    expect(panel.find('.app-data-table__native-header-tools').exists()).toBe(true)
    expect(panel.find('[data-testid="app-data-table-native-show-index"]').exists()).toBe(true)
    expect(panel.find('[data-testid="app-data-table-native-border"]').exists()).toBe(true)
    expect(panel.find('[data-testid="app-data-table-native-reset"]').exists()).toBe(true)

    await panel.get('[data-testid="app-data-table-native-show-index"]').setValue(true)
    const preference = Object.values(localStorage)
      .map((value) => {
        try {
          return JSON.parse(String(value)) as { showIndex?: boolean }
        } catch {
          return null
        }
      })
      .find((value) => value?.showIndex === true)
    expect(preference?.showIndex).toBe(true)

    await panel.get('[data-testid="app-data-table-native-reset"]').trigger('click')
    await flushPromises()
    const resetPreference = Object.values(localStorage)
      .map((value) => {
        try {
          return JSON.parse(String(value)) as { showIndex?: boolean }
        } catch {
          return null
        }
      })
      .find((value) => value?.showIndex === false)
    expect(resetPreference?.showIndex).toBe(false)
  })

  it('uses one visual panel contract for sort, group, download, print and row settings', async () => {
    const wrapper = mount(AppDataTable, {
      props: {
        tableKey: 'panel-contract',
        rows: [{ id: '1', name: 'one' }],
        columns: [{ field: 'name', title: '名称' }],
      },
    })

    for (const [trigger, panel] of [
      ['app-data-table-sort', 'app-data-table-sort-panel'],
      ['app-data-table-group', 'app-data-table-group-panel'],
      ['app-data-table-export', 'app-data-table-export-menu'],
      ['app-data-table-print', 'app-data-table-print-dialog'],
      ['app-data-table-table-settings', 'app-data-table-settings'],
    ]) {
      await wrapper.get(`[data-testid="${trigger}"]`).trigger('click')
      await flushPromises()
      const target = wrapper.get(`[data-testid="${panel}"]`)
      expect(target.classes()).toContain('app-data-table__panel')
      expect(target.attributes('data-font-size')).toBe('12px')
      if (panel === 'app-data-table-sort-panel' || panel === 'app-data-table-group-panel') {
        expect((target.element as HTMLElement).style.height).toContain('550px')
        expect((target.element as HTMLElement).style.height).toContain('240px')
      }
      expect(target.find('.app-data-table__dialog-header').exists()).toBe(true)
      expect(target.find('.app-data-table__panel-footer').exists()).toBe(true)
    }
  })

  it('passes the actual resizable action column width to the action slot', async () => {
    const wrapper = mount(AppDataTable, {
      props: {
        tableKey: 'responsive-actions',
        rows: [{ id: '1', name: 'one' }],
        columns: [{ field: 'name', title: '名称' }],
      },
      slots: {
        actions: ({ availableWidth }: { availableWidth: number }) =>
          h('span', { 'data-testid': 'action-width' }, String(availableWidth)),
      },
    })

    await flushPromises()
    expect(wrapper.get('[data-testid="action-width"]').text()).toBe('220')
    wrapper.findComponent(VxeTable).vm.$emit('resizable-change', {
      resizeColumn: { field: '__actions' },
      resizeWidth: 320,
    })
    await flushPromises()
    expect(wrapper.get('[data-testid="action-width"]').text()).toBe('320')
  })

  it('syncs action slot width from the rendered action-column header', async () => {
    let observer: { trigger: () => void } | undefined
    vi.stubGlobal(
      'ResizeObserver',
      class {
        private readonly callback: ResizeObserverCallback

        constructor(callback: ResizeObserverCallback) {
          this.callback = callback
          observer = {
            trigger: () => this.callback([], this as unknown as ResizeObserver),
          }
        }

        observe(): void {}

        disconnect(): void {}

        unobserve(): void {}
      },
    )
    const wrapper = mount(AppDataTable, {
      props: {
        tableKey: 'responsive-action-header',
        rows: [{ id: '1', name: 'one' }],
        columns: [{ field: 'name', title: '名称' }],
      },
      slots: {
        actions: ({ availableWidth }: { availableWidth: number }) =>
          h('span', { 'data-testid': 'action-header-width' }, String(availableWidth)),
      },
    })

    await flushPromises()
    const header = wrapper.get('.app-data-table__actions-column-header').element as HTMLElement
    header.getBoundingClientRect = () => ({ width: 367 }) as DOMRect
    observer?.trigger()
    await flushPromises()

    expect(wrapper.get('[data-testid="action-header-width"]').text()).toBe('367')
  })

  it('keeps business actions inside the card above the toolbar and renders an independent filter header row', async () => {
    const wrapper = mount(AppDataTable, {
      props: {
        tableKey: 'confirmed-layout',
        rows: [{ id: '1', name: 'one', createdOn: '2026-01-01' }],
        columns: [
          { field: 'name', title: '名称', filter: { kind: 'text' } },
          {
            field: 'createdOn',
            title: '创建日期',
            filter: { kind: 'date-range' },
          },
        ],
      },
      slots: {
        'toolbar-actions': '<button data-testid="business-create">新增</button>',
      },
    })

    const actions = wrapper.get('.app-data-table__business-actions')
    expect(actions.element.parentElement?.className).toContain('app-data-table__card')
    expect(
      wrapper.get('.app-data-table__card').find('.app-data-table__business-actions').exists(),
    ).toBe(true)
    await wrapper.get('[data-testid="app-data-table-query-toggle"]').trigger('click')
    await flushPromises()
    const filterRow = wrapper.get('tr.app-data-table__header-filter-row')
    expect(filterRow.element.parentElement?.tagName).toBe('THEAD')
    expect(filterRow.find('.app-data-table__header-filter-row').exists()).toBe(false)
    const filterCells = filterRow.findAll('th.app-data-table__header-filter-cell')
    expect(filterCells.every((cell) => cell.classes().includes('vxe-header--column'))).toBe(true)
    expect(window.getComputedStyle(filterCells[0]!.element).padding).toBe('5px 6px')
    expect(
      window.getComputedStyle(
        filterRow.get('[data-testid="app-data-table-header-filter-name"]').element,
      ).height,
    ).toBe('26px')
    expect(
      filterRow.find('[data-testid="app-data-table-header-filter-createdOn-range"]').exists(),
    ).toBe(true)
    expect(filterRow.findAll('.app-data-table__date-range-control')).toHaveLength(1)
    const range = filterRow.get('.el-date-editor--daterange')
    expect(range.findAll('input').map((input) => input.attributes('placeholder'))).toEqual([
      '开始',
      '结束',
    ])
  })

  it('binds the selected density to the VXE cell height', async () => {
    const wrapper = mount(AppDataTable, {
      props: {
        tableKey: 'row-density',
        rows: [{ id: '1', name: 'one' }],
        columns: [{ field: 'name', title: '名称' }],
      },
    })
    const table = wrapper.findComponent(VxeTable)
    expect(table.props('cellConfig')).toEqual({ height: 44 })

    await wrapper.get('[data-testid="app-data-table-table-settings"]').trigger('click')
    await wrapper.get('[data-testid="app-data-table-density-compact"]').trigger('click')
    await flushPromises()

    expect(table.props('cellConfig')).toEqual({ height: 32 })
  })

  it('summarizes and clears selected rows for both multiple and single selection', async () => {
    const rows = [
      { id: '1', name: 'one' },
      { id: '2', name: 'two' },
    ]
    for (const selection of ['multiple', 'single'] as const) {
      const wrapper = mount(AppDataTable, {
        props: {
          tableKey: `selection-${selection}`,
          rows,
          columns: [{ field: 'name', title: '名称' }],
          selection,
        },
      })
      wrapper
        .findComponent(VxeTable)
        .vm.$emit(
          selection === 'multiple' ? 'checkbox-change' : 'radio-change',
          selection === 'multiple' ? { records: rows } : { row: rows[0] },
        )
      await flushPromises()
      expect(wrapper.get('.app-data-table__selection-summary').text()).toContain(
        `已选择 ${selection === 'multiple' ? 2 : 1} 行`,
      )
      const clear = wrapper.get('[data-testid="app-data-table-clear-selection"]')
      expect((clear.element as HTMLButtonElement).disabled).toBe(false)
      await clear.trigger('click')
      expect(wrapper.get('.app-data-table__selection-summary').text()).toContain('已选择 0 行')
      expect((clear.element as HTMLButtonElement).disabled).toBe(true)
    }
  })

  it('groups local rows by one field and then by fields in selection order', async () => {
    const wrapper = mount(AppDataTable, {
      props: {
        tableKey: 'grouping',
        rows: [
          { id: '1', department: 'B', status: '启用' },
          { id: '2', department: 'A', status: '停用' },
          { id: '3', department: 'A', status: '启用' },
        ],
        columns: [
          { field: 'department', title: '部门' },
          { field: 'status', title: '状态' },
        ],
      },
    })

    await wrapper.get('[data-testid="app-data-table-group"]').trigger('click')
    await wrapper.get('[data-testid="app-data-table-group-field-department"]').trigger('click')
    let data = wrapper.findComponent(VxeTable).props('data') as Array<Record<string, unknown>>
    expect(
      data.filter((row) => row.__appDataTableGroup).map((row) => row.__appDataTableGroupLabel),
    ).toEqual(['部门：A', '部门：B'])
    await wrapper.get('[data-testid="app-data-table-group-field-status"]').trigger('click')
    data = wrapper.findComponent(VxeTable).props('data') as Array<Record<string, unknown>>
    expect(
      data.filter((row) => row.__appDataTableGroup).map((row) => row.__appDataTableGroupLabel),
    ).toEqual(['部门：A', '状态：停用', '状态：启用', '部门：B', '状态：启用'])
    expect(wrapper.findAll('.app-data-table__group-row').length).toBeGreaterThan(0)
    expect(
      wrapper
        .findAll('.app-data-table__group-row .vxe-cell--checkbox')
        .every((cell) => !cell.isVisible()),
    ).toBe(true)
    expect(wrapper.findAll('.app-data-table__group-row [data-testid^="business-"]')).toHaveLength(0)
    await wrapper.get('[data-testid="app-data-table-group-clear"]').trigger('click')
    expect(wrapper.findAll('.app-data-table__group-row')).toHaveLength(0)
  })

  it('merges a group row across structural and data columns while excluding the action column', async () => {
    const wrapper = mount(AppDataTable, {
      props: {
        tableKey: 'group-span',
        rows: [{ id: '1', department: 'A', status: '启用' }],
        columns: [
          { field: 'department', title: '部门' },
          { field: 'status', title: '状态' },
        ],
        selection: 'multiple',
      },
      slots: {
        actions: '<button data-testid="business-action">编辑</button>',
      },
    })

    await wrapper.get('[data-testid="app-data-table-group"]').trigger('click')
    await wrapper.get('[data-testid="app-data-table-group-field-department"]').trigger('click')
    const table = wrapper.findComponent(VxeTable)
    const data = table.props('data') as Array<Record<string, unknown>>
    const groupRow = data.find((row) => row.__appDataTableGroup)
    const spanMethod = table.props('spanMethod') as (params: Record<string, unknown>) => {
      rowspan: number
      colspan: number
    }

    expect(
      spanMethod({
        row: groupRow,
        column: { field: 'department' },
        columnIndex: 0,
        fixed: undefined,
      }),
    ).toEqual({ rowspan: 1, colspan: 2 })
    expect(
      spanMethod({
        row: groupRow,
        column: { type: 'checkbox' },
        columnIndex: 0,
        fixed: undefined,
      }),
    ).toEqual({ rowspan: 1, colspan: 1 })
    expect(
      spanMethod({
        row: groupRow,
        column: { field: '__actions' },
        columnIndex: 3,
        fixed: 'right',
      }),
    ).toEqual({ rowspan: 0, colspan: 0 })
    const domGroupRow = wrapper.get('tr.app-data-table__group-row')
    expect(domGroupRow.find('td[colspan="2"]').exists()).toBe(true)
    expect(domGroupRow.find('[data-testid="business-action"]').exists()).toBe(false)
  })

  it('sorts every selected group field stably so interleaved child values form one group', async () => {
    const wrapper = mount(AppDataTable, {
      props: {
        tableKey: 'grouping-interleaved',
        rows: [
          { id: '1', department: 'A', status: '启用' },
          { id: '2', department: 'A', status: '停用' },
          { id: '3', department: 'A', status: '启用' },
          { id: '4', department: 'B', status: '启用' },
        ],
        columns: [
          { field: 'department', title: '部门' },
          { field: 'status', title: '状态' },
        ],
      },
    })

    await wrapper.get('[data-testid="app-data-table-group"]').trigger('click')
    await wrapper.get('[data-testid="app-data-table-group-field-department"]').trigger('click')
    await wrapper.get('[data-testid="app-data-table-group-field-status"]').trigger('click')

    const data = wrapper.findComponent(VxeTable).props('data') as Array<Record<string, unknown>>
    expect(
      data.filter((row) => row.__appDataTableGroup).map((row) => row.__appDataTableGroupLabel),
    ).toEqual(['部门：A', '状态：启用', '状态：停用', '部门：B', '状态：启用'])
  })

  it('provides a top-mode quick search that filters local data and is disabled in header mode', async () => {
    const wrapper = mount(AppDataTable, {
      props: {
        tableKey: 'quick-search',
        rows: [
          { id: '1', name: 'Alpha', status: 'Active' },
          { id: '2', name: 'Beta', status: 'Disabled' },
        ],
        columns: [
          { field: 'name', title: '名称', filter: { kind: 'text' } },
          {
            field: 'status',
            title: '状态',
            filter: { kind: 'select', options: [] },
          },
        ],
      },
    })

    expect(wrapper.find('[data-testid="app-data-table-quick-search"]').exists()).toBe(true)
    expect(
      wrapper.get('[data-testid="app-data-table-quick-search"]').attributes('placeholder'),
    ).toBe('快速搜索当前数据')
    expect(wrapper.get('.app-data-table__quick-search').find('svg').exists()).toBe(true)
    await wrapper.get('[data-testid="app-data-table-quick-search"]').setValue('beta')
    await flushPromises()
    expect(wrapper.findComponent(VxeTable).props('data')).toEqual([
      { id: '2', name: 'Beta', status: 'Disabled' },
    ])

    await wrapper.get('[data-testid="app-data-table-query-toggle"]').trigger('click')
    await flushPromises()
    const quickSearch = wrapper.get('[data-testid="app-data-table-quick-search"]')
    expect(quickSearch.attributes('disabled')).toBeDefined()
    expect(wrapper.get('.app-data-table__quick-search').classes()).toContain('is-disabled')
  })

  it('keeps the query and unified table card on a compact stack in both modes', async () => {
    const wrapper = mount(AppDataTable, {
      props: {
        tableKey: 'compact-stack',
        rows: [{ id: '1', name: 'one' }],
        columns: [{ field: 'name', title: '名称', filter: { kind: 'text' } }],
      },
      slots: {
        toolbar: '<div data-testid="top-query">查询</div>',
        'toolbar-actions': '业务操作',
      },
    })

    expect(wrapper.get('[data-testid="app-data-table"]').classes()).toContain(
      'app-data-table--compact-stack',
    )
    expect(wrapper.find('.app-data-table__top-query').exists()).toBe(true)
    expect(
      wrapper.get('.app-data-table__card').find('.app-data-table__business-actions').exists(),
    ).toBe(true)
    await wrapper.get('[data-testid="app-data-table-query-toggle"]').trigger('click')
    await flushPromises()
    expect(wrapper.get('[data-testid="app-data-table"]').classes()).toContain(
      'app-data-table--compact-stack',
    )
    expect(wrapper.find('.app-data-table__top-query').exists()).toBe(false)
    expect(
      wrapper.get('.app-data-table__card').find('.app-data-table__business-actions').exists(),
    ).toBe(true)
  })

  it('filters only the currently loaded server rows without calling the loader', async () => {
    const loader = vi.fn(async () => ({
      items: [
        { id: '1', name: 'Alpha' },
        { id: '2', name: 'Beta' },
      ],
      total: 2,
      pageIndex: 1,
      pageSize: 25,
    }))
    const wrapper = mount(AppDataTable, {
      props: {
        tableKey: 'server-quick-search',
        routeKey: 'server-quick-search',
        userKey: 'operator',
        rows: [
          { id: '1', name: 'Alpha' },
          { id: '2', name: 'Beta' },
        ],
        total: 2,
        columns: [{ field: 'name', title: '名称' }],
        loader,
      },
    })

    await wrapper.get('[data-testid="app-data-table-quick-search"]').setValue('beta')
    await flushPromises()

    expect(loader).not.toHaveBeenCalled()
    expect(wrapper.findComponent(VxeTable).props('data')).toEqual([{ id: '2', name: 'Beta' }])
    expect((wrapper.vm as { topQuery: Record<string, unknown> }).topQuery).toEqual({})
  })

  it('exposes a pressed active state when column-header query mode is enabled', async () => {
    const wrapper = mount(AppDataTable, {
      props: {
        tableKey: 'query-toggle-state',
        rows: [{ id: '1', name: 'one' }],
        columns: [{ field: 'name', title: '名称' }],
      },
    })
    const toggle = wrapper.get('[data-testid="app-data-table-query-toggle"]')

    expect(toggle.attributes('aria-pressed')).toBe('false')
    expect(toggle.classes()).not.toContain('is-active')
    await toggle.trigger('click')
    await flushPromises()
    expect(toggle.attributes('aria-pressed')).toBe('true')
    expect(toggle.classes()).toContain('is-active')
  })

  it('prints selected visible columns without the operation column', async () => {
    const wrapper = mount(AppDataTable, {
      props: {
        tableKey: 'print-native',
        rows: [{ id: '1', name: 'one' }],
        columns: [
          { field: 'name', title: '名称' },
          { field: 'status', title: '状态' },
        ],
      },
      slots: { actions: '<button>操作</button>' },
    })
    const table = wrapper.findComponent(VxeTable)
    const getPrintHtml = vi
      .spyOn(
        table.vm as unknown as {
          getPrintHtml: () => Promise<{ html: string }>
        },
        'getPrintHtml',
      )
      .mockResolvedValue({ html: '<table><tr><td>one</td></tr></table>' })
    const print = vi.fn()
    const originalCreateElement = document.createElement.bind(document)
    const createElement = vi.spyOn(document, 'createElement')
    createElement.mockImplementation((tagName, options) => {
      const element = originalCreateElement(tagName, options)
      if (tagName.toLowerCase() === 'iframe') {
        Object.defineProperty(element, 'contentWindow', {
          configurable: true,
          value: { addEventListener: vi.fn(), print },
        })
      }
      return element
    })

    await wrapper.get('[data-testid="app-data-table-print"]').trigger('click')
    await flushPromises()
    expect(wrapper.get('[data-testid="app-data-table-print-dialog"]').text()).toContain(
      '当前页（已加载数据）',
    )
    await wrapper.get('[data-testid="app-data-table-print-field-status"]').setValue(false)
    await wrapper.get('[data-testid="app-data-table-print-confirm"]').trigger('click')
    expect(getPrintHtml).toHaveBeenCalledWith({
      columns: ['name'],
      mode: 'current',
      sheetName: 'print-native打印',
    })
    expect(print).toHaveBeenCalledTimes(1)
    expect(wrapper.find('[data-testid="app-data-table-print-dialog"]').exists()).toBe(false)
    createElement.mockRestore()
  })
})
