import { DOMWrapper, flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { createPinia } from 'pinia'
import ElementPlus from 'element-plus'
import { createMemoryHistory, createRouter } from 'vue-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import DashboardDesignerPage from '@/pages/pc/studio/dashboard/DashboardDesignerPage.vue'
import DashboardRuntimeView from '@/components/studio/dashboard/DashboardRuntimeView.vue'
import { dashboardComponentLabels } from '@/components/studio/dashboard/dashboardCopy'
import { useLocalizationStore } from '@/stores/localizationStore'
import sample from '@/pages/pc/studio/dashboard/samples/semiconductor-photoresist.dashboard.json'

const api = vi.hoisted(() => ({
  draft: vi.fn(),
  registry: vi.fn(),
  capabilities: vi.fn(),
  list: vi.fn(),
  save: vi.fn(),
  dashboardPreview: vi.fn(),
  revision: vi.fn(),
}))
vi.mock('@/api/studio', () => ({ getStudioApi: () => api }))
vi.mock('@/stores/authStore', () => ({ useAuthStore: () => ({ hasPermission: () => true }) }))
const resource = {
  nId: 'photoresist',
  name: sample.name,
  kind: 'Dashboard',
  optimisticVersion: '1',
  concurrencyVersion: '1',
}
let page: VueWrapper | undefined
let runtime: VueWrapper | undefined
const documentCopy = () => JSON.parse(JSON.stringify(sample.document))
function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((done) => {
    resolve = done
  })
  return { promise, resolve }
}
beforeEach(() => {
  vi.clearAllMocks()
  api.draft.mockResolvedValue({ resource, draftDocument: documentCopy() })
  api.registry.mockResolvedValue({ components: [] })
  api.capabilities.mockResolvedValue({
    modules: [{ key: 'Dashboard', installed: true, enabled: true }],
  })
  api.list.mockResolvedValue({ items: [], totalCount: '0' })
  api.save.mockImplementation(async (_resource, document) => ({
    resource,
    draftDocument: document,
  }))
  api.dashboardPreview.mockResolvedValue({ document: documentCopy(), instances: {} })
  api.revision.mockResolvedValue({ document: {} })
})
afterEach(() => {
  page?.unmount()
  runtime?.unmount()
  page = undefined
  runtime = undefined
  document.body.innerHTML = ''
  vi.useRealTimers()
})
async function open() {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/design/:nId', component: DashboardDesignerPage }],
  })
  await router.push('/design/photoresist')
  page = mount(DashboardDesignerPage, { global: { plugins: [createPinia(), ElementPlus, router] } })
  await flushPromises()
  return page
}
async function click(view: VueWrapper, label: string) {
  const button = view.findAll('button').find((button) => button.text().trim() === label)!
  expect(button, label).toBeDefined()
  await button.trigger('click')
  await flushPromises()
}
describe('dashboard designer live canvas', () => {
  it.each([{ path: [] }, { path: ['batches'] }])(
    'offers row-relative fields from the bound constant collection $path',
    async ({ path }) => {
      const document = documentCopy()
      const table = document.components.find(
        (component: { id: string }) => component.id === 'batch-table',
      )
      const rows = table.bindings[0].source.value
      document.instances = [
        {
          instanceId: 'batches',
          sourceType: 'Constant',
          value: path.length ? { batches: rows } : rows,
        },
      ]
      table.bindings = [
        {
          property: 'rows',
          source: { from: 'Instance', instanceId: 'batches', path, nullPolicy: 'Error' },
        },
      ]
      document.navigation = [
        {
          id: 'detail',
          componentId: table.id,
          event: 'RowClick',
          target: { kind: 'Dashboard', resourceNId: '' },
          parameterMap: [
            { target: 'batchNo', source: { from: 'Row', path: ['batch'], nullPolicy: 'Error' } },
          ],
          openMode: 'Current',
        },
      ]
      api.draft.mockResolvedValue({ resource, draftDocument: document })
      const view = await open()
      await click(view, '交互')
      const options = view
        .findAllComponents({ name: 'ElOption' })
        .map((option) => option.props('value'))
      expect(options).toContain('batch')
      expect(options).not.toContain('0.batch')
      expect(options).not.toContain('batches.0.batch')
    },
  )
  it('keeps page sources and parameters only in the toolbar and opening them does not add components', async () => {
    const view = await open()
    const toolbar = view.get('.dashboard-designer-toolbar')
    const library = view.get('.dashboard-component-panel')
    for (const label of ['页面数据源', '页面参数']) {
      expect(
        toolbar.findAll('button').filter((button) => button.text().trim() === label),
      ).toHaveLength(1)
      expect(library.findAll('button').some((button) => button.text().trim() === label)).toBe(false)
    }
    expect(view.find('.dashboard-library-settings').exists()).toBe(false)
    await click(view, '页面参数')
    const parameters = view
      .findAllComponents({ name: 'AppFormDrawer' })
      .find((drawer) => drawer.props('title') === '页面参数')!
    expect(parameters.props('modelValue')).toBe(true)
    parameters.vm.$emit('update:modelValue', false)
    await flushPromises()
    await click(view, '页面数据源')
    const sources = view
      .findAllComponents({ name: 'AppFormDrawer' })
      .find((drawer) => drawer.props('title') === '页面数据源')!
    expect(sources.props('modelValue')).toBe(true)
    sources.vm.$emit('update:modelValue', false)
    await flushPromises()
    await click(view, '保存')
    expect(api.save.mock.calls[0]![1]).toEqual(sample.document)
  })

  it('edits and saves parameters from the toolbar surface', async () => {
    const view = await open()
    await click(view, '页面参数')
    const form = new DOMWrapper(document.body.querySelector('.dashboard-page-parameters')!)
    await form.get('button').trigger('click')
    await flushPromises()
    await form.get('input[placeholder="参数名"]').setValue('lineId')
    await form.get('input[placeholder="默认值"]').setValue('L01')
    await click(view, '保存')
    expect(api.save.mock.calls[0]![1].parameters).toEqual([
      { name: 'lineId', label: '页面参数', type: 'String', required: false, defaultValue: 'L01' },
    ])
    expect(api.save.mock.calls[0]![1].components).toEqual(sample.document.components)
  })
  it('separates field mappings from data sources without changing the saved design', async () => {
    api.registry.mockResolvedValue({
      components: [
        {
          key: 'Table.Scroll',
          category: 'chart',
          bindableProperties: ['rows'],
          propsSchema: {
            properties: {
              instanceId: { type: 'string' },
              collectionPath: { type: 'array' },
              columns: { type: 'array' },
              rowHeight: { type: 'number', minimum: 16, maximum: 200 },
              rowKeyPath: { type: 'array' },
            },
          },
        },
      ],
    })
    const view = await open()
    await view
      .get('.dashboard-layer-list')
      .findAll('button')
      .find((item) => item.text() === '在制批次（合成演示数据）')!
      .trigger('click')
    await click(view, '数据绑定')
    const panel = view.get('.dashboard-properties')
    expect(panel.text()).toContain('表格列')
    expect(panel.text()).toContain('行标识路径')
    expect(panel.text()).not.toContain('集合路径')
    expect(panel.find('textarea[placeholder="常量值"]').exists()).toBe(false)
    await click(view, '配置数据来源')
    expect(
      panel.get('.dashboard-binding-categories button:nth-child(2)').attributes('aria-pressed'),
    ).toBe('true')
    expect(panel.text()).toContain('集合路径')
    expect((panel.get('form input').element as HTMLInputElement).value).toBe('')
    expect(panel.text()).not.toContain('表格列')
    expect(panel.get('.dashboard-structured-editor[data-kind="rows"]').isVisible()).toBe(true)
    expect(panel.find('input[placeholder="实例"]').exists()).toBe(false)
    await panel.get('.dashboard-binding-categories button:first-child').trigger('click')
    await click(view, '保存')
    expect(api.save.mock.calls[0]![1]).toEqual(sample.document)
    useLocalizationStore().setLocale('en-US', null)
    await flushPromises()
    expect(panel.get('.dashboard-binding-categories').text()).toContain('Bound fields')
    expect(panel.get('.dashboard-binding-categories').text()).toContain('Data sources')
  })
  it('edits appearance visually, applies fonts and keeps invalid JSON from replacing the design', async () => {
    const design = documentCopy()
    design.components[0].props.appearance.extension = 'retained'
    api.draft.mockResolvedValue({ resource, draftDocument: design })
    api.registry.mockResolvedValue({
      components: [
        {
          key: 'Text',
          category: 'basic',
          propsSchema: {
            properties: {
              appearance: { type: 'object' },
              align: { type: 'string' },
              wrap: { type: 'boolean' },
            },
          },
        },
      ],
    })
    const view = await open()
    await view.get('.dashboard-property-categories button:nth-child(3)').trigger('click')
    const editor = view.get('.dashboard-appearance-editor')
    expect(editor.find('textarea').exists()).toBe(false)
    await editor.get('input[role="spinbutton"]').setValue('36')
    const textColor = editor.get('input[aria-label="文字颜色"]')
    ;(textColor.element as HTMLInputElement).value = '#123456'
    await textColor.trigger('input')
    await flushPromises()
    expect((textColor.element as HTMLInputElement).value).toBe('#123456')
    await editor.get('input[aria-label="背景颜色"]').setValue('#112233')
    await editor.get('.el-select').trigger('click')
    await flushPromises()
    const mono = [...document.querySelectorAll('.el-select-dropdown__item')].find(
      (option) => option.textContent?.trim() === '等宽字体',
    ) as HTMLElement
    expect(mono).toBeDefined()
    mono.click()
    await flushPromises()
    const title = view.findAll('[data-component-id]')[0]!.element as HTMLElement
    expect(title.style.fontFamily).toBe('monospace')
    expect(title.style.fontSize).toBe('36px')
    expect(title.style.background).toBe('rgb(17, 34, 51)')
    expect(title.style.color).toBe('rgb(18, 52, 86)')
    await editor.get('input[value="json"]').setValue(true)
    const current = JSON.parse((editor.get('textarea').element as HTMLTextAreaElement).value)
    expect(current.extension).toBe('retained')
    expect(current.paddingPx).toBe(0)
    await editor.get('textarea').setValue('{"fontSizePx":999}')
    expect(editor.text()).toContain('原设置已保留')
    expect(title.style.fontSize).toBe('36px')
    await editor.get('input[value="visual"]').setValue(true)
    expect(editor.find('textarea').exists()).toBe(true)
    await editor
      .get('textarea')
      .setValue(JSON.stringify({ ...current, fontSizePx: 30, opacity: 0 }))
    await editor.get('input[value="visual"]').setValue(true)
    expect(editor.find('textarea').exists()).toBe(false)
    expect(title.style.opacity).toBe('0')
    expect(title.style.fontSize).toBe('30px')
    await click(view, '保存')
    expect(api.save.mock.calls[0]![1].components[0].props.appearance).toEqual({
      ...current,
      fontSizePx: 30,
      opacity: 0,
    })
  })
  it('shows property categories on demand and retains edits, bindings and undo across switches', async () => {
    api.registry.mockResolvedValue({
      components: [
        {
          key: 'Text',
          category: 'basic',
          propsSchema: {
            properties: {
              text: { type: 'string' },
              align: { type: 'string' },
              wrap: { type: 'boolean' },
              appearance: { type: 'object' },
            },
          },
          bindableProperties: ['text'],
        },
      ],
    })
    const view = await open()
    const panel = view.get('.dashboard-properties')
    const section = async (name: string) => {
      const button = panel
        .findAll('.dashboard-property-categories button')
        .find((item) => item.text() === name)!
      await button.trigger('click')
      await flushPromises()
    }
    expect(panel.findAll('.dashboard-property-categories button')).toHaveLength(5)
    expect(panel.text()).toContain('位置 / 尺寸')
    expect(panel.find('textarea').exists()).toBe(false)
    expect(panel.text()).not.toContain('属性绑定')
    await section('内容')
    expect(panel.text()).not.toContain('位置 / 尺寸')
    await panel.get('input').setValue('新的设计标题')
    await section('外观')
    await panel.get('.dashboard-appearance-editor input[value="json"]').setValue(true)
    await panel.get('textarea').setValue('{"color":"#66ffff","fontSizePx":28}')
    await section('内容')
    expect((panel.get('input').element as HTMLInputElement).value).toBe('新的设计标题')
    await section('数据绑定')
    await click(view, '添加')
    await click(view, '配置数据来源')
    await panel.get('input[placeholder="常量值"]').setValue('绑定后的标题')
    expect(view.findAll('[data-component-id]')[0]!.text()).toContain('绑定后的标题')
    await section('交互')
    await click(view, '添加规则')
    expect(panel.get('.el-collapse-item').classes()).toContain('is-active')
    expect(panel.find('input[placeholder="固定修订 NId"]').exists()).toBe(true)
    await click(view, '撤销')
    expect(panel.text()).toContain('暂无跳转规则')
    await section('基础')
    await click(view, '保存')
    const changed = api.save.mock.calls[0]![1]
    expect(changed.components[0].props.text).toBe('新的设计标题')
    expect(changed.components[0].props.appearance).toEqual({ color: '#66ffff', fontSizePx: 28 })
    expect(changed.components[0].bindings[0].source.value).toBe('绑定后的标题')
    expect(changed.navigation).toEqual(sample.document.navigation)
    useLocalizationStore().setLocale('en-US', null)
    await flushPromises()
    expect(
      panel.findAll('.dashboard-property-categories button').map((item) => item.text()),
    ).toEqual(['Basic', 'Content', 'Appearance', 'Data binding', 'Interaction'])
  })
  it('offers 32 visual components, hides the data category and keeps insertion undoable', async () => {
    api.registry.mockResolvedValue({
      components: Object.entries(dashboardComponentLabels).map(([key, entry]) => ({
        key,
        category: entry.category,
        propsSchema: {},
        version: '1',
      })),
    })
    const view = await open()
    const cards = () => view.findAll('.dashboard-registry-list button')
    expect(view.get('.dashboard-library-heading').text()).toContain('32 类')
    expect(cards()).toHaveLength(12)
    expect(cards().every((card) => card.find('svg[aria-hidden="true"]').exists())).toBe(true)
    const categories = view.findAll(
      '.dashboard-component-panel .dashboard-library-categories button',
    )
    await categories[1]!.trigger('click')
    expect(cards()).toHaveLength(10)
    expect(categories[1]!.attributes('aria-pressed')).toBe('true')
    const paletteCard = cards().find((card) => card.text() === '折线 / 多序列')!
    await paletteCard.trigger('click')
    expect(view.findAll('.dashboard-editor-component')).toHaveLength(24)
    const payload = new Map<string, string>()
    const dataTransfer = {
      effectAllowed: '',
      setData: (key: string, value: string) => payload.set(key, value),
      getData: (key: string) => payload.get(key) ?? '',
    }
    await paletteCard.trigger('dragstart', { dataTransfer })
    const canvas = view.get('.dashboard-editor-canvas')
    vi.spyOn(canvas.element, 'getBoundingClientRect').mockReturnValue(
      new DOMRect(100, 80, 1000, 600),
    )
    const scale = Number(view.findComponent(DashboardRuntimeView).props('editorScale'))
    await canvas.trigger('drop', {
      dataTransfer,
      clientX: 100 + 200 * scale,
      clientY: 80 + 150 * scale,
    })
    expect(view.findAll('.dashboard-editor-component')).toHaveLength(25)
    const added = view.findAll('[data-component-id]').at(-1)!.element as HTMLElement
    expect(added.style.left).toBe('200px')
    expect(added.style.top).toBe('150px')
    await click(view, '撤销')
    expect(view.findAll('.dashboard-editor-component')).toHaveLength(24)
    await view.get('input[aria-label="搜索组件"]').setValue('StatusLamp')
    expect(cards()).toHaveLength(1)
    expect(cards()[0]!.text()).toBe('状态灯')
    await categories[2]!.trigger('click')
    expect(cards()).toHaveLength(10)
    expect((view.get('input[aria-label="搜索组件"]').element as HTMLInputElement).value).toBe('')
    expect(categories).toHaveLength(3)
    for (const key of ['DataSource', 'PageParameter', 'DataQuality']) {
      await view.get('input[aria-label="搜索组件"]').setValue(key)
      expect(cards()).toHaveLength(0)
    }
    await view.get('input[aria-label="搜索组件"]').setValue('')
    useLocalizationStore().setLocale('en-US', null)
    await flushPromises()
    expect(view.get('.dashboard-library-heading').text()).toContain('Component library32 types')
    expect(view.get('.dashboard-component-panel').text()).not.toContain('Parameter value')
    expect(api.save).not.toHaveBeenCalled()
  })
  it('prioritizes the draft before loading catalog and resource options', async () => {
    const draft = deferred<unknown>()
    api.draft.mockReturnValue(draft.promise)
    const view = await open()
    expect(api.draft).toHaveBeenCalledTimes(1)
    expect(api.registry).not.toHaveBeenCalled()
    expect(api.capabilities).not.toHaveBeenCalled()
    expect(view.find('.dashboard-runtime-host').exists()).toBe(false)
    draft.resolve({ resource, draftDocument: documentCopy() })
    await flushPromises()
    expect(view.text()).toContain('PR-DEMO-101')
    expect(api.registry).toHaveBeenCalledTimes(1)
    expect(api.capabilities).toHaveBeenCalledTimes(1)
  })
  it('shows the authoritative draft while capabilities, registry and resource lists are still pending', async () => {
    const capabilities = deferred<unknown>(),
      registry = deferred<unknown>(),
      resources = deferred<unknown>()
    api.capabilities.mockReturnValue(capabilities.promise)
    api.registry.mockReturnValue(registry.promise)
    api.list.mockReturnValue(resources.promise)
    const view = await open()
    expect(api.draft).toHaveBeenCalledTimes(1)
    expect(view.text()).toContain('PR-DEMO-101')
    expect(view.findAll('.dashboard-editor-component')).toHaveLength(24)
    capabilities.resolve({ modules: [{ key: 'Dashboard', installed: true, enabled: true }] })
    await flushPromises()
    expect(api.list).toHaveBeenCalled()
    expect(view.text()).toContain('PR-DEMO-106')
    registry.resolve({ components: [] })
    resources.resolve({ items: [], totalCount: '0' })
    await flushPromises()
  })
  it('renders real sample rows, zero level, process states and appearance through the runtime renderer', async () => {
    const view = await open()
    runtime = mount(DashboardRuntimeView, {
      props: { runtime: { document: documentCopy(), instances: {} } },
      global: { plugins: [createPinia(), ElementPlus] },
    })
    expect(view.get('[data-component-id="batch-table"] table').text()).toBe(
      runtime.get('[data-component-id="batch-table"] table').text(),
    )
    expect(view.get('[data-component-id="batch-table"] tbody').findAll('tr')).toHaveLength(6)
    expect(view.text()).toContain('液位 0%')
    expect(view.text()).toContain('原料核对')
    expect(view.find('.dashboard-editor-preview').exists()).toBe(false)
    expect(view.find('.dashboard-editor-chart').exists()).toBe(false)
    const editorTitle = view.findAll('[data-component-id]')[0]!.element as HTMLElement
    const runtimeTitle = runtime.findAll('[data-component-id]')[0]!.element as HTMLElement
    expect(editorTitle.style.fontSize).toBe('32px')
    expect(editorTitle.style.cssText).toBe(runtimeTitle.style.cssText)
    expect(api.dashboardPreview).not.toHaveBeenCalled()
  })
  it('preserves drag, selection and undo while rendering the real component', async () => {
    const view = await open()
    const first = view.findAll('.dashboard-editor-component')[0]!
    first.element.dispatchEvent(
      new MouseEvent('pointerdown', { clientX: 100, clientY: 100, bubbles: true }),
    )
    const canvas = view.get('.dashboard-editor-canvas').element
    canvas.dispatchEvent(
      new MouseEvent('pointermove', { clientX: 126, clientY: 152, bubbles: true }),
    )
    canvas.dispatchEvent(new MouseEvent('pointerup', { bubbles: true }))
    await flushPromises()
    expect((view.findAll('[data-component-id]')[0]!.element as HTMLElement).style.left).toBe('74px')
    await click(view, '撤销')
    expect((view.findAll('[data-component-id]')[0]!.element as HTMLElement).style.left).toBe('24px')
    await click(view, '恢复')
    await click(view, '保存')
    const changed = api.save.mock.calls[0]![1]
    expect(changed.components[0].x).toBeCloseTo(sample.document.components[0]!.x + 50)
    expect(changed.components[0].y).toBeCloseTo(sample.document.components[0]!.y + 100)
  })
  it('fetches real instance data once and does not query again while moving components', async () => {
    vi.useFakeTimers()
    const document = documentCopy()
    document.instances = [
      { instanceId: 'live', datasetNId: 'dataset-1', revisionNId: 'rev-1', parameters: [] },
    ]
    document.components.find((item: { id: string }) => item.id === 'batch-table').bindings = [
      {
        property: 'rows',
        source: { from: 'Instance', instanceId: 'live', path: ['rows'], nullPolicy: 'Error' },
      },
    ]
    api.draft.mockResolvedValue({ resource, draftDocument: document })
    api.dashboardPreview.mockResolvedValue({
      document,
      instances: { live: { data: { rows: [{ batch: 'LIVE-001' }] } } },
    })
    const view = await open()
    await vi.advanceTimersByTimeAsync(400)
    await flushPromises()
    expect(view.get('[data-component-id="batch-table"]').text()).toContain('LIVE-001')
    expect(api.dashboardPreview).toHaveBeenCalledTimes(1)
    view
      .findAll('.dashboard-editor-component')[0]!
      .element.dispatchEvent(
        new MouseEvent('pointerdown', { clientX: 100, clientY: 100, bubbles: true }),
      )
    view
      .get('.dashboard-editor-canvas')
      .element.dispatchEvent(
        new MouseEvent('pointermove', { clientX: 126, clientY: 152, bubbles: true }),
      )
    view
      .get('.dashboard-editor-canvas')
      .element.dispatchEvent(new MouseEvent('pointerup', { bubbles: true }))
    await vi.advanceTimersByTimeAsync(500)
    await flushPromises()
    expect(api.dashboardPreview).toHaveBeenCalledTimes(1)
  })
  it('supports actual size without modifying the saved canvas', async () => {
    const view = await open()
    await click(view, '100%')
    expect(view.get('.dashboard-runtime-canvas').attributes('style')).toContain('scale(1)')
    await click(view, '保存')
    expect(api.save.mock.calls[0]![1].canvas).toEqual(sample.document.canvas)
  })
})
