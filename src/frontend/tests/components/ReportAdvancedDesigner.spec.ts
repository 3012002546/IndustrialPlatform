import type { Json } from '@/api/studio'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import ElementPlus from 'element-plus'
import { afterEach, expect, it, vi } from 'vitest'
import ReportDesignerPage from '@/pages/pc/studio/report/ReportDesignerPage.vue'
import ReportDocumentEditor from '@/components/studio/report/ReportDocumentEditor.vue'
import ReportAdvancedEditor from '@/components/studio/report/ReportAdvancedEditor.vue'
import { initialReportDocument } from '@/components/studio/report/model'
import { reportControls } from '@/components/studio/report/catalogue'
import { advancedControlTypes } from '@/components/studio/report/advancedControls'
const state = vi.hoisted(() => ({
  kind: 'Report',
  api: { draft: vi.fn(), list: vi.fn(), reportDraftContract: vi.fn(), save: vi.fn() },
}))
vi.mock('@/api/studio', () => ({ getStudioApi: () => state.api }))
vi.mock('vue-router', () => ({
  useRoute: () => ({ params: { nId: 'report' }, query: {} }),
  useRouter: () => ({}),
  onBeforeRouteLeave: vi.fn(),
}))
vi.mock('@/permissions', () => ({ PermissionGate: { template: '<div><slot /></div>' } }))
afterEach(() => {
  vi.unstubAllGlobals()
  vi.clearAllMocks()
})
function setup(document: Json, kind = 'Report') {
  vi.stubGlobal(
    'ResizeObserver',
    class {
      observe() {}
      disconnect() {}
    },
  )
  const resource = {
    nId: 'report',
    kind,
    name: 'R3 test',
    optimisticVersion: '1',
    concurrencyVersion: 'fixture',
  }
  state.api.draft.mockResolvedValue({ resource, draftDocument: document })
  state.api.list.mockResolvedValue({ items: [] })
  state.api.reportDraftContract.mockResolvedValue({})
  state.api.save.mockImplementation(async (_resource, draft) => ({
    resource,
    draftDocument: JSON.parse(JSON.stringify(draft)),
  }))
  return mount(ReportDesignerPage, {
    global: {
      plugins: [createPinia(), ElementPlus],
      stubs: { AppPage: { template: '<main><slot name="actions"/><slot/></main>' } },
    },
  })
}
it('adds advanced controls and saves their settings through the existing undo history', async () => {
  const document = initialReportDocument()
  const page = setup(document)
  const click = async (label: string) => {
    await page
      .findAll('button')
      .find((b) => b.text() === label)!
      .trigger('click')
    await flushPromises()
  }
  try {
    await flushPromises()
    const labels = ['字符格', '形状', '条形码', '图表', '仪表', '趋势图', '打印透视表']
    for (const label of labels) {
      await page
        .findAll('.report-control-palette button')
        .find((b) => b.text() === label)!
        .trigger('keydown.enter')
      await flushPromises()
    }
    await page
      .findAll('[role="tab"]')
      .find((tab) => tab.text() === '内容与绑定')!
      .trigger('click')
    await flushPromises()
    const editor = page.findComponent(ReportAdvancedEditor)
    const selected = editor.props('modelValue')
    editor.vm.$emit('update:modelValue', {
      ...selected,
      advanced: { ...selected.advanced, totals: false },
    })
    await flushPromises()
    await click('撤销')
    await click('恢复')
    await click('保存草稿')
    const saved = state.api.save.mock.lastCall![1]
    expect(saved.bands[0].controls.map((c: Json) => c.type)).toEqual(
      advancedControlTypes.filter((t) => t !== 'Line'),
    )
    for (const control of saved.bands[0].controls) expect(control.advanced).toBeDefined()
    expect(saved.bands[0].controls.find((c: Json) => c.type === 'Barcode').value.value).toBe(
      'CODE39',
    )
    expect(
      saved.bands[0].controls.find((c: Json) => c.type === 'Chart').advanced.series,
    ).toHaveLength(1)
    expect(reportControls.some((c) => c.type === 'Table')).toBe(true)
    expect(saved.bands[0].controls.find((c: Json) => c.type === 'PivotTable').advanced.totals).toBe(
      false,
    )
    expect(saved.paper).toEqual(document.paper)
  } finally {
    page.unmount()
  }
})
it.each(['Normal', 'DataDriven'])(
  'preserves %s bundle configuration on save',
  async (stitchMode) => {
    const document = initialReportDocument('ReportBundle', 'ElectronicRecord')
    document.assembly.stitchMode = stitchMode
    document.assembly.rules = [
      {
        id: 'normal',
        name: '原规则',
        reportNId: 'existing',
        parameterMap: [],
        presence: { mode: 'ParentPath', path: ['records'] },
        content: { mode: 'WholeReport' },
      },
    ]
    document.assembly.templates = [
      {
        id: 'driven',
        reportNId: 'existing',
        parameterMap: [],
        instanceMap: [{ target: 'rows', source: { from: 'Row', path: ['records'] } }],
        scope: 'Operation',
        required: true,
        breakBefore: 'Auto',
      },
    ]
    const before = JSON.parse(JSON.stringify(document.assembly))
    const page = setup(document, 'ReportBundle')
    try {
      await flushPromises()
      await page
        .findAll('button')
        .find((b) => b.text() === '保存草稿')!
        .trigger('click')
      await flushPromises()
      expect(state.api.save.mock.lastCall![1].assembly).toEqual(before)
    } finally {
      page.unmount()
    }
  },
)

