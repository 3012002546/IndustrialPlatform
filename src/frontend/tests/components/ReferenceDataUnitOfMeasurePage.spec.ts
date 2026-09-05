import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import ElementPlus, { ElMessageBox } from 'element-plus'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, type ComponentPublicInstance } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'
import { VxeTable } from 'vxe-table'
import { createApiError } from '@/api/errors'
import type {
  RuntimeUnitDimension,
  UnitDimension,
  UnitDimensionSummary,
} from '@/api/referenceData/unitOfMeasureTypes'
import AppDataTable from '@/components/management/AppDataTable.vue'
import AppFormDrawer from '@/components/management/AppFormDrawer.vue'
import UnitOfMeasurePage from '@/pages/pc/referenceData/UnitOfMeasurePage.vue'
import { PERMISSIONS } from '@/permissions'
import { useAuthStore } from '@/stores/authStore'
import { useDeviceStore } from '@/stores/deviceStore'
import { useLocalizationStore } from '@/stores/localizationStore'
import { persistAuthSession } from '../fixtures/session'

const { api } = vi.hoisted(() => ({
  api: {
    listUnitDimensions: vi.fn(),
    getUnitDimension: vi.fn(),
    createUnitDimension: vi.fn(),
    updateUnitDimension: vi.fn(),
    cloneUnitDimension: vi.fn(),
    publishUnitDimension: vi.fn(),
    disableUnitDimension: vi.fn(),
    getCurrentUnitDimension: vi.fn(),
    getUnitDimensionRevision: vi.fn(),
    convertUnit: vi.fn(),
  },
}))
vi.mock('@/api/referenceData', () => ({ getReferenceDataApi: () => api }))
vi.mock('element-plus/es/components/focus-trap/index', () => ({
  ElFocusTrap: defineComponent({ template: '<div><slot /></div>' }),
}))

const SelectStub = defineComponent({
  inheritAttrs: false,
  props: ['modelValue', 'disabled'],
  emits: ['update:modelValue', 'change'],
  template:
    '<select v-bind="$attrs" :value="modelValue" :disabled="disabled" @change="$emit(\'update:modelValue\', $event.target.value); $emit(\'change\', $event.target.value)"><slot /></select>',
})
const OptionStub = defineComponent({
  props: ['value', 'label'],
  template: '<option :value="value">{{ label }}</option>',
})
const DropdownStub = defineComponent({
  template:
    '<div data-testid="dropdown"><slot /><div data-testid="dropdown-menu"><slot name="dropdown" /></div></div>',
})
const DropdownMenuStub = defineComponent({ template: '<div><slot /></div>' })
const DropdownItemStub = defineComponent({
  inheritAttrs: false,
  props: ['disabled'],
  template: '<button type="button" v-bind="$attrs" :disabled="disabled"><slot /></button>',
})
const RadioGroupStub = defineComponent({
  inheritAttrs: false,
  props: ['modelValue'],
  emits: ['update:modelValue', 'change'],
  template: '<div v-bind="$attrs"><slot /></div>',
})
const RadioButtonStub = defineComponent({
  props: ['value'],
  template: '<button type="button">{{ value }}</button>',
})

const unit = (nId: string, factorToBase: string) => ({
  id: `unit-${nId}`,
  nId,
  name: nId === 'M' ? 'Metre' : 'Millimetre',
  symbol: nId.toLowerCase(),
  factorToBase,
  offsetToBase: '0',
  decimalPlaces: 3,
  roundingMode: 'ToEven' as const,
  enabled: true,
  sort: nId === 'M' ? 0 : 1,
  isFrozen: false,
  isLocked: false,
})
const summary = (overrides: Partial<UnitDimensionSummary> = {}): UnitDimensionSummary => ({
  id: 'tenant-draft',
  nId: 'LENGTH',
  name: 'Tenant length',
  description: null,
  scopeType: 'Tenant',
  tenantNId: 'TENANT-A',
  revision: 2,
  status: 'Draft',
  sourceRevision: 1,
  isSystemDefined: false,
  conversionKind: 'Ratio',
  baseUnitNId: 'M',
  unitCount: 2,
  optimisticVersion: 4,
  concurrencyVersion: 'token-4',
  lastUpdatedOn: '2026-09-05T00:00:00Z',
  publishedOn: null,
  publishedBy: null,
  isFrozen: false,
  isLocked: false,
  ...overrides,
})
const detail = (item: UnitDimensionSummary): UnitDimension => {
  return { ...item, units: [unit('M', '1'), unit('MM', '0.001000000000')] }
}
const runtime = (item: UnitDimensionSummary): RuntimeUnitDimension => ({
  nId: item.nId,
  name: item.name,
  description: item.description,
  sourceScope: item.scopeType,
  sourceTenantNId: item.tenantNId,
  revision: item.revision,
  status: item.status,
  sourceRevision: item.sourceRevision,
  publishedOn: item.publishedOn,
  isSystemDefined: item.isSystemDefined,
  conversionKind: item.conversionKind,
  baseUnitNId: item.baseUnitNId,
  units: [unit('M', '1'), unit('MM', '0.001000000000')],
})

