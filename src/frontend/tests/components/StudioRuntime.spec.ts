import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import ElementPlus from 'element-plus'
import { createRouter, createMemoryHistory } from 'vue-router'
import { describe, expect, it, vi } from 'vitest'
import { defineComponent, ref } from 'vue'
import { createStudioApi, registerStudioApi, type StudioRuntime } from '@/api/studio'
import type { HttpClient } from '@/api/httpClient'
import StudioRuntimePage from '@/pages/pc/studio/StudioRuntimePage.vue'
import StudioLayoutRenderer from '@/components/studio/shared/StudioLayoutRenderer.vue'
import StudioEditorShell from '@/components/studio/shared/StudioEditorShell.vue'
import { newNode } from '@/components/studio/shared/model'

function runtime(): StudioRuntime {
  const root = newNode('Container')
  root.children.push(newNode('Text'))
  return {
    resource: {
      nId: 'page',
      kind: 'LowCodePage',
      name: 'Page',
      isEnabled: true,
      publishedRevisionNId: 'fixed-revision',
      publishedRevisionNo: '9007199254740993',
      hasDraftChanges: false,
      lastUpdatedOn: '',
      optimisticVersion: '1',
      concurrencyVersion: 'guid',
    },
    revisionNId: 'fixed-revision',
    document: {
      mode: 'Compose',
      layout: root,
      parameters: [
        { name: 'lineCode', label: 'Line', type: 'String', required: true, source: 'Caller' },
      ],
      fields: [],
      actions: [],
    },
    instances: {},
    diagnostics: [],
  }
}
describe('Studio actual shared control composition', () => {
  it('opens an empty narrow designer with structure and keeps usable zoom after adding a node', async () => {
    localStorage.clear()
    const previousWidth = window.innerWidth
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 642 })
    let available = false,
      resized: (() => void) | undefined
    vi.stubGlobal(
      'ResizeObserver',
      class {
        constructor(callback: () => void) {
          resized = callback
        }
        observe() {}
        disconnect() {}
      },
    )
    const clientWidth = vi
      .spyOn(HTMLElement.prototype, 'clientWidth', 'get')
      .mockImplementation(function (this: HTMLElement) {
        // A hidden first grid column reproduces the browser's zero-width initial viewport.
        return available && this.style.gridColumn === '2' ? 640 : 0
      })
    const clientHeight = vi.spyOn(HTMLElement.prototype, 'clientHeight', 'get').mockReturnValue(800)
    const previousScroll = HTMLElement.prototype.scrollTo
    HTMLElement.prototype.scrollTo = vi.fn()
    const Host = defineComponent({
      components: { StudioEditorShell },
      setup() {
        const document = ref({ layout: newNode('Container') })
        const add = () => {
          document.value.layout.children.push(newNode('Text'))
        }
        return { document, add }
      },
      template: `<StudioEditorShell v-model="document"><template #structure><button data-add @click="add">Add Text</button><span>Structure palette</span></template><template #properties>Properties panel</template></StudioEditorShell>`,
    })
    const wrapper = mount(Host, { global: { plugins: [createPinia(), ElementPlus] } })
    await flushPromises()
    expect(wrapper.text()).not.toMatch(/(?:^|\s)0%/)
    expect(wrapper.get('.studio-editor').classes()).not.toContain('left-hidden')
    expect(wrapper.get('.studio-sidebar').text()).toContain('Structure palette')
    expect(wrapper.get('.studio-canvas-root').text()).toContain('Container')
    available = true
    resized!()
    await flushPromises()
    expect(wrapper.text()).toContain('51%')
    await wrapper
      .findAll('button')
      .find((button) => button.text() === '适应')!
      .trigger('click')
    await wrapper.get('[data-add]').trigger('click')
    await flushPromises()
    await wrapper.get('.studio-design-node').trigger('click')
    await flushPromises()
    expect(wrapper.text()).toContain('51%')
    expect(wrapper.get('.studio-editor').classes()).not.toContain('left-hidden')
    expect(wrapper.get('.studio-design-node').text()).toContain('Text')
    expect(wrapper.get('.studio-canvas').attributes('style')).toContain('scale(0.506')
    wrapper.unmount()
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: previousWidth })
    clientWidth.mockRestore()
    clientHeight.mockRestore()
    HTMLElement.prototype.scrollTo = previousScroll
    vi.unstubAllGlobals()
  })
  it('fits a large canvas without scrollbars and preserves canvas dimensions as one undo command', async () => {
    vi.stubGlobal(
      'ResizeObserver',
      class {
        observe() {}
        disconnect() {}
      },
    )
    const previousScroll = HTMLElement.prototype.scrollTo
    HTMLElement.prototype.scrollTo = vi.fn()
    const width = vi.spyOn(HTMLElement.prototype, 'clientWidth', 'get').mockReturnValue(900),
      height = vi.spyOn(HTMLElement.prototype, 'clientHeight', 'get').mockReturnValue(600)
    const root = newNode('Container')
    Object.assign(root.grid, { canvasWidth: 16384, canvasHeight: 1440 })
    const wrapper = mount(StudioEditorShell, {
      props: { modelValue: { layout: root } },
      global: { plugins: [createPinia(), ElementPlus] },
    })
    await flushPromises()
    expect((wrapper.get('.studio-viewport').element as HTMLElement).style.overflow).toBe('hidden')
    expect(wrapper.text()).toContain('5%')
    await wrapper
      .findAll('button')
      .find((button) => button.text() === '+')!
      .trigger('click')
    expect((wrapper.get('.studio-viewport').element as HTMLElement).style.overflow).toBe('auto')
    const input = wrapper.findAll('input[type="number"]')[0]!
    await input.setValue('2560')
    expect(
      (
        wrapper.emitted('update:modelValue')?.at(-1)?.[0] as {
          layout: { grid: { canvasWidth: number } }
        }
      ).layout.grid.canvasWidth,
    ).toBe(2560)
    const count = wrapper.emitted('update:modelValue')?.length
    await input.trigger('keydown', { key: 'z', ctrlKey: true })
    expect(wrapper.emitted('update:modelValue')?.length).toBe(count)
    await wrapper
      .findAll('button')
      .find((button) => button.text() === '撤销')!
      .trigger('click')
    expect(
      (
        wrapper.emitted('update:modelValue')?.at(-1)?.[0] as {
          layout: { grid: { canvasWidth: number } }
        }
      ).layout.grid.canvasWidth,
    ).toBe(16384)
    wrapper.unmount()
    width.mockRestore()
    height.mockRestore()
    HTMLElement.prototype.scrollTo = previousScroll
    vi.unstubAllGlobals()
  })
  it('renders parameter controls before any runtime request and passes the chosen value to the fixed revision', async () => {
    const projection = runtime(),
      http = {
        get: vi.fn().mockResolvedValue(projection),
        post: vi.fn().mockResolvedValue(projection),
        put: vi.fn(),
        delete: vi.fn(),
        getWithMeta: vi.fn(),
      } as HttpClient
    registerStudioApi(createStudioApi(http))
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: '/pc/studio/low-code/:nId/run', component: StudioRuntimePage }],
    })
    await router.push('/pc/studio/low-code/page/run')
    await router.isReady()
    const wrapper = mount(StudioRuntimePage, {
      global: { plugins: [createPinia(), router, ElementPlus] },
    })
    await flushPromises()
    expect(http.post).not.toHaveBeenCalled()
    await wrapper.get('input[aria-label="Line"]').setValue('L02')
    await wrapper.get('[data-testid="query-panel-submit"]').trigger('click')
    await flushPromises()
    expect(http.post).toHaveBeenCalledWith(
      '/api/v1/studio/pages/page/runtime',
      { revisionNId: 'fixed-revision', parameters: { lineCode: 'L02' } },
      expect.anything(),
    )
    wrapper.unmount()
  })
  it('writes edited values to the configured stable fieldId and preserves exact decimals', async () => {
    const projection = runtime(),
      node = newNode('FormField')
    node.props = { fieldId: 'old', control: 'Text' }
    projection.document.fields = [
      {
        componentId: node.id,
        property: 'value',
        writeTarget: 'stable-new',
        source: { from: 'Form', path: ['stable-new'], nullPolicy: 'Null' },
        validators: [],
      },
    ]
    projection.ledgerModel = {
      nId: 'model',
      revisionNId: 'model-rev',
      document: {
        fields: [{ id: 'stable-new', name: 'renamed', label: 'Amount', type: 'Decimal' }],
      },
    }
    const wrapper = mount(StudioLayoutRenderer, {
      props: { node, runtime: projection, form: {}, parameters: {} },
      global: { plugins: [createPinia(), ElementPlus] },
    })
    await wrapper.get('input[aria-label="Amount"]').setValue('9007199254740993.000001')
    expect(wrapper.emitted('update:form')?.at(-1)).toEqual([
      { 'stable-new': '9007199254740993.000001' },
    ])
    wrapper.unmount()
  })
})
