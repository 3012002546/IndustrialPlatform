import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import ElementPlus from 'element-plus'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'
import { VxeTable } from 'vxe-table'
import { makeAuthSession, persistAuthSession } from '../fixtures/session'
import { useAuthStore } from '@/stores/authStore'
import { useLocalizationStore } from '@/stores/localizationStore'
import { useDeviceStore } from '@/stores/deviceStore'
import { createApiError } from '@/api/errors'
import { PERMISSIONS } from '@/permissions'
import DictionariesPage from '@/pages/pc/referenceData/DictionariesPage.vue'
import AppFormDrawer from '@/components/management/AppFormDrawer.vue'
import type { DictionaryDetail, DictionarySummary } from '@/api/referenceData/types'

const { api } = vi.hoisted(() => ({
  api: {
    listDictionaries: vi.fn(),
    getDictionary: vi.fn(),
    createDictionary: vi.fn(),
    updateDictionary: vi.fn(),
    cloneDictionary: vi.fn(),
    checkDictionaryPublication: vi.fn(),
    publishDictionary: vi.fn(),
    disableDictionary: vi.fn(),
  },
}))
vi.mock('@/api/referenceData', () => ({ getReferenceDataApi: () => api }))
vi.mock('element-plus/es/components/focus-trap/index', () => ({
  ElFocusTrap: defineComponent({ template: '<div><slot /></div>' }),
}))
const wrappers: VueWrapper[] = []
const SelectStub = defineComponent({
  props: ['modelValue'],
  emits: ['update:modelValue'],
  template:
    '<select :value="modelValue" @change="$emit(\'update:modelValue\', $event.target.value)"><slot /></select>',
})
const OptionStub = defineComponent({
  props: ['value', 'label'],
  template: '<option :value="value">{{ label }}</option>',
})
const summary: DictionarySummary = {
  id: 'dictionary-1',
  nId: 'STATUS',
  name: 'Status',
  scopeType: 'Tenant',
  tenantNId: 't1',
  revision: 1,
  status: 'Draft',
  enabledItemCount: 1,
  optimisticVersion: 2,
  concurrencyVersion: 'token-2',
  lastUpdatedOn: '2026-09-05T00:00:00Z',
  publishedBy: null,
  isFrozen: false,
  isLocked: false,
}
const detail: DictionaryDetail = {
  ...summary,
  description: null,
  publishedOn: null,
  items: [{ nId: 'OPEN', name: 'Open', description: null, sort: 0, enabled: true }],
}
const editPermissions = [
  PERMISSIONS.referenceDataDictionaryView,
  PERMISSIONS.referenceDataDictionaryCreate,
  PERMISSIONS.referenceDataDictionaryUpdate,
  PERMISSIONS.referenceDataDictionaryPublish,
  PERMISSIONS.referenceDataDictionaryDisable,
]
async function mountPage(permissions = editPermissions, locale: 'zh-CN' | 'en-US' = 'zh-CN') {
  const pinia = createPinia()
  setActivePinia(pinia)
  persistAuthSession(permissions)
  await useAuthStore().restore()
  useLocalizationStore().setLocale(locale, null)
  useDeviceStore().setOverride('pc')
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/', component: DictionariesPage }],
  })
  await router.push('/')
  await router.isReady()
  const wrapper = mount(defineComponent({ template: '<router-view />' }), {
    global: {
      plugins: [pinia, ElementPlus, router],
      stubs: {
        teleport: true,
        'el-select': SelectStub,
        'el-option': OptionStub,
        'el-dropdown': defineComponent({
          name: 'ElDropdown',
          emits: ['command'],
          template: '<div><slot /></div>',
        }),
      },
    },
  })
  wrappers.push(wrapper)
  await flushPromises()
  return wrapper
}
async function button(wrapper: VueWrapper, label: string) {
  let found = wrapper.findAll('button').find((button) => button.text() === label)
  if (!found) {
    const master = wrapper.findAllComponents({ name: 'AppDataTable' })[0]!
    master.findComponent(VxeTable).vm.$emit('cell-click', {
      row: summary,
      column: { field: 'name' },
    })
    await flushPromises()
    found = wrapper.findAll('button').find((button) => button.text() === label)
  }
  expect(found, label).toBeDefined()
  await found!.trigger('click')
  await flushPromises()
}
beforeEach(() => {
  vi.resetAllMocks()
  localStorage.clear()
  sessionStorage.clear()
  vi.stubEnv('VITE_AUTH_MODE', 'mock')
  api.listDictionaries.mockResolvedValue({ items: [summary], total: 1, pageIndex: 1, pageSize: 25 })
  api.getDictionary.mockResolvedValue(structuredClone(detail))
  api.checkDictionaryPublication.mockResolvedValue({
    previousRevision: null,
    addedItems: ['OPEN'],
    changedItems: [],
    disabledItems: [],
    errors: [],
  })
  api.createDictionary.mockResolvedValue(detail)
  api.updateDictionary.mockResolvedValue(detail)
})
afterEach(() => {
  for (const wrapper of wrappers.splice(0)) wrapper.unmount()
  document.body.innerHTML = ''
  vi.unstubAllEnvs()
})

