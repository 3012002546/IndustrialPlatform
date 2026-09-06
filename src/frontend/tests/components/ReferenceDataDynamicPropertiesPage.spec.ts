import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import ElementPlus, { ElMessageBox } from 'element-plus'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, type ComponentPublicInstance } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'
import { VxeTable } from 'vxe-table'
import { createApiError } from '@/api/errors'
import type {
  DynamicConfiguration,
  DynamicConfigurationSummary,
  DynamicField,
  DynamicRecord,
} from '@/api/referenceData/dynamicTypes'
import AppDataTable from '@/components/management/AppDataTable.vue'
import AppFormDrawer from '@/components/management/AppFormDrawer.vue'
import ConfigurationValueEditor from '@/pages/pc/referenceData/ConfigurationValueEditor.vue'
import DynamicPropertiesPage from '@/pages/pc/referenceData/DynamicPropertiesPage.vue'
import { PERMISSIONS } from '@/permissions'
import { useAuthStore } from '@/stores/authStore'
import { useDeviceStore } from '@/stores/deviceStore'
import { useLocalizationStore } from '@/stores/localizationStore'
import { persistAuthSession } from '../fixtures/session'

const { api } = vi.hoisted(() => ({
  api: {
    listDynamicConfigurations: vi.fn(),
    getDynamicConfiguration: vi.fn(),
    createDynamicConfiguration: vi.fn(),
    updateDynamicConfiguration: vi.fn(),
    cloneDynamicConfiguration: vi.fn(),
    checkDynamicPublication: vi.fn(),
    publishDynamicConfiguration: vi.fn(),
    disableDynamicConfiguration: vi.fn(),
    listDynamicRecords: vi.fn(),
    addDynamicRecord: vi.fn(),
    updateDynamicRecord: vi.fn(),
    disableDynamicRecord: vi.fn(),
    getEffectiveDictionary: vi.fn(),
    listDictionaries: vi.fn(),
    getDictionary: vi.fn(),
  },
}))
vi.mock('@/api/referenceData', () => ({ getReferenceDataApi: () => api }))
vi.mock('element-plus/es/components/focus-trap/index', () => ({
  ElFocusTrap: defineComponent({ template: '<div><slot /></div>' }),
}))

const wrappers: VueWrapper[] = []
type TableInstance = ComponentPublicInstance<Parameters<typeof AppDataTable>[0]>
type TableWrapper = VueWrapper<TableInstance>
const SelectStub = defineComponent({
  props: ['modelValue', 'disabled'],
  emits: ['update:modelValue'],
  template:
    '<select :value="modelValue" :disabled="disabled" @change="$emit(\'update:modelValue\', $event.target.value)"><slot /></select>',
})
const OptionStub = defineComponent({
  props: ['value', 'label'],
  template: '<option :value="value">{{ label }}</option>',
})

