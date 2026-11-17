import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import ElementPlus from 'element-plus'
import { expect, it, vi } from 'vitest'
import ReportDesignerPage from '@/pages/pc/studio/report/ReportDesignerPage.vue'
import { initialReportDocument } from '@/components/studio/report/model'
import ReportQueryEditor from '@/components/studio/report/ReportQueryEditor.vue'

const state = vi.hoisted(() => ({
  api: {
    draft: vi.fn(),
    list: vi.fn(),
    reportDraftContract: vi.fn(),
    reportAsset: vi.fn(),
    save: vi.fn(),
    revision: vi.fn(),
  },
}))

it('opens Query as configuration without a paper canvas and saves field settings', async () => {
  const document = initialReportDocument()
  document.mode = 'Query'
  document.instances = [{ instanceId: 'rows', sourceType: 'Constant', value: [{ amount: 10 }] }]
  document.bands[0].data.instanceId = 'rows'
  document.bands[0].controls = [
    { id: 'table', type: 'Table', table: { columns: [{ id: 'amount', label: '数量' }] } },
  ]
  document.query = {
    instanceId: 'rows',
    resultBandId: document.bands[0].id,
    columns: [{ controlId: 'table', columnId: 'amount', visible: true, sortable: true }],
    filters: [],
    defaultSort: [],
  }
  const resource = { nId: 'report', kind: 'Report', name: 'Query fixture' }
  state.api.draft.mockResolvedValue({ resource, draftDocument: document })
  state.api.list.mockResolvedValue({
    items: [{ nId: 'dataset', name: 'Dataset', publishedRevisionNId: 'revision' }],
  })
  state.api.revision.mockResolvedValue({
    nId: 'revision',
    document: {
      result: {
        type: 'Array',
        item: {
          type: 'Object',
          children: Array.from({ length: 60 }, (_, i) => ({ name: 'f' + i, type: 'String' })),
        },
      },
      parameters: [{ name: 'code', type: 'String', required: false, nullable: true }],
    },
  })
  state.api.reportDraftContract.mockResolvedValue({})
  state.api.save.mockImplementation(async (_resource, draft) => ({
    resource,
    draftDocument: JSON.parse(JSON.stringify(draft)),
  }))
  const page = mount(ReportDesignerPage, {
    global: {
      plugins: [createPinia(), ElementPlus],
      stubs: {
        AppPage: { template: '<main><slot name="actions"/><slot/></main>' },
        AppFormDrawer: {
          props: ['modelValue'],
          template: '<aside v-if="modelValue"><slot /></aside>',
        },
      },
    },
  })
  try {
    await flushPromises()
    expect(page.findComponent({ name: 'StudioEditorShell' }).exists()).toBe(false)
    expect(page.find('.report-design-shell').exists()).toBe(false)
    expect(page.text()).not.toContain('预览')
    const editor = page.getComponent(ReportQueryEditor)
    expect(editor.props('queryOnly')).toBe(true)
    await editor.findAllComponents({ name: 'ElCheckbox' })[0]!.vm.$emit('update:modelValue', false)
    await page
      .findAll('button')
      .find((button) => button.text() === '保存草稿')!
      .trigger('click')
    await flushPromises()
    expect(state.api.save.mock.lastCall![1].query.columns[0].visible).toBe(false)
    expect(state.api.save.mock.lastCall![1].mode).toBe('Query')
    await page
      .findAll('button')
      .find((button) => button.text() === '数据与参数')!
      .trigger('click')
    await flushPromises()
    await page
      .findAll('[role="tab"]')
      .find((tab) => tab.text() === '参数')!
      .trigger('click')
    await page
      .findAll('button')
      .find((button) => button.text() === '添加参数')!
      .trigger('click')
    await page
      .findAll('button')
      .find((button) => button.text() === '保存草稿')!
      .trigger('click')
    await flushPromises()
    expect(state.api.save.mock.lastCall![1].parameters).toHaveLength(1)
    page
      .get('.report-query-config')
      .getComponent({ name: 'ElSelect' })
      .vm.$emit('update:modelValue', 'dataset')
    await flushPromises()
    expect(page.getComponent(ReportQueryEditor).props('modelValue').query.columns).toHaveLength(60)
    expect(page.text()).not.toContain('添加字段')
    expect(page.getComponent(ReportQueryEditor).props('modelValue').query.filters).toEqual([])
    const filterSelect = page
      .getComponent(ReportQueryEditor)
      .findAllComponents({ name: 'ElSelect' })
      .find((select) => select.props('multiple') === true)!
    const first = page.getComponent(ReportQueryEditor).props('modelValue').query.columns[0]
    filterSelect.vm.$emit('update:modelValue', [first.columnId])
    await flushPromises()
    expect(page.getComponent(ReportQueryEditor).props('modelValue').query.filters[0]).toMatchObject(
      { fieldId: first.columnId, dataType: 'String' },
    )
    const beforeFailure = JSON.stringify(page.getComponent(ReportQueryEditor).props('modelValue'))
    state.api.revision.mockRejectedValueOnce(new Error('Schema unavailable'))
    await page
      .findAll('button')
      .find((button) => button.text() === '同步数据集字段')!
      .trigger('click')
    await flushPromises()
    expect(JSON.stringify(page.getComponent(ReportQueryEditor).props('modelValue'))).toBe(
      beforeFailure,
    )
    expect(page.text()).toContain('Schema unavailable')
  } finally {
    page.unmount()
  }
})
vi.mock('@/api/studio', () => ({ getStudioApi: () => state.api }))
vi.mock('vue-router', () => ({
  useRoute: () => ({ params: { nId: 'report' }, query: {} }),
  useRouter: () => ({}),
  onBeforeRouteLeave: vi.fn(),
}))
vi.mock('@/permissions', () => ({ PermissionGate: { template: '<div><slot /></div>' } }))
vi.mock('@/components/studio/shared/StudioFileField.vue', () => ({
  default: {
    template: `<button class="choose-image" @click="$emit('update:modelValue', { fileNId: 'image-file' })">Choose image</button>`,
    emits: ['update:modelValue'],
  },
}))