describe('Dictionary management page', () => {
  it('keeps the page heading free of an unlabeled record count', async () => {
    const wrapper = await mountPage()

    expect(wrapper.find('.app-page__heading-meta').exists()).toBe(false)
  })

  it('keeps the dictionary filters with the directory instead of consuming the detail workspace', async () => {
    const wrapper = await mountPage()

    expect(
      wrapper.get('.dictionary-directory').findComponent({ name: 'AppQueryPanel' }).exists(),
    ).toBe(true)
    expect(
      wrapper.findAllComponents({ name: 'AppDataTable' })[0]!.props('quickSearchEnabled'),
    ).toBe(false)
  })

  it('distinguishes no selection from an empty directory and keeps the empty-state create entry local', async () => {
    const wrapper = await mountPage()
    expect(wrapper.get('.dictionary-content').text()).toContain('请选择一个字典')

    api.listDictionaries.mockResolvedValueOnce({ items: [], total: 0, pageIndex: 1, pageSize: 25 })
    const empty = await mountPage()
    expect(empty.get('.dictionary-content').text()).toContain('暂无字典')
    expect(empty.get('[data-testid="dictionary-empty-create"]').text()).toBe('新建字典')
  })

  it('makes a directory click the active object without opening the editor drawer', async () => {
    const wrapper = await mountPage()
    const master = wrapper.findAllComponents({ name: 'AppDataTable' })[0]!
    expect(master.props('selection')).toBe('none')
    expect(master.props('activeRowKey')).toBeNull()
    expect(master.props('columns')).toHaveLength(1)
    master.findComponent(VxeTable).vm.$emit('cell-click', {
      row: summary,
      column: { field: 'name' },
    })
    await flushPromises()

    expect(api.getDictionary).toHaveBeenCalledWith(summary.id, expect.anything())
    expect(master.props('activeRowKey')).toBe(summary.id)
    expect(wrapper.get('.dictionary-content').text()).toContain('Status')
    expect(wrapper.findAllComponents(AppFormDrawer).every((item) => item.props('modelValue'))).toBe(
      false,
    )
  })

  it('opens one dictionary item in its own editor and cancel leaves the parent closed', async () => {
    const wrapper = await mountPage()
    const master = wrapper.findAllComponents({ name: 'AppDataTable' })[0]!
    master.findComponent(VxeTable).vm.$emit('cell-click', {
      row: summary,
      column: { field: 'name' },
    })
    await flushPromises()

    await wrapper.get('[data-testid="dictionary-item-edit-OPEN"]').trigger('click')
    await flushPromises()
    const itemDrawer = wrapper
      .findAllComponents(AppFormDrawer)
      .find(
        (item) =>
          item.props('modelValue') && item.find('[data-testid="dictionary-item-save"]').exists(),
      )
    expect(itemDrawer).toBeDefined()
    expect(itemDrawer!.find('[data-testid="dictionary-item-name"]').exists()).toBe(true)
    expect(itemDrawer!.find('[data-testid="dictionary-name"]').exists()).toBe(false)
    expect(
      wrapper.findAllComponents(AppFormDrawer).filter((item) => item.props('modelValue')),
    ).toHaveLength(1)

    await itemDrawer!.get('[data-testid="dictionary-item-cancel"]').trigger('click')
    expect(api.updateDictionary).not.toHaveBeenCalled()
    expect(itemDrawer!.props('modelValue')).toBe(false)
  })

  it('saves a dictionary item through the aggregate version contract without opening the parent editor', async () => {
    const wrapper = await mountPage()
    const master = wrapper.findAllComponents({ name: 'AppDataTable' })[0]!
    master.findComponent(VxeTable).vm.$emit('cell-click', {
      row: summary,
      column: { field: 'name' },
    })
    await flushPromises()

    await wrapper.get('[data-testid="dictionary-item-edit-OPEN"]').trigger('click')
    await flushPromises()
    const itemDrawer = wrapper
      .findAllComponents(AppFormDrawer)
      .find(
        (item) =>
          item.props('modelValue') && item.find('[data-testid="dictionary-item-save"]').exists(),
      )!
    await itemDrawer.get('[data-testid="dictionary-item-name"]').setValue('Open state')
    await itemDrawer.get('[data-testid="dictionary-item-save"]').trigger('click')
    await flushPromises()

    expect(api.updateDictionary).toHaveBeenCalledWith(
      summary.id,
      expect.objectContaining({
        expectedOptimisticVersion: summary.optimisticVersion,
        expectedConcurrencyVersion: summary.concurrencyVersion,
        items: [expect.objectContaining({ nId: 'OPEN', name: 'Open state' })],
      }),
    )
    expect(itemDrawer.props('modelValue')).toBe(false)
    expect(
      wrapper.findAllComponents(AppFormDrawer).filter((item) => item.props('modelValue')),
    ).toHaveLength(0)
  })

  it('uses server filters in both query modes and hides write actions from a viewer', async () => {
    const wrapper = await mountPage([PERMISSIONS.referenceDataDictionaryView], 'en-US')
    expect(wrapper.text()).toContain('Dictionaries')
    expect(wrapper.find('[data-testid="dictionary-create"]').exists()).toBe(false)
    expect(wrapper.findAll('button').some((button) => button.text() === 'Edit')).toBe(false)
    await wrapper.get('input[aria-label="Keyword"]').setValue('STATUS')
    await wrapper.get('[data-testid="query-panel-submit"]').trigger('click')
    await flushPromises()
    expect(api.listDictionaries).toHaveBeenLastCalledWith(
      expect.objectContaining({ keyword: 'STATUS' }),
      expect.anything(),
    )
    const master = wrapper.findAllComponents({ name: 'AppDataTable' })[0]!
    expect(master.find('[data-testid="app-data-table-query-toggle"]').exists()).toBe(false)
    await master.props('loader')!({
      pageIndex: 1,
      pageSize: 25,
      queryMode: 'column',
      filters: { name: 'Open' },
      columns: [],
    })
    expect(api.listDictionaries).toHaveBeenLastCalledWith(
      expect.objectContaining({ keyword: 'Open' }),
      expect.anything(),
    )
    expect(wrapper.findComponent({ name: 'AppDataTable' }).props('selection')).toBe('none')
  })

  it('creates through a structured form and retains modal preference', async () => {
    localStorage.setItem('industrial-platform:form-surface-mode', 'modal')
    const wrapper = await mountPage()
    await wrapper.get('[data-testid="dictionary-create"]').trigger('click')
    await flushPromises()
    expect(wrapper.find('.app-form-drawer--modal').exists()).toBe(true)
    await wrapper.get('input[data-testid="dictionary-nid"]').setValue('STATE')
    await wrapper.get('input[data-testid="dictionary-name"]').setValue('State')
    await wrapper.get('[data-testid="dictionary-save"]').trigger('click')
    await flushPromises()
    expect(api.createDictionary).toHaveBeenCalledWith({
      nId: 'STATE',
      name: 'State',
      description: null,
      scopeType: 'Tenant',
      scopeId: null,
      items: [],
    })
    expect(wrapper.findComponent(AppFormDrawer).props('modelValue')).toBe(false)
  })

  it('preserves local input after a stale update and offers reload and copy', async () => {
    api.updateDictionary.mockRejectedValue(
      createApiError('business', 'Conflict', 'corr', {
        status: 409,
        code: 'REF-CONCURRENCY-CONFLICT',
        traceId: 'trace-test',
      }),
    )
    const wrapper = await mountPage()
    await button(wrapper, '编辑')
    await wrapper.get('input[data-testid="dictionary-name"]').setValue('My unsaved name')
    await wrapper.get('[data-testid="dictionary-save"]').trigger('click')
    await flushPromises()
    expect(
      (wrapper.get('input[data-testid="dictionary-name"]').element as HTMLInputElement).value,
    ).toBe('My unsaved name')
    expect(wrapper.text()).toContain('复制未保存内容')
    expect(wrapper.text()).toContain('重新加载')
    expect(wrapper.text()).toContain('trace-test')
    expect(api.updateDictionary).toHaveBeenCalledWith(
      summary.id,
      expect.objectContaining({
        expectedOptimisticVersion: 2,
        expectedConcurrencyVersion: 'token-2',
        name: 'My unsaved name',
      }),
    )
  })

  it('does not let a late detail response overwrite a new form', async () => {
    let resolveDetail!: (value: DictionaryDetail) => void
    api.getDictionary.mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          resolveDetail = resolve
        }),
    )
    const wrapper = await mountPage()
    const master = wrapper.findAllComponents({ name: 'AppDataTable' })[0]!
    master.findComponent(VxeTable).vm.$emit('cell-click', {
      row: summary,
      column: { field: 'name' },
    })
    await flushPromises()
    await wrapper.get('[data-testid="dictionary-create"]').trigger('click')
    await flushPromises()
    await wrapper.get('input[data-testid="dictionary-name"]').setValue('New local draft')
    resolveDetail(detail)
    await flushPromises()
    expect(
      (wrapper.get('input[data-testid="dictionary-name"]').element as HTMLInputElement).value,
    ).toBe('New local draft')
    expect(api.checkDictionaryPublication).not.toHaveBeenCalled()
  })

  it('retains input but disables save after permission revocation', async () => {
    const wrapper = await mountPage()
    await button(wrapper, '编辑')
    await wrapper.get('input[data-testid="dictionary-name"]').setValue('Preserve on revocation')
    useAuthStore().adoptSession(makeAuthSession([PERMISSIONS.referenceDataDictionaryView]))
    await flushPromises()
    expect(wrapper.get('[data-testid="dictionary-save"]').attributes('disabled')).toBeDefined()
    expect(
      (wrapper.get('input[data-testid="dictionary-name"]').element as HTMLInputElement).value,
    ).toBe('Preserve on revocation')
    await wrapper.get('[data-testid="dictionary-save"]').trigger('click')
    expect(api.updateDictionary).not.toHaveBeenCalled()
  })

  it('shows publication differences and blocks all close paths while publishing', async () => {
    let finish!: () => void
    api.publishDictionary.mockImplementationOnce(
      () =>
        new Promise<void>((resolve) => {
          finish = resolve
        }),
    )
    const wrapper = await mountPage()
    const master = wrapper.findAllComponents({ name: 'AppDataTable' })[0]!
    master.findComponent(VxeTable).vm.$emit('cell-click', {
      row: summary,
      column: { field: 'name' },
    })
    await flushPromises()
    wrapper.findComponent({ name: 'ElDropdown' }).vm.$emit('command', 'publish')
    await flushPromises()
    const surface = wrapper
      .findAllComponents(AppFormDrawer)
      .find((item) => item.props('title') === '发布校验与差异')!
    expect(surface.text()).toContain('OPEN')
    expect(surface.text()).toContain('校验通过')
    await surface.get('[data-testid="dictionary-publish-confirm"]').trigger('click')
    await flushPromises()
    await surface.get('[data-testid="form-drawer-close"]').trigger('click')
    await surface.get('[data-testid="form-drawer-backdrop"]').trigger('click')
    await surface.get('[role="dialog"]').trigger('keydown', { key: 'Escape' })
    expect(surface.props('modelValue')).toBe(true)
    expect(
      surface
        .findAll('button')
        .find((item) => item.text() === '取消')!
        .attributes('disabled'),
    ).toBeDefined()
    finish()
    await flushPromises()
    expect(surface.props('modelValue')).toBe(false)
  })
})