const field = (
  nId: string,
  dataType: DynamicField['dataType'],
  overrides: Partial<DynamicField> = {},
): DynamicField => ({
  id: `field-${nId}`,
  nId,
  name: nId,
  dataType,
  required: false,
  enabled: true,
  sort: 0,
  defaultValueJson: null,
  defaultValue: null,
  minLength: null,
  maxLength: null,
  minValueJson: null,
  maxValueJson: null,
  minValue: null,
  maxValue: null,
  scale: null,
  pattern: null,
  dictionaryNId: null,
  referenceTarget: null,
  description: null,
  hasHadValue: false,
  wasPublished: false,
  ...overrides,
})
const fields: DynamicField[] = [
  field('TEXT', 'String'),
  field('COUNT', 'Integer'),
  field('AMOUNT', 'Decimal'),
  field('FLAG', 'Boolean'),
  field('DAY', 'Date'),
  field('STAMP', 'DateTime'),
  field('STATE', 'Enum', { dictionaryNId: 'STATE' }),
  field('PAYLOAD', 'Json'),
  field('TARGET', 'Reference', { referenceTarget: 'Equipment' }),
]
const summaryA: DynamicConfigurationSummary = {
  id: 'definition-a',
  nId: 'ALARM_LIMITS',
  name: 'Alarm limits',
  scopeType: 'Tenant',
  tenantNId: 'tenant-a',
  revision: 2,
  status: 'Draft',
  fieldCount: fields.length,
  recordCount: 1,
  valueCount: 9,
  optimisticVersion: 4,
  concurrencyVersion: 'token-a-4',
  lastUpdatedOn: '2026-09-05T00:00:00Z',
  isFrozen: false,
  isLocked: false,
  publishedOn: null,
}
const summaryB: DynamicConfigurationSummary = {
  ...summaryA,
  id: 'definition-b',
  nId: 'MAPPING',
  name: 'Mapping',
  concurrencyVersion: 'token-b-4',
}
const detailA: DynamicConfiguration = {
  ...summaryA,
  description: null,
  fields,
  publishedBy: null,
}
const detailB: DynamicConfiguration = {
  ...summaryB,
  description: null,
  fields: [field('SOURCE', 'String')],
  publishedBy: null,
}
const record: DynamicRecord = {
  id: 'record-1',
  nId: 'ROW_A',
  name: 'Row A',
  category: 'DEFAULT',
  sort: 0,
  enabled: true,
  values: {},
  valuesJson: {},
  revision: 2,
  isFrozen: false,
  isLocked: false,
}
const editPermissions: string[] = [
  PERMISSIONS.referenceDataDynamicPropertyView,
  PERMISSIONS.referenceDataDynamicPropertyCreate,
  PERMISSIONS.referenceDataDynamicPropertyUpdate,
  PERMISSIONS.referenceDataDynamicPropertyPublish,
  PERMISSIONS.referenceDataDynamicPropertyDisable,
]

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
      { path: '/', component: DynamicPropertiesPage },
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
  return { wrapper, router }
}
function table(wrapper: VueWrapper, tableKey: string): TableWrapper {
  const result = wrapper
    .findAllComponents<TableInstance>({ name: 'AppDataTable' })
    .find((item) => item.props('tableKey') === tableKey)
  expect(result, tableKey).toBeDefined()
  return result!
}
async function select(wrapper: VueWrapper, row: DynamicConfigurationSummary) {
  table(wrapper, 'reference-data-dynamic-properties')
    .findComponent(VxeTable)
    .vm.$emit('cell-click', {
      row,
      column: { field: 'name' },
    })
  await flushPromises()
}
async function clickButton(wrapper: VueWrapper, label: string) {
  const result = wrapper.findAll('button').find((item) => item.text() === label)
  expect(result, label).toBeDefined()
  await result!.trigger('click')
  await flushPromises()
}
function openDrawer(wrapper: VueWrapper, testId: string) {
  const drawer = wrapper
    .findAllComponents(AppFormDrawer)
    .find((item) => item.props('modelValue') && item.find(`[data-testid="${testId}"]`).exists())
  expect(drawer, testId).toBeDefined()
  return drawer!
}