const editPermissions = [
  PERMISSIONS.referenceDataUnitOfMeasureView,
  PERMISSIONS.referenceDataUnitOfMeasureCreate,
  PERMISSIONS.referenceDataUnitOfMeasureUpdate,
  PERMISSIONS.referenceDataUnitOfMeasurePublish,
  PERMISSIONS.referenceDataUnitOfMeasureDisable,
  PERMISSIONS.referenceDataPlatformManage,
]
const wrappers: VueWrapper[] = []
type TableInstance = ComponentPublicInstance<Parameters<typeof AppDataTable>[0]>

async function mountPage(permissions = editPermissions) {
  const pinia = createPinia()
  setActivePinia(pinia)
  persistAuthSession(permissions)
  await useAuthStore().restore()
  useLocalizationStore().setLocale('en-US', null)
  useDeviceStore().setOverride('pc')
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: UnitOfMeasurePage },
      { path: '/elsewhere', component: defineComponent({ template: '<p>Elsewhere</p>' }) },
    ],
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
        'el-radio-group': RadioGroupStub,
        'el-radio-button': RadioButtonStub,
        'el-dropdown': DropdownStub,
        'el-dropdown-menu': DropdownMenuStub,
        'el-dropdown-item': DropdownItemStub,
      },
    },
  })
  wrappers.push(wrapper)
  await flushPromises()
  return { wrapper, router }
}
function button(wrapper: VueWrapper, label: string, index = 0) {
  const items = wrapper.findAll('button').filter((item) => item.text() === label)
  expect(items[index], `${label}[${index}]`).toBeDefined()
  return items[index]!
}
function drawer(wrapper: VueWrapper, testId: string) {
  const result = wrapper
    .findAllComponents(AppFormDrawer)
    .find((item) => item.props('modelValue') && item.find(`[data-testid="${testId}"]`).exists())
  expect(result, testId).toBeDefined()
  return result!
}

beforeEach(() => {
  vi.resetAllMocks()
  localStorage.clear()
  sessionStorage.clear()
  vi.stubEnv('VITE_AUTH_MODE', 'mock')
  const tenant = summary()
  const system = summary({
    id: 'system-length',
    name: 'Platform length',
    scopeType: 'Platform',
    tenantNId: null,
    revision: 1,
    status: 'Published',
    sourceRevision: null,
    isSystemDefined: true,
    optimisticVersion: 1,
    concurrencyVersion: 'system-token',
    publishedOn: '2026-09-01T00:00:00Z',
    publishedBy: 'SYSTEM',
  })
  api.listUnitDimensions.mockResolvedValue({
    items: [tenant, system],
    total: 2,
    pageIndex: 1,
    pageSize: 20,
  })
  api.getUnitDimension.mockImplementation(async (id: string) =>
    detail(id === system.id ? system : tenant),
  )
  api.createUnitDimension.mockImplementation(async (request) =>
    detail(summary({ id: 'created', nId: request.nId, name: request.name })),
  )
  api.updateUnitDimension.mockResolvedValue(detail(tenant))
  api.cloneUnitDimension.mockResolvedValue(detail(summary({ id: 'clone', revision: 3 })))
  api.publishUnitDimension.mockResolvedValue(
    detail({ ...tenant, status: 'Published', publishedOn: '2026-09-05T00:00:00Z' }),
  )
  api.getCurrentUnitDimension.mockResolvedValue(runtime(system))
  api.getUnitDimensionRevision.mockImplementation(async () => runtime(system))
  api.convertUnit.mockResolvedValue({
    unitDimensionNId: 'LENGTH',
    unitRevision: 1,
    sourceScope: 'Platform',
    sourceTenantNId: null,
    fromUnitNId: 'M',
    toUnitNId: 'MM',
    inputValue: '0',
    resultValue: '0',
    wasRounded: false,
    conversionSnapshot: {
      sourceFactorToBase: '1',
      sourceOffsetToBase: '0',
      targetFactorToBase: '0.001000000000',
      targetOffsetToBase: '0',
      decimalPlaces: 3,
      roundingMode: 'ToEven',
    },
  })
})
afterEach(() => {
  for (const wrapper of wrappers.splice(0)) wrapper.unmount()
  vi.restoreAllMocks()
  vi.unstubAllEnvs()
})

