import { flushPromises, mount } from '@vue/test-utils'
import ElementPlus from 'element-plus'
import { createPinia, setActivePinia } from 'pinia'
import { createMemoryHistory, createRouter, RouterView } from 'vue-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { Component } from 'vue'
import DashboardRuntimeView from '@/components/studio/dashboard/DashboardRuntimeView.vue'
import DashboardRuntimePage from '@/pages/pc/studio/dashboard/DashboardRuntimePage.vue'
import PlaylistRuntimePage from '@/pages/pc/studio/dashboard/PlaylistRuntimePage.vue'
import {
  createStudioApi,
  registerStudioApi,
  type DashboardRuntime,
  type Json,
  type StudioResource,
} from '@/api/studio'
import type { HttpClient } from '@/api/httpClient'
import { useWorkspaceTabsStore } from '@/stores/workspaceTabsStore'
import { buildTabId, toPersistedRoute } from '@/workspace'
import { ROUTE_NAMES } from '@/router/routeNames'

type Deferred<T> = {
  promise: Promise<T>
  resolve: (value: T | PromiseLike<T>) => void
  reject: (reason?: unknown) => void
}

function deferred<T>(): Deferred<T> {
  let resolve!: Deferred<T>['resolve']
  let reject!: Deferred<T>['reject']
  const promise = new Promise<T>((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

function httpClient(get: (path: string) => unknown): HttpClient {
  return {
    get: vi.fn((path: string) => get(path)),
    post: vi.fn().mockResolvedValue({}),
    put: vi.fn(),
    delete: vi.fn(),
    getWithMeta: vi.fn(),
  } as unknown as HttpClient
}

function resource(kind: string, name: string): StudioResource {
  return {
    nId: name.toLowerCase().replaceAll(' ', '-'),
    kind,
    name,
    isEnabled: true,
    publishedRevisionNId: 'published-revision',
    publishedRevisionNo: '1',
    hasDraftChanges: false,
    lastUpdatedOn: '',
    optimisticVersion: '1',
    concurrencyVersion: 'concurrency',
  }
}

function component(
  id: string,
  type: string,
  props: Json = {},
  bindings: Json[] = [],
  events: string[] = [],
): Json {
  return {
    id,
    type,
    registryVersion: '1',
    name: id,
    x: 0,
    y: 0,
    width: 320,
    height: 140,
    rotation: 0,
    zIndex: 0,
    locked: false,
    visible: true,
    props,
    bindings,
    events,
    children: [],
  }
}

function dashboardRuntime(
  name: string,
  options: {
    lineCode?: string
    value?: number
    quality?: Json
    observedOn?: string
    components?: Json[]
    parameters?: Json[]
    navigation?: Json[]
    instanceDefinitions?: Json[]
  } = {},
): DashboardRuntime {
  const lineCode = options.lineCode ?? 'L01'
  const components = options.components ?? [
    component('line', 'Text', { text: name }, [
      {
        property: 'text',
        source: { from: 'PageParam', path: ['lineCode'], nullPolicy: 'Null' },
      },
    ]),
  ]
  return {
    resource: resource('Dashboard', name),
    revisionNId: 'dashboard-revision',
    document: {
      schemaVersion: 1,
      canvas: { width: 1920, height: 1080, fit: 'Contain', background: '#081525' },
      parameters: options.parameters ?? [
        { name: 'lineCode', label: 'Line', type: 'String', required: false, source: 'Caller' },
      ],
      instances: options.instanceDefinitions ?? [{ instanceId: 'lineData', staleAfterSeconds: 15 }],
      components,
      navigation: options.navigation ?? [],
    },
    instances: {
      lineData: {
        data: {
          lineCode,
          value: options.value ?? 0,
          rows: [{ name: lineCode, value: options.value ?? 0 }],
        },
        observedOn: options.observedOn,
        quality: options.quality,
      },
    },
    diagnostics: [],
    transport: 'Connected',
  }
}

function dashboardWithDataQuality(
  name: string,
  quality: Json,
  observedOn?: string,
): DashboardRuntime {
  return dashboardRuntime(name, {
    value: 0,
    quality,
    ...(observedOn === undefined ? {} : { observedOn }),
    components: [
      component('value', 'Number', { unit: '件' }, [
        {
          property: 'value',
          source: { from: 'Instance', instanceId: 'lineData', path: ['value'] },
        },
      ]),
      component('quality', 'DataQuality', { instanceId: 'lineData' }),
    ],
  })
}

function playlistRuntime(
  items: Json[],
  options: { onFailure?: 'Skip' | 'Pause'; loop?: boolean; parameters?: Json[] } = {},
): Json {
  return {
    resource: resource('DashboardPlaylist', 'Line playlist'),
    revisionNId: 'playlist-revision',
    document: {
      schemaVersion: 1,
      parameters: options.parameters ?? [
        { name: 'lineCode', label: 'Line', type: 'String', defaultValue: 'L01' },
      ],
    },
    items,
    loop: options.loop ?? false,
    onFailure: options.onFailure ?? 'Pause',
    preloadCount: 1,
  }
}

function playlistItem(label: string, lineCode: string, dashboardNId = 'line-template'): Json {
  return {
    dashboardNId,
    revisionNId: 'dashboard-revision',
    label,
    staySeconds: 3,
    parameterMap: [
      {
        target: 'lineCode',
        source: { from: 'Constant', value: lineCode },
      },
    ],
  }
}

async function routerFor(path: string, component: Component) {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/pc/studio/dashboards/:nId/run', name: ROUTE_NAMES.studioDashboardRun, component },
      { path: '/pc/studio/playlists/:nId/run', name: ROUTE_NAMES.studioPlaylistRun, component },
    ],
  })
  await router.push(path)
  await router.isReady()
  return router
}