it('saves and reloads subreport pins, PDF assets and contents without changing data values', async () => {
  const document = initialReportDocument()
  document.assets = [
    {
      id: 'pdf-a',
      fileNId: 'file-a',
      referenceNId: 'ref-a',
      sha256: 'a'.repeat(64),
      mediaType: 'application/pdf',
    },
  ]
  const page = setup(document)
  try {
    await flushPromises()
    for (const [label, type] of [
      ['子报表', 'SubReport'],
      ['PDF 内容', 'PdfContent'],
      ['目录', 'TableOfContents'],
    ]) {
      await page
        .findAll('.report-control-palette button')
        .find((b) => b.text() === label)!
        .trigger('keydown.enter')
      await flushPromises()
      await page
        .findAll('[role="tab"]')
        .find((t) => t.text() === '内容与绑定')!
        .trigger('click')
      await flushPromises()
      const editor = page.findComponent(ReportDocumentEditor)
      const control = JSON.parse(JSON.stringify(editor.props('modelValue')))
      expect(control.type).toBe(type)
      if (type === 'SubReport')
        control.subReport = {
          reportNId: 'child',
          revisionNId: 'revision-fixed',
          parameterMap: [],
          instanceMap: [
            {
              target: 'rows',
              source: { from: 'Instance', instanceId: 'root', path: [], nullPolicy: 'Error' },
            },
          ],
        }
      if (type === 'PdfContent')
        control.value = { from: 'Constant', value: 'pdf-a', nullPolicy: 'Error' }
      if (type === 'TableOfContents') control.toc.title = '光刻胶批记录目录'
      editor.vm.$emit('update:modelValue', control)
      await flushPromises()
    }
    await page
      .findAll('button')
      .find((b) => b.text() === '保存草稿')!
      .trigger('click')
    await flushPromises()
    const saved = state.api.save.mock.lastCall![1]
    const controls = saved.bands.flatMap((b: Json) => b.controls)
    expect(controls.find((c: Json) => c.type === 'SubReport').subReport.revisionNId).toBe(
      'revision-fixed',
    )
    expect(controls.find((c: Json) => c.type === 'PdfContent').value.value).toBe('pdf-a')
    expect(controls.find((c: Json) => c.type === 'TableOfContents').toc.title).toBe(
      '光刻胶批记录目录',
    )
    expect(saved.assets).toEqual(document.assets)
    expect(saved.instances).toEqual(document.instances)
    for (const band of saved.bands.filter((b: Json) =>
      b.controls.some((c: Json) => ['SubReport', 'PdfContent', 'TableOfContents'].includes(c.type)),
    ))
      expect(band.controls).toHaveLength(1)
    page.unmount()
    const reopened = setup(JSON.parse(JSON.stringify(saved)))
    try {
      await flushPromises()
      expect(reopened.findAll('.report-control')).toHaveLength(3)
      await reopened
        .findAll('.report-control')
        .find((c) => c.text() === '子报表')!
        .trigger('click')
      await reopened
        .findAll('[role="tab"]')
        .find((t) => t.text() === '内容与绑定')!
        .trigger('click')
      await flushPromises()
      expect(reopened.findComponent(ReportDocumentEditor).props('modelValue').subReport).toEqual(
        controls.find((c: Json) => c.type === 'SubReport').subReport,
      )
    } finally {
      reopened.unmount()
    }
  } finally {
    if (page.exists()) page.unmount()
  }
})
