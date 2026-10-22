import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import ElementPlus, { ElDropdown } from 'element-plus'
import { createMemoryHistory, createRouter } from 'vue-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  createStudioApi,
  registerStudioApi,
  type Json,
  type StudioResource,
  type StudioRuntime,
} from '@/api/studio'
import type { HttpClient } from '@/api/httpClient'
import { useAuthStore } from '@/stores/authStore'
import { initialDocument, newNode } from '@/components/studio/shared/model'
import StudioListPage from '@/pages/pc/studio/StudioListPage.vue'
import StudioDesignerPage from '@/pages/pc/studio/StudioDesignerPage.vue'
import StudioRuntimeView from '@/components/studio/shared/StudioRuntimeView.vue'
import AppFormDrawer from '@/components/management/AppFormDrawer.vue'
import StudioLayoutRenderer from '@/components/studio/shared/StudioLayoutRenderer.vue'

vi.mock('element-plus/es/components/focus-trap/index', async () => {
  const { defineComponent } = await import('vue')
  return { ElFocusTrap: defineComponent({ template: '<div><slot /></div>' }) }
})
const resource: StudioResource = {
  nId: 'page',
  kind: 'LowCodePage',
  name: 'Contract page',
  isEnabled: true,
  publishedRevisionNId: 'page-rev',
  publishedRevisionNo: '1',
  hasDraftChanges: false,
  lastUpdatedOn: '',
  optimisticVersion: '1',
  concurrencyVersion: 'guid',
}
function httpClient(get: (path: string) => unknown): HttpClient {
  return {
    get: vi.fn(async (path: string) => get(path)),
    post: vi.fn().mockResolvedValue({ status: 'Succeeded' }),
    put: vi.fn(),
    delete: vi.fn(),
    getWithMeta: vi.fn(),
  } as unknown as HttpClient
}
function page(document: Json): StudioRuntime {
  return { resource, revisionNId: 'page-rev', document, instances: {}, diagnostics: [] }
}
async function environment(path = '/pc/studio/low-code') {
  const pinia = createPinia()
  setActivePinia(pinia)
  useAuthStore().adoptSession({
    transport: 'embedded-cookie',
    expiresAt: '2099-01-01',
    user: {
      userId: 'contract-user',
      username: 'tester',
      displayName: 'Tester',
      tenantId: 'default',
      mustChangePassword: false,
      roles: [],
      permissions: [
        'studio.lowcode.view',
        'studio.lowcode.design',
        'studio.lowcode.publish',
        'studio.lowcode.run',
        'studio.ledger.write',
      ],
    },
  })
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/:pathMatch(.*)*', component: { template: '<div />' } }],
  })
  await router.push(path)
  await router.isReady()
  return { plugins: [pinia, router, ElementPlus] }
}
beforeEach(() => {
  localStorage.clear()
  sessionStorage.clear()
  vi.stubGlobal(
    'ResizeObserver',
    class {
      observe() {}
      disconnect() {}
    },
  )
  HTMLElement.prototype.scrollTo = vi.fn()
})
afterEach(() => vi.unstubAllGlobals())
describe('Studio real endpoint and document contracts', () => {
  it('opens revision summaries returned as a bare array and makes rollback available', async () => {
    const http = httpClient((path) =>
      path.endsWith('/deployment/profile')
        ? { installedModules: ['LowCode'], enabledModules: ['LowCode'] }
        : path.endsWith('/revisions')
          ? [
              {
                nId: 'old-rev',
                revisionNo: '9007199254740993',
                releaseNote: 'Actual release summary',
                createdOn: '2026-10-22',
              },
            ]
          : { items: [resource], totalCount: '1', page: 1, pageSize: 20 },
    )
    registerStudioApi(createStudioApi(http))
    const wrapper = mount(StudioListPage, { global: await environment() })
    await flushPromises()
    await vi.waitFor(() => expect(wrapper.findAllComponents(ElDropdown).length).toBeGreaterThan(1))
    wrapper.findAllComponents(ElDropdown).at(-1)!.vm.$emit('command', 'revisions')
    await flushPromises()
    expect(
      wrapper.findAllComponents(AppFormDrawer).some((drawer) => drawer.props('modelValue')),
    ).toBe(true)
    expect(document.body.textContent).toContain('Actual release summary')
    expect(document.body.textContent).toContain('9007199254740993')
    expect(document.body.textContent).toContain('回退到此修订')
    wrapper.unmount()
  })
  it('edits and saves instance parameters using the actual published document property', async () => {
    const document = initialDocument('LowCodePage')
    document.instances = [
      {
        instanceId: 'production',
        datasetNId: 'dataset',
        revisionNId: 'dataset-rev',
        parameters: [
          {
            target: 'lineCode',
            source: { from: 'PageParam', path: ['line'], nullPolicy: 'Error' },
          },
        ],
      },
    ]
    const draft = { ...resource, draftDocument: document, diagnostics: [] }
    const http = httpClient((path) =>
      path.endsWith('/draft')
        ? draft
        : path.endsWith('/registry/catalog')
          ? { components: [], actions: [], codePages: [] }
          : { items: [], totalCount: '0', page: 1, pageSize: 200 },
    )
    vi.mocked(http.put).mockResolvedValue(draft)
    registerStudioApi(createStudioApi(http))
    const global = await environment('/pc/studio/low-code/page/design')
    // Designer obtains the resource id from the route, just as the registered route does.
    const router = global.plugins[1] as ReturnType<typeof createRouter>
    router.addRoute({ path: '/pc/studio/low-code/:nId/design', component: StudioDesignerPage })
    await router.replace('/pc/studio/low-code/page/design')
    const wrapper = mount(StudioDesignerPage, { global })
    await flushPromises()
    const target = wrapper
      .findAll('input')
      .find((input) => (input.element as HTMLInputElement).value === 'lineCode')
    expect(target).toBeDefined()
    await target!.setValue('backupLineCode')
    await wrapper
      .findAll('button')
      .find((button) => button.text() === '保存草稿')!
      .trigger('click')
    await flushPromises()
    expect(http.put).toHaveBeenCalledWith(
      '/api/v1/studio/resources/page/draft',
      expect.objectContaining({
        document: expect.objectContaining({
          instances: [
            expect.objectContaining({
              parameters: [expect.objectContaining({ target: 'backupLineCode' })],
            }),
          ],
        }),
      }),
      expect.anything(),
    )
    const body = vi.mocked(http.put).mock.calls[0]![1] as Json
    expect(body.document.instances[0]).not.toHaveProperty('parameterMap')
    wrapper.unmount()
  })
  it('keeps action form/row context separate from page preview parameters', async () => {
    const document = initialDocument('LowCodePage')
    document.parameters = [
      { name: 'lineCode', label: 'Line', type: 'String', source: 'Caller', required: true },
    ]
    document.actions = [
      {
        id: 'save',
        label: 'Save action',
        event: 'Click',
        actionKey: 'studio.ledger.save',
        executorVersion: '1',
        inputMap: [{ target: 'values', source: { from: 'Form', path: [], nullPolicy: 'Error' } }],
        success: { refreshInstanceIds: [], closeForm: false },
      },
    ]
    const draft = { ...resource, draftDocument: document, diagnostics: [] }
    const http = httpClient((path) =>
      path.endsWith('/draft')
        ? draft
        : path.endsWith('/registry/catalog')
          ? { components: [], actions: [], codePages: [] }
          : { items: [], totalCount: '0', page: 1, pageSize: 200 },
    )
    vi.mocked(http.post).mockImplementation(async (path, body) =>
      path.endsWith('/actions/preview')
        ? { mode: 'Mock', resolvedInput: { values: (body as Json).context.form } }
        : page((body as Json).document),
    )
    registerStudioApi(createStudioApi(http))
    const global = await environment('/pc/studio/low-code/page/design')
    const router = global.plugins[1] as ReturnType<typeof createRouter>
    router.addRoute({ path: '/pc/studio/low-code/:nId/design', component: StudioDesignerPage })
    await router.replace('/pc/studio/low-code/page/design')
    const wrapper = mount(StudioDesignerPage, { global })
    await flushPromises()
    await wrapper.get('input[aria-label="试查参数"]').setValue('{"lineCode":"page-only"}')
    const context = {
      form: { amount: '12.500001' },
      pageParameters: { lineCode: 'action-line' },
      row: { nId: 'row-2' },
    }
    await wrapper
      .get('textarea[aria-label="上下文 (form, pageParameters, row)"]')
      .setValue(JSON.stringify(context))
    await wrapper
      .findAll('button')
      .find((button) => button.text() === '模拟动作')!
      .trigger('click')
    await flushPromises()
    expect(http.post).toHaveBeenCalledWith(
      '/api/v1/studio/actions/preview',
      { pageDocument: document, actionId: 'save', context },
      expect.anything(),
    )
    await wrapper
      .findAll('button')
      .find((button) => button.text() === '试运行')!
      .trigger('click')
    await flushPromises()
    expect(http.post).toHaveBeenCalledWith(
      '/api/v1/studio/pages/page/preview',
      { document, parameters: { lineCode: 'page-only' } },
      expect.anything(),
    )
    wrapper.unmount()
  })
  it('submits untouched read-bound values to the configured ledger field ID', async () => {
    const document = initialDocument('LowCodePage'),
      field = newNode('FormField')
    document.layout.children.push(field)
    document.fields = [
      {
        componentId: field.id,
        property: 'value',
        writeTarget: 'amount',
        source: { from: 'Constant', value: '9007199254740993.000001' },
        validators: [],
      },
    ]
    const runtime = page(document)
    runtime.ledgerModel = {
      nId: 'model',
      revisionNId: 'model-rev',
      document: {
        fields: [
          { id: 'amount', name: 'amount', label: 'Amount', type: 'Decimal', required: true },
        ],
      },
    }
    const http = httpClient(() => ({}))
    registerStudioApi(createStudioApi(http))
    const wrapper = mount(StudioRuntimeView, {
      props: { runtime, recordMode: 'new' },
      global: await environment(),
    })
    await flushPromises()
    expect((wrapper.get('input[aria-label="Amount"]').element as HTMLInputElement).value).toBe(
      '9007199254740993.000001',
    )
    await wrapper.get('input[maxlength="128"]').setValue('business-record')
    await wrapper.get('form').trigger('submit')
    await flushPromises()
    expect(http.post).toHaveBeenCalledWith(
      '/api/v1/studio/ledger/model/records',
      expect.objectContaining({ values: { amount: '9007199254740993.000001' } }),
      expect.anything(),
    )
    wrapper.unmount()
  })
  it('propagates selected Row values and submits registered actions with one double-click key', async () => {
    const document = initialDocument('LowCodePage'),
      table = newNode('Table'),
      text = newNode('Text')
    document.mode = 'Compose'
    table.props = {
      instanceId: 'lines',
      collectionPath: ['rows'],
      rowKeyPath: 'nId',
      columns: [{ field: 'name', title: 'Name' }],
    }
    document.layout.children.push(table, text)
    document.fields = [
      {
        componentId: text.id,
        property: 'text',
        source: { from: 'Row', path: ['name'], nullPolicy: 'Null' },
      },
    ]
    document.actions = [
      {
        id: 'submit',
        label: 'Registered submit',
        event: 'Submit',
        actionKey: 'registered',
        executorVersion: '1',
        inputMap: [],
      },
    ]
    const runtime = page(document)
    runtime.instances = { lines: { data: { rows: [{ nId: 'line-2', name: 'Line 02' }] } } }
    const http = httpClient(() => ({}))
    registerStudioApi(createStudioApi(http))
    const wrapper = mount(StudioRuntimeView, { props: { runtime }, global: await environment() })
    await flushPromises()
    expect(wrapper.findComponent({ name: 'AppDataTable' }).props('rows')).toEqual([
      { nId: 'line-2', name: 'Line 02' },
    ])
    wrapper
      .findComponent({ name: 'AppDataTable' })
      .vm.$emit('row-click', { nId: 'line-2', name: 'Line 02' })
    await flushPromises()
    expect(wrapper.text()).toContain('Line 02')
    expect(
      wrapper
        .findAllComponents(StudioLayoutRenderer)
        .find((renderer) => renderer.props('node').id === text.id)!
        .text(),
    ).toBe('Line 02')
    await wrapper.get('form').trigger('submit')
    await flushPromises()
    await wrapper.get('form').trigger('submit')
    await flushPromises()
    const calls = vi.mocked(http.post).mock.calls
    expect(calls).toHaveLength(2)
    expect(calls[0]![0]).toBe('/api/v1/studio/pages/page/actions/submit/execute')
    expect(calls[0]![1]).toMatchObject({
      context: { rowKey: { instanceId: 'lines', key: 'line-2' } },
    })
    expect((calls[0]![1] as Json).context).not.toHaveProperty('row')
    expect(calls[0]![2]!.headers?.['Idempotency-Key']).toBe(
      calls[1]![2]!.headers?.['Idempotency-Key'],
    )
    wrapper.unmount()
  })
})