beforeEach(() => {
  vi.resetAllMocks()
  localStorage.clear()
  sessionStorage.clear()
  vi.stubEnv('VITE_AUTH_MODE', 'mock')
  api.listDynamicConfigurations.mockResolvedValue({
    items: [summaryA, summaryB],
    total: 2,
    pageIndex: 1,
    pageSize: 20,
  })
  api.getDynamicConfiguration.mockImplementation(async (id: string) =>
    structuredClone(id === summaryA.id ? detailA : detailB),
  )
  api.listDynamicRecords.mockResolvedValue({
    items: [record],
    total: 1,
    pageIndex: 1,
    pageSize: 20,
  })
  api.checkDynamicPublication.mockResolvedValue({
    previousRevision: 1,
    addedFields: ['STAMP'],
    changedFields: ['AMOUNT'],
    disabledFields: [],
    recordCount: 1,
    valueCount: 9,
    errors: [],
  })
  api.getEffectiveDictionary.mockResolvedValue({
    nId: 'STATE',
    name: 'State',
    sourceScope: 'Tenant',
    sourceTenantNId: 'tenant-a',
    revision: 1,
    publishedOn: '2026-09-05T00:00:00Z',
    items: [{ nId: 'OPEN', name: 'Open', description: null, sort: 0, enabled: true }],
  })
  api.listDictionaries.mockResolvedValue({
    items: [
      {
        id: 'dictionary-state',
        nId: 'STATE',
        name: 'State',
        scopeType: 'Platform',
        tenantNId: null,
        revision: 1,
        status: 'Published',
        enabledItemCount: 1,
        optimisticVersion: 1,
        concurrencyVersion: 'dictionary-token',
        lastUpdatedOn: '2026-09-05T00:00:00Z',
        publishedBy: 'admin',
        isFrozen: false,
        isLocked: false,
      },
    ],
    total: 1,
    pageIndex: 1,
    pageSize: 100,
  })
  api.getDictionary.mockResolvedValue({
    id: 'dictionary-state',
    nId: 'STATE',
    name: 'State',
    description: null,
    scopeType: 'Platform',
    tenantNId: null,
    revision: 1,
    status: 'Published',
    items: [{ nId: 'OPEN', name: 'Open', description: null, sort: 0, enabled: true }],
    optimisticVersion: 1,
    concurrencyVersion: 'dictionary-token',
    lastUpdatedOn: '2026-09-05T00:00:00Z',
    publishedOn: '2026-09-05T00:00:00Z',
    publishedBy: 'admin',
    isFrozen: false,
    isLocked: false,
  })
  api.updateDynamicConfiguration.mockResolvedValue(structuredClone(detailA))
  api.createDynamicConfiguration.mockResolvedValue({
    ...structuredClone(detailA),
    id: 'definition-new',
    nId: 'NEW_CONFIGURATION',
    name: 'New configuration',
    optimisticVersion: 1,
    concurrencyVersion: 'token-new-1',
  })
  api.addDynamicRecord.mockResolvedValue({
    record,
    optimisticVersion: 5,
    concurrencyVersion: 'token-a-5',
  })
})
afterEach(() => {
  for (const wrapper of wrappers.splice(0)) wrapper.unmount()
  document.body.innerHTML = ''
  vi.restoreAllMocks()
  vi.unstubAllEnvs()
})