describe('UnitOfMeasurePage', () => {
  it('moves Edit into More below 190px and restores the direct action when widened', async () => {
    api.listUnitDimensions.mockResolvedValue({
      items: [
        summary(),
        summary({
          id: 'tenant-published',
          revision: 1,
          status: 'Published',
          publishedOn: '2026-09-04T00:00:00Z',
        }),
      ],
      total: 2,
      pageIndex: 1,
      pageSize: 20,
    })
    const { wrapper } = await mountPage()
    const actions = () => wrapper.findAll('.unit-actions')[0]!
    const action = (label: string) =>
      actions()
        .findAll('button')
        .find((item) => item.text() === label)!

    expect(actions().get('[data-testid="unit-dimension-more"]').text()).toBe('More')
    expect(action('Edit').element.closest('[data-testid="dropdown-menu"]')).toBeNull()
    for (const label of ['Publish', 'Disable']) {
      expect(action(label).element.closest('[data-testid="dropdown-menu"]')).not.toBeNull()
    }
    const publishedActions = wrapper.findAll('.unit-actions')[1]!
    for (const label of ['Clone new revision', 'Disable', 'Try conversion']) {
      const item = publishedActions.findAll('button').find((button) => button.text() === label)!
      expect(item.element.closest('[data-testid="dropdown-menu"]')).not.toBeNull()
    }

    wrapper.findComponent(VxeTable).vm.$emit('resizable-change', {
      resizeColumn: { field: '__actions' },
      resizeWidth: 120,
    })
    await flushPromises()

    expect(action('Edit').element.closest('[data-testid="dropdown-menu"]')).not.toBeNull()
    expect(action('Details').element.closest('[data-testid="dropdown-menu"]')).toBeNull()

    wrapper.findComponent(VxeTable).vm.$emit('resizable-change', {
      resizeColumn: { field: '__actions' },
      resizeWidth: 220,
    })
    await flushPromises()

    expect(action('Edit').element.closest('[data-testid="dropdown-menu"]')).toBeNull()
  })

  it('keeps platform and tenant names side by side in an ordinary non-selecting list and protects system definitions', async () => {
    const { wrapper } = await mountPage()
    const table = wrapper.findComponent<TableInstance>(AppDataTable)
    expect(table.props('selection')).toBe('none')
    expect(wrapper.text()).toContain('Tenant length')
    expect(wrapper.text()).toContain('Platform length')

    await button(wrapper, 'Details', 1).trigger('click')
    await flushPromises()
    expect(wrapper.text()).toContain('Versioned seeds maintain this system dimension')
    expect(
      wrapper.findAll('button').filter((item) => item.text() === 'Clone new revision'),
    ).toHaveLength(0)
  })

  it('creates the aggregate in one drawer and preserves decimal strings while forcing ratio offsets to zero', async () => {
    api.listUnitDimensions.mockResolvedValue({ items: [], total: 0, pageIndex: 1, pageSize: 20 })
    const { wrapper } = await mountPage()
    await wrapper.get('[data-testid="unit-dimension-create"]').trigger('click')
    const editor = drawer(wrapper, 'unit-dimension-save')
    await editor.get('[data-testid="unit-dimension-nid"]').setValue('LENGTH_PRECISE')
    await editor.get('[data-testid="unit-dimension-name"]').setValue('Precise length')
    await editor.get('[data-testid="unit-definition-nid-0"]').setValue('M')
    await editor.findAll('input[aria-label="Unit name"]')[0]!.setValue('Metre')
    await editor.findAll('input[aria-label="Symbol"]')[0]!.setValue('m')
    await wrapper.get('[data-testid="unit-definition-add"]').trigger('click')
    await flushPromises()
    await editor.get('[data-testid="unit-definition-nid-1"]').setValue('MM')
    await editor.findAll('input[aria-label="Unit name"]')[1]!.setValue('Millimetre')
    await editor.findAll('input[aria-label="Symbol"]')[1]!.setValue('mm')
    await editor.get('[data-testid="unit-definition-factor-1"]').setValue('0.001000000000')
    await editor.get('[data-testid="unit-dimension-base-unit"]').setValue('M')
    await flushPromises()
    await editor.get('[data-testid="unit-dimension-save"]').trigger('click')
    await flushPromises()

    expect(api.createUnitDimension).toHaveBeenCalledWith(
      expect.objectContaining({
        nId: 'LENGTH_PRECISE',
        conversionKind: 'Ratio',
        baseUnitNId: 'M',
        units: [
          expect.objectContaining({ nId: 'M', factorToBase: '1', offsetToBase: '0' }),
          expect.objectContaining({ nId: 'MM', factorToBase: '0.001000000000', offsetToBase: '0' }),
        ],
      }),
    )
  })

  it('reads a fixed source and revision for server conversion and renders a zero result', async () => {
    const published = summary({
      status: 'Published',
      publishedOn: '2026-09-05T00:00:00Z',
      publishedBy: 'operator',
    })
    api.listUnitDimensions.mockResolvedValue({
      items: [published],
      total: 1,
      pageIndex: 1,
      pageSize: 20,
    })
    api.getUnitDimensionRevision.mockResolvedValue(runtime(published))
    const { wrapper } = await mountPage()
    await button(wrapper, 'Try conversion').trigger('click')
    await flushPromises()
    const conversion = drawer(wrapper, 'unit-conversion-submit')
    await conversion.get('[data-testid="unit-conversion-source-unit"]').setValue('M')
    await conversion.get('[data-testid="unit-conversion-target-unit"]').setValue('MM')
    await conversion.get('[data-testid="unit-conversion-value"]').setValue('0')
    await conversion.get('[data-testid="unit-conversion-submit"]').trigger('click')
    await flushPromises()

    expect(api.getUnitDimensionRevision).toHaveBeenCalledWith(
      'LENGTH',
      2,
      'Tenant',
      'TENANT-A',
      expect.anything(),
    )
    expect(api.convertUnit).toHaveBeenCalledWith(
      expect.objectContaining({
        sourceScope: 'Tenant',
        sourceTenantNId: 'TENANT-A',
        unitDimensionNId: 'LENGTH',
        unitRevision: 2,
        value: '0',
      }),
      expect.anything(),
    )
    expect(conversion.get('[data-testid="unit-conversion-result"]').text()).toContain('0')
  })

  it('keeps form input after a 409 and exposes copy and reload recovery actions', async () => {
    api.updateUnitDimension.mockRejectedValue(
      createApiError('business', 'Conflict', 'corr', {
        status: 409,
        code: 'REF-CONCURRENCY-CONFLICT',
        traceId: 'trace-conflict',
      }),
    )
    const { wrapper } = await mountPage()
    await button(wrapper, 'Edit').trigger('click')
    await flushPromises()
    const editor = drawer(wrapper, 'unit-dimension-save')
    await editor.get('[data-testid="unit-dimension-name"]').setValue('Unsaved precise length')
    await editor.get('[data-testid="unit-dimension-save"]').trigger('click')
    await flushPromises()

    expect(
      (editor.get('[data-testid="unit-dimension-name"]').element as HTMLInputElement).value,
    ).toBe('Unsaved precise length')
    expect(editor.text()).toContain('Your unsaved input is preserved')
    expect(editor.text()).toContain('Copy unsaved input')
    expect(editor.text()).toContain('Reload')
    expect(api.updateUnitDimension).toHaveBeenCalledWith(
      'tenant-draft',
      expect.objectContaining({
        expectedOptimisticVersion: 4,
        expectedConcurrencyVersion: 'token-4',
      }),
    )
  })

  it('guards navigation while the aggregate drawer has unsaved edits', async () => {
    api.listUnitDimensions.mockResolvedValue({ items: [], total: 0, pageIndex: 1, pageSize: 20 })
    const confirm = vi.spyOn(ElMessageBox, 'confirm').mockRejectedValue(new Error('cancel'))
    const { wrapper, router } = await mountPage()
    await wrapper.get('[data-testid="unit-dimension-create"]').trigger('click')
    await drawer(wrapper, 'unit-dimension-save')
      .get('[data-testid="unit-dimension-name"]')
      .setValue('Unsaved dimension')

    await router.push('/elsewhere')

    expect(router.currentRoute.value.path).toBe('/')
    expect(confirm).toHaveBeenCalled()
  })
})