function trackWorkspace(router: Awaited<ReturnType<typeof routerFor>>) {
  const tabs = useWorkspaceTabsStore()
  const remember = (
    location: Pick<ReturnType<typeof router.resolve>, 'name' | 'path' | 'params' | 'query'>,
  ) => {
    const name = String(location.name ?? location.path)
    return tabs.requestOpen({
      id: buildTabId(name, location.params, location.query),
      title: name,
      kind: 'business',
      route: toPersistedRoute({ name, params: location.params, query: location.query }),
    })
  }
  remember(router.currentRoute.value)
  router.beforeEach((to) => (remember(to).kind === 'limit-reached' ? false : undefined))
  return { tabs, remember }
}

beforeEach(() => {
  setActivePinia(createPinia())
  vi.stubGlobal(
    'ResizeObserver',
    class {
      observe() {}
      disconnect() {}
    },
  )
})

afterEach(() => {
  vi.useRealTimers()
  vi.unstubAllGlobals()
})

describe('Dashboard runtime resilience', () => {
  it('keeps the displayed playlist item parameters paired with its canvas during a pending switch', async () => {
    const next = deferred<DashboardRuntime>()
    const playlist = {
      ...playlistRuntime([playlistItem('first', 'L01'), playlistItem('second', 'L02')]),
      preloadCount: 0,
    }
    const http = httpClient((path) =>
      path.includes('/playlists/')
        ? Promise.resolve(playlist)
        : path.includes('%22L02%22')
          ? next.promise
          : Promise.resolve(dashboardRuntime('first')),
    )
    registerStudioApi(createStudioApi(http))
    const router = await routerFor('/pc/studio/playlists/parameters/run', PlaylistRuntimePage)
    const wrapper = mount(PlaylistRuntimePage, { global: { plugins: [router, ElementPlus] } })
    await flushPromises()
    await wrapper
      .findAll('button')
      .find((button) => button.text() === '下一页')!
      .trigger('click')
    await flushPromises()
    expect(wrapper.getComponent(DashboardRuntimeView).props('parameters')).toEqual({
      lineCode: 'L01',
    })
    expect(wrapper.getComponent(DashboardRuntimeView).props('runtime').resource.name).toBe('first')
    next.resolve(dashboardRuntime('second'))
    await flushPromises()
    expect(wrapper.getComponent(DashboardRuntimeView).props('parameters')).toEqual({
      lineCode: 'L02',
    })
    wrapper.unmount()
  })

  it.each([
    ['Current', true],
    ['Current', false],
    ['Modal', true],
    ['Modal', false],
  ] as const)(
    'reuses playlist navigation results with one guarded resolve (%s, included=%s)',
    async (mode, included) => {
      const overview = dashboardRuntime('playlist overview', {
        navigation: [
          {
            id: 'detail',
            componentId: 'batches',
            event: 'RowClick',
            target: { kind: 'Dashboard' },
            openMode: mode,
          },
        ],
      })
      const playlist = playlistRuntime([playlistItem('line', 'L01')])
      const detail = dashboardRuntime('resolved playlist detail')
      const http = httpClient((path) =>
        Promise.resolve(
          path.includes('/playlists/')
            ? playlist
            : path.includes('/dashboards/detail/')
              ? detail
              : overview,
        ),
      )
      const pending = deferred<Json>()
      vi.mocked(http.post).mockReturnValue(pending.promise)
      registerStudioApi(createStudioApi(http))
      const router = createRouter({
        history: createMemoryHistory(),
        routes: [
          {
            path: '/pc/studio/playlists/:nId/run',
            name: ROUTE_NAMES.studioPlaylistRun,
            component: PlaylistRuntimePage,
          },
          {
            path: '/pc/studio/dashboards/:nId/run',
            name: ROUTE_NAMES.studioDashboardRun,
            component: DashboardRuntimePage,
          },
        ],
      })
      const sourcePath = '/pc/studio/playlists/playlist-one/run'
      await router.push(sourcePath)
      await router.isReady()
      const { tabs } = trackWorkspace(router)
      const wrapper = mount(RouterView, { global: { plugins: [router, ElementPlus] } })
      await flushPromises()
      const view = wrapper.getComponent(DashboardRuntimeView)
      view.vm.$emit(
        'navigate',
        { id: 'batches', props: { rowKeyPath: ['batch'] } },
        { batch: 'B-01' },
      )
      view.vm.$emit(
        'navigate',
        { id: 'batches', props: { rowKeyPath: ['batch'] } },
        { batch: 'B-01' },
      )
      await flushPromises()
      expect(http.post).toHaveBeenCalledTimes(1)
      expect(vi.mocked(http.post).mock.calls[0]![1]).toMatchObject({
        includeRuntime: true,
        rowKey: { key: 'B-01' },
      })
      pending.resolve({
        targetResourceNId: 'detail',
        targetRevisionNId: 'detail-revision',
        parameters: { batchNo: 'B-01' },
        ...(included ? { targetRuntime: detail } : {}),
      })
      await flushPromises()
      expect(http.get).toHaveBeenCalledTimes(included ? 2 : 3)
      if (mode === 'Current') {
        expect(router.currentRoute.value.params.nId).toBe('detail')
        expect(router.currentRoute.value.query.batchNo).toBe('B-01')
        expect(tabs.businessTabs).toHaveLength(2)
        await wrapper
          .findAll('button')
          .find((button) => button.text() === '返回')!
          .trigger('click')
        await flushPromises()
        expect(router.currentRoute.value.fullPath).toBe(sourcePath)
        expect(tabs.businessTabs).toHaveLength(2)
      } else {
        expect(router.currentRoute.value.fullPath).toBe(sourcePath)
        expect(wrapper.getComponent({ name: 'ElDialog' }).props('modelValue')).toBe(true)
        expect(tabs.businessTabs).toHaveLength(1)
      }
      wrapper.unmount()
    },
  )

  it('renders a real numeric zero and does not turn it into Unknown', async () => {
    const wrapper = mount(DashboardRuntimeView, {
      props: {
        runtime: dashboardWithDataQuality('zero', {
          status: 'Fresh',
          basis: 'SourceTime',
          sourceObservedOn: new Date().toISOString(),
        }),
      },
      global: { plugins: [ElementPlus] },
    })
    await flushPromises()

    expect(wrapper.get('.dashboard-component-Number').text()).toContain('0')
    expect(wrapper.text()).not.toContain('Unknown')
    expect(wrapper.text()).toContain('来源数据正常')
    wrapper.unmount()
  })

  it('renders bound Tank readings and DataQuality timestamps', async () => {
    const runtime = dashboardRuntime('tank', {
      quality: {
        status: 'Fresh',
        basis: 'SourceTime',
        sourceObservedOn: '2026-10-22T00:00:00Z',
      },
      observedOn: '2026-10-22T00:01:00Z',
      components: [
        component('tank', 'Tank', { unit: '%' }, [
          {
            property: 'level',
            source: { from: 'Instance', instanceId: 'lineData', path: ['level'] },
          },
          {
            property: 'temperature',
            source: { from: 'Instance', instanceId: 'lineData', path: ['temperature'] },
          },
          {
            property: 'state',
            source: { from: 'Instance', instanceId: 'lineData', path: ['state'] },
          },
        ]),
        component('quality', 'DataQuality', {
          instanceId: 'lineData',
          showLastSuccess: true,
          showSourceTime: true,
        }),
      ],
    })
    ;(runtime.instances.lineData as Json).data = {
      level: 0,
      temperature: 72,
      state: 'Running',
    }
    const wrapper = mount(DashboardRuntimeView, {
      props: { runtime },
      global: { plugins: [ElementPlus] },
    })
    await flushPromises()

    const text = wrapper.text()
    expect(text).toContain('液位 0%')
    expect(text).toContain('温度 72')
    expect(text).toContain('状态 Running')
    expect(text).toContain('最后成功取数')
    expect(text).toContain('来源采样时间')
    wrapper.unmount()
  })

  it('keeps SourceTime Stale after a successful refresh and reports missing source time as Unknown', async () => {
    const stale = dashboardWithDataQuality('stale', {
      status: 'Stale',
      basis: 'SourceTime',
      sourceObservedOn: '2020-01-01T00:00:00Z',
    })
    const successfulButOld = dashboardWithDataQuality('successful but old', {
      status: 'Fresh',
      basis: 'SourceTime',
      sourceObservedOn: '2020-01-01T00:00:00Z',
    })
    const missingSourceTime = dashboardWithDataQuality('missing source time', {
      status: 'Unknown',
      basis: 'SourceTime',
      reasonCode: 'SOURCE_TIME_NOT_PROVIDED',
    })
    const responses = [stale, successfulButOld, missingSourceTime]
    const http = httpClient(() => Promise.resolve(responses.shift()!))
    registerStudioApi(createStudioApi(http))
    const router = await routerFor('/pc/studio/dashboards/stale/run', DashboardRuntimePage)
    const wrapper = mount(DashboardRuntimePage, {
      global: { plugins: [router, ElementPlus] },
    })
    await flushPromises()

    expect(wrapper.text()).toContain('来源数据 Stale')
    await wrapper
      .findAll('button')
      .find((button) => button.text() === '刷新')!
      .trigger('click')
    await flushPromises()
    expect(wrapper.text()).toContain('来源数据 Stale')

    await wrapper
      .findAll('button')
      .find((button) => button.text() === '刷新')!
      .trigger('click')
    await flushPromises()
    expect(wrapper.text()).toContain('来源时间未知')
    wrapper.unmount()
  })

  it('loads parameter metadata when the first runtime call rejects missing required input', async () => {
    const http = httpClient((path) => {
      if (path.includes('/runtime')) return Promise.reject(new Error('PARAM_REQUIRED'))
      if (path.includes('/definition'))
        return Promise.resolve({
          ...resource('Dashboard', 'Missing'),
          document: {
            parameters: [
              { name: 'lineCode', label: 'Line', type: 'String', required: true, source: 'Caller' },
            ],
          },
          diagnostics: [],
        })
      return Promise.resolve({})
    })
    registerStudioApi(createStudioApi(http))
    const router = await routerFor('/pc/studio/dashboards/missing/run', DashboardRuntimePage)
    const wrapper = mount(DashboardRuntimePage, {
      global: { plugins: [router, ElementPlus] },
    })
    await flushPromises()

    expect(wrapper.find('.app-query-panel input').exists()).toBe(true)
    expect(wrapper.text()).toContain('Line')
    expect(wrapper.text()).toContain('PARAM_REQUIRED')
    wrapper.unmount()
  })

  it('ignores a late successful response from the previous dashboard parameter context', async () => {
    const oldResponse = deferred<DashboardRuntime>()
    const currentResponse = deferred<DashboardRuntime>()
    let dashboardCalls = 0
    const http = httpClient((path) => {
      if (!path.includes('/dashboards/')) return Promise.resolve({})
      dashboardCalls += 1
      if (dashboardCalls === 1)
        return Promise.resolve(dashboardRuntime('initial', { lineCode: 'L01' }))
      return dashboardCalls === 2 ? oldResponse.promise : currentResponse.promise
    })
    registerStudioApi(createStudioApi(http))
    const router = await routerFor('/pc/studio/dashboards/line/run', DashboardRuntimePage)
    const wrapper = mount(DashboardRuntimePage, {
      global: { plugins: [router, ElementPlus] },
    })
    await flushPromises()

    const input = wrapper.get('.app-query-panel input')
    await input.setValue('L02')
    await wrapper.get('[data-testid="query-panel-submit"]').trigger('click')
    await input.setValue('L03')
    await wrapper.get('[data-testid="query-panel-submit"]').trigger('click')
    expect(dashboardCalls).toBe(3)
    expect(vi.mocked(http.get).mock.calls[1]?.[0]).toContain('%22L02%22')
    expect(vi.mocked(http.get).mock.calls[2]?.[0]).toContain('%22L03%22')

    currentResponse.resolve(dashboardRuntime('current L03', { lineCode: 'L03' }))
    await flushPromises()
    oldResponse.resolve(dashboardRuntime('old L02', { lineCode: 'L02' }))
    await flushPromises()
    expect(wrapper.get('h1').text()).toBe('current L03')
    wrapper.unmount()
  })

  it('ignores a late failed response from the previous dashboard parameter context', async () => {
    const oldResponse = deferred<DashboardRuntime>()
    const currentResponse = deferred<DashboardRuntime>()
    let dashboardCalls = 0
    const http = httpClient((path) => {
      if (!path.includes('/dashboards/')) return Promise.resolve({})
      dashboardCalls += 1
      if (dashboardCalls === 1)
        return Promise.resolve(dashboardRuntime('initial', { lineCode: 'L01' }))
      return dashboardCalls === 2 ? oldResponse.promise : currentResponse.promise
    })
    registerStudioApi(createStudioApi(http))
    const router = await routerFor('/pc/studio/dashboards/line/run', DashboardRuntimePage)
    const wrapper = mount(DashboardRuntimePage, {
      global: { plugins: [router, ElementPlus] },
    })
    await flushPromises()

    const input = wrapper.get('.app-query-panel input')
    await input.setValue('L02')
    await wrapper.get('[data-testid="query-panel-submit"]').trigger('click')
    await input.setValue('L03')
    await wrapper.get('[data-testid="query-panel-submit"]').trigger('click')
    expect(dashboardCalls).toBe(3)
    expect(vi.mocked(http.get).mock.calls[1]?.[0]).toContain('%22L02%22')
    expect(vi.mocked(http.get).mock.calls[2]?.[0]).toContain('%22L03%22')

    currentResponse.resolve(dashboardRuntime('current L03', { lineCode: 'L03' }))
    await flushPromises()
    oldResponse.reject(new Error('old L02 failed'))
    await flushPromises()
    expect(wrapper.get('h1').text()).toBe('current L03')
    expect(wrapper.find('.el-alert--error').exists()).toBe(false)
    wrapper.unmount()
  })

  it('refreshes once per action when number, chart and table components share one instance', async () => {
    const sharedComponents = [
      component('number', 'Number', {}, [
        {
          property: 'value',
          source: { from: 'Instance', instanceId: 'lineData', path: ['value'] },
        },
      ]),
      component('chart', 'Chart', {
        kind: 'Bar',
        xField: 'name',
        series: [{ valueField: 'value' }],
      }),
      component('table', 'Table', {
        presentation: 'Table',
        instanceId: 'lineData',
        collectionPath: ['rows'],
        columns: [{ field: 'name', label: '产线' }],
      }),
    ]
    const response = dashboardRuntime('shared instance', {
      value: 0,
      components: sharedComponents,
    })
    const http = httpClient(() => Promise.resolve(response))
    registerStudioApi(createStudioApi(http))
    const router = await routerFor('/pc/studio/dashboards/shared/run', DashboardRuntimePage)
    const wrapper = mount(DashboardRuntimePage, {
      global: { plugins: [router, ElementPlus] },
    })
    await flushPromises()
    const refresh = wrapper.findAll('button').find((button) => button.text() === '刷新')!
    for (let index = 0; index < 10; index += 1) {
      await refresh.trigger('click')
      await flushPromises()
    }

    expect(wrapper.findAll('.dashboard-runtime-component')).toHaveLength(3)
    expect(wrapper.get('.dashboard-component-Number').text()).toContain('0')
    expect(vi.mocked(http.get)).toHaveBeenCalledTimes(11)
    wrapper.unmount()
  })

  it('polls configured instances and keeps the last value when transport fails', async () => {
    vi.useFakeTimers()
    const first = dashboardRuntime('last successful value', {
      instanceDefinitions: [{ instanceId: 'lineData', refreshSeconds: 1, staleAfterSeconds: 15 }],
    })
    let calls = 0
    const http = httpClient(() => {
      calls += 1
      return calls === 1 ? Promise.resolve(first) : Promise.reject(new Error('source offline'))
    })
    registerStudioApi(createStudioApi(http))
    const router = await routerFor('/pc/studio/dashboards/poll/run', DashboardRuntimePage)
    const wrapper = mount(DashboardRuntimePage, {
      global: { plugins: [router, ElementPlus] },
    })
    await flushPromises()

    expect(wrapper.get('h1').text()).toBe('last successful value')
    vi.advanceTimersByTime(1000)
    await flushPromises()
    expect(calls).toBe(2)
    expect(wrapper.get('h1').text()).toBe('last successful value')
    expect(wrapper.text()).toContain('source offline')
    expect(wrapper.text()).toContain('传输断开')
    wrapper.unmount()
  })

  it('preserves dashboard source context when a runtime navigation resolves to another dashboard', async () => {
    const resolveResponse = deferred<Json>()
    const runtime = dashboardRuntime('source dashboard', {
      navigation: [
        {
          id: 'open-target',
          componentId: 'open',
          event: 'Click',
          target: { kind: 'Dashboard' },
        },
      ],
      components: [component('open', 'Button', { text: '打开目标' }, [], ['open-target'])],
    })
    const http = httpClient((path) =>
      path.includes('/dashboards/') ? Promise.resolve(runtime) : Promise.resolve({}),
    )
    vi.mocked(http.post).mockImplementation(() => resolveResponse.promise as never)
    registerStudioApi(createStudioApi(http))
    const router = await routerFor(
      '/pc/studio/dashboards/source/run?lineCode=L01',
      DashboardRuntimePage,
    )
    const wrapper = mount(DashboardRuntimePage, {
      global: { plugins: [router, ElementPlus] },
    })
    await flushPromises()

    await wrapper.get('.dashboard-button').trigger('click')
    expect(http.post).toHaveBeenCalledWith(
      '/api/v1/studio/dashboards/source/navigation/resolve',
      expect.objectContaining({ ruleId: 'open-target' }),
      expect.anything(),
    )
    resolveResponse.resolve({
      targetResourceNId: 'target',
      targetRevisionNId: 'target-revision',
      parameters: { lineCode: 'L02' },
    })
    await flushPromises()
    expect(router.currentRoute.value.path).toBe('/pc/studio/dashboards/target/run')
    expect(router.currentRoute.value.query).toMatchObject({
      revisionNId: 'target-revision',
      lineCode: 'L02',
      sourceNId: 'source',
      sourceRevisionNId: 'dashboard-revision',
    })
    wrapper.unmount()
  })
  it.each([false, true])(
    'opens one detail tab and reuses an included runtime when available (%s)',
    async (includeRuntime) => {
      const runtime = dashboardRuntime('batch overview', {
        components: [
          component(
            'batches',
            'Table',
            {
              instanceId: 'lineData',
              collectionPath: ['rows'],
              rowKeyPath: ['name'],
              columns: [{ field: 'name', label: '批次编号' }],
            },
            [],
            ['detail'],
          ),
        ],
        navigation: [
          {
            id: 'detail',
            componentId: 'batches',
            event: 'RowClick',
            target: { kind: 'Dashboard' },
            openMode: 'Current',
          },
        ],
      })
      const http = httpClient(() => Promise.resolve(runtime))
      vi.mocked(http.post).mockResolvedValue({
        targetResourceNId: 'detail',
        targetRevisionNId: 'detail-revision',
        parameters: { batchNo: 'PR-DEMO-104' },
        ...(includeRuntime ? { targetRuntime: dashboardRuntime('resolved detail') } : {}),
      })
      registerStudioApi(createStudioApi(http))
      const sourcePath = '/pc/studio/dashboards/overview/run?lineCode=L01'
      const router = await routerFor(sourcePath, DashboardRuntimePage)
      const { tabs } = trackWorkspace(router)
      const sourceTab = JSON.parse(JSON.stringify(tabs.businessTabs[0]))
      const wrapper = mount(DashboardRuntimePage, { global: { plugins: [router, ElementPlus] } })
      await flushPromises()
      await wrapper.get('tbody tr').trigger('click')
      await flushPromises()
      expect(http.post).toHaveBeenCalledTimes(1)
      expect(vi.mocked(http.post).mock.calls[0]![1]).toMatchObject({ includeRuntime: true })
      expect(router.currentRoute.value.params.nId).toBe('detail')
      expect(router.currentRoute.value.query.batchNo).toBe('PR-DEMO-104')
      expect(tabs.businessTabs).toHaveLength(2)
      expect(tabs.businessTabs[0]).toEqual(sourceTab)
      expect(tabs.businessTabs.filter((tab) => tab.route.params.nId === 'overview')).toHaveLength(1)
      expect(tabs.businessTabs.filter((tab) => tab.route.params.nId === 'detail')).toHaveLength(1)
      expect(router.currentRoute.value.query.sourceTabId).toBe(sourceTab.id)
      expect(http.get).toHaveBeenCalledTimes(includeRuntime ? 1 : 2)
      if (includeRuntime) {
        expect(wrapper.text()).toContain('resolved detail')
        await wrapper
          .findAll('button')
          .find((button) => button.text() === '刷新')!
          .trigger('click')
        await flushPromises()
        expect(http.get).toHaveBeenCalledTimes(2)
      }
      const targetRequest = vi.mocked(http.get).mock.calls.at(-1)![0]
      expect(
        JSON.parse(new URL(targetRequest, 'https://localhost').searchParams.get('parameters')!),
      ).not.toHaveProperty('sourceTabId')
      await wrapper
        .findAll('button')
        .find((button) => button.text() === '返回')!
        .trigger('click')
      await flushPromises()
      expect(router.currentRoute.value.fullPath).toBe(sourcePath)
      expect(tabs.activeTabId).toBe(sourceTab.id)
      expect(tabs.businessTabs).toHaveLength(2)
      expect(tabs.businessTabs[0]).toEqual(sourceTab)
      wrapper.unmount()
    },
  )
  it.each([
    { kind: 'dashboards', legacy: false, closed: false },
    { kind: 'playlists', legacy: false, closed: false },
    { kind: 'dashboards', legacy: true, closed: false },
    { kind: 'dashboards', legacy: false, closed: true },
  ])(
    'returns to the source $kind tab (legacy=$legacy, closed=$closed)',
    async ({ kind, legacy, closed }) => {
      const http = httpClient(() => Promise.resolve(dashboardRuntime('detail')))
      registerStudioApi(createStudioApi(http))
      const router = await routerFor('/pc/studio/dashboards/detail/run', DashboardRuntimePage)
      const { tabs, remember } = trackWorkspace(router)
      const source = router.resolve(`/pc/studio/${kind}/overview/run?lineCode=L01`)
      const sourceName = String(source.name)
      const sourceId = buildTabId(sourceName, source.params, source.query)
      // New links identify the exact original when the same resource has multiple parameter tabs.
      if (!legacy && !closed)
        remember(router.resolve(`/pc/studio/${kind}/overview/run?lineCode=L02`))
      if (!closed) remember(source)
      const sourceKey = kind === 'dashboards' ? 'sourceNId' : 'sourcePlaylistNId'
      const parametersKey = kind === 'dashboards' ? 'sourceParameters' : 'sourcePlaylistParameters'
      const revisionKey = kind === 'dashboards' ? 'sourceRevisionNId' : 'sourcePlaylistRevisionNId'
      await router.replace({
        query: {
          [sourceKey]: 'overview',
          [parametersKey]: '{"lineCode":"L01"}',
          [revisionKey]: 'source-revision',
          ...(!legacy ? { sourceTabId: sourceId } : {}),
        },
      })
      const before = tabs.businessTabs.length
      const wrapper = mount(DashboardRuntimePage, { global: { plugins: [router, ElementPlus] } })
      await flushPromises()
      await wrapper
        .findAll('button')
        .find((button) => button.text() === '返回')!
        .trigger('click')
      await flushPromises()
      expect(router.currentRoute.value.name).toBe(sourceName)
      expect(router.currentRoute.value.params.nId).toBe('overview')
      expect(router.currentRoute.value.query.lineCode).toBe('L01')
      expect(tabs.businessTabs).toHaveLength(before + (closed ? 1 : 0))
      if (!closed) {
        expect(router.currentRoute.value.fullPath).toBe(source.fullPath)
        expect(tabs.activeTabId).toBe(sourceId)
      } else expect(router.currentRoute.value.query.revisionNId).toBe('source-revision')
      wrapper.unmount()
    },
  )
})

