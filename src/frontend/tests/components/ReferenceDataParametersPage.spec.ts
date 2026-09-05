import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import ElementPlus from 'element-plus'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, type ComponentPublicInstance } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'
import { VxeTable } from 'vxe-table'
import { makeAuthSession, persistAuthSession } from '../fixtures/session'
import { createApiError } from '@/api/errors'
import type {
  ConfigurationDomain,
  ConfigurationDomainSummary,
  ConfigurationKey,
} from '@/api/referenceData/parameterTypes'
import AppDataTable from '@/components/management/AppDataTable.vue'
import AppFormDrawer from '@/components/management/AppFormDrawer.vue'
import ConfigurationValueEditor from '@/pages/pc/referenceData/ConfigurationValueEditor.vue'
import ParametersPage from '@/pages/pc/referenceData/ParametersPage.vue'
import { PERMISSIONS } from '@/permissions'
import { useAuthStore } from '@/stores/authStore'
import { useDeviceStore } from '@/stores/deviceStore'
import { useLocalizationStore } from '@/stores/localizationStore'

const { api } = vi.hoisted(() => ({
  api: {
    listConfigurationDomains: vi.fn(),
    getConfigurationDomain: vi.fn(),
    createConfigurationDomain: vi.fn(),
    updateConfigurationDomain: vi.fn(),
    setConfigurationDomainStatus: vi.fn(),
    addConfigurationKey: vi.fn(),
    updateConfigurationKey: vi.fn(),
    setConfigurationKeyStatus: vi.fn(),
    addConfigurationValue: vi.fn(),
    updateConfigurationValue: vi.fn(),
    setConfigurationValueEnabled: vi.fn(),
    getConfigurationHistory: vi.fn(),
    resolveConfiguration: vi.fn(),
    getEffectiveDictionary: vi.fn(),
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
const summaryA: ConfigurationDomainSummary = {
  id: 'domain-a',
  nId: 'ALPHA',
  name: 'Alpha domain',
  scopeType: 'Tenant',
  tenantNId: 't1',
  status: 'Active',
  revision: 3,
  keyCount: 1,
  lastUpdatedOn: '2026-09-05T00:00:00Z',
  optimisticVersion: 2,
  concurrencyVersion: 'domain-a-token-2',
  isFrozen: false,
  isLocked: false,
}
const summaryB: ConfigurationDomainSummary = {
  ...summaryA,
  id: 'domain-b',
  nId: 'BETA',
  name: 'Beta domain',
  concurrencyVersion: 'domain-b-token-2',
}
const keyA: ConfigurationKey = {
  id: 'key-a',
  nId: 'ALPHA_LIMIT',
  fullNId: 'ALPHA.ALPHA_LIMIT',
  name: 'Alpha limit',
  description: null,
  dataType: 'Decimal',
  valueMode: 'Single',
  value: 1.25,
  defaultValue: null,
  valueJson: '1.25',
  defaultValueJson: null,
  isMandatory: false,
  isReadOnly: false,
  dictionaryNId: null,
  referenceTarget: null,
  status: 'Active',
  sort: 0,
  multiValues: [],
  hasHadValue: true,
  isFrozen: false,
  isLocked: false,
}
const domainA: ConfigurationDomain = { ...summaryA, description: null, keys: [keyA] }
const domainB: ConfigurationDomain = {
  ...summaryB,
  description: null,
  keys: [
    { ...keyA, id: 'key-b', nId: 'BETA_LIMIT', fullNId: 'BETA.BETA_LIMIT', name: 'Beta limit' },
  ],
}
const editPermissions = [
  PERMISSIONS.referenceDataParameterView,
  PERMISSIONS.referenceDataParameterCreate,
  PERMISSIONS.referenceDataParameterUpdate,
  PERMISSIONS.referenceDataParameterDisable,
]

async function mountPage() {
  const pinia = createPinia()
  setActivePinia(pinia)
  persistAuthSession(editPermissions)
  await useAuthStore().restore()
  useLocalizationStore().setLocale('en-US', null)
  useDeviceStore().setOverride('pc')
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/', component: ParametersPage }],
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
        'el-dropdown': defineComponent({ name: 'ElDropdown', template: '<div><slot /></div>' }),
      },
    },
  })
  wrappers.push(wrapper)
  await flushPromises()
  return wrapper
}

