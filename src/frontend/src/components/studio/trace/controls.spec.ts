import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import ElementPlus, { ElInput } from 'element-plus'
import { createStudioApi, registerStudioApi } from '@/api/studio'
import type { HttpClient } from '@/api/httpClient'
import { useLocalizationStore } from '@/stores/localizationStore'
import BindingEditor from '../shared/BindingEditor.vue'
import TraceInspector from './TraceInspector.vue'
import TraceSearchForm from './TraceSearchForm.vue'
import { initialTraceDocument, newTraceObject } from './model'

const wrappers: VueWrapper[] = []
beforeEach(() => {
  vi.stubGlobal(
    'ResizeObserver',
    class {
      observe() {}
      unobserve() {}
      disconnect() {}
    },
  )
  HTMLElement.prototype.scrollTo = vi.fn()
  registerStudioApi(
    createStudioApi({
      get: vi.fn().mockResolvedValue({
        ownedAvailable: false,
        external: [
          {
            key: 'mes-details',
            embeddable: true,
            valueParameter: 'id',
            allowedParameters: ['id'],
          },
        ],
        mes: [
          {
            key: 'mes-attachments',
            scanPolicy: 'ImportAndScan',
            allowedContentTypes: ['application/pdf'],
            maxBytes: '10485760',
            requiresFiles: true,
            available: true,
          },
        ],
      }),
      post: vi.fn(),
      put: vi.fn(),
      delete: vi.fn(),
      getWithMeta: vi.fn(),
    } as unknown as HttpClient),
  )
})
afterEach(() => {
  wrappers.splice(0).forEach((w) => w.unmount())
  document.body.innerHTML = ''
  vi.unstubAllGlobals()
})
function plugins() {
  const pinia = createPinia()
  setActivePinia(pinia)
  useLocalizationStore().setLocale('en-US')
  return [pinia, ElementPlus]
}
describe('real trace configuration and query controls', () => {
  it('configures the default runtime layout and applies it without changing the source draft', async () => {
    const document = initialTraceDocument()
    const wrapper = mount(TraceInspector, {
      attachTo: globalThis.document.body,
      props: { document, target: null, datasets: [] },
      global: { plugins: plugins() },
    })
    wrappers.push(wrapper)
    await wrapper
      .findAll('[role="tab"]')
      .find((tab) => tab.text() === 'Presentation')!
      .trigger('click')
    await wrapper.get('[id$="-presentation"] .el-select__wrapper').trigger('click')
    await flushPromises()
    const vertical = [...globalThis.document.querySelectorAll('.el-select-dropdown__item')].find(
      (option) => option.textContent === 'Vertical',
    )!
    vertical.dispatchEvent(new MouseEvent('click', { bubbles: true }))
    await flushPromises()
    await wrapper
      .findAll('button')
      .find((button) => button.text() === 'Apply changes')!
      .trigger('click')
    expect(wrapper.emitted('apply')?.[0]?.[0]).toMatchObject({
      type: 'trace.start',
      presentation: { layoutDirection: 'TopToBottom' },
    })
    expect(document.presentation).toBeUndefined()
  })
  it('edits a real BindingEditor input, retains the original document until Apply and emits the typed property command', async () => {
    const doc = initialTraceDocument()
    doc.objects.push(newTraceObject('lot'))
    const original = JSON.stringify(doc)
    const wrapper = mount(TraceInspector, {
      attachTo: document.body,
      props: { document: doc, target: { kind: 'node', id: 'lot' }, datasets: [] },
      global: { plugins: plugins() },
    })
    wrappers.push(wrapper)
    await flushPromises()
    await wrapper.get('input[aria-label="Name"]').setValue('Lot identity')
    const binding = wrapper.findAllComponents(BindingEditor)[0]!
    const pathInput = binding
      .findAllComponents(ElInput)
      .find((input) => input.props('modelValue') === 'sourceSystem')!
    await pathInput.get('input').setValue('source_code')
    expect(JSON.stringify(doc)).toBe(original)
    const apply = wrapper.findAll('button').find((button) => button.text() === 'Apply changes')!
    await apply.trigger('click')
    const command = wrapper.emitted('apply')?.[0]?.[0]
    expect(command).toMatchObject({
      type: 'trace.object',
      object: {
        id: 'lot',
        name: 'Lot identity',
        identity: { sourceSystem: { from: 'Row', path: ['source_code'] } },
      },
    })
    expect(JSON.stringify(doc)).toBe(original)
  })
  it('loads authorized Select options using parameter name and submits the selected value through the actual dropdown', async () => {
    const loader = vi.fn().mockResolvedValue({ items: [{ value: 'F1', label: 'Factory One' }] })
    const wrapper = mount(TraceSearchForm, {
      attachTo: document.body,
      props: {
        modelValue: { keyword: 'part' },
        fields: [{ parameterName: 'factory', label: 'Factory', control: 'Select', required: true }],
        loadOptions: loader,
      },
      global: { plugins: plugins() },
    })
    wrappers.push(wrapper)
    await wrapper.get('.el-select__wrapper').trigger('click')
    await flushPromises()
    expect(loader).toHaveBeenCalledWith('factory', { keyword: 'part' }, expect.any(AbortSignal))
    const option = [...document.querySelectorAll('.el-select-dropdown__item')].find((element) =>
      element.textContent?.includes('Factory One'),
    )!
    expect(option).toBeTruthy()
    option.dispatchEvent(new MouseEvent('click', { bubbles: true }))
    await flushPromises()
    expect(wrapper.emitted('update:modelValue')?.at(-1)?.[0]).toEqual({
      keyword: 'part',
      factory: 'F1',
    })
  })
  it('shows the registered MES scan policy and unavailable Owned file source without offering arbitrary source URLs', async () => {
    const doc = initialTraceDocument(),
      object = newTraceObject('lot')
    object.detail.links.push({
      id: 'file',
      kind: 'File',
      label: 'Report',
      value: { from: 'Row', path: ['attachmentId'] },
      openMode: 'Modal',
      fileSource: { mode: 'Mes', sourceKey: 'mes-attachments' },
    })
    doc.objects.push(object)
    const wrapper = mount(TraceInspector, {
      attachTo: document.body,
      props: { document: doc, target: { kind: 'node', id: 'lot' }, datasets: [] },
      global: { plugins: plugins() },
    })
    wrappers.push(wrapper)
    await flushPromises()
    expect(wrapper.text()).toContain('ImportAndScan')
    expect(wrapper.text()).toContain('application/pdf')
    expect(wrapper.text()).toContain('10485760')
    expect(wrapper.find('input[placeholder="https://"]').exists()).toBe(false)
  })
})