describe('Playlist runtime resilience', () => {
  beforeEach(() => {
    vi.useFakeTimers()
  })

  it('pauses after one failed loop instead of recursively retrying forever', async () => {
    const playlist = playlistRuntime([playlistItem('one', 'L01'), playlistItem('two', 'L02')], {
      loop: true,
      onFailure: 'Skip',
    })
    let calls = 0
    const http = httpClient((path) => {
      if (path.includes('/playlists/')) return Promise.resolve(playlist)
      calls++
      if (calls > 4) return new Promise(() => {})
      return Promise.reject(new Error('unavailable'))
    })
    registerStudioApi(createStudioApi(http))
    const router = await routerFor('/pc/studio/playlists/all-failed/run', PlaylistRuntimePage)
    const wrapper = mount(PlaylistRuntimePage, { global: { plugins: [router, ElementPlus] } })
    await flushPromises()
    expect(calls).toBe(2)
    expect(wrapper.findAll('button').some((button) => button.text() === '继续')).toBe(true)
    vi.advanceTimersByTime(30000)
    await flushPromises()
    expect(calls).toBe(2)
    wrapper.unmount()
  })

  it('honors preloadCount zero and fetches the next item only when advancing', async () => {
    const playlist = {
      ...playlistRuntime([playlistItem('one', 'L01'), playlistItem('two', 'L02')]),
      preloadCount: 0,
    }
    let calls = 0
    const http = httpClient((path) => {
      if (path.includes('/playlists/')) return Promise.resolve(playlist)
      calls++
      return Promise.resolve(dashboardRuntime('item'))
    })
    registerStudioApi(createStudioApi(http))
    const router = await routerFor('/pc/studio/playlists/no-preload/run', PlaylistRuntimePage)
    const wrapper = mount(PlaylistRuntimePage, { global: { plugins: [router, ElementPlus] } })
    await flushPromises()
    expect(calls).toBe(1)
    vi.advanceTimersByTime(3000)
    await flushPromises()
    expect(calls).toBe(2)
    wrapper.unmount()
    vi.advanceTimersByTime(10000)
    await flushPromises()
    expect(calls).toBe(2)
  })

  it('advances the same dashboard template with item-specific parameters after its stay time', async () => {
    const playlist = playlistRuntime([
      playlistItem('L01 view', 'L01'),
      playlistItem('L02 view', 'L02'),
    ])
    const calls: string[] = []
    const http = httpClient((path) => {
      if (path.includes('/playlists/')) return Promise.resolve(playlist)
      calls.push(path)
      return Promise.resolve(
        dashboardRuntime(path.includes('%22L02%22') ? 'L02 runtime' : 'L01 runtime'),
      )
    })
    registerStudioApi(createStudioApi(http))
    const router = await routerFor('/pc/studio/playlists/lines/run', PlaylistRuntimePage)
    const wrapper = mount(PlaylistRuntimePage, {
      global: { plugins: [router, ElementPlus] },
    })
    await flushPromises()

    expect(wrapper.text()).toContain('1 / 2')
    expect(wrapper.text()).toContain('L01 view')
    expect(calls).toHaveLength(2)
    expect(calls[0]).toContain('%22L01%22')
    expect(calls[1]).toContain('%22L02%22')

    vi.advanceTimersByTime(3000)
    await flushPromises()
    expect(wrapper.text()).toContain('2 / 2')
    expect(wrapper.text()).toContain('L02 view')
    expect(calls).toHaveLength(2)
    wrapper.unmount()
  })

  it('skips a failed item when the playlist failure policy is Skip', async () => {
    const failed = deferred<DashboardRuntime>()
    const next = deferred<DashboardRuntime>()
    const playlist = playlistRuntime(
      [playlistItem('L01 view', 'L01'), playlistItem('L02 view', 'L02')],
      { onFailure: 'Skip' },
    )
    let dashboardCalls = 0
    const http = httpClient((path) => {
      if (path.includes('/playlists/')) return Promise.resolve(playlist)
      dashboardCalls += 1
      return dashboardCalls === 1 ? failed.promise : next.promise
    })
    registerStudioApi(createStudioApi(http))
    const router = await routerFor('/pc/studio/playlists/skip/run', PlaylistRuntimePage)
    const wrapper = mount(PlaylistRuntimePage, {
      global: { plugins: [router, ElementPlus] },
    })
    await flushPromises()
    failed.reject(new Error('L01 unavailable'))
    await flushPromises()
    expect(dashboardCalls).toBe(2)
    next.resolve(dashboardRuntime('L02 runtime'))
    await flushPromises()

    expect(wrapper.text()).toContain('2 / 2')
    expect(wrapper.text()).toContain('L02 view')
    expect(wrapper.text()).not.toContain('L01 unavailable')
    expect(wrapper.findAll('button').find((button) => button.text() === '暂停')).toBeTruthy()
    wrapper.unmount()
  })

  it('pauses on a failed item and continues to the next item only after manual resume', async () => {
    const failed = deferred<DashboardRuntime>()
    const next = deferred<DashboardRuntime>()
    const playlist = playlistRuntime(
      [playlistItem('L01 view', 'L01'), playlistItem('L02 view', 'L02')],
      { onFailure: 'Pause' },
    )
    let dashboardCalls = 0
    const http = httpClient((path) => {
      if (path.includes('/playlists/')) return Promise.resolve(playlist)
      dashboardCalls += 1
      return dashboardCalls === 1 ? failed.promise : next.promise
    })
    registerStudioApi(createStudioApi(http))
    const router = await routerFor('/pc/studio/playlists/pause/run', PlaylistRuntimePage)
    const wrapper = mount(PlaylistRuntimePage, {
      global: { plugins: [router, ElementPlus] },
    })
    await flushPromises()
    failed.reject(new Error('L01 unavailable'))
    await flushPromises()
    expect(wrapper.text()).toContain('L01 unavailable')
    expect(wrapper.findAll('button').find((button) => button.text() === '继续')).toBeTruthy()
    expect(dashboardCalls).toBe(1)

    await wrapper
      .findAll('button')
      .find((button) => button.text() === '继续')!
      .trigger('click')
    vi.advanceTimersByTime(3000)
    await flushPromises()
    expect(dashboardCalls).toBe(2)
    next.resolve(dashboardRuntime('L02 runtime'))
    await flushPromises()
    expect(wrapper.text()).toContain('2 / 2')
    expect(wrapper.text()).toContain('L02 view')
    wrapper.unmount()
  })

  it('stops automatic advance while manually paused and resumes the preloaded next item', async () => {
    const playlist = playlistRuntime([
      playlistItem('L01 view', 'L01'),
      playlistItem('L02 view', 'L02'),
    ])
    const http = httpClient((path) => {
      if (path.includes('/playlists/')) return Promise.resolve(playlist)
      return Promise.resolve(dashboardRuntime(path.includes('%22L02%22') ? 'L02' : 'L01'))
    })
    registerStudioApi(createStudioApi(http))
    const router = await routerFor('/pc/studio/playlists/manual-pause/run', PlaylistRuntimePage)
    const wrapper = mount(PlaylistRuntimePage, {
      global: { plugins: [router, ElementPlus] },
    })
    await flushPromises()

    await wrapper
      .findAll('button')
      .find((button) => button.text() === '暂停')!
      .trigger('click')
    vi.advanceTimersByTime(6000)
    await flushPromises()
    expect(wrapper.text()).toContain('1 / 2')
    expect(wrapper.text()).toContain('L01 view')
    expect(wrapper.findAll('button').find((button) => button.text() === '继续')).toBeTruthy()

    await wrapper
      .findAll('button')
      .find((button) => button.text() === '继续')!
      .trigger('click')
    vi.advanceTimersByTime(3000)
    await flushPromises()
    expect(wrapper.text()).toContain('2 / 2')
    expect(wrapper.text()).toContain('L02 view')
    wrapper.unmount()
  })

  it('clears the carousel timer when leaving the page', async () => {
    const playlist = playlistRuntime([playlistItem('only view', 'L01')])
    let dashboardCalls = 0
    const http = httpClient((path) => {
      if (path.includes('/playlists/')) return Promise.resolve(playlist)
      dashboardCalls += 1
      return Promise.resolve(dashboardRuntime('only runtime'))
    })
    registerStudioApi(createStudioApi(http))
    const router = await routerFor('/pc/studio/playlists/only/run', PlaylistRuntimePage)
    const wrapper = mount(PlaylistRuntimePage, {
      global: { plugins: [router, ElementPlus] },
    })
    await flushPromises()
    expect(dashboardCalls).toBe(1)

    wrapper.unmount()
    vi.advanceTimersByTime(10000)
    await flushPromises()
    expect(dashboardCalls).toBe(1)
  })

  it('refreshes the current playlist item without advancing its stay timer', async () => {
    const playlist = playlistRuntime([
      playlistItem('L01 view', 'L01'),
      playlistItem('L02 view', 'L02'),
    ])
    const calls: string[] = []
    const http = httpClient((path) => {
      if (path.includes('/playlists/')) return Promise.resolve(playlist)
      calls.push(path)
      return Promise.resolve(
        dashboardRuntime(path.includes('%22L01%22') ? 'L01 refreshed' : 'L02 runtime', {
          components: [
            component('refresh-marker', 'Text', {
              text: path.includes('%22L01%22') ? 'L01 refreshed' : 'L02 runtime',
            }),
          ],
          ...(path.includes('%22L01%22')
            ? {
                instanceDefinitions: [
                  { instanceId: 'lineData', refreshSeconds: 1, staleAfterSeconds: 15 },
                ],
              }
            : {}),
        }),
      )
    })
    registerStudioApi(createStudioApi(http))
    const router = await routerFor(
      '/pc/studio/playlists/independent-refresh/run',
      PlaylistRuntimePage,
    )
    const wrapper = mount(PlaylistRuntimePage, {
      global: { plugins: [router, ElementPlus] },
    })
    await flushPromises()
    expect(calls).toHaveLength(2)

    vi.advanceTimersByTime(1000)
    await flushPromises()
    expect(calls).toHaveLength(3)
    expect(wrapper.text()).toContain('1 / 2')
    expect(wrapper.text()).toContain('L01 refreshed')

    vi.advanceTimersByTime(2000)
    await flushPromises()
    expect(wrapper.text()).toContain('2 / 2')
    expect(wrapper.text()).toContain('L02 view')
    wrapper.unmount()
  })
})