function table(wrapper: VueWrapper, tableKey: string): TableWrapper {
  const result = wrapper
    .findAllComponents<TableInstance>({ name: 'AppDataTable' })
    .find((item) => item.props('tableKey') === tableKey)
  expect(result, tableKey).toBeDefined()
  return result!
}
function master(wrapper: VueWrapper): TableWrapper {
  return table(wrapper, 'reference-data-parameter-domains')
}
function keys(wrapper: VueWrapper): TableWrapper {
  return table(wrapper, 'reference-data-parameter-keys')
}
async function select(wrapper: VueWrapper, row: ConfigurationDomainSummary) {
  master(wrapper).findComponent(VxeTable).vm.$emit('radio-change', { row })
  await flushPromises()
}
async function clickButton(wrapper: VueWrapper, label: string) {
  const result = wrapper.findAll('button').find((item) => item.text() === label)
  expect(result, label).toBeDefined()
  await result!.trigger('click')
  await flushPromises()
}
function editor(wrapper: VueWrapper): VueWrapper<InstanceType<typeof AppFormDrawer>> {
  const result = wrapper.findAllComponents(AppFormDrawer).find((item) => item.props('modelValue'))
  expect(result).toBeDefined()
  return result!
}

beforeEach(() => {
  vi.resetAllMocks()
  localStorage.clear()
  sessionStorage.clear()
  vi.stubEnv('VITE_AUTH_MODE', 'mock')
  api.listConfigurationDomains.mockResolvedValue({
    items: [summaryA, summaryB],
    total: 2,
    pageIndex: 1,
    pageSize: 20,
  })
  api.getConfigurationDomain.mockImplementation(async (id: string) =>
    structuredClone(id === summaryA.id ? domainA : domainB),
  )
  api.updateConfigurationKey.mockResolvedValue(structuredClone(domainA))
})
afterEach(() => {
  for (const wrapper of wrappers.splice(0)) wrapper.unmount()
  document.body.innerHTML = ''
  vi.unstubAllEnvs()
})