describe('Dynamic property management page', () => {
  it('keeps the page heading free of an unlabeled record count', async () => {
    const { wrapper } = await mountPage()

    expect(wrapper.find('.app-page__heading-meta').exists()).toBe(false)
  })

  it('loads details only after single selection and rejects an aborted late response', async () => {
    let finishA!: (value: DynamicConfiguration) => void
    api.getDynamicConfiguration.mockImplementationOnce(
      () => new Promise((resolve) => (finishA = resolve)),
    )
    const { wrapper } = await mountPage()
    const master = table(wrapper, 'reference-data-dynamic-properties')
    expect(master.props('selection')).toBe('none')
    expect(master.props('activeRowKey')).toBeNull()
    expect(master.props('columns')).toHaveLength(1)
    expect(api.getDynamicConfiguration).not.toHaveBeenCalled()
    await select(wrapper, summaryA)
    const firstSignal = api.getDynamicConfiguration.mock.calls[0]![1].signal as AbortSignal
    await select(wrapper, summaryB)
    expect(firstSignal.aborted).toBe(true)
    expect(wrapper.get('.dynamic-property-context h2').text()).toBe('Mapping')
    finishA(structuredClone(detailA))
    await flushPromises()
    expect(master.props('activeRowKey')).toBe(summaryB.id)
    expect(wrapper.get('.dynamic-property-context h2').text()).toBe('Mapping')
    expect(wrapper.text()).not.toContain('AMOUNT')
  })

  it('passes record paging and all three filters to the server and aborts the superseded load', async () => {
    const { wrapper } = await mountPage()
    await select(wrapper, summaryA)
    const records = table(wrapper, 'reference-data-dynamic-records')
    let finish!: (value: unknown) => void
    api.listDynamicRecords.mockImplementationOnce(
      () => new Promise((resolve) => (finish = resolve)),
    )
    const first = records.props('loader')!({
      pageIndex: 2,
      pageSize: 100,
      queryMode: 'top',
      filters: { keyword: 'row', nId: 'ROW_A', category: 'DEFAULT' },
      columns: [],
    })
    const firstSignal = api.listDynamicRecords.mock.calls.at(-1)![2].signal as AbortSignal
    const second = records.props('loader')!({
      pageIndex: 1,
      pageSize: 20,
      queryMode: 'top',
      filters: {},
      columns: [],
    })
    expect(firstSignal.aborted).toBe(true)
    expect(api.listDynamicRecords).toHaveBeenNthCalledWith(
      api.listDynamicRecords.mock.calls.length - 1,
      summaryA.id,
      { pageIndex: 2, pageSize: 100, keyword: 'row', nId: 'ROW_A', category: 'DEFAULT' },
      expect.anything(),
    )
    finish!({ items: [], total: 0, pageIndex: 2, pageSize: 100 })
    await Promise.all([first, second])
  })

  it('edits a field with the typed default editor and aggregate version tokens', async () => {
    const { wrapper } = await mountPage()
    await select(wrapper, summaryA)
    await clickButton(wrapper, 'Edit field')
    const drawer = openDrawer(wrapper, 'dynamic-field-save')
    expect(drawer.findComponent(ConfigurationValueEditor).exists()).toBe(true)
    await drawer.get('input[data-testid="dynamic-field-name"]').setValue('Text label')
    await drawer.get('[data-testid="dynamic-field-save"]').trigger('click')
    await flushPromises()
    expect(api.updateDynamicConfiguration).toHaveBeenCalledWith(
      summaryA.id,
      expect.objectContaining({
        expectedOptimisticVersion: 4,
        expectedConcurrencyVersion: 'token-a-4',
        fields: expect.arrayContaining([
          expect.objectContaining({ nId: 'TEXT', name: 'Text label' }),
        ]),
      }),
    )
  })

  it('creates a separate empty draft even when another definition is selected', async () => {
    const { wrapper } = await mountPage()
    await select(wrapper, summaryA)
    await wrapper.get('[data-testid="dynamic-property-create"]').trigger('click')
    await flushPromises()
    const drawer = openDrawer(wrapper, 'dynamic-definition-save')
    await drawer.get('input[data-testid="dynamic-definition-nid"]').setValue('NEW_CONFIGURATION')
    await drawer.get('input[data-testid="dynamic-definition-name"]').setValue('New configuration')
    await drawer.get('[data-testid="dynamic-definition-save"]').trigger('click')
    await flushPromises()
    expect(api.createDynamicConfiguration).toHaveBeenCalledWith({
      nId: 'NEW_CONFIGURATION',
      name: 'New configuration',
      description: null,
      scopeType: 'Tenant',
      scopeId: null,
      fields: [],
    })
    expect(api.updateDynamicConfiguration).not.toHaveBeenCalled()
  })

  it('renders explicit editors for all nine field types and writes only configured values', async () => {
    const { wrapper } = await mountPage()
    await select(wrapper, summaryA)
    await clickButton(wrapper, 'Add record')
    const drawer = openDrawer(wrapper, 'dynamic-record-save')
    expect(drawer.findAllComponents(ConfigurationValueEditor)).toHaveLength(9)
    await drawer.get('input[data-testid="dynamic-record-nid"]').setValue('ROW_B')
    const textEditor = drawer
      .findAllComponents(ConfigurationValueEditor)
      .find((item) => item.props('dataType') === 'String')!
    textEditor.vm.$emit('update:modelValue', '"configured"')
    await drawer.get('[data-testid="dynamic-record-save"]').trigger('click')
    await flushPromises()
    expect(api.addDynamicRecord).toHaveBeenCalledWith(
      summaryA.id,
      expect.objectContaining({
        expectedOptimisticVersion: 4,
        expectedConcurrencyVersion: 'token-a-4',
        valuesJson: { TEXT: '"configured"' },
      }),
    )
  })

  it('preserves record input after conflict and offers copy and guarded reload', async () => {
    api.addDynamicRecord.mockRejectedValue(
      createApiError('business', 'Conflict', 'corr', {
        status: 409,
        code: 'REF-CONCURRENCY-CONFLICT',
        traceId: 'trace-dynamic',
      }),
    )
    const writeText = vi.fn().mockResolvedValue(undefined)
    Object.assign(navigator, { clipboard: { writeText } })
    const confirm = vi.spyOn(ElMessageBox, 'confirm').mockResolvedValue('confirm' as never)
    const { wrapper } = await mountPage()
    await select(wrapper, summaryA)
    await clickButton(wrapper, 'Add record')
    const drawer = openDrawer(wrapper, 'dynamic-record-save')
    await drawer.get('input[data-testid="dynamic-record-nid"]').setValue('LOCAL_ROW')
    await drawer.get('[data-testid="dynamic-record-save"]').trigger('click')
    await flushPromises()
    expect(
      (drawer.get('input[data-testid="dynamic-record-nid"]').element as HTMLInputElement).value,
    ).toBe('LOCAL_ROW')
    expect(drawer.text()).toContain('Copy unsaved input')
    expect(drawer.text()).toContain('Reload')
    expect(drawer.text()).toContain('trace-dynamic')
    await clickButton(drawer, 'Copy unsaved input')
    expect(writeText).toHaveBeenCalledWith(expect.stringContaining('LOCAL_ROW'))
    await clickButton(drawer, 'Reload')
    expect(confirm).toHaveBeenCalled()
    expect(api.getDynamicConfiguration).toHaveBeenCalledTimes(2)
  })

  it('blocks route navigation until dirty record input is explicitly discarded', async () => {
    const confirm = vi.spyOn(ElMessageBox, 'confirm').mockRejectedValueOnce('cancel')
    const { wrapper, router } = await mountPage()
    await select(wrapper, summaryA)
    await clickButton(wrapper, 'Add record')
    await wrapper.get('input[data-testid="dynamic-record-nid"]').setValue('LOCAL_ROW')
    await router.push('/elsewhere')
    expect(router.currentRoute.value.path).toBe('/')
    confirm.mockResolvedValueOnce('confirm' as never)
    await router.push('/elsewhere')
    expect(router.currentRoute.value.path).toBe('/elsewhere')
  })

  it('enforces lifecycle, platform scope, permissions and publication differences', async () => {
    const platformPublished: DynamicConfiguration = {
      ...detailA,
      scopeType: 'Platform',
      status: 'Published',
      publishedOn: '2026-09-05T00:00:00Z',
      publishedBy: 'admin',
    }
    api.listDynamicConfigurations.mockResolvedValue({
      items: [{ ...platformPublished, fieldCount: fields.length }],
      total: 1,
      pageIndex: 1,
      pageSize: 20,
    })
    api.getDynamicConfiguration.mockResolvedValue(platformPublished)
    const { wrapper } = await mountPage([PERMISSIONS.referenceDataDynamicPropertyView])
    await select(wrapper, { ...platformPublished, fieldCount: fields.length })
    expect(wrapper.find('[data-testid="dynamic-property-create"]').exists()).toBe(false)
    expect(wrapper.findAll('button').some((item) => item.text() === 'Edit definition')).toBe(false)
    expect(wrapper.text()).toContain('read-only')

    api.listDynamicConfigurations.mockResolvedValue({
      items: [summaryA],
      total: 1,
      pageIndex: 1,
      pageSize: 20,
    })
    api.getDynamicConfiguration.mockResolvedValue(detailA)
    const editable = await mountPage()
    await select(editable.wrapper, summaryA)
    await clickButton(editable.wrapper, 'Publication check')
    const publish = openDrawer(editable.wrapper, 'dynamic-publish-confirm')
    expect(publish.text()).toContain('STAMP')
    expect(publish.text()).toContain('AMOUNT')
    expect(publish.text()).toContain('1')
    expect(publish.text()).toContain('9')
  })

  it('requires platform manage in addition to the action permission', async () => {
    const platformDraft: DynamicConfiguration = { ...detailA, scopeType: 'Platform' }
    api.listDynamicConfigurations.mockResolvedValue({
      items: [{ ...platformDraft, fieldCount: fields.length }],
      total: 1,
      pageIndex: 1,
      pageSize: 20,
    })
    api.getDynamicConfiguration.mockResolvedValue(platformDraft)
    const denied = await mountPage()
    await select(denied.wrapper, { ...platformDraft, fieldCount: fields.length })
    expect(denied.wrapper.findAll('button').some((item) => item.text() === 'Edit definition')).toBe(
      false,
    )
    expect(denied.wrapper.findAll('button').some((item) => item.text() === 'Add record')).toBe(
      false,
    )

    const granted = await mountPage([...editPermissions, PERMISSIONS.referenceDataPlatformManage])
    await select(granted.wrapper, { ...platformDraft, fieldCount: fields.length })
    expect(
      granted.wrapper.findAll('button').some((item) => item.text() === 'Edit definition'),
    ).toBe(true)
    expect(granted.wrapper.findAll('button').some((item) => item.text() === 'Add record')).toBe(
      true,
    )
  })
})
