import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import ElementPlus from 'element-plus'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import PlaylistRuntimePage from '@/pages/pc/studio/dashboard/PlaylistRuntimePage.vue'
import { studioScreenRoutes } from '@/router/studioRoutes'
import { installRouterGuards } from '@/router/guards'
import { ROUTE_NAMES } from '@/router/routeNames'
import { useAuthStore } from '@/stores/authStore'
import { dashboardScreenUrl } from '@/api/dashboardScreen'
import type { Json } from '@/api/studio'

const key = 'v1.' + 'p'.repeat(100)
const fetchMock = vi.fn()
let wrapper: VueWrapper | undefined
let manifest: Json
const rule = { id: 'batch-detail', componentId: 'batches', event: 'RowClick' }
const component = { id: 'batches', props: { rowKeyPath: ['batch'] } }
const steps = [{ ruleId: rule.id, rowKey: { key: 'B-04' } }]
const response = (data: Json) => ({ ok: true, json: async () => ({ success: true, data }) })
const requestUrl = (url: string) => new URL(url, window.location.origin)
const itemRequests = (id: string) =>
  fetchMock.mock.calls.filter(([url]) => requestUrl(url).searchParams.get('playlistItemId') === id)
function runtimeFor(url: string): Json {
  const query = requestUrl(url).searchParams
  const id = query.get('playlistItemId')!
  const detail = query.has('navigation')
  return {
    resource: {
      nId: `dashboard-${id}`,
      kind: 'Dashboard',
      name: detail ? `批次 B-04 · ${id}` : `看板 ${id}`,
    },
    revisionNId: `revision-${id}`,
    document: {
      canvas: { width: 1920, height: 1080, fit: 'Actual' },
      parameters: [{ name: 'lineId', type: 'String' }],
      instances: [
        { instanceId: 'static', sourceType: 'Constant', value: { value: 7 }, refreshSeconds: 0 },
      ],
      navigation: detail ? [] : [rule],
    },
    instances: { static: { data: { value: 7 } } },
    parameters: detail ? { lineId: 'FIXED-LINE', batchNo: 'B-04' } : { lineId: 'FIXED-LINE' },
    playlist: manifest,
    expiresOn: null,
    followPublished: true,
  }
}
function serve(url: string) {
  return response(requestUrl(url).searchParams.has('playlistItemId') ? runtimeFor(url) : manifest)
}

beforeEach(() => {
  vi.useFakeTimers()
  setActivePinia(createPinia())
  vi.stubGlobal('fetch', fetchMock)
  manifest = {
    kind: 'Playlist',
    resource: { nId: 'playlist-1', name: '公开轮播', kind: 'Playlist' },
    revisionNId: 'playlist-rev-1',
    loop: true,
    onFailure: 'Pause',
    preloadCount: 0,
    items: ['first', 'second', 'third'].map((id) => ({
      id,
      label: `项目 ${id}`,
      staySeconds: 3600,
    })),
    expiresOn: null,
    followPublished: true,
  }
  fetchMock.mockReset().mockImplementation(async (url: string) => serve(url))
})
afterEach(() => {
  wrapper?.unmount()
  wrapper = undefined
  document.body.innerHTML = ''
  vi.useRealTimers()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})
async function open(hash = `#key=${key}`, query = '') {
  const router = createRouter({ history: createMemoryHistory(), routes: studioScreenRoutes })
  installRouterGuards(router)
  await router.push(`/screen/playlists/screen-1${query}${hash}`)
  await router.isReady()
  wrapper = mount(PlaylistRuntimePage, {
    attachTo: document.body,
    global: {
      plugins: [router, ElementPlus],
      stubs: {
        DashboardRuntimeView: {
          name: 'DashboardRuntimeView',
          props: ['runtime', 'parameters'],
          emits: ['navigate'],
          template: '<section data-testid="playlist-runtime">{{ runtime.resource.name }}</section>',
        },
      },
    },
  })
  await flushPromises()
  return { page: wrapper, router }
}
async function click(text: string) {
  await wrapper!.get('.playlist-host').trigger('pointermove')
  const button = wrapper!.findAll('button').find((button) => button.text().trim() === text)
  expect(button, text).toBeDefined()
  await button!.trigger('click')
  await flushPromises()
}
async function chooseItem(id: string) {
  await wrapper!.get('.el-select__wrapper').trigger('click')
  await flushPromises()
  const option = [...document.querySelectorAll('.el-select-dropdown__item')].find((option) =>
    option.textContent?.includes(`项目 ${id}`),
  )
  expect(option).toBeDefined()
  ;(option as HTMLElement).click()
  await flushPromises()
}
function view() {
  return wrapper!.getComponent({ name: 'DashboardRuntimeView' })
}
function currentItem() {
  return new URLSearchParams(wrapper!.vm.$route.hash.slice(1)).get('item')
}

