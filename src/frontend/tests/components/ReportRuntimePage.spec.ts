import { defineComponent, h } from 'vue'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { createPinia } from 'pinia'
import ElementPlus from 'element-plus'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import ReportRuntimePage from '@/pages/pc/studio/report/ReportRuntimePage.vue'

const state = vi.hoisted(() => ({
  reload: vi.fn(),
  api: {
    client: { get: vi.fn() },
    reportQueryOptions: vi.fn(),
    reportQuery: vi.fn(),
    reportRun: vi.fn(),
  },
}))
vi.mock('@/api/studio', () => ({ getStudioApi: () => state.api }))
vi.mock('vue-router', () => ({
  useRoute: () => ({ params: { nId: 'report-a' } }),
  useRouter: () => ({ back: vi.fn() }),
}))
vi.mock('@/permissions', () => ({ PermissionGate: { template: '<div><slot /></div>' } }))
const Table = defineComponent({
  props: ['loader'],
  setup(_props, { expose }) {
    expose({ reload: state.reload })
    return () => h('div')
  },
})
const Query = defineComponent({
  emits: ['reset'],
  setup(_props, { slots, emit }) {
    return () =>
      h('section', [
        slots.default?.(),
        h('button', { class: 'reset', onClick: () => emit('reset') }, 'Reset'),
      ])
  },
})
let page: VueWrapper
beforeEach(() => {
  vi.clearAllMocks()
  state.reload.mockResolvedValue(undefined)
  state.api.client.get.mockResolvedValue({
    resource: { nId: 'report-a', publishedRevisionNId: 'revision-a' },
    document: {
      mode: 'Query',
      bands: [],
      parameters: [{ name: 'title', defaultValue: 'Default title' }],
      query: {
        columns: [],
        filters: [
          { parameterName: 'title', control: 'Text' },
          { parameterName: 'choice', control: 'Select' },
        ],
      },
    },
  })
  state.api.reportQueryOptions.mockResolvedValue({ options: {} })
  state.api.reportQuery.mockResolvedValue({ items: [], totalCount: '0', page: 1, pageSize: 20 })
  state.api.reportRun.mockResolvedValue({ runNId: 'run-a' })
})
afterEach(() => page?.unmount())
function render() {
  page = mount(ReportRuntimePage, {
    global: {
      plugins: [createPinia(), ElementPlus],
      stubs: {
        AppPage: { template: '<main><slot /></main>' },
        AppQueryPanel: Query,
        AppDataTable: Table,
        ReportRunsPanel: true,
      },
    },
  })
  return page
}

it('restores parameter defaults on reset and applies defaults until sorting is explicitly cleared', async () => {
  render()
  await flushPromises()
  const input = page.get('input[aria-label="title"]')
  expect((input.element as HTMLInputElement).value).toBe('Default title')
  await input.setValue('Changed title')
  await page.get('.reset').trigger('click')
  await flushPromises()
  expect((input.element as HTMLInputElement).value).toBe('Default title')
  const loader = page.getComponent(Table).props('loader')
  await loader({ pageIndex: 1, pageSize: 20 })
  expect(state.api.reportQuery.mock.lastCall![1].sort).toBeUndefined()
  await loader({ pageIndex: 1, pageSize: 20, sort: { field: 'total', order: 'desc' } })
  expect(state.api.reportQuery.mock.lastCall![1].sort).toEqual([
    { fieldId: 'total', direction: 'Desc', nulls: 'Last' },
  ])
  await loader({ pageIndex: 1, pageSize: 20 })
  expect(state.api.reportQuery.mock.lastCall![1].sort).toEqual([])
})

it('ignores an old option failure after parameters change and newer options are loaded', async () => {
  let rejectOld!: (reason: Error) => void
  state.api.reportQueryOptions
    .mockReturnValueOnce(
      new Promise((_resolve, reject) => {
        rejectOld = reject
      }),
    )
    .mockResolvedValueOnce({ options: { choice: [{ value: 'new', label: 'Current option' }] } })
  render()
  await flushPromises()
  await page.get('input[aria-label="title"]').setValue('Changed title')
  page.findAllComponents({ name: 'ElSelect' })[0]!.vm.$emit('visible-change', true)
  await flushPromises()
  rejectOld(new Error('Old options unavailable'))
  await flushPromises()
  expect(page.text()).not.toContain('Old options unavailable')
  expect(
    page
      .findAllComponents({ name: 'ElOption' })
      .some((option) => option.props('label') === 'Current option'),
  ).toBe(true)
})

it('reuses the formal run key after a lost acknowledgement and changes it when input changes', async () => {
  state.api.reportRun.mockRejectedValue(new Error('Acknowledgement lost'))
  render()
  await flushPromises()
  const generate = () =>
    page.findAll('button').find((button) => button.text() === '生成当前查询全部结果')!
  await generate().trigger('click')
  await flushPromises()
  await generate().trigger('click')
  await flushPromises()
  const first = state.api.reportRun.mock.calls[0]![2]
  expect(first).toBeTruthy()
  expect(state.api.reportRun.mock.calls[1]![2]).toBe(first)
  await page.get('input[aria-label="title"]').setValue('Changed command')
  await generate().trigger('click')
  await flushPromises()
  expect(state.api.reportRun.mock.calls[2]![2]).not.toBe(first)
})
