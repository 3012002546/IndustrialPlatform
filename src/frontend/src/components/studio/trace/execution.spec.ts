import { afterEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import ElementPlus from 'element-plus'
import { defineComponent, ref } from 'vue'
import TraceStartPicker from './TraceStartPicker.vue'
import TraceRuntimePage from '@/pages/pc/studio/trace/TraceRuntimePage.vue'
import GraphWorkbench from '../graph/GraphWorkbench.vue'
import { studioRoutes } from '@/router/studioRoutes'

const api = vi.hoisted(() => ({
  models: vi.fn(),
  contract: vi.fn(),
  starts: vi.fn(),
  query: vi.fn(),
  detail: vi.fn(),
  section: vi.fn(),
}))
vi.mock('@/api/studioTrace', () => ({ getTraceApi: () => api }))
vi.mock('@/stores/authStore', () => ({ useAuthStore: () => ({ user: { nId: 'runner' } }) }))
vi.mock('../graph/GraphWorkbench.vue', () => ({
  default: {
    name: 'GraphWorkbench',
    props: ['view', 'selection'],
    emits: ['selectionChange'],
    template: '<div />',
  },
}))
vi.mock('./TraceLinkModal.vue', () => ({ default: { template: '<div />' } }))

afterEach(() => {
  document.body.innerHTML = ''
  vi.unstubAllGlobals()
  vi.clearAllMocks()
})
describe('trace execution entry', () => {
  it('never renders an unresolved model ID as the dropdown label', async () => {
    const id = '21b43867-65f7-4153-a4f2-1c5a62ab7ddb'
    let resolveModels!: (value: unknown) => void
    api.models.mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          resolveModels = resolve
        }),
    )
    api.contract.mockResolvedValue({ revisionNId: 'v1', maxDepth: 12, relations: [] })
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: '/trace', component: TraceRuntimePage }],
    })
    await router.push(`/trace?model=${id}`)
    const wrapper = mount(TraceRuntimePage, {
      global: {
        plugins: [createPinia(), router, ElementPlus],
        stubs: { PermissionGate: { template: '<div><slot /></div>' } },
      },
    })
    try {
      await flushPromises()
      expect(wrapper.get('.trace-model-bar .el-select').text()).not.toContain(id)
      expect(wrapper.get('.trace-model-bar .el-select').text()).toContain('加载中')
      resolveModels({ items: [{ nId: id, name: '通用模板·生产全链路追溯' }] })
      await flushPromises()
      expect(wrapper.get('.trace-model-bar .el-select').text()).toContain('通用模板·生产全链路追溯')
      api.models.mockRejectedValueOnce(new Error('服务暂时不可用'))
      await wrapper
        .findAll('button')
        .find((button) => button.text() === '刷新模型')!
        .trigger('click')
      await flushPromises()
      expect(wrapper.get('.trace-model-bar .el-select').text()).not.toContain(id)
      expect(wrapper.text()).toContain('服务暂时不可用')
    } finally {
      wrapper.unmount()
    }
  })
  it('keeps one fixed run route and redirects old links with the chosen model', async () => {
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: '/pc', children: studioRoutes }],
    })
    const fixed = router.resolve({ name: 'studio-trace-run' })
    expect(fixed.path).toBe('/pc/studio/trace')
    expect(fixed.meta.permission).toBe('studio.trace.run')
    expect(fixed.meta.workspaceQueryKeys).toEqual([])
    const legacy = studioRoutes.find((r) => r.path === 'studio/trace-models/:nId/run')!
    expect(typeof legacy.redirect).toBe('function')
    if (typeof legacy.redirect === 'function')
      expect(
        legacy.redirect(
          router.resolve('/pc/studio/trace-models/model-a/run?revisionNId=v1'),
          router.currentRoute.value,
        ),
      ).toMatchObject({ query: { model: 'model-a', revisionNId: 'v1' } })
  })
  it('selects an authorized model using the real dropdown and ignores a late previous contract', async () => {
    vi.stubGlobal(
      'ResizeObserver',
      class {
        observe() {}
        unobserve() {}
        disconnect() {}
      },
    )
    HTMLElement.prototype.scrollTo = vi.fn()
    api.models.mockResolvedValue({
      items: [
        { nId: 'a', name: '模型甲', revisionNId: 'va', revisionNo: '1' },
        { nId: 'b', name: '模型乙', revisionNId: 'vb', revisionNo: '2' },
      ],
    })
    let rejectOld!: (error: Error) => void
    const old = new Promise((_, reject) => {
      rejectOld = reject
    })
    api.contract.mockImplementation((id: string) =>
      id === 'a'
        ? old
        : Promise.resolve({
            resource: { name: '模型乙' },
            revisionNId: 'vb',
            revisionNo: '2',
            maxDepth: 6,
            parameters: [],
            relations: [],
            start: { searchFields: [], columns: [] },
          }),
    )
    api.starts.mockResolvedValue({ items: [], totalCount: '0' })
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: '/pc/studio/trace', component: { template: '<div />' } }],
    })
    await router.push('/pc/studio/trace')
    await router.isReady()
    const wrapper = mount(TraceRuntimePage, {
      attachTo: document.body,
      global: {
        plugins: [createPinia(), router, ElementPlus],
        stubs: {
          AppPage: { template: '<main><slot /></main>' },
          PermissionGate: { template: '<div><slot /></div>' },
          AppQueryPanel: {
            template: '<button class="query" @click="$emit(\'submit\')">查询</button>',
          },
          AppDataTable: defineComponent({
            name: 'AppDataTable',
            props: ['loader'],
            setup(_props, { expose }) {
              expose({ setTopQuery: () => {}, clearSelection: () => {} })
            },
            template: '<div />',
          }),
          TraceSearchForm: true,
        },
      },
    })
    try {
      await flushPromises()
      expect(api.contract).not.toHaveBeenCalled()
      const choose = async (name: string) => {
        await wrapper.get('.trace-model-bar .el-select__wrapper').trigger('click')
        await flushPromises()
        const option = [...document.querySelectorAll('.el-select-dropdown__item')].find(
          (e) => e.textContent === name,
        )!
        expect(option).toBeTruthy()
        option.dispatchEvent(new MouseEvent('click', { bubbles: true }))
        await flushPromises()
      }
      await choose('模型甲')
      const firstSignal = api.contract.mock.calls[0]![2] as AbortSignal
      await choose('模型乙')
      expect(firstSignal.aborted).toBe(true)
      rejectOld(new Error('old model error'))
      await flushPromises()
      await wrapper
        .findAll('button')
        .find((b) => b.text() === '高级选项')!
        .trigger('click')
      expect(wrapper.text()).toContain('当前版本 2')
      expect(wrapper.text()).not.toContain('old model error')
      expect(api.starts).not.toHaveBeenCalled()
      await wrapper
        .findAll('button')
        .find((b) => b.text() === '选择追溯图谱')!
        .trigger('click')
      await flushPromises()
      const picker = wrapper.getComponent(TraceStartPicker)
      await picker.getComponent({ name: 'AppDataTable' }).props('loader')({
        pageIndex: 1,
        pageSize: 20,
      })
      expect(api.starts).toHaveBeenCalledWith(
        'b',
        expect.objectContaining({ revisionNId: 'vb' }),
        expect.any(AbortSignal),
      )
    } finally {
      wrapper.unmount()
    }
  })
  it('confirms starts without querying, loads explicitly, and preserves the graph when the picker is cancelled', async () => {
    vi.stubGlobal(
      'ResizeObserver',
      class {
        observe() {}
        unobserve() {}
        disconnect() {}
      },
    )
    HTMLElement.prototype.scrollTo = vi.fn()
    api.models.mockResolvedValue({ items: [{ nId: 'a', name: '模型甲' }] })
    api.contract.mockResolvedValue({
      presentation: { layoutDirection: 'TopToBottom' },
      revisionNId: 'va',
      revisionNo: '1',
      maxDepth: 12,
      parameters: [{ name: 'keyword', required: false, defaultValue: 'one-only' }],
      relations: [],
      start: {
        searchFields: [{ parameterName: 'keyword', control: 'Text', label: '编号' }],
        columns: [],
      },
    })
    const row = {
      startKey: 'server-key',
      objectTypeId: 'lot',
      identity: { businessKey: 'LOT-1' },
      values: {},
    }
    api.starts.mockResolvedValue({ items: [row], totalCount: '1' })
    api.query.mockResolvedValue({
      revisionNId: 'va',
      nodes: [
        {
          key: 'node-1',
          nodeToken: 'token-1',
          objectTypeId: 'lot',
          identity: { sourceSystem: 'MES', factory: 'F1', businessKey: 'LOT-1' },
          summary: [],
          grain: 'Batch',
        },
      ],
      edges: [],
      status: 'Complete',
      partialReasons: [],
      sourceObservations: [],
    })
    api.detail.mockResolvedValue({
      fields: [{ label: 'Selected lot', value: 'LOT-1' }],
      links: [],
      observedOn: null,
      sections: [{ id: 'checks', label: '测量结果', kind: 'Fields', pageSize: 20 }],
    })
    api.section.mockResolvedValue({
      sectionId: 'checks',
      kind: 'Fields',
      fields: [{ label: 'Reading', value: '12.3' }],
      items: [],
      columns: [],
      page: 1,
      pageSize: 20,
      totalCount: '1',
      observedOn: null,
    })
    const tableStub = defineComponent({
      props: ['loader'],
      emits: ['selection-change'],
      setup(props, { emit, expose }) {
        const rows = ref<unknown[]>([])
        async function load() {
          rows.value = (await props.loader({ pageIndex: 1, pageSize: 20 })).items
        }
        expose({ setTopQuery: load, clearSelection: () => emit('selection-change', []) })
        return { rows }
      },
      template:
        '<button class="select-start" @click="$emit(\'selection-change\', rows)">select</button>',
    })
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/trace', component: TraceRuntimePage },
        { path: '/other', component: { template: '<p>另一页面</p>' } },
      ],
    })
    await router.push('/trace?model=a')
    const wrapper = mount(
      defineComponent({
        template:
          '<RouterView v-slot="{ Component }"><KeepAlive include="TraceRuntimePage"><component :is="Component" /></KeepAlive></RouterView>',
      }),
      {
        attachTo: document.body,
        global: {
          plugins: [createPinia(), router, ElementPlus],
          stubs: {
            AppPage: { template: '<main><slot /></main>' },
            PermissionGate: { template: '<div><slot /></div>' },
            AppFormDrawer: {
              template: '<section role="dialog"><slot /><slot name="footer" /></section>',
            },
            AppDataTable: tableStub,
            TraceSearchForm: true,
          },
        },
      },
    )
    const click = async (label: string) => {
      await wrapper
        .findAll('button')
        .find((b) => b.text() === label)!
        .trigger('click')
      await flushPromises()
    }
    try {
      await flushPromises()
      expect(wrapper.find('[role="dialog"]').exists()).toBe(false)
      await click('选择追溯图谱')
      expect(api.starts).toHaveBeenLastCalledWith(
        'a',
        expect.objectContaining({ parameters: { keyword: '' } }),
        expect.any(AbortSignal),
      )
      await wrapper.get('.select-start').trigger('click')
      await click('确认')
      expect(wrapper.find('[role="dialog"]').exists()).toBe(false)
      expect(wrapper.text()).toContain('已选起点: 1')
      expect(api.query).not.toHaveBeenCalled()
      await click('加载图谱')
      expect(api.query).toHaveBeenCalledWith(
        'a',
        expect.objectContaining({
          revisionNId: 'va',
          startKeys: ['server-key'],
          direction: 'Both',
        }),
        expect.any(AbortSignal),
      )
      expect(wrapper.text()).toContain('完整结果')
      const graph = wrapper.getComponent(GraphWorkbench)
      expect(graph.props('view').nodes[0]!.ports[0]!.side).toBe('top')
      graph.vm.$emit('selectionChange', [{ kind: 'node', id: 'node-1' }])
      await flushPromises()
      expect(wrapper.text()).toContain('Selected lot')
      expect(api.section).not.toHaveBeenCalled()
      await wrapper
        .findAll('[role="tab"]')
        .find((tab) => tab.text() === '测量结果')!
        .trigger('click')
      await flushPromises()
      expect(wrapper.text()).toContain('12.3')
      expect(api.section).toHaveBeenCalledTimes(1)
      await router.push('/other?model=unrelated&revisionNId=unrelated')
      await flushPromises()
      expect(wrapper.text()).toContain('另一页面')
      await router.push('/trace?model=a')
      await flushPromises()
      expect(api.models).toHaveBeenCalledTimes(1)
      expect(api.contract).toHaveBeenCalledTimes(1)
      expect(api.query).toHaveBeenCalledTimes(1)
      expect(api.detail).toHaveBeenCalledTimes(1)
      expect(api.section).toHaveBeenCalledTimes(1)
      expect(wrapper.text()).toContain('已选起点: 1')
      expect(wrapper.text()).toContain('12.3')
      expect(wrapper.get('.trace-model-bar .el-select').text()).toContain('模型甲')
      await wrapper.get('.trace-layout-direction .el-select__wrapper').trigger('click')
      await flushPromises()
      const horizontal = [...document.querySelectorAll('.el-select-dropdown__item')].find(
        (option) => option.textContent === '横向',
      )!
      horizontal.dispatchEvent(new MouseEvent('click', { bubbles: true }))
      await flushPromises()
      expect(graph.props('view').nodes[0]!.ports[0]!.side).toBe('left')
      expect(graph.props('selection')).toEqual([{ kind: 'node', id: 'node-1' }])
      expect(wrapper.text()).toContain('12.3')
      expect(api.query).toHaveBeenCalledTimes(1)
      expect(api.detail).toHaveBeenCalledTimes(1)
      expect(api.section).toHaveBeenCalledTimes(1)
      await click('刷新模型')
      expect(api.contract).toHaveBeenCalledTimes(1)
      expect(wrapper.text()).toContain('完整结果')
      expect(wrapper.text()).toContain('已选起点: 1')
      await click('选择追溯图谱')
      await wrapper.get('.select-start').trigger('click')
      await click('取消')
      expect(wrapper.text()).toContain('完整结果')
      expect(wrapper.text()).toContain('已选起点: 1')
      expect(api.query).toHaveBeenCalledTimes(1)
    } finally {
      wrapper.unmount()
    }
  })
})
