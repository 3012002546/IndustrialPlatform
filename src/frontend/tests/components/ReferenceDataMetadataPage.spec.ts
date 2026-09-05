import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import ElementPlus, { ElMessageBox } from 'element-plus'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, type ComponentPublicInstance } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'
import { VxeTable } from 'vxe-table'
import { createApiError } from '@/api/errors'
import type {
  EffectiveMetadataSchema,
  MetadataAttribute,
  MetadataDataType,
  MetadataSchema,
  MetadataSchemaSummary,
} from '@/api/referenceData/metadataTypes'
import AppDataTable from '@/components/management/AppDataTable.vue'
import AppFormDrawer from '@/components/management/AppFormDrawer.vue'
import MetadataPage from '@/pages/pc/referenceData/MetadataPage.vue'
import { PERMISSIONS } from '@/permissions'
import { useAuthStore } from '@/stores/authStore'
import { useDeviceStore } from '@/stores/deviceStore'
import { useLocalizationStore } from '@/stores/localizationStore'
import { persistAuthSession } from '../fixtures/session'

const { api } = vi.hoisted(() => ({
  api: {
    listMetadataSchemas: vi.fn(),
    getMetadataSchema: vi.fn(),
    createMetadataSchema: vi.fn(),
    updateMetadataSchema: vi.fn(),
    cloneMetadataSchema: vi.fn(),
    checkMetadataPublication: vi.fn(),
    publishMetadataSchema: vi.fn(),
    disableMetadataSchema: vi.fn(),
    getEffectiveMetadataSchema: vi.fn(),
    getMetadataSchemaRevision: vi.fn(),
    listDictionaries: vi.fn(),
    listAvailableUnitDimensions: vi.fn(),
    getUnitDimensionRevision: vi.fn(),
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

const attribute = (
  nId: string,
  dataType: MetadataDataType,
  overrides: Partial<MetadataAttribute> = {},
): MetadataAttribute => ({
  id: `attribute-${nId}`,
  nId,
  name: nId,
  dataType,
  required: false,
  isArray: false,
  enabled: true,
  sort: 0,
  defaultValue: null,
  minLength: null,
  maxLength: null,
  minValue: null,
  maxValue: null,
  pattern: null,
  dictionaryNId: null,
  referenceTarget: null,
  precision: null,
  scale: null,
  unitDimensionNId: null,
  defaultUnitNId: null,
  unitRevision: null,
  unitSourceScope: null,
  unitSourceTenantNId: null,
  description: null,
  wasPublished: true,
  isFrozen: false,
  isLocked: false,
  ...overrides,
})
const attributes: MetadataAttribute[] = [
  attribute('LABEL', 'String', { minLength: 1, maxLength: 40, pattern: '^[A-Z]+$' }),
  attribute('COUNT', 'Integer', { minValue: '0', maxValue: '100' }),
  attribute('WEIGHT', 'Decimal', {
    defaultValue: '0.000000000001',
    minValue: '-0.000000000001',
    maxValue: '9999999999999999.123456789012',
    precision: 28,
    scale: 12,
    unitDimensionNId: 'MASS',
    defaultUnitNId: 'KG',
    unitRevision: 4,
    unitSourceScope: 'Tenant',
    unitSourceTenantNId: 'TENANT-A',
  }),
  attribute('ACTIVE', 'Boolean', { defaultValue: 'false' }),
  attribute('BUILT_ON', 'Date', { defaultValue: '2026-09-05' }),
  attribute('OBSERVED_ON', 'DateTime', { defaultValue: '2026-09-05T08:00:00+08:00' }),
  attribute('MODE', 'Enum', { dictionaryNId: 'EQUIPMENT_MODE', defaultValue: 'AUTO' }),
  attribute('MATERIAL', 'Reference', { referenceTarget: 'MATERIAL' }),
]
const summary = (overrides: Partial<MetadataSchemaSummary> = {}): MetadataSchemaSummary => ({
  id: 'metadata-draft',
  nId: 'EQUIPMENT',
  name: 'Equipment',
  description: null,
  scopeType: 'Tenant',
  tenantNId: 'TENANT-A',
  revision: 2,
  status: 'Draft',
  sourceRevision: 1,
  attributeCount: attributes.length,
  optimisticVersion: 4,
  concurrencyVersion: 'token-4',
  lastUpdatedOn: '2026-09-05T00:00:00Z',
  publishedOn: null,
  publishedBy: null,
  isFrozen: false,
  isLocked: false,
  ...overrides,
})
const detail = (item = summary()): MetadataSchema => ({ ...item, attributes })
const runtime = (fixed = false): EffectiveMetadataSchema => ({
  nId: 'EQUIPMENT',
  name: 'Equipment',
  description: null,
  sourceScope: 'Tenant',
  sourceTenantNId: 'TENANT-A',
  revision: 2,
  publishedOn: '2026-09-05T00:00:00Z',
  attributes: fixed
    ? [...attributes, { ...attribute('LEGACY', 'String'), enabled: false }]
    : attributes,
})

const permissions = [
  PERMISSIONS.referenceDataDictionaryView,
  PERMISSIONS.referenceDataUnitOfMeasureView,
  PERMISSIONS.referenceDataMetadataView,
  PERMISSIONS.referenceDataMetadataCreate,
  PERMISSIONS.referenceDataMetadataUpdate,
  PERMISSIONS.referenceDataMetadataPublish,
  PERMISSIONS.referenceDataMetadataDisable,
  PERMISSIONS.referenceDataPlatformManage,
]
const wrappers: VueWrapper[] = []
type TableInstance = ComponentPublicInstance<Parameters<typeof AppDataTable>[0]>

async function mountPage(grants = permissions) {
  const pinia = createPinia()
  setActivePinia(pinia)
  persistAuthSession(grants)
  await useAuthStore().restore()
  useLocalizationStore().setLocale('en-US', null)
  useDeviceStore().setOverride('pc')
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: MetadataPage },
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
function table(wrapper: VueWrapper, key: string) {
  const found = wrapper
    .findAllComponents<TableInstance>({ name: 'AppDataTable' })
    .find((item) => item.props('tableKey') === key)
  expect(found, key).toBeDefined()
  return found!
}
function button(wrapper: VueWrapper, label: string) {
  const found = wrapper.findAll('button').find((item) => item.text() === label)
  expect(found, label).toBeDefined()
  return found!
}
function drawer(wrapper: VueWrapper, testId: string) {
  const found = wrapper
    .findAllComponents(AppFormDrawer)
    .find((item) => item.props('modelValue') && item.find(`[data-testid="${testId}"]`).exists())
  expect(found, testId).toBeDefined()
  return found!
}

beforeEach(() => {
  vi.resetAllMocks()
  localStorage.clear()
  sessionStorage.clear()
  vi.stubEnv('VITE_AUTH_MODE', 'mock')
  api.listMetadataSchemas.mockResolvedValue({
    items: [summary()],
    total: 1,
    pageIndex: 1,
    pageSize: 20,
  })
  api.getMetadataSchema.mockResolvedValue(structuredClone(detail()))
  api.updateMetadataSchema.mockResolvedValue(structuredClone(detail()))
  api.createMetadataSchema.mockResolvedValue(structuredClone(detail(summary({ id: 'created' }))))
  api.cloneMetadataSchema.mockResolvedValue(structuredClone(detail(summary({ id: 'clone' }))))
  api.checkMetadataPublication.mockResolvedValue({
    previousRevision: 1,
    addedAttributes: ['WEIGHT'],
    tightenedAttributes: ['LABEL'],
    relaxedAttributes: ['COUNT'],
    disabledAttributes: ['LEGACY'],
    incompatibleChanges: [{ attributeNId: 'WEIGHT', code: 'UnitDimensionChanged' }],
    errors: [],
  })
  api.getEffectiveMetadataSchema.mockResolvedValue(runtime(false))
  api.getMetadataSchemaRevision.mockResolvedValue(runtime(true))
  api.listDictionaries.mockResolvedValue({
    items: [
      {
        id: 'dictionary-mode',
        nId: 'EQUIPMENT_MODE',
        name: 'Equipment mode',
        scopeType: 'Tenant',
        tenantNId: 'TENANT-A',
        revision: 3,
        status: 'Published',
        enabledItemCount: 2,
        optimisticVersion: 1,
        concurrencyVersion: 'dictionary-token',
        lastUpdatedOn: '2026-09-05T00:00:00Z',
        publishedBy: 'tester',
        isFrozen: true,
        isLocked: false,
      },
    ],
    total: 1,
    pageIndex: 1,
    pageSize: 100,
  })
  api.listAvailableUnitDimensions.mockResolvedValue({
    items: [
      {
        nId: 'MASS',
        name: 'Mass',
        sourceScope: 'Tenant',
        sourceTenantNId: 'TENANT-A',
        revision: 4,
        publishedOn: '2026-09-05T00:00:00Z',
        isSystemDefined: false,
        conversionKind: 'Ratio',
        baseUnitNId: 'KG',
        unitCount: 2,
      },
    ],
    total: 1,
    pageIndex: 1,
    pageSize: 100,
  })
  api.getUnitDimensionRevision.mockResolvedValue({
    nId: 'MASS',
    name: 'Mass',
    description: null,
    sourceScope: 'Tenant',
    sourceTenantNId: 'TENANT-A',
    revision: 4,
    status: 'Published',
    sourceRevision: null,
    publishedOn: '2026-09-05T00:00:00Z',
    isSystemDefined: false,
    conversionKind: 'Ratio',
    baseUnitNId: 'KG',
    units: [
      {
        nId: 'KG',
        name: 'Kilogram',
        symbol: 'kg',
        factorToBase: '1',
        offsetToBase: '0',
        decimalPlaces: 3,
        roundingMode: 'ToEven',
        enabled: true,
        sort: 0,
      },
      {
        nId: 'G',
        name: 'Gram',
        symbol: 'g',
        factorToBase: '0.001',
        offsetToBase: '0',
        decimalPlaces: 3,
        roundingMode: 'ToEven',
        enabled: true,
        sort: 1,
      },
    ],
  })
})
afterEach(() => {
  for (const wrapper of wrappers.splice(0)) wrapper.unmount()
  document.body.innerHTML = ''
  vi.restoreAllMocks()
  vi.unstubAllEnvs()
})

describe('Metadata management page', () => {
  it('moves Edit into More below 190px and restores the direct action when widened', async () => {
    api.listMetadataSchemas.mockResolvedValue({
      items: [
        summary(),
        summary({
          id: 'metadata-published',
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
    const actions = () => wrapper.findAll('.metadata-actions')[0]!
    const action = (label: string) =>
      actions()
        .findAll('button')
        .find((item) => item.text() === label)!

    expect(actions().get('[data-testid="metadata-more"]').text()).toBe('More')
    expect(action('Edit').element.closest('[data-testid="dropdown-menu"]')).toBeNull()
    for (const label of ['Publication check', 'Disable']) {
      expect(action(label).element.closest('[data-testid="dropdown-menu"]')).not.toBeNull()
    }
    const publishedActions = wrapper.findAll('.metadata-actions')[1]!
    for (const label of ['Clone new revision', 'Disable', 'Runtime read']) {
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

  it('uses an ordinary list and aborts a superseded server query', async () => {
    const { wrapper } = await mountPage()
    const schemas = table(wrapper, 'reference-data-metadata')
    expect(schemas.props('selection')).toBe('none')
    let finish!: (value: unknown) => void
    api.listMetadataSchemas.mockImplementationOnce(
      () => new Promise((resolve) => (finish = resolve)),
    )
    const first = schemas.props('loader')!({
      pageIndex: 2,
      pageSize: 100,
      queryMode: 'top',
      filters: { keyword: 'asset', scopeType: 'Tenant', status: 'Draft' },
      columns: [],
    })
    const signal = api.listMetadataSchemas.mock.calls.at(-1)![1].signal as AbortSignal
    const second = schemas.props('loader')!({
      pageIndex: 1,
      pageSize: 20,
      queryMode: 'top',
      filters: {},
      columns: [],
    })
    expect(signal.aborted).toBe(true)
    finish!({ items: [], total: 0, pageIndex: 2, pageSize: 100 })
    await Promise.all([first, second])
  })

  it('edits all eight attribute types and preserves fixed unit coordinates and versions', async () => {
    const { wrapper } = await mountPage()
    await button(wrapper, 'Edit').trigger('click')
    await flushPromises()
    const editor = drawer(wrapper, 'metadata-schema-save')
    expect(editor.findAll('[data-testid^="metadata-attribute-type-"]')).toHaveLength(8)
    expect(editor.find('[data-testid="metadata-attribute-dictionary-6"]').exists()).toBe(true)
    expect(editor.find('[data-testid="metadata-attribute-reference-7"]').exists()).toBe(true)
    expect(editor.find('[data-testid="metadata-attribute-unit-revision-2"]').exists()).toBe(true)
    expect(editor.get('[data-testid="metadata-attribute-unit-dimension-2"]').element.tagName).toBe(
      'SELECT',
    )
    expect(editor.get('[data-testid="metadata-attribute-default-unit-2"]').text()).toContain(
      'Kilogram',
    )
    expect(editor.get('[data-testid="metadata-attribute-dictionary-6"]').element.tagName).toBe(
      'SELECT',
    )
    expect(editor.get('[data-testid="metadata-attribute-dictionary-6"]').text()).toContain(
      'Equipment mode',
    )
    await editor.get('[data-testid="metadata-schema-name"]').setValue('Equipment schema')
    await editor.get('[data-testid="metadata-schema-save"]').trigger('click')
    await flushPromises()

    expect(api.updateMetadataSchema).toHaveBeenCalledWith(
      'metadata-draft',
      expect.objectContaining({
        name: 'Equipment schema',
        expectedOptimisticVersion: 4,
        expectedConcurrencyVersion: 'token-4',
        attributes: expect.arrayContaining([
          expect.objectContaining({
            nId: 'WEIGHT',
            defaultValue: '0.000000000001',
            minValue: '-0.000000000001',
            maxValue: '9999999999999999.123456789012',
            unitDimensionNId: 'MASS',
            defaultUnitNId: 'KG',
            unitRevision: 4,
            unitSourceScope: 'Tenant',
            unitSourceTenantNId: 'TENANT-A',
          }),
        ]),
      }),
    )
    expect(api.listDictionaries).toHaveBeenCalled()
    expect(api.listAvailableUnitDimensions).toHaveBeenCalled()
    expect(api.getUnitDimensionRevision).toHaveBeenCalledWith(
      'MASS',
      4,
      'Tenant',
      'TENANT-A',
      expect.anything(),
    )
  })

  it('shows complete publication differences before publishing', async () => {
    const { wrapper } = await mountPage()
    await button(wrapper, 'Publication check').trigger('click')
    await flushPromises()
    const publication = drawer(wrapper, 'metadata-publish-confirm')
    expect(publication.text()).toContain('WEIGHT')
    expect(publication.text()).toContain('LABEL')
    expect(publication.text()).toContain('COUNT')
    expect(publication.text()).toContain('LEGACY')
    expect(publication.text()).toContain('UnitDimensionChanged')
  })

  it('blocks unsafe patterns and decimal defaults outside the declared scale', async () => {
    const { wrapper } = await mountPage()
    await button(wrapper, 'Edit').trigger('click')
    await flushPromises()
    const editor = drawer(wrapper, 'metadata-schema-save')
    await editor.get('[data-testid="metadata-attribute-pattern-0"]').setValue('(a+)+$')
    await editor.get('[data-testid="metadata-attribute-default-2"]').setValue('0.0000000000001')
    await editor.get('[data-testid="metadata-schema-save"]').trigger('click')
    await flushPromises()
    await new Promise((resolve) => setTimeout(resolve, 20))

    expect(api.updateMetadataSchema).not.toHaveBeenCalled()
    expect(editor.text()).toContain('The attribute constraints are invalid')
    expect(editor.text()).toContain('The default value does not match')
  })

  it('compares decimal bounds without rounding distinct values to the same Number', async () => {
    const { wrapper } = await mountPage()
    await button(wrapper, 'Edit').trigger('click')
    await flushPromises()
    const editor = drawer(wrapper, 'metadata-schema-save')
    await editor
      .get('[data-testid="metadata-attribute-min-value-2"]')
      .setValue('9007199254740992.000000000002')
    await editor
      .get('[data-testid="metadata-attribute-max-value-2"]')
      .setValue('9007199254740992.000000000001')
    await editor.get('[data-testid="metadata-schema-save"]').trigger('click')
    await flushPromises()

    expect(api.updateMetadataSchema).not.toHaveBeenCalled()
    expect(editor.text()).toContain('The attribute constraints are invalid')
  })

  it('reads current effective and explicit fixed history without requiring admin ids', async () => {
    api.listMetadataSchemas.mockResolvedValue({
      items: [summary({ status: 'Published', publishedOn: '2026-09-05T00:00:00Z' })],
      total: 1,
      pageIndex: 1,
      pageSize: 20,
    })
    const { wrapper } = await mountPage()
    await button(wrapper, 'Runtime read').trigger('click')
    await flushPromises()
    const viewer = drawer(wrapper, 'metadata-runtime-mode')
    expect(api.getMetadataSchemaRevision).toHaveBeenCalledWith(
      'EQUIPMENT',
      2,
      'Tenant',
      'TENANT-A',
      expect.anything(),
    )
    expect(viewer.text()).toContain('LEGACY')
    await viewer.get('[data-testid="metadata-runtime-mode"]').setValue('Current')
    await flushPromises()
    expect(api.getEffectiveMetadataSchema).toHaveBeenCalledWith('EQUIPMENT', expect.anything())
    expect(viewer.text()).not.toContain('LEGACY')
  })

  it('preserves dirty input after 409 and guards route navigation', async () => {
    api.updateMetadataSchema.mockRejectedValue(
      createApiError('business', 'Conflict', 'corr', {
        status: 409,
        code: 'REF-CONCURRENCY-CONFLICT',
        traceId: 'trace-metadata',
      }),
    )
    const confirm = vi.spyOn(ElMessageBox, 'confirm').mockRejectedValue(new Error('cancel'))
    const { wrapper, router } = await mountPage()
    await button(wrapper, 'Edit').trigger('click')
    await flushPromises()
    const editor = drawer(wrapper, 'metadata-schema-save')
    await editor.get('[data-testid="metadata-schema-name"]').setValue('Unsaved schema')
    await editor.get('[data-testid="metadata-schema-save"]').trigger('click')
    await flushPromises()
    expect(
      (editor.get('[data-testid="metadata-schema-name"]').element as HTMLInputElement).value,
    ).toBe('Unsaved schema')
    expect(editor.text()).toContain('Your unsaved input is preserved')
    expect(editor.text()).toContain('Copy unsaved input')
    expect(editor.text()).toContain('Reload')
    await router.push('/elsewhere')
    expect(router.currentRoute.value.path).toBe('/')
    expect(confirm).toHaveBeenCalled()
  })
})
