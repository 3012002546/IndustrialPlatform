import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import ElementPlus, { ElButton, ElMessageBox } from 'element-plus'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, type ComponentPublicInstance } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'
import { VxeTable } from 'vxe-table'
import { createApiError } from '@/api/errors'
import type {
  RuntimeStateMachine,
  StateMachineDetail,
  StateMachineSummary,
} from '@/api/referenceData/stateMachineTypes'
import AppDataTable from '@/components/management/AppDataTable.vue'
import AppFormDrawer from '@/components/management/AppFormDrawer.vue'
import StateMachinesPage from '@/pages/pc/referenceData/StateMachinesPage.vue'
import { PERMISSIONS } from '@/permissions'
import { useAuthStore } from '@/stores/authStore'
import { useDeviceStore } from '@/stores/deviceStore'
import { useLocalizationStore } from '@/stores/localizationStore'
import { persistAuthSession } from '../fixtures/session'

const { api } = vi.hoisted(() => ({
  api: {
    listStateMachines: vi.fn(),
    getStateMachine: vi.fn(),
    createStateMachine: vi.fn(),
    updateStateMachine: vi.fn(),
    cloneStateMachine: vi.fn(),
    checkStateMachinePublication: vi.fn(),
    publishStateMachine: vi.fn(),
    disableStateMachine: vi.fn(),
    listAvailableStateMachines: vi.fn(),
    getCurrentStateMachine: vi.fn(),
    getStateMachineRevision: vi.fn(),
    evaluateStateTransition: vi.fn(),
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

const nodes = [
  {
    id: 'node-open',
    nId: 'OPEN',
    name: 'Open',
    description: null,
    isInitial: true,
    isTerminal: false,
    outcome: 'None' as const,
    color: '#1677FF',
    sort: 0,
  },
  {
    id: 'node-done',
    nId: 'DONE',
    name: 'Done',
    description: null,
    isInitial: false,
    isTerminal: true,
    outcome: 'Success' as const,
    color: '#52C41A',
    sort: 1,
  },
]
const transitions = [
  {
    id: 'transition-approve',
    fromStatusNId: 'OPEN',
    actionNId: 'APPROVE',
    actionName: 'Approve',
    toStatusNId: 'DONE',
    description: null,
  },
]
const summary = (overrides: Partial<StateMachineSummary> = {}): StateMachineSummary => ({
  id: 'tenant-draft',
  nId: 'ORDER_FLOW',
  name: 'Tenant order flow',
  description: null,
  scopeType: 'Tenant',
  tenantNId: 'TENANT-A',
  revision: 2,
  status: 'Draft',
  sourceRevision: 1,
  nodeCount: 2,
  transitionCount: 1,
  optimisticVersion: 4,
  concurrencyVersion: 'token-4',
  lastUpdatedOn: '2026-09-05T00:00:00Z',
  publishedOn: null,
  publishedBy: null,
  isFrozen: false,
  isLocked: false,
  ...overrides,
})
const detail = (item: StateMachineSummary): StateMachineDetail => ({
  ...item,
  nodes: structuredClone(nodes),
  transitions: structuredClone(transitions),
})
const runtime = (item: StateMachineSummary): RuntimeStateMachine => ({
  nId: item.nId,
  name: item.name,
  description: item.description,
  sourceScope: item.scopeType,
  sourceTenantNId: item.tenantNId,
  revision: item.revision,
  status: item.status,
  sourceRevision: item.sourceRevision,
  publishedOn: item.publishedOn,
  nodes: nodes.map((node) => ({
    nId: node.nId,
    name: node.name,
    description: node.description,
    isInitial: node.isInitial,
    isTerminal: node.isTerminal,
    outcome: node.outcome,
    color: node.color,
    sort: node.sort,
  })),
  transitions: transitions.map((transition) => ({
    fromStatusNId: transition.fromStatusNId,
    actionNId: transition.actionNId,
    actionName: transition.actionName,
    toStatusNId: transition.toStatusNId,
    description: transition.description,
  })),
})

const permissions = [
  PERMISSIONS.referenceDataStateMachineView,
  PERMISSIONS.referenceDataStateMachineCreate,
  PERMISSIONS.referenceDataStateMachineUpdate,
  PERMISSIONS.referenceDataStateMachinePublish,
  PERMISSIONS.referenceDataStateMachineDisable,
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
      { path: '/', component: StateMachinesPage },
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
async function selectRow(wrapper: VueWrapper, row: StateMachineSummary) {
  const master = wrapper.findComponent<TableInstance>(AppDataTable)
  master.findComponent(VxeTable).vm.$emit('cell-click', {
    row,
    column: { field: 'name' },
  })
  await flushPromises()
}
async function button(wrapper: VueWrapper, label: string, index = 0) {
  let items = wrapper.findAll('button').filter((item) => item.text() === label)
  if (!items[index]) {
    await selectRow(wrapper, summary())
    items = wrapper.findAll('button').filter((item) => item.text() === label)
  }
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
  const platform = summary({
    id: 'platform-published',
    name: 'Platform order flow',
    scopeType: 'Platform',
    tenantNId: null,
    revision: 1,
    status: 'Published',
    sourceRevision: null,
    optimisticVersion: 2,
    concurrencyVersion: 'token-2',
    publishedOn: '2026-09-01T00:00:00Z',
    publishedBy: 'SYSTEM',
  })
  api.listStateMachines.mockResolvedValue({
    items: [tenant, platform],
    total: 2,
    pageIndex: 1,
    pageSize: 20,
  })
  api.getStateMachine.mockImplementation(async (id: string) =>
    structuredClone(detail(id === platform.id ? platform : tenant)),
  )
  api.createStateMachine.mockImplementation(async (request) =>
    detail(summary({ id: 'created', nId: request.nId, name: request.name })),
  )
  api.updateStateMachine.mockResolvedValue(detail(tenant))
  api.cloneStateMachine.mockResolvedValue(detail(summary({ id: 'clone', revision: 3 })))
  api.checkStateMachinePublication.mockResolvedValue({
    previousRevision: 1,
    addedNodeNIds: ['REVIEW'],
    removedNodeNIds: ['LEGACY'],
    changedNodeNIds: ['OPEN'],
    addedTransitionKeys: ['OPEN:SUBMIT'],
    removedTransitionKeys: ['OPEN:CANCEL'],
    changedTransitionKeys: ['OPEN:APPROVE'],
    errors: [],
  })
  api.publishStateMachine.mockResolvedValue(
    detail({ ...tenant, status: 'Published', publishedOn: '2026-09-05T00:00:00Z' }),
  )
  api.disableStateMachine.mockResolvedValue(detail({ ...platform, status: 'Disabled' }))
  api.listAvailableStateMachines.mockResolvedValue({
    items: [
      {
        nId: platform.nId,
        name: platform.name,
        sourceScope: 'Platform',
        sourceTenantNId: null,
        revision: 1,
        publishedOn: platform.publishedOn,
        nodeCount: 2,
        transitionCount: 1,
      },
      {
        nId: platform.nId,
        name: 'Tenant current order flow',
        sourceScope: 'Tenant',
        sourceTenantNId: 'TENANT-A',
        revision: 3,
        publishedOn: '2026-09-04T00:00:00Z',
        nodeCount: 2,
        transitionCount: 1,
      },
    ],
    total: 2,
    pageIndex: 1,
    pageSize: 100,
  })
  api.getCurrentStateMachine.mockResolvedValue(runtime(platform))
  api.getStateMachineRevision.mockResolvedValue(runtime(platform))
  api.evaluateStateTransition.mockResolvedValue({
    stateMachineNId: 'ORDER_FLOW',
    stateMachineRevision: 1,
    sourceScope: 'Platform',
    sourceTenantNId: null,
    fromStatusNId: 'OPEN',
    actionNId: 'REJECT',
    allowedByDefinition: false,
    toStatusNId: null,
    reasonCode: 'TRANSITION_NOT_DEFINED',
  })
})
afterEach(() => {
  for (const wrapper of wrappers.splice(0)) wrapper.unmount()
  document.body.innerHTML = ''
  vi.restoreAllMocks()
  vi.unstubAllEnvs()
})

describe('StateMachinesPage', () => {
  it('makes a catalog click the active definition without opening the editor drawer', async () => {
    const { wrapper } = await mountPage()
    const master = wrapper.findComponent<TableInstance>(AppDataTable)
    expect(master.props('selection')).toBe('none')
    expect(master.props('activeRowKey')).toBeNull()
    expect(master.props('columns')).toHaveLength(1)
    master.findComponent(VxeTable).vm.$emit('cell-click', {
      row: summary(),
      column: { field: 'name' },
    })
    await flushPromises()

    expect(api.getStateMachine).toHaveBeenCalledWith(summary().id, expect.anything())
    expect(master.props('activeRowKey')).toBe(summary().id)
    expect(wrapper.get('.state-detail-panel').text()).toContain('Tenant order flow')
    expect(wrapper.findAllComponents(AppFormDrawer).every((item) => item.props('modelValue'))).toBe(
      false,
    )
  })

  it('keeps directory actions in the selected definition context header', async () => {
    const { wrapper } = await mountPage()
    await selectRow(wrapper, summary())
    const actions = () => wrapper.findAll('.state-machine-actions')[0]!
    const action = (label: string) =>
      actions()
        .findAll('button')
        .find((item) => item.text() === label)!

    expect(actions().get('[data-testid="state-machine-more"]').text()).toBe('More')
    expect(action('Edit').element.closest('[data-testid="dropdown-menu"]')).toBeNull()
    for (const label of ['Publication check', 'Disable']) {
      expect(action(label).element.closest('[data-testid="dropdown-menu"]')).not.toBeNull()
    }
    expect(wrapper.find('.app-data-table__actions-column').exists()).toBe(false)

    const published = summary({
      id: 'platform-published',
      name: 'Platform order flow',
      scopeType: 'Platform',
      tenantNId: null,
      revision: 1,
      status: 'Published',
      sourceRevision: null,
      optimisticVersion: 2,
      concurrencyVersion: 'token-2',
      publishedOn: '2026-09-01T00:00:00Z',
      publishedBy: 'SYSTEM',
    })
    await selectRow(wrapper, published)
    const publishedActions = wrapper.find('.state-machine-actions')
    for (const label of ['Clone new revision', 'Disable', 'Definition check']) {
      const item = publishedActions.findAll('button').find((button) => button.text() === label)!
      expect(item.element.closest('[data-testid="dropdown-menu"]')).not.toBeNull()
    }
  })

  it('shows tenant and platform definitions in an ordinary list and aborts a superseded query', async () => {
    const { wrapper } = await mountPage()
    const table = wrapper.findComponent<TableInstance>(AppDataTable)
    expect(table.props('selection')).toBe('none')
    expect(wrapper.text()).toContain('Tenant order flow')
    expect(wrapper.text()).toContain('Platform order flow')

    let finish!: (value: unknown) => void
    api.listStateMachines.mockImplementationOnce(() => new Promise((resolve) => (finish = resolve)))
    const first = table.props('loader')!({
      pageIndex: 2,
      pageSize: 100,
      queryMode: 'top',
      filters: { keyword: 'flow' },
      columns: [],
    })
    const signal = api.listStateMachines.mock.calls.at(-1)![1].signal as AbortSignal
    const second = table.props('loader')!({
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

  it('edits the complete graph and sends both aggregate version tokens', async () => {
    const { wrapper } = await mountPage()
    await (await button(wrapper, 'Edit')).trigger('click')
    await flushPromises()
    const editor = drawer(wrapper, 'state-machine-save')
    await editor.get('[data-testid="state-machine-name"]').setValue('Order lifecycle')
    await editor.get('[data-testid="state-node-color-0"]').setValue('#112233')
    await editor.get('[data-testid="state-machine-save"]').trigger('click')
    await flushPromises()

    expect(api.updateStateMachine).toHaveBeenCalledWith(
      'tenant-draft',
      expect.objectContaining({
        name: 'Order lifecycle',
        expectedOptimisticVersion: 4,
        expectedConcurrencyVersion: 'token-4',
        nodes: expect.arrayContaining([
          expect.objectContaining({ nId: 'OPEN', color: '#112233', isInitial: true }),
          expect.objectContaining({ nId: 'DONE', isTerminal: true, outcome: 'Success' }),
        ]),
        transitions: [
          expect.objectContaining({
            fromStatusNId: 'OPEN',
            actionNId: 'APPROVE',
            toStatusNId: 'DONE',
          }),
        ],
      }),
    )
  })

  it('blocks a terminal node with an outgoing transition and duplicate deterministic actions', async () => {
    const { wrapper } = await mountPage()
    await (await button(wrapper, 'Edit')).trigger('click')
    await flushPromises()
    const editor = drawer(wrapper, 'state-machine-save')
    await editor.get('[data-testid="state-transition-from-0"]').setValue('DONE')
    await editor.get('[data-testid="state-machine-save"]').trigger('click')
    await flushPromises()

    expect(api.updateStateMachine).not.toHaveBeenCalled()
    expect(editor.text()).toContain('Terminal nodes cannot have outgoing transitions')
  })

  it('requires a non-empty disable reason and never submits a blank prompt result', async () => {
    const prompt = vi.spyOn(ElMessageBox, 'prompt').mockResolvedValue({
      value: '   ',
      action: 'confirm',
    } as never)
    const { wrapper } = await mountPage()

    await (await button(wrapper, 'Disable', 0)).trigger('click')
    await flushPromises()

    const options = prompt.mock.calls[0]?.[2] as
      { inputValidator?: (value: string) => boolean | string } | undefined
    expect(options?.inputValidator?.('   ')).toBe('This field is required')
    expect(options?.inputValidator?.('Retired by operations')).toBe(true)
    expect(api.disableStateMachine).not.toHaveBeenCalled()
  })

  it('shows every publication difference before publishing with both versions', async () => {
    const { wrapper } = await mountPage()

    await (await button(wrapper, 'Publication check')).trigger('click')
    await flushPromises()

    expect(api.checkStateMachinePublication).toHaveBeenCalledWith(
      'tenant-draft',
      expect.objectContaining({ signal: expect.any(AbortSignal) }),
    )
    const publication = drawer(wrapper, 'state-machine-publish-confirm')
    for (const value of ['REVIEW', 'LEGACY', 'OPEN', 'OPEN:SUBMIT', 'OPEN:CANCEL', 'OPEN:APPROVE'])
      expect(publication.text()).toContain(value)

    await publication.get('[data-testid="state-machine-publish-submit"]').trigger('click')
    await flushPromises()
    expect(api.publishStateMachine).toHaveBeenCalledWith('tenant-draft', {
      expectedOptimisticVersion: 4,
      expectedConcurrencyVersion: 'token-4',
    })
  })

  it('disables publication and still guards the handler when checks return errors', async () => {
    api.checkStateMachinePublication.mockResolvedValueOnce({
      previousRevision: null,
      addedNodeNIds: [],
      removedNodeNIds: [],
      changedNodeNIds: [],
      addedTransitionKeys: [],
      removedTransitionKeys: [],
      changedTransitionKeys: [],
      errors: [{ code: 'REF-STATE-MACHINE-INVALID', field: 'nodes' }],
    })
    const { wrapper } = await mountPage()

    await (await button(wrapper, 'Publication check')).trigger('click')
    await flushPromises()

    const publication = drawer(wrapper, 'state-machine-publish-confirm')
    expect(publication.text()).toContain('REF-STATE-MACHINE-INVALID (nodes)')
    const submit = publication
      .findAllComponents(ElButton)
      .find((item) => item.attributes('data-testid') === 'state-machine-publish-submit')
    expect(submit).toBeDefined()
    expect(submit!.props('disabled')).toBe(true)
    submit!.vm.$emit('click')
    await flushPromises()
    expect(api.publishStateMachine).not.toHaveBeenCalled()
  })

  it('clones and disables with dual concurrency and protects platform writes', async () => {
    vi.spyOn(ElMessageBox, 'prompt').mockResolvedValue({
      value: 'No longer current',
      action: 'confirm',
    } as never)
    const { wrapper } = await mountPage()
    const published = summary({
      id: 'platform-published',
      name: 'Platform order flow',
      scopeType: 'Platform',
      tenantNId: null,
      revision: 1,
      status: 'Published',
      sourceRevision: null,
      optimisticVersion: 2,
      concurrencyVersion: 'token-2',
      publishedOn: '2026-09-01T00:00:00Z',
      publishedBy: 'SYSTEM',
    })
    await selectRow(wrapper, published)

    await (await button(wrapper, 'Clone new revision')).trigger('click')
    await flushPromises()
    expect(api.cloneStateMachine).toHaveBeenCalledWith('platform-published', {
      expectedOptimisticVersion: 2,
      expectedConcurrencyVersion: 'token-2',
    })

    await (await button(wrapper, 'Disable', 0)).trigger('click')
    await flushPromises()
    expect(api.disableStateMachine).toHaveBeenCalledWith(
      expect.any(String),
      expect.objectContaining({
        expectedOptimisticVersion: expect.any(Number),
        expectedConcurrencyVersion: expect.any(String),
        changeReason: 'No longer current',
      }),
    )

    const withoutPlatformManage = permissions.filter(
      (permission) => permission !== PERMISSIONS.referenceDataPlatformManage,
    )
    const restricted = (await mountPage(withoutPlatformManage)).wrapper
    await restricted.get('[data-testid="state-machine-create"]').trigger('click')
    expect(drawer(restricted, 'state-machine-save').find('option[value="Platform"]').exists()).toBe(
      false,
    )
    expect(
      restricted.findAll('button').filter((item) => item.text() === 'Clone new revision'),
    ).toHaveLength(0)
  })

  it('loads available definitions, reads an explicit fixed source, and keeps a false evaluation visible', async () => {
    const { wrapper } = await mountPage()
    await selectRow(
      wrapper,
      summary({
        id: 'platform-published',
        name: 'Platform order flow',
        scopeType: 'Platform',
        tenantNId: null,
        revision: 1,
        status: 'Published',
        sourceRevision: null,
        optimisticVersion: 2,
        concurrencyVersion: 'token-2',
        publishedOn: '2026-09-01T00:00:00Z',
        publishedBy: 'SYSTEM',
      }),
    )
    await (await button(wrapper, 'Definition check')).trigger('click')
    await flushPromises()
    const runtimeDrawer = drawer(wrapper, 'state-machine-evaluate')

    expect(api.listAvailableStateMachines).toHaveBeenCalledWith(
      { pageIndex: 1, pageSize: 100, keyword: 'ORDER_FLOW' },
      expect.objectContaining({ signal: expect.any(AbortSignal) }),
    )
    expect(api.getStateMachineRevision).toHaveBeenCalledWith(
      'ORDER_FLOW',
      1,
      'Platform',
      null,
      expect.objectContaining({ signal: expect.any(AbortSignal) }),
    )
    await runtimeDrawer.get('[data-testid="state-machine-from-status"]').setValue('OPEN')
    await runtimeDrawer.get('[data-testid="state-machine-action-nid"]').setValue('REJECT')
    await runtimeDrawer.get('[data-testid="state-machine-evaluate"]').trigger('click')
    await flushPromises()

    expect(api.evaluateStateTransition).toHaveBeenCalledWith(
      'ORDER_FLOW',
      {
        sourceScope: 'Platform',
        sourceTenantNId: null,
        revision: 1,
        fromStatusNId: 'OPEN',
        actionNId: 'REJECT',
      },
      expect.objectContaining({ signal: expect.any(AbortSignal) }),
    )
    expect(runtimeDrawer.get('[data-testid="state-machine-evaluation-result"]').text()).toContain(
      'Not allowed',
    )
    expect(runtimeDrawer.text()).toContain('TRANSITION_NOT_DEFINED')

    await runtimeDrawer.get('[data-testid="state-machine-read-mode"]').setValue('Current')
    await flushPromises()
    expect(api.getCurrentStateMachine).toHaveBeenCalledWith(
      'ORDER_FLOW',
      'Platform',
      null,
      expect.anything(),
    )
  })

  it('preserves dirty input after 409 and exposes copy and reload recovery', async () => {
    api.updateStateMachine.mockRejectedValue(
      createApiError('business', 'Conflict', 'corr', {
        status: 409,
        code: 'REF-CONCURRENCY-CONFLICT',
        traceId: 'trace-state-machine',
      }),
    )
    const { wrapper } = await mountPage()
    await (await button(wrapper, 'Edit')).trigger('click')
    await flushPromises()
    const editor = drawer(wrapper, 'state-machine-save')
    await editor.get('[data-testid="state-machine-name"]').setValue('Unsaved state machine')
    await editor.get('[data-testid="state-machine-save"]').trigger('click')
    await flushPromises()

    expect(
      (editor.get('[data-testid="state-machine-name"]').element as HTMLInputElement).value,
    ).toBe('Unsaved state machine')
    expect(editor.text()).toContain('Your unsaved input is preserved')
    expect(editor.text()).toContain('Copy unsaved input')
    expect(editor.text()).toContain('Reload')
  })

  it('aborts stale details and guards navigation while the editor is dirty', async () => {
    let resolveFirst!: (value: StateMachineDetail) => void
    const first = new Promise<StateMachineDetail>((resolve) => (resolveFirst = resolve))
    api.getStateMachine.mockImplementation((id: string) =>
      id === 'tenant-draft'
        ? first
        : Promise.resolve(
            detail(
              summary({
                id: 'platform-published',
                name: 'Platform order flow',
                scopeType: 'Platform',
                tenantNId: null,
                status: 'Published',
                publishedOn: '2026-09-01T00:00:00Z',
              }),
            ),
          ),
    )
    const confirm = vi.spyOn(ElMessageBox, 'confirm').mockRejectedValue(new Error('cancel'))
    const { wrapper, router } = await mountPage()
    const master = wrapper.findComponent<TableInstance>(AppDataTable)
    const tenant = summary()
    const platform = summary({
      id: 'platform-published',
      name: 'Platform order flow',
      scopeType: 'Platform',
      tenantNId: null,
      status: 'Published',
      publishedOn: '2026-09-01T00:00:00Z',
      publishedBy: 'SYSTEM',
    })
    master.findComponent(VxeTable).vm.$emit('cell-click', {
      row: tenant,
      column: { field: 'name' },
    })
    await flushPromises()
    const signal = api.getStateMachine.mock.calls[0]![1].signal as AbortSignal
    master.findComponent(VxeTable).vm.$emit('cell-click', {
      row: platform,
      column: { field: 'name' },
    })
    await flushPromises()
    expect(signal.aborted).toBe(true)
    resolveFirst(detail(summary()))
    await flushPromises()
    expect(wrapper.get('.state-detail-panel').text()).toContain('Platform order flow')

    api.getStateMachine.mockResolvedValue(detail(tenant))
    await selectRow(wrapper, tenant)
    await (await button(wrapper, 'Edit')).trigger('click')
    await flushPromises()
    await drawer(wrapper, 'state-machine-save')
      .get('[data-testid="state-machine-name"]')
      .setValue('Unsaved graph')
    await router.push('/elsewhere')
    expect(router.currentRoute.value.path).toBe('/')
    expect(confirm).toHaveBeenCalled()
  })
})
