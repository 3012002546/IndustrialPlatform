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
  onBeforeRouteLeave: vi.fn(),
}))
vi.mock('@/permissions', () => ({ PermissionGate: { template: '<div><slot /></div>' } }))
const Table = defineComponent({
  props: ['loader'],
  setup(_props, { expose }) {
    expose({
      reload: state.reload,
      exportSnapshot: async () => ({ columns: ['Edited'], rows: [[777]] }),
    })
    return () => h('div')
  },
})
const Query = defineComponent({
  emits: ['reset', 'submit'],
  setup(_props, { slots, emit }) {
    return () =>
      h('section', [
        slots.default?.(),
        h('button', { class: 'reset', onClick: () => emit('reset') }, 'Reset'),
        h('button', { class: 'submit', onClick: () => emit('submit') }, 'Submit'),
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
function render(props = {}) {
  page = mount(ReportRuntimePage, {
    props,
    global: {
      plugins: [createPinia(), ElementPlus],
      stubs: {
        AppPage: {
          template: '<main><slot name="heading-meta" /><slot name="actions" /><slot /></main>',
        },
        AppFormDrawer: {
          props: ['modelValue'],
          template: '<aside v-if="modelValue"><slot /></aside>',
        },
        AppQueryPanel: Query,
        AppQuerySpreadsheet: Table,
        ReportRunsPanel: true,
      },
    },
  })
  return page
}
async function openExport() {
  await page
    .findAll('button')
    .find((button) => button.text() === '整份报表导出')!
    .trigger('click')
  await flushPromises()
}

it('uses typed dataset field controls without parameters and retains submitted filters for paging and export', async () => {
  const contract = await state.api.client.get()
  contract.document.parameters = []
  contract.document.query.filters = [
    { fieldId: 'factory', label: '工厂', dataType: 'String' },
    { fieldId: 'amount', label: '数量', dataType: 'Decimal' },
    { fieldId: 'day', label: '生产日期', dataType: 'DateTime' },
    { fieldId: 'valid', label: '复核', dataType: 'Boolean' },
  ]
  render()
  await flushPromises()
  const rawLoader = page.getComponent(Table).props('loader')
  const loader = async (request: unknown) => {
    const result = await rawLoader(request)
    result.accepted?.()
    return result
  }
  state.reload.mockImplementation(() => loader({ pageIndex: 1, pageSize: 200 }))
  await page.get('input[aria-label="工厂"]').setValue('上海')
  await page.get('input[aria-label="数量 最小值"]').setValue('0')
  await page.get('input[aria-label="数量 最大值"]').setValue('9007199254740993.123456')
  page
    .getComponent({ name: 'ElDatePicker' })
    .vm.$emit('update:modelValue', ['2026-11-01', '2026-11-16'])
  page.getComponent({ name: 'ElSelect' }).vm.$emit('update:modelValue', false)
  await page.get('.submit').trigger('click')
  await flushPromises()
  const expected = [
    { fieldId: 'factory', value: '上海' },
    { fieldId: 'amount', min: '0', max: '9007199254740993.123456' },
    { fieldId: 'day', min: '2026-11-01', max: '2026-11-16' },
    { fieldId: 'valid', value: false },
  ]
  expect(state.api.reportQuery.mock.lastCall![1]).toMatchObject({
    page: 1,
    filters: expected,
    parameters: {},
  })
  await page.get('input[aria-label="工厂"]').setValue('尚未提交')
  await loader({ pageIndex: 2, pageSize: 20 })
  expect(state.api.reportQuery.mock.lastCall![1]).toMatchObject({ page: 2, filters: expected })
  await openExport()
  await page
    .findAll('button')
    .find((button) => button.text() === '生成当前筛选及临时计算结果')!
    .trigger('click')
  await flushPromises()
  expect(state.api.reportRun.mock.lastCall![1].filters).toEqual(expected)
  expect(state.api.reportRun.mock.lastCall![1].querySnapshot).toEqual({
    columns: ['Edited'],
    rows: [[777]],
  })
  await page.get('.reset').trigger('click')
  await flushPromises()
  expect(state.api.reportQuery.mock.lastCall![1].filters).toEqual([])
  expect(state.api.reportQuery.mock.lastCall![1].page).toBe(1)
})

it('identifies Query reports and shows background export history only inside the export panel', async () => {
  const contract = await state.api.client.get()
  contract.document.query.filters = []
  render()
  await flushPromises()
  expect(page.text()).toContain('查询报表（Query）')
  expect(page.findComponent(Query).exists()).toBe(false)
  expect(page.findComponent({ name: 'ReportRunsPanel' }).exists()).toBe(false)
  await openExport()
  expect(page.findComponent({ name: 'ReportRunsPanel' }).exists()).toBe(true)
  expect(page.text()).toContain('后台')
})

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
  await openExport()
  const generate = () =>
    page.findAll('button').find((button) => button.text() === '生成当前筛选及临时计算结果')!
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
  expect(state.api.reportRun.mock.calls[2]![2]).toBe(first)
  const result = await page.getComponent(Table).props('loader')({ pageIndex: 1, pageSize: 20 })
  result.accepted()
  await generate().trigger('click')
  await flushPromises()
  expect(state.api.reportRun.mock.calls[3]![2]).not.toBe(first)
})

it('uses the pinned resource revision and typed parameters in an embedded content host', async () => {
  const contract = await state.api.client.get()
  state.api.client.get.mockResolvedValue({
    ...contract,
    revisionNId: 'pinned-revision',
    resource: { nId: 'linked-report', publishedRevisionNId: 'newer-revision' },
  })
  state.api.client.get.mockClear()
  render({
    resourceNId: 'linked-report',
    revisionNId: 'pinned-revision',
    initialParameters: { title: 'Inspection A' },
    embedded: true,
  })
  await flushPromises()
  expect(state.api.client.get).toHaveBeenCalledWith(
    '/api/v1/studio/reports/linked-report/runtime?revisionNId=pinned-revision',
  )
  expect((page.get('input[aria-label="title"]').element as HTMLInputElement).value).toBe(
    'Inspection A',
  )
  const loader = page.getComponent(Table).props('loader')
  await loader({ pageIndex: 1, pageSize: 20 })
  expect(state.api.reportQuery).toHaveBeenLastCalledWith(
    'linked-report',
    expect.objectContaining({
      revisionNId: 'pinned-revision',
      parameters: { title: 'Inspection A' },
    }),
    undefined,
  )
  await page.get('input[aria-label="title"]').setValue('Changed')
  await page.get('.reset').trigger('click')
  expect((page.get('input[aria-label="title"]').element as HTMLInputElement).value).toBe(
    'Inspection A',
  )
})

it('submits every registered export format without changing the fixed revision or parameters', async () => {
  render()
  await flushPromises()
  await openExport()
  const selector = page.findAllComponents({ name: 'ElSelect' }).at(-1)!
  const formats = ['Pdf', 'Html', 'Xlsx', 'Xls', 'Docx', 'Rtf', 'Mht', 'Text', 'Csv', 'Png', 'Jpeg']
  expect(
    selector
      .findAllComponents({ name: 'ElOption' })
      .map((option) => option.props('value'))
      .sort(),
  ).toEqual([...formats].sort())
  for (const format of formats) {
    selector.vm.$emit('update:modelValue', format)
    await flushPromises()
    await page
      .findAll('button')
      .find((button) => button.text() === '生成当前筛选及临时计算结果')!
      .trigger('click')
    await flushPromises()
    expect(state.api.reportRun.mock.lastCall![1]).toMatchObject({
      format,
      revisionNId: 'revision-a',
      parameters: { title: 'Default title' },
    })
  }
  expect(new Set(state.api.reportRun.mock.calls.map((call) => call[2])).size).toBe(formats.length)
})

it('exports accepted parameters after a draft change or failed query', async () => {
  render()
  await flushPromises()
  const loader = page.getComponent(Table).props('loader')
  const result = await loader({ pageIndex: 1, pageSize: 20 })
  result.accepted()
  await page.get('input[aria-label="title"]').setValue('Unsubmitted')
  state.api.reportQuery.mockRejectedValueOnce(new Error('failed'))
  await expect(loader({ pageIndex: 1, pageSize: 20 })).rejects.toThrow('failed')
  await openExport()
  await page
    .findAll('button')
    .find((b) => b.text() === '生成当前筛选及临时计算结果')!
    .trigger('click')
  await flushPromises()
  expect(state.api.reportRun.mock.lastCall![1].parameters).toEqual({ title: 'Default title' })
})