it('keeps attaching an image and its binding in one undoable edit', async () => {
  vi.stubGlobal(
    'ResizeObserver',
    class {
      observe() {}
      disconnect() {}
    },
  )
  const document = initialReportDocument()
  document.bands[0].controls = [
    {
      id: 'picture',
      type: 'Image',
      xMm: 0,
      yMm: 0,
      widthMm: 50,
      heightMm: 5,
      value: { from: 'Constant', value: '', nullPolicy: 'Error' },
    },
  ]
  const resource = {
    nId: 'report',
    kind: 'Report',
    name: 'Fixture',
    optimisticVersion: '1',
    concurrencyVersion: 'fixture',
  }
  state.api.draft.mockResolvedValue({ resource, draftDocument: document })
  state.api.list.mockResolvedValue({ items: [] })
  state.api.reportDraftContract.mockResolvedValue({})
  state.api.reportAsset.mockResolvedValue({ id: 'attached-image', referenceNId: 'image-reference' })
  state.api.save.mockImplementation(async (_resource, draft) => ({
    resource,
    draftDocument: structuredClone(JSON.parse(JSON.stringify(draft))),
  }))
  const page = mount(ReportDesignerPage, {
    global: {
      plugins: [createPinia(), ElementPlus],
      stubs: { AppPage: { template: '<main><slot name="actions"/><slot/></main>' } },
    },
  })
  const click = async (label: string) => {
    await page
      .findAll('button')
      .find((button) => button.text() === label)!
      .trigger('click')
    await flushPromises()
  }
  try {
    await flushPromises()
    await page.get('.report-control').trigger('click')
    await page
      .findAll('[role="tab"]')
      .find((tab) => tab.text() === '内容与绑定')!
      .trigger('click')
    await flushPromises()
    await page.get('.choose-image').trigger('click')
    await click('附加图片（扫描未完成可重试）')
    await click('撤销')
    await click('恢复')
    await click('保存草稿')
    expect(state.api.save.mock.lastCall![1].bands[0].controls[0].value.value).toBe('attached-image')
    expect(state.api.save.mock.lastCall![1].assets).toHaveLength(1)
  } finally {
    page.unmount()
    vi.unstubAllGlobals()
  }
})
