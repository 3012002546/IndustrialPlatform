import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import ElementPlus from 'element-plus'
import { createStudioApi, registerStudioApi, type Json, type StudioResource } from '@/api/studio'
import type { HttpClient } from '@/api/httpClient'
import { ApiError } from '@/api/errors'
import { useAuthStore } from '@/stores/authStore'
import { useLocalizationStore } from '@/stores/localizationStore'
import TraceDesignerPage from '@/pages/pc/studio/trace/TraceDesignerPage.vue'
import { initialTraceDocument, newTraceObject } from './model'

// The persistence contract does not require a browser SVG engine. Form controls and the
// shared editor shell remain real; graph rendering has its own browser evidence.
vi.mock('../graph/GraphWorkbench.vue', () => ({ default: { template: '<div />' } }))
vi.mock('element-plus/es/components/focus-trap/index', async () => ({
  ElFocusTrap: (await import('vue')).defineComponent({ template: '<div><slot /></div>' }),
}))
const wrappers: VueWrapper[] = []
beforeEach(() => {
  localStorage.clear()
  sessionStorage.clear()
  vi.stubGlobal(
    'ResizeObserver',
    class {
      observe() {}
      unobserve() {}
      disconnect() {}
    },
  )
  HTMLElement.prototype.scrollTo = vi.fn()
})
afterEach(() => {
  wrappers.splice(0).forEach((w) => w.unmount())
  document.body.innerHTML = ''
  vi.unstubAllGlobals()
})
function deferred<T>() {
  let resolve!: (value: T) => void, reject!: (error: unknown) => void
  const promise = new Promise<T>((a, b) => {
    resolve = a
    reject = b
  })
  return { promise, resolve, reject }
}
async function setup() {
  const pinia = createPinia()
  setActivePinia(pinia)
  useLocalizationStore().setLocale('en-US')
  useAuthStore().adoptSession({
    transport: 'embedded-cookie',
    expiresAt: '2099-01-01',
    user: {
      userId: 'trace-user',
      username: 'tester',
      displayName: 'Tester',
      tenantId: 'tenant',
      mustChangePassword: false,
      roles: [],
      permissions: ['studio.trace.design', 'studio.trace.publish'],
    },
  })
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/pc/studio/trace-models/:nId/design', component: { template: '<div />' } }],
  })
  await router.push('/pc/studio/trace-models/model/design')
  await router.isReady()
  const resource: StudioResource = {
    nId: 'model',
    kind: 'TraceModel',
    name: 'Trace',
    isEnabled: false,
    publishedRevisionNId: null,
    publishedRevisionNo: null,
    hasDraftChanges: false,
    lastUpdatedOn: '',
    optimisticVersion: '9007199254740993',
    concurrencyVersion: 'cas-1',
  }
  const document = initialTraceDocument()
  const object = newTraceObject('lot')
  object.name = 'Lot'
  document.objects.push(object)
  let saved = { ...resource, draftDocument: document, diagnostics: [] as unknown[] }
  const pending = deferred<typeof saved>()
  const client = {
    get: vi.fn(async (path: string) =>
      path.includes('/draft')
        ? saved
        : path.includes('/link-sources')
          ? { ownedAvailable: false, external: [], mes: [] }
          : { items: [], totalCount: '0', page: 1, pageSize: 200 },
    ),
    put: vi
      .fn<(path: string, body: unknown) => Promise<typeof saved>>()
      .mockImplementation(() => pending.promise),
    post: vi.fn(),
    delete: vi.fn(),
    getWithMeta: vi.fn(),
  }
  registerStudioApi(createStudioApi(client as unknown as HttpClient))
  const wrapper = mount(TraceDesignerPage, {
    attachTo: window.document.body,
    global: { plugins: [pinia, router, ElementPlus] },
  })
  wrappers.push(wrapper)
  await flushPromises()
  expect(wrapper.get('.studio-editor').classes()).not.toContain('left-hidden')
  await wrapper
    .findAll('button')
    .find((b) => b.classes().includes('studio-node-item') && b.text().startsWith('Lot'))!
    .trigger('click')
  await flushPromises()
  async function edit(name: string) {
    await wrapper.get('input[aria-label="Name"]').setValue(name)
    await wrapper
      .findAll('button')
      .find((b) => b.text() === 'Apply changes')!
      .trigger('click')
    await flushPromises()
  }
  async function save() {
    await wrapper
      .findAll('button')
      .find((b) => b.text() === 'Save draft')!
      .trigger('click')
  }
  return {
    wrapper,
    client,
    pending,
    edit,
    save,
    resource,
    setSaved: (value: typeof saved) => {
      saved = value
    },
  }
}
describe('trace draft persistence snapshots', () => {
  it('opens start configuration from the toolbar and retains pending object edits', async () => {
    vi.stubGlobal('innerWidth', 1440)
    const { wrapper } = await setup()
    await wrapper.get('input[aria-label="Name"]').setValue('Pending name')
    await wrapper
      .findAll('button')
      .find((b) => b.text() === 'Collapse properties')!
      .trigger('click')
    expect(wrapper.get('.studio-editor').classes()).toContain('right-hidden')
    const actions = wrapper.findAll('.trace-toolbar-group button')
    expect(actions[0]!.text()).toBe('Start configuration')
    expect(actions[1]!.text()).toBe('Collapse structure')
    await actions[0]!.trigger('click')
    await flushPromises()
    expect(wrapper.get('.studio-editor').classes()).not.toContain('right-hidden')
    expect(wrapper.get('.trace-node-list .studio-node-item').text()).toContain('Pending name')
    expect(wrapper.find('.trace-structure .trace-start').exists()).toBe(false)
    expect(wrapper.find('button[aria-label="Delete selected"]').exists()).toBe(false)
    expect(wrapper.get('.trace-inspector-footer').text()).toContain('Apply changes')
  })
  it('retains property edits across tabs and exposes reversible deletion only in the inspector', async () => {
    const { wrapper } = await setup()
    await wrapper.get('input[aria-label="Name"]').setValue('Tab edit')
    await wrapper
      .findAll('[role="tab"]')
      .find((tab) => tab.text() === 'Identity')!
      .trigger('click')
    expect(wrapper.find('[role="tabpanel"][aria-hidden="false"]').exists()).toBe(true)
    await wrapper
      .findAll('[role="tab"]')
      .find((tab) => tab.text() === 'General')!
      .trigger('click')
    expect((wrapper.get('input[aria-label="Name"]').element as HTMLInputElement).value).toBe(
      'Tab edit',
    )
    expect(wrapper.find('.trace-node-list button[aria-label^="Delete"]').exists()).toBe(false)
    expect(wrapper.find('.trace-toolbar button[aria-label^="Delete"]').exists()).toBe(false)
    await wrapper
      .get('.trace-inspector-heading button[aria-label="Delete selected"]')
      .trigger('click')
    expect(wrapper.findAll('.trace-node-list .studio-node-item')).toHaveLength(0)
    expect(wrapper.find('button[aria-label="Delete selected"]').exists()).toBe(false)
    await wrapper
      .findAll('button')
      .find((b) => b.text() === 'Undo')!
      .trigger('click')
    expect(wrapper.findAll('.trace-node-list .studio-node-item')).toHaveLength(1)
    expect(wrapper.get('.trace-node-list .studio-node-item').text()).toContain('Tab edit')
  })
  it('shows Chinese configuration choices and readable diagnostic locations without changing binding values', async () => {
    const { wrapper } = await setup()
    useLocalizationStore().setLocale('zh-CN')
    await flushPromises()
    expect(wrapper.text()).toContain('批次')
    expect(wrapper.text()).toContain('当前数据行')
    expect(wrapper.text()).toContain('报错并停止')
    expect(wrapper.get('.trace-diagnostics').text()).toContain('请选择已发布的起点数据集')
    expect(wrapper.get('.trace-diagnostics').text()).not.toContain('Select a published')
    expect(wrapper.get('.trace-diagnostics').text()).not.toContain('start.datasetRevisionNId')
    expect(wrapper.get('input[aria-label="字段路径"]').element).toHaveProperty(
      'value',
      'sourceSystem',
    )
  })
  it('creates objects by dragging the palette rather than clicking it and blocks readonly drag', async () => {
    const { wrapper } = await setup()
    const palette = wrapper.get('button[aria-label="Object node"]')
    expect(palette.attributes('draggable')).toBe('true')
    const count = wrapper.findAll('.trace-node-list button').length
    await palette.trigger('click')
    expect(wrapper.findAll('.trace-node-list button')).toHaveLength(count)
    const setData = vi.fn()
    await palette.trigger('dragstart', { dataTransfer: { setData } })
    expect(wrapper.findAll('.trace-object-tool')).toHaveLength(1)
    expect(setData).toHaveBeenCalledWith('application/x-studio-graph-node', 'trace.object')
    await wrapper.get('button[aria-label="Connect relation"]').trigger('click')
    expect(wrapper.get('button[aria-label="Connect relation"]').attributes('aria-pressed')).toBe(
      'true',
    )
    await wrapper.get('button[aria-label="Pan"]').trigger('click')
    expect(wrapper.get('button[aria-label="Connect relation"]').attributes('aria-pressed')).toBe(
      'false',
    )
    useAuthStore().user!.permissions = []
    await flushPromises()
    expect(palette.attributes('draggable')).toBe('false')
    expect(
      wrapper.get('button[aria-label="Connect relation"]').attributes('disabled'),
    ).toBeDefined()
    setData.mockClear()
    await palette.trigger('dragstart', { dataTransfer: { setData } })
    expect(setData).not.toHaveBeenCalled()
  })
  it('retains edits made during save and advances only the accepted snapshot/CAS baseline', async () => {
    const { wrapper, client, pending, edit, save, resource } = await setup()
    await edit('Before request')
    await save()
    await flushPromises()
    const submitted = client.put.mock.calls[0]![1] as Json
    expect(submitted.optimisticVersion).toBe('9007199254740993')
    await edit('Edited during request')
    pending.resolve({
      ...resource,
      optimisticVersion: '9007199254740994',
      concurrencyVersion: 'cas-2',
      draftDocument: submitted.document,
      diagnostics: [],
    })
    await flushPromises()
    expect((wrapper.get('input[aria-label="Name"]').element as HTMLInputElement).value).toBe(
      'Edited during request',
    )
    expect(wrapper.text()).toContain('Unsaved changes')
    expect(client.put).toHaveBeenCalledTimes(1)
  })
  it('preserves local edits and history when a save returns 409 without an automatic second write', async () => {
    const { wrapper, client, pending, edit, save } = await setup()
    await edit('Local conflict draft')
    await save()
    pending.reject(
      new ApiError({
        kind: 'business',
        status: 409,
        code: 'STUDIO_VERSION_CONFLICT',
        message: 'conflict',
        correlationId: 'test',
      }),
    )
    await flushPromises()
    expect((wrapper.get('input[aria-label="Name"]').element as HTMLInputElement).value).toBe(
      'Local conflict draft',
    )
    expect(wrapper.text()).toContain('Local edits are retained')
    expect(
      wrapper
        .findAll('button')
        .find((b) => b.text() === 'Undo')
        ?.attributes('disabled'),
    ).toBeUndefined()
    expect(client.put).toHaveBeenCalledTimes(1)
  })
})
