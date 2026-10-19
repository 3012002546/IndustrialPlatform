import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { defineComponent } from 'vue'
import { beforeEach, afterEach, describe, expect, it, vi } from 'vitest'
import ElementPlus, { ElDropdown } from 'element-plus'
import {
  registerSchedulerApi,
  type SchedulerApi,
  type HealthSummary,
  type SchedulerJob,
  type SchedulerRun,
} from '@/api/systemData/schedulerApi'
import PlatformHealthSummary from '@/components/home/PlatformHealthSummary.vue'
import SchedulerJobsPage from '@/pages/pc/scheduler/SchedulerJobsPage.vue'
import SchedulerRunsPage from '@/pages/pc/scheduler/SchedulerRunsPage.vue'
import PlatformHealthPage from '@/pages/pc/scheduler/PlatformHealthPage.vue'
import { useAuthStore } from '@/stores/authStore'
import { PERMISSIONS } from '@/permissions'
import { persistAuthSession } from '../fixtures/session'

vi.mock('vue-router', () => ({
  useRouter: () => ({ push: vi.fn() }),
  useRoute: () => ({ query: {} }),
}))
const tableStub = defineComponent({
  props: ['rows', 'columns', 'selectedRowKey'],
  emits: ['row-click'],
  template:
    '<div><button v-for="row in rows" :key="row.nId ?? row.serviceNId" class="select-row" @click="$emit(\'row-click\', row)">{{ row.name }}</button></div>',
})
const drawerStub = defineComponent({
  props: ['modelValue', 'busy', 'hideFooter'],
  emits: ['submit'],
  template:
    '<section v-if="modelValue"><slot /><button class="save" :disabled="busy" @click="$emit(\'submit\')">Save</button></section>',
})
const summary: HealthSummary = {
  state: 'Unknown',
  generatedOn: '2026-10-12T12:00:00Z',
  services: [],
  healthyCount: 0,
  degradedCount: 0,
  unavailableCount: 0,
  unknownCount: 1,
  disabledCount: 0,
}
const job: SchedulerJob = {
  nId: 'one',
  name: 'Job one',
  description: null,
  handlerKey: 'handler',
  parameters: { schemaVersion: 1 },
  cronExpression: '*/15 * * * *',
  timeZoneId: 'Etc/UTC',
  timeoutSeconds: 300,
  maxAttempts: 1,
  optimisticVersion: '9223372036854775807',
  concurrencyVersion: 'token',
  status: 'Disabled',
  retrySafety: 'NoAutomaticRecovery',
  isSystemOwned: false,
  createdOn: '',
  lastUpdatedOn: '',
  syncState: 'Pending',
  syncErrorCode: null,
  nextFireOn: null,
}
const run: SchedulerRun = {
  nId: 'run-one',
  jobNId: 'one',
  jobName: 'Job one',
  source: 'Manual',
  originalRunNId: null,
  status: 'Unknown',
  acceptedOn: '2026-10-19T00:00:00Z',
  startedOn: null,
  deadlineOn: null,
  completedOn: null,
  cancelRequestedOn: null,
  attemptsUsed: 1,
  maxAttempts: 1,
  retrySafety: 'NoAutomaticRecovery',
  resultCode: null,
  resultSummary: null,
  traceId: 'trace-one',
  stateVersion: '9223372036854775807',
  actions: { canCancel: false, canRetry: false, canResolve: true },
}
async function auth(permissions: string[]) {
  const pinia = createPinia()
  setActivePinia(pinia)
  persistAuthSession(permissions)
  await useAuthStore().restore()
  return pinia
}
const api = (): SchedulerApi => ({
  handlers: vi.fn().mockResolvedValue([
    {
      handlerKey: 'handler',
      name: 'handler',
      description: null,
      retrySafety: 'NoAutomaticRecovery',
      maxTimeoutSeconds: 3600,
      supportsCancellation: true,
      parametersEditable: true,
    },
  ]),
  jobs: vi.fn().mockResolvedValue({ items: [job], totalCount: '1', page: 1, pageSize: 20 }),
  job: vi.fn().mockResolvedValue(job),
  create: vi.fn(),
  update: vi.fn().mockResolvedValue(job),
  state: vi.fn(),
  preview: vi.fn(),
  trigger: vi.fn(),
  runs: vi.fn().mockResolvedValue({ items: [run], totalCount: '1', page: 1, pageSize: 25 }),
  run: vi.fn(),
  cancel: vi.fn(),
  retry: vi.fn(),
  resolve: vi.fn(),
  health: vi.fn().mockResolvedValue(summary),
})
describe('PF07 task and health flows', () => {
  beforeEach(() => {
    vi.stubEnv('VITE_AUTH_MODE', 'mock')
    sessionStorage.clear()
    localStorage.clear()
  })
  afterEach(() => {
    vi.useRealTimers()
    vi.unstubAllEnvs()
  })
  it('does not read health without permission', async () => {
    const service = api()
    registerSchedulerApi(service)
    const pinia = await auth([])
    const wrapper = mount(PlatformHealthSummary, { global: { plugins: [pinia, ElementPlus] } })
    await flushPromises()
    expect(service.health).not.toHaveBeenCalled()
    expect(wrapper.text()).toBe('')
    wrapper.unmount()
  })
  it('shows Unknown and original summary time, then clears stale success after failed refresh', async () => {
    const service = api()
    registerSchedulerApi(service)
    const pinia = await auth([PERMISSIONS.platformHealthView])
    const wrapper = mount(PlatformHealthSummary, { global: { plugins: [pinia, ElementPlus] } })
    await flushPromises()
    expect(wrapper.text()).toContain('Unknown')
    expect(wrapper.text()).toContain('0')
    vi.mocked(service.health).mockRejectedValueOnce(new Error('Unavailable'))
    await wrapper.get('button').trigger('click')
    await flushPromises()
    expect(wrapper.text()).toContain('摘要不可用')
    expect(wrapper.text()).not.toContain('Healthy')
    wrapper.unmount()
  })
  it('edits the row directly and keeps version strings unchanged', async () => {
    const service = api()
    registerSchedulerApi(service)
    const pinia = await auth([PERMISSIONS.schedulerTaskView, PERMISSIONS.schedulerTaskManage])
    const wrapper = mount(SchedulerJobsPage, {
      global: {
        plugins: [pinia, ElementPlus],
        stubs: { AppFormDrawer: drawerStub },
      },
    })
    await flushPromises()
    const edit = () => wrapper.findAll('button').find((button) => button.text() === '编辑')!
    expect(edit().attributes('disabled')).toBeUndefined()
    await edit().trigger('click')
    await flushPromises()
    await wrapper.get('.save').trigger('click')
    await flushPromises()
    expect(service.update).toHaveBeenCalledWith(
      'one',
      expect.objectContaining({
        optimisticVersion: '9223372036854775807',
        concurrencyVersion: 'token',
      }),
    )
    expect(wrapper.find('.app-data-table__selection-summary').exists()).toBe(false)
    wrapper.unmount()
  })
  it('switches job query surfaces, sends header filters, and keeps row actions instead of a button strip', async () => {
    const service = api()
    registerSchedulerApi(service)
    const pinia = await auth([PERMISSIONS.schedulerTaskView, PERMISSIONS.schedulerTaskManage])
    const wrapper = mount(SchedulerJobsPage, {
      global: { plugins: [pinia, ElementPlus], stubs: { AppFormDrawer: drawerStub } },
    })
    await flushPromises()
    await wrapper.get('input[aria-label="名称关键词"]').setValue('old-top-query')
    await wrapper.get('[data-testid="query-panel-submit"]').trigger('click')
    await flushPromises()
    expect(service.jobs).toHaveBeenLastCalledWith(
      expect.objectContaining({ keyword: 'old-top-query' }),
    )
    await wrapper.get('[data-testid="app-data-table-query-toggle"]').trigger('click')
    await flushPromises()
    expect(wrapper.find('.app-query-panel').exists()).toBe(false)
    expect(wrapper.find('.app-data-table__business-actions').exists()).toBe(false)
    expect(wrapper.find('.app-data-table__selection-summary').exists()).toBe(false)
    expect(wrapper.find('.row-actions').text()).toContain('详情')
    expect(wrapper.find('.row-actions').text()).toContain('编辑')
    await wrapper
      .get('[data-testid="app-data-table-header-filter-name"]')
      .setValue('new-header-query')
    await wrapper.get('[data-testid="app-data-table-header-filter-name"]').trigger('keyup.enter')
    await wrapper.get('[data-testid="app-data-table-header-filter-status"]').setValue('Disabled')
    await flushPromises()
    expect(service.jobs).toHaveBeenLastCalledWith(
      expect.objectContaining({ keyword: 'new-header-query', status: 'Disabled', page: 1 }),
    )
    await wrapper.get('[data-testid="app-data-table-clear"]').trigger('click')
    await flushPromises()
    expect(service.jobs).toHaveBeenLastCalledWith(
      expect.objectContaining({ keyword: '', status: '' }),
    )
    await wrapper.get('[data-testid="app-data-table-query-toggle"]').trigger('click')
    await flushPromises()
    expect(wrapper.find('.app-query-panel').exists()).toBe(true)
    expect(wrapper.find('tr.app-data-table__header-filter-row').exists()).toBe(false)
    wrapper.unmount()
  })
  it('maps run header conditions and the inclusive date range while keeping guarded actions on each row', async () => {
    const service = api()
    registerSchedulerApi(service)
    const pinia = await auth([
      PERMISSIONS.schedulerRunView,
      PERMISSIONS.schedulerRunCancel,
      PERMISSIONS.schedulerRunRetry,
      PERMISSIONS.schedulerRunResolve,
    ])
    const wrapper = mount(SchedulerRunsPage, {
      global: {
        plugins: [pinia, ElementPlus],
        stubs: { AppFormDrawer: drawerStub },
      },
    })
    await flushPromises()
    await wrapper.get('input[aria-label="任务标识"]').setValue('old-task')
    await wrapper.get('[data-testid="app-data-table-query-toggle"]').trigger('click')
    await flushPromises()
    expect(wrapper.find('.app-query-panel').exists()).toBe(false)
    expect(wrapper.find('.app-data-table__business-actions').exists()).toBe(false)
    expect(wrapper.find('.row-actions').text()).toContain('详情')
    expect(wrapper.find('.row-actions').text()).toContain('更多')
    await wrapper.get('[data-testid="app-data-table-header-filter-jobNId"]').setValue('one')
    await wrapper.get('[data-testid="app-data-table-header-filter-jobNId"]').trigger('keyup.enter')
    await wrapper.get('[data-testid="app-data-table-header-filter-source"]').setValue('Manual')
    await wrapper.get('[data-testid="app-data-table-header-filter-status"]').setValue('Unknown')
    const range = ['2026-10-01', '2026-10-02']
    expect(
      wrapper.find('[data-testid="app-data-table-header-filter-acceptedOn-range"]').exists(),
    ).toBe(true)
    const table = wrapper.findComponent({ name: 'AppDataTable' }).vm as unknown as {
      setHeaderFilter: (field: string, value: unknown) => void
      reload: () => Promise<void>
    }
    table.setHeaderFilter('acceptedOn', range)
    await table.reload()
    await flushPromises()
    expect(service.runs).toHaveBeenLastCalledWith(
      expect.objectContaining({
        jobNId: 'one',
        source: 'Manual',
        status: 'Unknown',
        from: new Date('2026-10-01T00:00:00').toISOString(),
        to: new Date('2026-10-02T23:59:59.999').toISOString(),
      }),
    )
    const dropdown = wrapper.findComponent(ElDropdown)
    expect(dropdown.text()).toContain('更多')
    expect(
      wrapper
        .findAllComponents({ name: 'ElDropdownItem' })
        .map((item) => ({ label: item.text(), disabled: item.props('disabled') })),
    ).toEqual([
      { label: '取消', disabled: true },
      { label: '手工重试', disabled: true },
      { label: '核对结果', disabled: false },
    ])
    await wrapper.get('[data-testid="app-data-table-query-toggle"]').trigger('click')
    await flushPromises()
    expect(wrapper.find('.app-query-panel').exists()).toBe(true)
    expect(service.runs).toHaveBeenLastCalledWith(
      expect.objectContaining({
        jobNId: '',
        status: '',
        source: '',
        from: undefined,
        to: undefined,
      }),
    )
    wrapper.unmount()
  })
  it('does not expose job mutations without their permissions', async () => {
    registerSchedulerApi(api())
    const pinia = await auth([PERMISSIONS.schedulerTaskView])
    const wrapper = mount(SchedulerJobsPage, { global: { plugins: [pinia, ElementPlus] } })
    await flushPromises()
    expect(wrapper.find('.row-actions').text()).toBe('详情')
    wrapper.unmount()
  })
  it('clears service selection when a new health summary removes the service', async () => {
    const service = api()
    const existing = {
      serviceNId: 'service-one',
      name: 'Visible service',
      state: 'Degraded',
      lastObservedOn: null,
      checks: [],
      configuredCount: 1,
      enabledCount: 1,
      healthyCount: 0,
      degradedCount: 1,
      unavailableCount: 0,
      unknownCount: 0,
      disabledCount: 0,
    }
    vi.mocked(service.health)
      .mockResolvedValueOnce({ ...summary, services: [existing] })
      .mockResolvedValueOnce(summary)
    registerSchedulerApi(service)
    const pinia = await auth([PERMISSIONS.platformHealthWorkspaceView])
    const wrapper = mount(PlatformHealthPage, {
      global: { plugins: [pinia, ElementPlus], stubs: { AppDataTable: tableStub } },
    })
    await flushPromises()
    expect(wrapper.text()).toContain('Visible service')
    await wrapper
      .findAll('button')
      .find((button) => button.text() === '刷新')!
      .trigger('click')
    await flushPromises()
    expect(wrapper.text()).not.toContain('Visible service')
    expect(wrapper.text()).toContain('请选择服务')
    wrapper.unmount()
  })
})
