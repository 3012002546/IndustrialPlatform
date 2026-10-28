import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import DashboardScreenPage from '@/pages/pc/studio/dashboard/DashboardScreenPage.vue'
import { dashboardScreenUrl, loadDashboardScreen } from '@/api/dashboardScreen'
import { studioScreenRoutes } from '@/router/studioRoutes'
import { installRouterGuards } from '@/router/guards'
import { useAuthStore } from '@/stores/authStore'
import { ROUTE_NAMES } from '@/router/routeNames'

const key = 'a'.repeat(64)
const runtime = () => ({
  resource: { name: '光刻胶制造运营总览' },
  revisionNId: 'rev-1',
  document: { canvas: { width: 1920, height: 1080, fit: 'Actual' }, instances: [] },
  parameters: { lineId: 'L01' },
  expiresOn: new Date(Date.now() + 86400000).toISOString(),
})
const success = () => ({ ok: true, json: async () => ({ success: true, data: runtime() }) })
const fetchMock = vi.fn()
let wrapper: ReturnType<typeof mount> | undefined
beforeEach(() => {
  setActivePinia(createPinia())
  vi.stubGlobal('fetch', fetchMock)
  fetchMock.mockReset()
  fetchMock.mockResolvedValue(success())
  vi.useFakeTimers()
})
afterEach(() => {
  wrapper?.unmount()
  wrapper = undefined
  vi.useRealTimers()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

async function mountScreen(hash = `#key=${key}`) {
  const router = createRouter({ history: createMemoryHistory(), routes: studioScreenRoutes })
  await router.push(`/screen/dashboards/screen-1${hash}`)
  await router.isReady()
  wrapper = mount(DashboardScreenPage, {
    global: {
      plugins: [router],
      stubs: {
        DashboardRuntimeView: {
          name: 'DashboardRuntimeView',
          props: ['runtime', 'parameters'],
          template: '<section data-testid="screen-runtime">{{ runtime.resource.name }}</section>',
        },
      },
    },
  })
  await flushPromises()
  return wrapper
}

describe('standalone anonymous dashboard screen', () => {
  it('puts the scoped secret only in the fragment', () => {
    const url = new URL(dashboardScreenUrl('screen-1', key))
    expect(url.pathname).toBe('/screen/dashboards/screen-1')
    expect(url.search).toBe('')
    expect(url.hash).toBe(`#key=${key}`)
  })
  it('uses a cookie-free no-cache request without login redirects', async () => {
    await loadDashboardScreen('screen-1', key, new AbortController().signal)
    const [url, options] = fetchMock.mock.calls[0]!
    expect(url).toMatch(/\/api\/v1\/studio\/screens\/screen-1\/runtime$/)
    expect(String(url)).not.toContain(key)
    expect(options).toMatchObject({
      credentials: 'omit',
      cache: 'no-store',
      redirect: 'error',
      headers: { 'X-Dashboard-Screen-Key': key },
    })
  })
  it('renders a viewport screen with a contained canvas and fixed parameters', async () => {
    const page = await mountScreen()
    expect(page.find('main.dashboard-screen').exists()).toBe(true)
    expect(page.text()).toContain('光刻胶制造运营总览')
    const view = page.findComponent({ name: 'DashboardRuntimeView' })
    expect(view.props('runtime').document.canvas.fit).toBe('Contain')
    expect(view.props('parameters')).toEqual({ lineId: 'L01' })
    expect(document.title).toBe('光刻胶制造运营总览')
  })
  it('accepts the protected key copied from an existing screen link', async () => {
    const copiedKey = 'v1.' + 'b'.repeat(180)
    const page = await mountScreen(`#key=${copiedKey}`)
    expect(page.text()).toContain('光刻胶制造运营总览')
    expect(fetchMock.mock.calls[0]![1].headers['X-Dashboard-Screen-Key']).toBe(copiedKey)
  })
  it('drills into a batch in the same anonymous screen and returns without opening tabs', async () => {
    const rule = { id: 'batch-detail', componentId: 'batches', event: 'RowClick' }
    fetchMock.mockImplementation(async (url: string) => ({
      ok: true,
      json: async () => ({
        success: true,
        data: {
          ...runtime(),
          resource: {
            name: new URL(url).searchParams.has('navigation')
              ? '批次 PR-DEMO-104'
              : '光刻胶制造运营总览',
          },
          document: { ...runtime().document, navigation: [rule] },
        },
      }),
    }))
    const page = await mountScreen()
    page
      .findComponent({ name: 'DashboardRuntimeView' })
      .vm.$emit(
        'navigate',
        { id: 'batches', props: { rowKeyPath: ['batch'] } },
        { batch: 'PR-DEMO-104', targetResourceNId: 'forged' },
      )
    await flushPromises()
    expect(page.text()).toContain('批次 PR-DEMO-104')
    const steps = JSON.parse(
      new URL(fetchMock.mock.calls.at(-1)![0]).searchParams.get('navigation')!,
    )
    expect(steps).toEqual([{ ruleId: 'batch-detail', rowKey: { key: 'PR-DEMO-104' } }])
    expect(page.vm.$router.currentRoute.value.path).toBe('/screen/dashboards/screen-1')
    await vi.advanceTimersByTimeAsync(30000)
    await flushPromises()
    expect(
      JSON.parse(new URL(fetchMock.mock.calls.at(-1)![0]).searchParams.get('navigation')!),
    ).toEqual(steps)
    await page
      .findAll('button')
      .find((button) => button.text() === '返回')!
      .trigger('click')
    await flushPromises()
    expect(page.text()).toContain('光刻胶制造运营总览')
    expect(new URL(fetchMock.mock.calls.at(-1)![0]).searchParams.has('navigation')).toBe(false)
  })
  it('rejects a malformed detail path without losing the error screen', async () => {
    const page = await mountScreen(`#key=${key}&navigation=invalid`)
    expect(fetchMock).not.toHaveBeenCalled()
    expect(page.text()).toContain('大屏明细路径无效')
  })
  it('shows a preloaded batch before revalidation finishes, returns immediately, and ignores its late response', async () => {
    const rule = { id: 'detail', componentId: 'batches', event: 'RowClick' }
    const item = {
      component: { id: 'batches', props: { rowKeyPath: ['batch'] } },
      row: { batch: 'B-04' },
    }
    let finish: (value: unknown) => void = () => {}
    let detailReads = 0
    fetchMock.mockImplementation((url: string) => {
      const detail = new URL(url).searchParams.has('navigation')
      const response = {
        ok: true,
        json: async () => ({
          success: true,
          data: {
            ...runtime(),
            resource: { name: detail ? '批次 B-04' : '总览' },
            parameters: detail ? { batchNo: 'B-04' } : {},
            document: { ...runtime().document, navigation: detail ? [] : [rule] },
          },
        }),
      }
      if (detail && ++detailReads === 2)
        return new Promise((resolve) => {
          finish = resolve
        })
      return Promise.resolve(response)
    })
    const page = await mountScreen()
    page.findComponent({ name: 'DashboardRuntimeView' }).vm.$emit('preload', [item])
    await vi.advanceTimersByTimeAsync(251)
    await flushPromises()
    page
      .findComponent({ name: 'DashboardRuntimeView' })
      .vm.$emit('navigate', item.component, item.row)
    // A duplicate click must not add a second navigation level.
    page
      .findComponent({ name: 'DashboardRuntimeView' })
      .vm.$emit('navigate', item.component, item.row)
    await flushPromises()
    expect(detailReads).toBe(2)
    const checkQuery = new URL(fetchMock.mock.calls.at(-1)![0]).searchParams
    expect(checkQuery.get('instanceIds')).toBe('[]')
    expect(checkQuery.get('knownRevisionNId')).toBe('rev-1')
    expect(page.text()).toContain('批次 B-04')
    expect(page.text()).toContain('正在更新')
    expect(page.find('[data-testid="screen-runtime"]').exists()).toBe(true)
    expect(page.findComponent({ name: 'DashboardRuntimeView' }).props('parameters')).toEqual({
      batchNo: 'B-04',
    })
    const back = page.findAll('button').find((button) => button.text() === '返回')!
    expect(back.attributes('disabled')).toBeUndefined()
    await back.trigger('click')
    await flushPromises()
    expect(page.text()).toContain('总览')
    finish({
      ok: true,
      json: async () => ({
        success: true,
        data: { ...runtime(), resource: { name: '迟到的旧批次' } },
      }),
    })
    await flushPromises()
    expect(page.text()).not.toContain('迟到的旧批次')
    expect(page.vm.$route.hash).not.toContain('navigation')
  })
  it('adopts an in-flight preload once instead of restarting the same detail request', async () => {
    const rule = { id: 'detail', componentId: 'batches', event: 'RowClick' }
    const item = {
      component: { id: 'batches', props: { rowKeyPath: ['batch'] } },
      row: { batch: 'B-05' },
    }
    let finish: (value: unknown) => void = () => {}
    fetchMock.mockImplementation((url: string) =>
      new URL(url).searchParams.has('navigation')
        ? new Promise((resolve) => {
            finish = resolve
          })
        : Promise.resolve({
            ok: true,
            json: async () => ({
              success: true,
              data: { ...runtime(), document: { ...runtime().document, navigation: [rule] } },
            }),
          }),
    )
    const page = await mountScreen()
    page.findComponent({ name: 'DashboardRuntimeView' }).vm.$emit('preload', [item])
    await vi.advanceTimersByTimeAsync(251)
    const signal = fetchMock.mock.calls[1]![1].signal as AbortSignal
    page
      .findComponent({ name: 'DashboardRuntimeView' })
      .vm.$emit('navigate', item.component, item.row)
    await flushPromises()
    expect(fetchMock).toHaveBeenCalledTimes(2)
    expect(signal.aborted).toBe(false)
    finish({
      ok: true,
      json: async () => ({
        success: true,
        data: { ...runtime(), resource: { name: '批次 B-05' } },
      }),
    })
    await flushPromises()
    expect(page.text()).toContain('批次 B-05')
  })
  it('rereads data when live navigation changes parameters on the same target revision', async () => {
    const rule = { id: 'detail', componentId: 'batches', event: 'RowClick' }
    const item = {
      component: { id: 'batches', props: { rowKeyPath: ['batch'] } },
      row: { batch: 'B-04' },
    }
    let changed = false
    fetchMock.mockImplementation(async (url: string) => {
      const query = new URL(url).searchParams
      const detail = query.has('navigation')
      return {
        ok: true,
        json: async () => ({
          success: true,
          data: {
            ...runtime(),
            parameters: detail ? { batchNo: 'B-04', lineId: changed ? 'L02' : 'L01' } : {},
            document: { ...runtime().document, navigation: detail ? [] : [rule] },
            instances: query.has('instanceIds') ? {} : { value: { data: changed ? 12 : 7 } },
          },
        }),
      }
    })
    const page = await mountScreen()
    page.findComponent({ name: 'DashboardRuntimeView' }).vm.$emit('preload', [item])
    await vi.advanceTimersByTimeAsync(251)
    await flushPromises()
    changed = true
    page
      .findComponent({ name: 'DashboardRuntimeView' })
      .vm.$emit('navigate', item.component, item.row)
    await flushPromises()
    const detailReads = fetchMock.mock.calls.filter(([url]) =>
      new URL(url).searchParams.has('navigation'),
    )
    expect(detailReads).toHaveLength(3)
    expect(new URL(detailReads[1]![0]).searchParams.get('instanceIds')).toBe('[]')
    expect(new URL(detailReads[2]![0]).searchParams.has('instanceIds')).toBe(false)
    const props = page.findComponent({ name: 'DashboardRuntimeView' }).props()
    expect(props.parameters).toEqual({ batchNo: 'B-04', lineId: 'L02' })
    expect(props.runtime.instances.value.data).toBe(12)
  })
  it('clears a preloaded screen and the return buffer when live access is revoked', async () => {
    const rule = { id: 'detail', componentId: 'batches', event: 'RowClick' }
    const item = { component: { id: 'batches' }, row: { id: 'B-04' } }
    fetchMock.mockResolvedValue({
      ok: true,
      json: async () => ({
        success: true,
        data: { ...runtime(), document: { ...runtime().document, navigation: [rule] } },
      }),
    })
    const page = await mountScreen()
    page.findComponent({ name: 'DashboardRuntimeView' }).vm.$emit('preload', [item])
    await vi.advanceTimersByTimeAsync(251)
    await flushPromises()
    fetchMock.mockResolvedValue({
      ok: false,
      status: 404,
      json: async () => ({ success: false, message: '已撤销' }),
    })
    page
      .findComponent({ name: 'DashboardRuntimeView' })
      .vm.$emit('navigate', item.component, item.row)
    await flushPromises()
    expect(page.find('[data-testid="screen-runtime"]').exists()).toBe(false)
    expect(page.text()).toContain('已撤销')
    await page
      .findAll('button')
      .find((button) => button.text() === '返回')!
      .trigger('click')
    await flushPromises()
    expect(page.find('[data-testid="screen-runtime"]').exists()).toBe(false)
  })
  it('bounds speculative requests and aborts them on unmount', async () => {
    const rule = { id: 'detail', componentId: 'batches', event: 'RowClick' }
    fetchMock.mockResolvedValueOnce({
      ok: true,
      json: async () => ({
        success: true,
        data: { ...runtime(), document: { ...runtime().document, navigation: [rule] } },
      }),
    })
    fetchMock.mockImplementation(() => new Promise(() => {}))
    const page = await mountScreen()
    page.findComponent({ name: 'DashboardRuntimeView' }).vm.$emit(
      'preload',
      Array.from({ length: 20 }, (_, id) => ({ component: { id: 'batches' }, row: { id } })),
    )
    await vi.advanceTimersByTimeAsync(251)
    expect(fetchMock).toHaveBeenCalledTimes(3)
    const signals = fetchMock.mock.calls.slice(1).map((call) => call[1].signal as AbortSignal)
    page.unmount()
    wrapper = undefined
    expect(signals.every((signal) => signal.aborted)).toBe(true)
    await vi.advanceTimersByTimeAsync(60000)
    expect(fetchMock).toHaveBeenCalledTimes(3)
  })
  it('does not display an expired preload and keeps a short-lived cached screen expiry during revalidation', async () => {
    const rule = { id: 'detail', componentId: 'batches', event: 'RowClick' }
    const item = { component: { id: 'batches' }, row: { id: 'B-04' } }
    let detailReads = 0
    let finish: (value: unknown) => void = () => {}
    fetchMock.mockImplementation((url: string) => {
      if (new URL(url).searchParams.has('navigation')) {
        detailReads++
        if (detailReads > 1)
          return new Promise((resolve) => {
            finish = resolve
          })
        return Promise.resolve({
          ok: true,
          json: async () => ({
            success: true,
            data: {
              ...runtime(),
              resource: { name: '短期批次' },
              expiresOn: new Date(Date.now() + 1000).toISOString(),
            },
          }),
        })
      }
      return Promise.resolve({
        ok: true,
        json: async () => ({
          success: true,
          data: { ...runtime(), document: { ...runtime().document, navigation: [rule] } },
        }),
      })
    })
    const page = await mountScreen()
    page.findComponent({ name: 'DashboardRuntimeView' }).vm.$emit('preload', [item])
    await vi.advanceTimersByTimeAsync(251)
    await flushPromises()
    page
      .findComponent({ name: 'DashboardRuntimeView' })
      .vm.$emit('navigate', item.component, item.row)
    await flushPromises()
    expect(page.text()).toContain('短期批次')
    await vi.advanceTimersByTimeAsync(1000)
    expect(page.find('[data-testid="screen-runtime"]').exists()).toBe(false)
    expect(page.text()).toContain('已过期')
    finish(success())
    await flushPromises()
    expect(page.find('[data-testid="screen-runtime"]').exists()).toBe(false)
    await page
      .findAll('button')
      .find((button) => button.text() === '返回')!
      .trigger('click')
    await flushPromises()
    page.findComponent({ name: 'DashboardRuntimeView' }).vm.$emit('preload', [item])
    // The old short-lived result and its ancestors were cleared, so an unresolved read cannot render it.
    page
      .findComponent({ name: 'DashboardRuntimeView' })
      .vm.$emit('navigate', item.component, item.row)
    await flushPromises()
    expect(page.find('[data-testid="screen-runtime"]').exists()).toBe(false)
  })
  it('rejects incomplete links without a server request or login prompt', async () => {
    const page = await mountScreen('')
    expect(fetchMock).not.toHaveBeenCalled()
    expect(page.text()).toContain('完整分享链接')
    expect(page.find('[data-testid="screen-runtime"]').exists()).toBe(false)
  })
  it('refreshes data and clears the previous screen immediately when access is revoked', async () => {
    const page = await mountScreen()
    fetchMock.mockResolvedValueOnce({
      ok: false,
      json: async () => ({ success: false, message: '大屏链接已撤销' }),
    })
    await vi.advanceTimersByTimeAsync(30000)
    await flushPromises()
    expect(fetchMock).toHaveBeenCalledTimes(2)
    expect(page.find('[data-testid="screen-runtime"]').exists()).toBe(false)
    expect(page.text()).toContain('已撤销')
    expect(page.text()).not.toContain('光刻胶制造运营总览')
  })
  it('stops scheduled requests on unmount', async () => {
    const page = await mountScreen()
    page.unmount()
    wrapper = undefined
    await vi.advanceTimersByTimeAsync(60000)
    expect(fetchMock).toHaveBeenCalledTimes(1)
  })
  it('keeps unlimited links visible while refreshing normally', async () => {
    fetchMock.mockImplementation(async () => ({
      ok: true,
      json: async () => ({
        success: true,
        data: { ...runtime(), expiresOn: null, followPublished: true },
      }),
    }))
    const page = await mountScreen()
    await vi.advanceTimersByTimeAsync(90000)
    await flushPromises()
    expect(fetchMock).toHaveBeenCalledTimes(4)
    expect(page.find('[data-testid="screen-runtime"]').exists()).toBe(true)
    expect(page.text()).not.toContain('已过期')
  })
  it('updates the rendered revision on the existing link at its next refresh', async () => {
    const page = await mountScreen()
    fetchMock.mockResolvedValueOnce({
      ok: true,
      json: async () => ({
        success: true,
        data: {
          ...runtime(),
          revisionNId: 'rev-2',
          resource: { name: '光刻胶已发布调整' },
          expiresOn: null,
          followPublished: true,
        },
      }),
    })
    await vi.advanceTimersByTimeAsync(30000)
    await flushPromises()
    expect(page.findComponent({ name: 'DashboardRuntimeView' }).props('runtime').revisionNId).toBe(
      'rev-2',
    )
    expect(page.text()).toContain('光刻胶已发布调整')
    const firstUrl = new URL(fetchMock.mock.calls[0]![0])
    const nextUrl = new URL(fetchMock.mock.calls[1]![0])
    expect(firstUrl.pathname).toBe(nextUrl.pathname)
    expect(nextUrl.searchParams.get('instanceIds')).toBe('[]')
    expect(nextUrl.searchParams.get('knownRevisionNId')).toBe('rev-1')
  })
  it('clears the canvas at expiry without waiting for the next refresh', async () => {
    fetchMock.mockResolvedValueOnce({
      ok: true,
      json: async () => ({
        success: true,
        data: { ...runtime(), expiresOn: new Date(Date.now() + 2000).toISOString() },
      }),
    })
    const page = await mountScreen()
    await vi.advanceTimersByTimeAsync(2000)
    expect(page.find('[data-testid="screen-runtime"]').exists()).toBe(false)
    expect(page.text()).toContain('已过期')
    expect(fetchMock).toHaveBeenCalledTimes(1)
  })
  it('keeps the dashboard visible and shows F11 guidance if fullscreen is unavailable', async () => {
    const requestFullscreen = vi.fn().mockRejectedValue(new Error('Document is not active'))
    vi.stubGlobal('requestFullscreen', undefined)
    const page = await mountScreen()
    Object.defineProperty(page.element, 'requestFullscreen', {
      value: requestFullscreen,
      configurable: true,
    })
    await page
      .findAll('button')
      .find((button) => button.text().includes('全屏'))!
      .trigger('click')
    await flushPromises()
    expect(requestFullscreen).toHaveBeenCalledTimes(1)
    expect(page.get('[role="alert"]').text()).toContain('F11')
    expect(page.find('[data-testid="screen-runtime"]').exists()).toBe(true)
  })
  it('bypasses session restoration and workspace tabs only for the public screen route', async () => {
    const restore = vi
      .spyOn(useAuthStore(), 'restore')
      .mockRejectedValue(new Error('No system session'))
    const router = createRouter({ history: createMemoryHistory(), routes: studioScreenRoutes })
    installRouterGuards(router)
    await router.push(`/screen/dashboards/screen-1#key=${key}`)
    expect(router.currentRoute.value.name).toBe(ROUTE_NAMES.studioDashboardScreen)
    expect(router.currentRoute.value.meta).toMatchObject({ requiresAuth: false, workspace: 'none' })
    expect(restore).not.toHaveBeenCalled()
  })
})
