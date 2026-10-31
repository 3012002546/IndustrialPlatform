import { defineComponent, h } from 'vue'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { createPinia } from 'pinia'
import ElementPlus, { ElMessageBox } from 'element-plus'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import ReportRunsPanel from '@/components/studio/report/ReportRunsPanel.vue'

const state = vi.hoisted(() => ({
  reload: vi.fn(),
  api: { reportState: vi.fn(), reportRuns: vi.fn(), reportResolve: vi.fn(), reportRetry: vi.fn() },
}))
vi.mock('@/api/studio', () => ({ getStudioApi: () => state.api }))
vi.mock('vue-router', () => ({ useRouter: () => ({ push: vi.fn() }) }))
vi.mock('@/permissions', () => ({
  PermissionGate: { template: '<div><slot /></div>' },
}))
const Table = defineComponent({
  props: ['rows'],
  setup(props, { expose }) {
    expose({ reload: state.reload })
    return () => h('output', JSON.stringify(props.rows))
  },
})
const Drawer = defineComponent({
  props: ['modelValue'],
  emits: ['update:modelValue'],
  setup(props, { slots, emit }) {
    return () =>
      props.modelValue
        ? h('aside', [
            h(
              'button',
              { class: 'close', onClick: () => emit('update:modelValue', false) },
              'Close',
            ),
            slots.default?.(),
          ])
        : null
  },
})
function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((done) => {
    resolve = done
  })
  return { promise, resolve }
}
const run = (runNId: string, status = 'Succeeded') => ({
  runNId,
  status,
  stateVersion: '3',
  artifacts: [{ nId: `artifact-${runNId}` }],
})
let panel: VueWrapper<InstanceType<typeof ReportRunsPanel>>
beforeEach(() => {
  vi.clearAllMocks()
  state.reload.mockResolvedValue(undefined)
  panel = mount(ReportRunsPanel, {
    props: { resourceNId: 'report-a' },
    global: {
      plugins: [createPinia(), ElementPlus],
      stubs: { AppDataTable: Table, AppFormDrawer: Drawer },
    },
  })
})
afterEach(() => {
  panel.unmount()
  vi.useRealTimers()
  vi.restoreAllMocks()
})

it('loads generation records on initial entry without a manual refresh', () => {
  expect(state.reload).toHaveBeenCalledOnce()
})

it('reuses the retry operation key after a lost reply without submitting a different command', async () => {
  state.api.reportState.mockResolvedValue(run('a'))
  state.api.reportRetry.mockRejectedValue(new Error('Reply lost'))
  await panel.vm.open('a')
  await flushPromises()
  const retry = () => panel.findAll('button').find((button) => button.text() === '重新取数生成')!
  await retry().trigger('click')
  await flushPromises()
  await retry().trigger('click')
  await flushPromises()
  expect(state.api.reportRetry).toHaveBeenCalledTimes(2)
  expect(state.api.reportRetry.mock.calls[0]![3]).toBeTruthy()
  expect(state.api.reportRetry.mock.calls[1]![3]).toBe(state.api.reportRetry.mock.calls[0]![3])
})

it('keeps the newest run detail when an earlier request finishes late', async () => {
  const first = deferred<ReturnType<typeof run>>()
  state.api.reportState.mockReturnValueOnce(first.promise).mockResolvedValueOnce(run('b'))
  const opening = panel.vm.open('a')
  await panel.vm.open('b')
  first.resolve(run('a'))
  await opening
  await flushPromises()
  expect(panel.text()).toContain('artifact-b')
  expect(panel.text()).not.toContain('artifact-a')
})

it('ignores a polling response after the drawer closes and another run opens', async () => {
  vi.useFakeTimers()
  const polling = deferred<ReturnType<typeof run>>()
  state.api.reportState
    .mockResolvedValueOnce(run('a', 'Running'))
    .mockReturnValueOnce(polling.promise)
    .mockResolvedValueOnce(run('b'))
  await panel.vm.open('a')
  await flushPromises()
  await vi.advanceTimersByTimeAsync(4000)
  await panel.get('.close').trigger('click')
  await panel.vm.open('b')
  polling.resolve(run('a'))
  await flushPromises()
  expect(panel.text()).toContain('artifact-b')
  expect(panel.text()).not.toContain('artifact-a')
})

it('does not resolve a different run if the selection changes during the reason prompt', async () => {
  const prompt = deferred<{ value: string; action: 'confirm' }>()
  vi.spyOn(ElMessageBox, 'prompt').mockReturnValue(
    prompt.promise as ReturnType<typeof ElMessageBox.prompt>,
  )
  state.api.reportState
    .mockResolvedValueOnce(run('a', 'Unknown'))
    .mockResolvedValueOnce(run('b', 'Unknown'))
  await panel.vm.open('a')
  await flushPromises()
  await panel
    .findAll('button')
    .find((button) => button.text() === '核对未知运行（验证服务端证据）')!
    .trigger('click')
  await panel.vm.open('b')
  prompt.resolve({ value: 'Verified the process has stopped', action: 'confirm' })
  await flushPromises()
  expect(state.api.reportResolve).not.toHaveBeenCalled()
})
