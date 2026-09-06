import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import ElementPlus, { ElMessageBox } from 'element-plus'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, type ComponentPublicInstance } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'
import { VxeTable } from 'vxe-table'
import { createApiError } from '@/api/errors'
import type {
  CodePreview,
  CodingRuleDetail,
  CodingRuleSummary,
  GeneratedCode,
} from '@/api/referenceData/codingRuleTypes'
import AppDataTable from '@/components/management/AppDataTable.vue'
import AppFormDrawer from '@/components/management/AppFormDrawer.vue'
import CodingRulesPage from '@/pages/pc/referenceData/CodingRulesPage.vue'
import { PERMISSIONS } from '@/permissions'
import { useAuthStore } from '@/stores/authStore'
import { useDeviceStore } from '@/stores/deviceStore'
import { useLocalizationStore } from '@/stores/localizationStore'
import { persistAuthSession } from '../fixtures/session'

const { api } = vi.hoisted(() => ({
  api: {
    listCodingRules: vi.fn(),
    getCodingRule: vi.fn(),
    createCodingRule: vi.fn(),
    updateCodingRule: vi.fn(),
    cloneCodingRule: vi.fn(),
    publishCodingRule: vi.fn(),
    disableCodingRule: vi.fn(),
    previewCodingRule: vi.fn(),
    previewCode: vi.fn(),
    generateCode: vi.fn(),
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
  props: ['value', 'label', 'disabled'],
  template: '<option :value="value" :disabled="disabled">{{ label }}</option>',
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

const summary = (overrides: Partial<CodingRuleSummary> = {}): CodingRuleSummary => ({
  id: 'tenant-draft',
  nId: 'LOT_RULE',
  name: 'Tenant lot rule',
  targetEntityNId: 'LOT',
  template: 'LOT-{TENANT}-{SEQ:4}',
  resetPolicy: 'Never',
  scopeType: 'Tenant',
  tenantNId: 'TENANT-A',
  revision: 2,
  status: 'Draft',
  sourceRevision: 1,
  optimisticVersion: 4,
  concurrencyVersion: 'token-4',
  lastUpdatedOn: '2026-09-05T00:00:00Z',
  publishedOn: null,
  publishedBy: null,
  isFrozen: false,
  isLocked: false,
  ...overrides,
})
const detail = (item: CodingRuleSummary): CodingRuleDetail => ({
  ...item,
  createdOn: '2026-09-01T00:00:00Z',
})
const preview: CodePreview = {
  codingRuleNId: 'LOT_RULE',
  ruleRevision: 2,
  sourceScope: 'Tenant',
  sourceTenantNId: 'TENANT-A',
  code: 'LOT-TENANT-A-0001',
  sampleSequence: 1,
  periodKey: 'ALL',
  previewedOn: '2026-09-05T00:00:00Z',
  consumesSequence: false,
}
const generated: GeneratedCode = {
  codingRuleNId: 'LOT_RULE',
  ruleRevision: 1,
  code: 'P-0001',
  sequence: 1,
  periodKey: 'ALL',
  generatedOn: '2026-09-05T00:00:00Z',
}

const permissions = [
  PERMISSIONS.referenceDataCodingRuleView,
  PERMISSIONS.referenceDataCodingRuleCreate,
  PERMISSIONS.referenceDataCodingRuleUpdate,
  PERMISSIONS.referenceDataCodingRulePublish,
  PERMISSIONS.referenceDataCodingRuleDisable,
  PERMISSIONS.referenceDataCodingRulePreview,
  PERMISSIONS.referenceDataCodingRuleGenerate,
  PERMISSIONS.referenceDataPlatformManage,
]
const wrappers: VueWrapper[] = []
type TableInstance = ComponentPublicInstance<Parameters<typeof AppDataTable>[0]>

async function mountPage(granted = permissions) {
  const pinia = createPinia()
  setActivePinia(pinia)
  persistAuthSession(granted)
  await useAuthStore().restore()
  useLocalizationStore().setLocale('en-US', null)
  useDeviceStore().setOverride('pc')
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: CodingRulesPage },
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

function button(wrapper: VueWrapper, label: string, index = 0) {
  const matches = wrapper.findAll('button').filter((item) => item.text() === label)
  expect(matches[index], `${label}[${index}]`).toBeDefined()
  return matches[index]!
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
  const draft = summary()
  const published = summary({
    id: 'platform-published',
    name: 'Platform lot rule',
    scopeType: 'Platform',
    tenantNId: null,
    revision: 1,
    status: 'Published',
    sourceRevision: null,
    optimisticVersion: 2,
    concurrencyVersion: 'token-2',
    publishedOn: '2026-09-02T00:00:00Z',
    publishedBy: 'USER-P',
    template: 'P-{SEQ:4}',
  })
  api.listCodingRules.mockResolvedValue({
    items: [draft, published],
    total: 2,
    pageIndex: 1,
    pageSize: 20,
  })
  api.getCodingRule.mockImplementation(async (id: string) =>
    detail(id === published.id ? published : draft),
  )
  api.createCodingRule.mockImplementation(async (request) =>
    detail(summary({ id: 'created', nId: request.nId, name: request.name })),
  )
  api.updateCodingRule.mockResolvedValue(detail(draft))
  api.cloneCodingRule.mockResolvedValue(detail(summary({ id: 'clone', revision: 2 })))
  api.publishCodingRule.mockResolvedValue(detail({ ...draft, status: 'Published' }))
  api.disableCodingRule.mockResolvedValue(detail({ ...draft, status: 'Disabled' }))
  api.previewCodingRule.mockResolvedValue(preview)
  api.previewCode.mockResolvedValue({ ...preview, ruleRevision: 1, code: 'P-0001' })
  api.generateCode.mockResolvedValue(generated)
})

afterEach(() => {
  for (const wrapper of wrappers.splice(0)) wrapper.unmount()
  vi.restoreAllMocks()
  vi.unstubAllEnvs()
})

describe('CodingRulesPage', () => {
  it('moves Edit into More below 190px and restores the direct action when widened', async () => {
    const { wrapper } = await mountPage()
    const actions = () => wrapper.findAll('.coding-actions')[0]!
    const action = (label: string) =>
      actions()
        .findAll('button')
        .find((item) => item.text() === label)!

    expect(actions().get('[data-testid="coding-rule-more"]').text()).toBe('More')
    expect(action('Edit').element.closest('[data-testid="dropdown-menu"]')).toBeNull()
    for (const label of ['Publish', 'Disable', 'Code tools']) {
      expect(action(label).element.closest('[data-testid="dropdown-menu"]')).not.toBeNull()
    }
    const publishedActions = wrapper.findAll('.coding-actions')[1]!
    for (const label of ['Clone new revision', 'Disable', 'Code tools']) {
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

  it('renders an ordinary list and a bilingual, platform-aware editor with token guidance', async () => {
    const { wrapper } = await mountPage()
    const table = wrapper.findComponent<TableInstance>(AppDataTable)
    expect(table.props('selection')).toBe('none')
    expect(table.props('activeRowKey')).toBeNull()
    expect(table.props('toolbarProfile')).toBe('full')
    for (const tool of ['query-toggle', 'sort', 'group', 'export']) {
      expect(wrapper.find(`[data-testid="app-data-table-${tool}"]`).exists()).toBe(true)
    }
    expect(wrapper.text()).toContain('Tenant lot rule')
    expect(wrapper.text()).toContain('Platform lot rule')

    await wrapper.get('[data-testid="coding-rule-create"]').trigger('click')
    const editor = drawer(wrapper, 'coding-rule-save')
    expect(editor.text()).toContain('{YYYY} {MM} {DD} {TENANT} {FACTORY} {SEQ:n}')
    expect(editor.text()).toContain('Factory context is unavailable')
    expect(editor.get('[data-testid="coding-rule-factory"]').attributes('disabled')).toBeDefined()
    expect(editor.find('option[value="Platform"]').exists()).toBe(true)
  })

  it('keeps dirty input and both versions after a 409 response', async () => {
    api.updateCodingRule.mockRejectedValue(
      createApiError('business', 'Conflict', 'corr', {
        status: 409,
        code: 'REF-CONCURRENCY-CONFLICT',
        traceId: 'trace-conflict',
      }),
    )
    const { wrapper } = await mountPage()
    await button(wrapper, 'Edit').trigger('click')
    await flushPromises()
    const editor = drawer(wrapper, 'coding-rule-save')
    await editor.get('[data-testid="coding-rule-template"]').setValue('NEW-{SEQ:4}')
    await editor.get('[data-testid="coding-rule-save"]').trigger('click')
    await flushPromises()

    expect(
      (editor.get('[data-testid="coding-rule-template"]').element as HTMLTextAreaElement).value,
    ).toBe('NEW-{SEQ:4}')
    expect(editor.text()).toContain('Your unsaved input is preserved')
    expect(editor.text()).toContain('Copy unsaved input')
    expect(editor.text()).toContain('Reload')
    expect(api.updateCodingRule).toHaveBeenCalledWith(
      'tenant-draft',
      expect.objectContaining({
        template: 'NEW-{SEQ:4}',
        expectedOptimisticVersion: 4,
        expectedConcurrencyVersion: 'token-4',
      }),
    )
  })

  it('previews a draft without consuming a number', async () => {
    const { wrapper } = await mountPage()
    await button(wrapper, 'Code tools', 0).trigger('click')
    const tools = drawer(wrapper, 'coding-rule-preview-draft')
    await tools.get('[data-testid="coding-rule-preview-draft"]').trigger('click')
    await flushPromises()

    expect(api.previewCodingRule).toHaveBeenCalledWith(
      'tenant-draft',
      { factoryId: null },
      expect.objectContaining({ signal: expect.any(AbortSignal) }),
    )
    expect(tools.get('[data-testid="coding-rule-preview-result"]').text()).toContain(
      'LOT-TENANT-A-0001',
    )
    expect(tools.text()).toContain('does not consume a sequence number')
    expect(api.generateCode).not.toHaveBeenCalled()
  })

  it('uses explicit source and revision and reuses one idempotency key for generate retries', async () => {
    api.listCodingRules.mockResolvedValue({
      items: [
        summary({
          id: 'platform-published',
          scopeType: 'Platform',
          tenantNId: null,
          revision: 1,
          status: 'Published',
          publishedOn: '2026-09-02T00:00:00Z',
          template: 'P-{SEQ:4}',
        }),
      ],
      total: 1,
      pageIndex: 1,
      pageSize: 20,
    })
    const { wrapper } = await mountPage()
    await button(wrapper, 'Code tools').trigger('click')
    const tools = drawer(wrapper, 'coding-rule-preview-runtime')
    await tools.get('[data-testid="coding-rule-preview-runtime"]').trigger('click')
    await flushPromises()
    await tools.get('[data-testid="coding-rule-idempotency-key"]').setValue('same-key')
    await tools.get('[data-testid="coding-rule-generate"]').trigger('click')
    await flushPromises()
    await tools.get('[data-testid="coding-rule-generate"]').trigger('click')
    await flushPromises()

    expect(api.previewCode).toHaveBeenCalledWith(
      'LOT_RULE',
      { sourceScope: 'Platform', sourceTenantNId: null, ruleRevision: 1, factoryId: null },
      expect.objectContaining({ signal: expect.any(AbortSignal) }),
    )
    expect(api.generateCode).toHaveBeenNthCalledWith(
      1,
      'LOT_RULE',
      { sourceScope: 'Platform', sourceTenantNId: null, ruleRevision: 1, factoryId: null },
      'same-key',
      expect.anything(),
    )
    expect(api.generateCode.mock.calls[1]?.[2]).toBe('same-key')
    expect(tools.get('[data-testid="coding-rule-generated-result"]').text()).toContain('P-0001')
  })

  it('sends both version tokens for publish, disable, and published-revision clone actions', async () => {
    vi.spyOn(ElMessageBox, 'confirm').mockResolvedValue('confirm' as never)
    vi.spyOn(ElMessageBox, 'prompt').mockResolvedValue({
      value: 'No longer current',
      action: 'confirm',
    } as never)
    const { wrapper } = await mountPage()

    await button(wrapper, 'Publish').trigger('click')
    await flushPromises()
    expect(api.publishCodingRule).toHaveBeenCalledWith('tenant-draft', {
      expectedOptimisticVersion: 4,
      expectedConcurrencyVersion: 'token-4',
    })

    await button(wrapper, 'Disable', 0).trigger('click')
    await flushPromises()
    expect(api.disableCodingRule).toHaveBeenCalledWith('tenant-draft', {
      expectedOptimisticVersion: 4,
      expectedConcurrencyVersion: 'token-4',
      changeReason: 'No longer current',
    })

    await button(wrapper, 'Clone new revision').trigger('click')
    await flushPromises()
    expect(api.cloneCodingRule).toHaveBeenCalledWith('platform-published', {
      expectedOptimisticVersion: 2,
      expectedConcurrencyVersion: 'token-2',
    })
  })

  it('hides platform mutations without PlatformManage and rejects unavailable template tokens', async () => {
    api.listCodingRules.mockResolvedValue({ items: [], total: 0, pageIndex: 1, pageSize: 20 })
    const granted = permissions.filter(
      (permission) => permission !== PERMISSIONS.referenceDataPlatformManage,
    )
    const { wrapper } = await mountPage(granted)
    await wrapper.get('[data-testid="coding-rule-create"]').trigger('click')
    const editor = drawer(wrapper, 'coding-rule-save')
    expect(editor.find('option[value="Platform"]').exists()).toBe(false)
    await editor.get('[data-testid="coding-rule-nid"]').setValue('INVALID_TOKEN_RULE')
    await editor.get('[data-testid="coding-rule-name"]').setValue('Invalid token rule')
    await editor.get('[data-testid="coding-rule-target"]').setValue('LOT')
    await editor.get('[data-testid="coding-rule-template"]').setValue('{FACTORY}-{SEQ:4}')
    await editor.get('[data-testid="coding-rule-save"]').trigger('click')
    await flushPromises()

    expect(api.createCodingRule).not.toHaveBeenCalled()
    expect(editor.text()).toContain(
      'The template tokens, sequence width, or reset policy are invalid',
    )
  })

  it('aborts a stale detail request and ignores its later response', async () => {
    let resolveFirst!: (value: CodingRuleDetail) => void
    const first = new Promise<CodingRuleDetail>((resolve) => {
      resolveFirst = resolve
    })
    const platform = summary({
      id: 'platform-published',
      name: 'Platform lot rule',
      scopeType: 'Platform',
      tenantNId: null,
      revision: 1,
      status: 'Published',
      publishedOn: '2026-09-02T00:00:00Z',
      template: 'P-{SEQ:4}',
    })
    api.getCodingRule.mockImplementation((id: string) =>
      id === 'tenant-draft' ? first : Promise.resolve(detail(platform)),
    )
    const { wrapper } = await mountPage()

    await button(wrapper, 'Details', 0).trigger('click')
    const firstSignal = api.getCodingRule.mock.calls[0]?.[1]?.signal as AbortSignal
    await button(wrapper, 'Details', 1).trigger('click')
    await flushPromises()
    expect(firstSignal.aborted).toBe(true)
    const viewer = wrapper
      .findAllComponents(AppFormDrawer)
      .find((item) => item.props('modelValue') && item.text().includes('Coding rule details'))!
    expect(
      (viewer.get('[data-testid="coding-rule-template"]').element as HTMLTextAreaElement).value,
    ).toBe('P-{SEQ:4}')

    resolveFirst(detail(summary()))
    await flushPromises()
    expect(
      (viewer.get('[data-testid="coding-rule-template"]').element as HTMLTextAreaElement).value,
    ).toBe('P-{SEQ:4}')
  })

  it('guards navigation while the editor contains unsaved input', async () => {
    api.listCodingRules.mockResolvedValue({ items: [], total: 0, pageIndex: 1, pageSize: 20 })
    const confirm = vi.spyOn(ElMessageBox, 'confirm').mockRejectedValue(new Error('cancel'))
    const { wrapper, router } = await mountPage()
    await wrapper.get('[data-testid="coding-rule-create"]').trigger('click')
    await drawer(wrapper, 'coding-rule-save')
      .get('[data-testid="coding-rule-name"]')
      .setValue('Unsaved rule')

    await router.push('/elsewhere')

    expect(router.currentRoute.value.path).toBe('/')
    expect(confirm).toHaveBeenCalled()
  })
})
