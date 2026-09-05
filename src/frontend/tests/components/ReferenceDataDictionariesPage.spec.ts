import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import ElementPlus from 'element-plus'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'
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
  const found = wrapper.findAll('button').find((button) => button.text() === label)
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
    await wrapper.get('[data-testid="app-data-table-query-toggle"]').trigger('click')
    await flushPromises()
    await wrapper.get('[data-testid="app-data-table-header-filter-name"]').setValue('Open')
    await wrapper.get('[data-testid="app-data-table-header-filter-name"]').trigger('keyup.enter')
    await flushPromises()
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
    await button(wrapper, '详情')
    await wrapper.get('[data-testid="dictionary-create"]').trigger('click')
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