describe('anonymous playlist screen', () => {
  it('uses a compact initial loading state instead of full-width skeleton rows', async () => {
    let finish!: (value: unknown) => void
    fetchMock.mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          finish = resolve
        }),
    )
    const { page } = await open()
    expect(page.find('.el-skeleton').exists()).toBe(false)
    expect(page.get('[role="status"]').text()).toContain('加载中')
    expect(page.find('.playlist-loading.is-initial').exists()).toBe(true)
    finish(response(manifest))
    await flushPromises()
    expect(page.find('.playlist-loading').exists()).toBe(false)
    expect(view().props('runtime').resource.name).toBe('看板 first')
  })

  it.each([true, false])(
    'keeps the last canvas while switching, blocks stale clicks and clears it if access fails (%s)',
    async (success) => {
      await open()
      let finish!: (value: unknown) => void
      fetchMock.mockImplementationOnce(
        () =>
          new Promise((resolve) => {
            finish = resolve
          }),
      )
      await click('下一页')
      expect(view().props('runtime').resource.name).toBe('看板 first')
      expect(view().attributes('inert')).toBeDefined()
      expect(wrapper!.find('.el-skeleton').exists()).toBe(false)
      expect(wrapper!.get('[role="status"]').text()).toContain('加载中')
      const count = fetchMock.mock.calls.length
      view().vm.$emit('navigate', component, { batch: 'B-04' })
      await flushPromises()
      expect(fetchMock).toHaveBeenCalledTimes(count)
      finish(
        success
          ? serve(itemRequests('second')[0]![0])
          : { ok: false, status: 404, json: async () => ({ success: false, message: '已撤销' }) },
      )
      await flushPromises()
      expect(wrapper!.find('.playlist-loading').exists()).toBe(false)
      if (success) {
        expect(view().props('runtime').resource.name).toBe('看板 second')
        expect(view().attributes('inert')).toBeUndefined()
      } else {
        expect(wrapper!.find('[data-testid="playlist-runtime"]').exists()).toBe(false)
        expect(wrapper!.text()).toContain('已撤销')
      }
    },
  )

  it('resumes an item and its detail with one authorized response, and refreshes without rereading the manifest', async () => {
    const hash = `#key=${key}&item=second&navigation=${encodeURIComponent(JSON.stringify(steps))}`
    await open(hash)
    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(view().props('parameters')).toEqual({ lineId: 'FIXED-LINE', batchNo: 'B-04' })
    expect(requestUrl(fetchMock.mock.calls[0]![0]).searchParams.get('playlistItemId')).toBe(
      'second',
    )
    await click('刷新')
    expect(fetchMock).toHaveBeenCalledTimes(2)
    await click('返回')
    expect(view().props('runtime').resource.name).toBe('看板 second')
    const beforeRefresh = fetchMock.mock.calls.length
    await click('刷新')
    expect(fetchMock).toHaveBeenCalledTimes(beforeRefresh + 1)
    expect(view().props('runtime').resource.name).toBe('看板 second')
  })

  it('does not preload or rotate a single item, but still rechecks its public access', async () => {
    manifest.items = [manifest.items[0]]
    manifest.items[0].staySeconds = 3
    manifest.preloadCount = 1
    await open()
    expect(fetchMock).toHaveBeenCalledTimes(2)
    await vi.advanceTimersByTimeAsync(10000)
    expect(fetchMock).toHaveBeenCalledTimes(2)
    await vi.advanceTimersByTimeAsync(20000)
    await flushPromises()
    expect(fetchMock).toHaveBeenCalledTimes(3)
    expect(currentItem()).toBe('first')
    expect(requestUrl(fetchMock.mock.calls[2]![0]).searchParams.get('instanceIds')).toBe('[]')
  })

  it.each(['metadata', 'target-revision', 'parameters'])(
    'reuses a publication refresh response and only rereads missing instances for changed parameters (%s)',
    async (change) => {
      let updated = false
      fetchMock.mockImplementation(async (url: string) => {
        const query = requestUrl(url).searchParams
        if (!query.has('playlistItemId')) return response(manifest)
        const value = runtimeFor(url)
        if (updated) {
          if (change === 'target-revision') value.revisionNId = 'new-target-revision'
          if (change === 'parameters') value.parameters = { lineId: 'NEW-LINE' }
          value.instances.static.data.value = 12
        }
        if (query.has('instanceIds') && query.get('knownRevisionNId') === value.revisionNId)
          value.instances = {}
        return response(value)
      })
      await open()
      updated = true
      manifest = { ...manifest, revisionNId: 'new-playlist-revision' }
      await vi.advanceTimersByTimeAsync(30000)
      await flushPromises()
      expect(fetchMock).toHaveBeenCalledTimes(change === 'parameters' ? 4 : 3)
      expect(view().props('runtime').playlist.revisionNId).toBe('new-playlist-revision')
      expect(view().props('runtime').instances.static.data.value).toBe(
        change === 'metadata' ? 7 : 12,
      )
      expect(view().props('parameters').lineId).toBe(
        change === 'parameters' ? 'NEW-LINE' : 'FIXED-LINE',
      )
    },
  )

  it('adopts a full changed-target response when revalidating a preload without reading it again', async () => {
    manifest.preloadCount = 1
    await open()
    const previousServe = fetchMock.getMockImplementation()!
    fetchMock.mockImplementation(async (url: string) => {
      const query = requestUrl(url).searchParams
      if (query.get('playlistItemId') !== 'second') return previousServe(url)
      const value = runtimeFor(url)
      value.revisionNId = 'new-second-revision'
      value.instances.static.data.value = 15
      return response(value)
    })
    manifest = { ...manifest, revisionNId: 'new-playlist-revision' }
    await click('下一页')
    expect(itemRequests('second')).toHaveLength(2)
    expect(view().props('runtime').revisionNId).toBe('new-second-revision')
    expect(view().props('runtime').instances.static.data.value).toBe(15)
  })

  it('bypasses login restoration, keeps its secret in the fragment and passes fixed server parameters to the contained view', async () => {
    const restore = vi
      .spyOn(useAuthStore(), 'restore')
      .mockRejectedValue(new Error('No login session'))
    const link = new URL(dashboardScreenUrl('screen-1', key, 'Playlist'))
    expect(link.pathname).toBe('/screen/playlists/screen-1')
    expect(link.search).toBe('')
    expect(new URLSearchParams(link.hash.slice(1)).get('key')).toBe(key)
    const { page, router } = await open(link.hash, '?lineId=FORGED&access_token=USER-TOKEN')
    expect(router.currentRoute.value.name).toBe(ROUTE_NAMES.studioPlaylistScreen)
    expect(router.currentRoute.value.meta).toMatchObject({
      requiresAuth: false,
      workspace: 'none',
      playlistScreen: true,
    })
    expect(restore).not.toHaveBeenCalled()
    expect(page.find('main.playlist-stage').exists()).toBe(true)
    expect(view().props('parameters')).toEqual({ lineId: 'FIXED-LINE' })
    expect(view().props('runtime').document.canvas.fit).toBe('Contain')
    expect(page.find('form').exists()).toBe(false)
    expect(page.text()).not.toContain('FIXED-LINE')
    expect(page.text()).not.toContain('FORGED')
    expect(document.title).toBe('公开轮播')
    for (const [url, options] of fetchMock.mock.calls) {
      expect(url).not.toContain(key)
      expect(url).not.toContain('USER-TOKEN')
      expect(url).not.toContain('FORGED')
      expect(options).toMatchObject({ credentials: 'omit', cache: 'no-store', redirect: 'error' })
      expect(options.headers).toEqual({ 'X-Dashboard-Screen-Key': key })
    }
  })

  it('automatically advances, supports previous/next and a real select, and preserves pause after manual switching', async () => {
    manifest.items.forEach((item: Json) => {
      item.staySeconds = 5
    })
    await open()
    expect(currentItem()).toBe('first')
    await vi.advanceTimersByTimeAsync(5000)
    await flushPromises()
    expect(currentItem()).toBe('second')
    await click('暂停')
    await click('上一页')
    expect(currentItem()).toBe('first')
    await click('下一页')
    expect(currentItem()).toBe('second')
    await chooseItem('third')
    expect(currentItem()).toBe('third')
    expect(view().props('runtime').resource.name).toBe('看板 third')
    await vi.advanceTimersByTimeAsync(15000)
    expect(currentItem()).toBe('third')
    expect(wrapper!.text()).toContain('继续')
    await click('继续')
    await vi.advanceTimersByTimeAsync(5000)
    await flushPromises()
    expect(currentItem()).toBe('first')
  })

  it('persists the active item and server detail path across refresh/reload, suppresses automatic switching in detail and returns to that item', async () => {
    manifest.items.forEach((item: Json) => {
      item.staySeconds = 5
    })
    await open(`#key=${key}&item=second`)
    view().vm.$emit('navigate', component, { batch: 'B-04', targetResourceNId: 'FORGED' })
    await flushPromises()
    expect(view().props('parameters')).toEqual({ lineId: 'FIXED-LINE', batchNo: 'B-04' })
    expect(view().props('runtime').resource.name).toBe('批次 B-04 · second')
    const hash = wrapper!.vm.$route.hash
    const state = new URLSearchParams(hash.slice(1))
    expect(state.get('key')).toBe(key)
    expect(state.get('item')).toBe('second')
    expect(JSON.parse(state.get('navigation')!)).toEqual(steps)
    expect(
      JSON.parse(requestUrl(fetchMock.mock.calls.at(-1)![0]).searchParams.get('navigation')!),
    ).toEqual(steps)
    await vi.advanceTimersByTimeAsync(15000)
    expect(currentItem()).toBe('second')
    expect(itemRequests('third')).toHaveLength(0)
    await click('刷新')
    expect(view().props('runtime').resource.name).toBe('批次 B-04 · second')
    wrapper!.unmount()
    wrapper = undefined
    await open(hash)
    expect(view().props('runtime').resource.name).toBe('批次 B-04 · second')
    await click('返回')
    expect(view().props('runtime').resource.name).toBe('看板 second')
    expect(currentItem()).toBe('second')
    expect(new URLSearchParams(wrapper!.vm.$route.hash.slice(1)).has('navigation')).toBe(false)
  })

  it('revalidates static public data after 30 seconds, clears revoked content on 404 and stops further reads', async () => {
    await open()
    expect(fetchMock).toHaveBeenCalledTimes(2)
    fetchMock.mockResolvedValue({
      ok: false,
      status: 404,
      json: async () => ({ success: false, message: '已撤销' }),
    })
    await vi.advanceTimersByTimeAsync(29999)
    expect(fetchMock).toHaveBeenCalledTimes(2)
    await vi.advanceTimersByTimeAsync(1)
    await flushPromises()
    expect(fetchMock).toHaveBeenCalledTimes(3)
    const query = requestUrl(fetchMock.mock.calls[2]![0]).searchParams
    expect(query.get('instanceIds')).toBe('[]')
    expect(query.get('knownRevisionNId')).toBe('revision-first')
    expect(wrapper!.find('[data-testid="playlist-runtime"]').exists()).toBe(false)
    expect(wrapper!.text()).not.toContain('看板 first')
    expect(wrapper!.text()).toContain('大屏暂不可用')
    await vi.advanceTimersByTimeAsync(60000)
    expect(fetchMock).toHaveBeenCalledTimes(3)
  })

  it('does not prefetch when preloadCount is zero and makes no more calls after unmount', async () => {
    const { page } = await open()
    expect(fetchMock).toHaveBeenCalledTimes(2)
    expect(itemRequests('second')).toHaveLength(0)
    expect(itemRequests('third')).toHaveLength(0)
    page.unmount()
    wrapper = undefined
    await vi.advanceTimersByTimeAsync(120000)
    expect(fetchMock).toHaveBeenCalledTimes(2)
  })

  it('uses a completed preload after a lightweight authorization check without fetching the same full item twice', async () => {
    manifest.preloadCount = 1
    await open()
    expect(itemRequests('second')).toHaveLength(1)
    expect(requestUrl(itemRequests('second')[0]![0]).searchParams.has('instanceIds')).toBe(false)
    await click('下一页')
    expect(view().props('runtime').resource.name).toBe('看板 second')
    const reads = itemRequests('second').map(([url]) => requestUrl(url).searchParams)
    expect(reads).toHaveLength(2)
    expect(reads.filter((query) => !query.has('instanceIds'))).toHaveLength(1)
    expect(reads[1]!.get('instanceIds')).toBe('[]')
    expect(reads[1]!.get('knownRevisionNId')).toBe('revision-second')
  })

  it('adopts an in-flight preload once, then aborts remaining speculative work on unmount and ignores its late result', async () => {
    manifest.preloadCount = 1
    const pending = new Map<string, { finish: (value: unknown) => void; url: string }>()
    fetchMock.mockImplementation((url: string) => {
      const id = requestUrl(url).searchParams.get('playlistItemId')
      if (id === 'second' || id === 'third')
        return new Promise((finish) => {
          pending.set(id, { finish, url })
        })
      return Promise.resolve(serve(url))
    })
    await open()
    expect(itemRequests('second')).toHaveLength(1)
    await click('下一页')
    expect(itemRequests('second')).toHaveLength(1)
    const second = pending.get('second')!
    second.finish(serve(second.url))
    await flushPromises()
    expect(view().props('runtime').resource.name).toBe('看板 second')
    expect(itemRequests('second')).toHaveLength(1)
    expect(itemRequests('third')).toHaveLength(1)
    const signal = itemRequests('third')[0]![1].signal as AbortSignal
    expect(signal.aborted).toBe(false)
    wrapper!.unmount()
    wrapper = undefined
    expect(signal.aborted).toBe(true)
    const count = fetchMock.mock.calls.length
    const third = pending.get('third')!
    third.finish(serve(third.url))
    await flushPromises()
    await vi.advanceTimersByTimeAsync(120000)
    expect(fetchMock).toHaveBeenCalledTimes(count)
  })
})