describe('Parameter management page', () => {
  it('does not load a domain before selection or after an empty selection event', async () => {
    const wrapper = await mountPage()
    expect(api.listConfigurationDomains).toHaveBeenCalled()
    expect(master(wrapper).props('selection')).toBe('single')
    expect(master(wrapper).props('selectedRowKey')).toBeNull()
    expect(wrapper.get('.parameter-detail').text()).toContain('Select a domain')
    expect(api.getConfigurationDomain).not.toHaveBeenCalled()
    master(wrapper).findComponent(VxeTable).vm.$emit('radio-change', { records: [] })
    await flushPromises()
    expect(api.getConfigurationDomain).not.toHaveBeenCalled()
  })

  it('clears the selected domain and keys through the real table clear control', async () => {
    const wrapper = await mountPage()
    await select(wrapper, summaryA)
    expect(keys(wrapper).props('rows')).toEqual(domainA.keys)
    expect(wrapper.get('.parameter-detail').text()).toContain('Alpha limit')
    expect(api.getConfigurationDomain).toHaveBeenCalledTimes(1)
    await master(wrapper).get('[data-testid="app-data-table-clear-selection"]').trigger('click')
    await flushPromises()
    expect(master(wrapper).props('selectedRowKey')).toBeNull()
    expect(wrapper.get('.parameter-detail').text()).toContain('Select a domain')
    expect(wrapper.get('.parameter-detail').text()).not.toContain('Alpha limit')
    expect(
      wrapper
        .findAllComponents<TableInstance>({ name: 'AppDataTable' })
        .some((item) => item.props('tableKey') === 'reference-data-parameter-keys'),
    ).toBe(false)
    expect(api.getConfigurationDomain).toHaveBeenCalledTimes(1)
  })

  it('keeps B when the cancelled A request still completes after B', async () => {
    let finishA!: (domain: ConfigurationDomain) => void
    api.getConfigurationDomain.mockImplementationOnce(
      () =>
        new Promise<ConfigurationDomain>((resolve) => {
          finishA = resolve
        }),
    )
    const wrapper = await mountPage()
    await select(wrapper, summaryA)
    const firstSignal = api.getConfigurationDomain.mock.calls[0]![1].signal as AbortSignal
    await select(wrapper, summaryB)
    expect(firstSignal.aborted).toBe(true)
    expect(wrapper.get('.parameter-context h2').text()).toBe('Beta domain')
    finishA(structuredClone(domainA))
    await flushPromises()
    expect(master(wrapper).props('selectedRowKey')).toBe(summaryB.id)
    expect(wrapper.get('.parameter-context h2').text()).toBe('Beta domain')
    expect(keys(wrapper).props('rows')).toEqual(domainB.keys)
    expect(wrapper.get('.parameter-detail').text()).not.toContain('Alpha limit')
  })

  it('retains a dirty key form but disables saving after update permission is revoked', async () => {
    const wrapper = await mountPage()
    await select(wrapper, summaryA)
    await clickButton(keys(wrapper), 'Edit')
    await editor(wrapper).get('input[aria-label="Value"]').setValue('42.125')
    await editor(wrapper).get('textarea[aria-label="Change reason"]').setValue('Retain this reason')
    useAuthStore().adoptSession(makeAuthSession([PERMISSIONS.referenceDataParameterView]))
    await flushPromises()
    const form = editor(wrapper)
    expect(form.props('modelValue')).toBe(true)
    expect(form.get('[data-testid="parameter-save"]').attributes('disabled')).toBeDefined()
    expect((form.get('input[aria-label="Value"]').element as HTMLInputElement).value).toBe('42.125')
    expect(
      (form.get('textarea[aria-label="Change reason"]').element as HTMLTextAreaElement).value,
    ).toBe('Retain this reason')
    expect(form.findComponent(ConfigurationValueEditor).props('modelValue')).toBe('42.125')
    await form.get('[data-testid="parameter-save"]').trigger('click')
    await flushPromises()
    expect(api.updateConfigurationKey).not.toHaveBeenCalled()
    expect(api.addConfigurationKey).not.toHaveBeenCalled()
  })

  it.each(['Single', 'Multi'] as const)(
    'shows a read-only %s key without edit, save, or value mutation actions',
    async (mode) => {
      const readonly = structuredClone(domainA)
      readonly.keys[0] = {
        ...readonly.keys[0]!,
        isReadOnly: true,
        valueMode: mode,
        value: mode === 'Single' ? 1.25 : null,
        valueJson: mode === 'Single' ? '1.25' : null,
        multiValues:
          mode === 'Multi'
            ? [
                {
                  id: 'value-1',
                  nId: 'FIXED',
                  name: null,
                  value: 1.25,
                  valueJson: '1.25',
                  sort: 0,
                  isDefault: false,
                  enabled: true,
                },
              ]
            : [],
      }
      api.getConfigurationDomain.mockResolvedValue(readonly)
      const wrapper = await mountPage()
      await select(wrapper, summaryA)
      expect(
        keys(wrapper)
          .findAll('button')
          .some((item) => item.text() === 'Edit'),
      ).toBe(false)
      await clickButton(keys(wrapper), 'Details')
      const form = editor(wrapper)
      expect(form.text()).toContain(
        'Only trusted synchronization or migration can change this key.',
      )
      expect(form.get('input[aria-label="Name"]').attributes('disabled')).toBeDefined()
      expect(form.find('[data-testid="parameter-save"]').exists()).toBe(false)
      if (mode === 'Single') {
        expect(form.get('input[aria-label="Value"]').attributes('disabled')).toBeDefined()
      } else {
        const tab = form.findAll('[role="tab"]').find((item) => item.text() === 'Value entries')
        expect(tab).toBeDefined()
        await tab!.trigger('click')
        await flushPromises()
        const values = table(form, 'reference-data-parameter-values')
        expect(values.props('rows')).toHaveLength(1)
        expect(
          form
            .findAll('button')
            .some((item) => ['Add value', 'Edit', 'Disable'].includes(item.text())),
        ).toBe(false)
      }
      expect(api.updateConfigurationKey).not.toHaveBeenCalled()
      expect(api.setConfigurationKeyStatus).not.toHaveBeenCalled()
      expect(api.updateConfigurationValue).not.toHaveBeenCalled()
      expect(api.setConfigurationValueEnabled).not.toHaveBeenCalled()
    },
  )

  it('preserves the exact decimal valueJson and change reason after a 409 conflict', async () => {
    api.updateConfigurationKey.mockRejectedValue(
      createApiError('business', 'Conflict', 'correlation-parameter', {
        status: 409,
        code: 'REF-CONCURRENCY-CONFLICT',
        traceId: 'parameter-conflict-trace',
      }),
    )
    const wrapper = await mountPage()
    await select(wrapper, summaryA)
    await clickButton(keys(wrapper), 'Edit')
    const value = '9007199254740993.0000000001'
    const reason = 'Preserve exact decimal during a conflict'
    await editor(wrapper).get('input[aria-label="Value"]').setValue(value)
    await editor(wrapper).get('textarea[aria-label="Change reason"]').setValue(reason)
    await editor(wrapper).get('[data-testid="parameter-save"]').trigger('click')
    await flushPromises()
    expect(api.updateConfigurationKey).toHaveBeenCalledWith(
      summaryA.id,
      keyA.id,
      expect.objectContaining({
        valueJson: value,
        changeReason: reason,
        expectedAppDomainOptimisticVersion: summaryA.optimisticVersion,
        expectedAppDomainConcurrencyVersion: summaryA.concurrencyVersion,
      }),
    )
    const form = editor(wrapper)
    expect((form.get('input[aria-label="Value"]').element as HTMLInputElement).value).toBe(value)
    expect(
      (form.get('textarea[aria-label="Change reason"]').element as HTMLTextAreaElement).value,
    ).toBe(reason)
    expect(form.findComponent(ConfigurationValueEditor).props('modelValue')).toBe(value)
    expect(form.text()).toContain('parameter-conflict-trace')
    expect(form.text()).toContain('Reload')
    expect(form.text()).toContain('Copy unsaved')
    expect(form.props('modelValue')).toBe(true)
    expect(api.getConfigurationDomain).toHaveBeenCalledTimes(1)
  })
})
