import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import ElementPlus from 'element-plus'
import { defineComponent, ref } from 'vue'
import type { TraceSectionResult } from '@/api/studioTrace'
import { useLocalizationStore } from '@/stores/localizationStore'
import TraceDetailSection from './TraceDetailSection.vue'
import TraceSectionsEditor from './TraceSectionsEditor.vue'
import type { TraceDetailSection as Section } from './model'

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
})
afterEach(() => {
  wrappers.splice(0).forEach((wrapper) => wrapper.unmount())
  document.body.innerHTML = ''
  vi.unstubAllGlobals()
})
function plugins() {
  const pinia = createPinia()
  setActivePinia(pinia)
  useLocalizationStore().setLocale('en-US')
  return [pinia, ElementPlus]
}
function page(number = 1): TraceSectionResult {
  return {
    sectionId: 'checks',
    kind: 'Table',
    fields: [],
    columns: [{ fieldId: 'result.value', label: 'Result', width: 160, visible: true }],
    items: [{ key: `check-${number}`, values: { 'result.value': `Page ${number}` } }],
    totalCount: '21',
    page: number,
    pageSize: 20,
    observedOn: null,
  }
}
describe('configurable node detail sections', () => {
  it('edits sections using the real form controls without business-specific names', async () => {
    const Host = defineComponent({
      components: { TraceSectionsEditor },
      setup() {
        return { sections: ref<Section[]>([]) }
      },
      template: '<TraceSectionsEditor v-model="sections" :datasets="[]" />',
    })
    const wrapper = mount(Host, { attachTo: document.body, global: { plugins: plugins() } })
    wrappers.push(wrapper)
    await wrapper
      .findAll('button')
      .find((button) => button.text() === 'Add detail section')!
      .trigger('click')
    await flushPromises()
    await wrapper.get('input[aria-label="Section name"]').setValue('Measurements')
    const section = (wrapper.vm as unknown as { sections: Section[] }).sections[0]!
    expect(section.label).toBe('Measurements')
    expect(section.kind).toBe('Table')
    expect(section.rowKey).toMatchObject({ from: 'Row', path: ['id'] })
    expect(wrapper.text()).toContain('Offset paging')
    await wrapper.get('.el-select__wrapper').trigger('click')
    await flushPromises()
    const fields = [...document.querySelectorAll('.el-select-dropdown__item')].find(
      (option) => option.textContent === 'Fields',
    )!
    fields.dispatchEvent(new MouseEvent('click', { bubbles: true }))
    await flushPromises()
    expect(section.kind).toBe('Fields')
    expect(section.rowKey).toBeUndefined()
    await wrapper.get('.trace-section-actions button').trigger('click')
    expect((wrapper.vm as unknown as { sections: Section[] }).sections).toEqual([])
  })
  it('renders the shared table and sends a real pager click to the section loader', async () => {
    const load = vi.fn(async (_id: string, number: number) => page(number))
    const wrapper = mount(TraceDetailSection, {
      attachTo: document.body,
      props: {
        section: { id: 'checks', label: 'Measurements', kind: 'Table', pageSize: 20 },
        tableKey: 'trace-section-real-pager',
        load,
      },
      global: { plugins: plugins() },
    })
    wrappers.push(wrapper)
    await flushPromises()
    expect(load).toHaveBeenCalledWith('checks', 1, 20, expect.any(AbortSignal))
    expect(wrapper.text()).toContain('Page 1')
    expect(wrapper.text()).toContain('Result')
    await wrapper.get('.el-pagination .btn-next').trigger('click')
    await flushPromises()
    expect(load).toHaveBeenLastCalledWith('checks', 2, 20, expect.any(AbortSignal))
    expect(wrapper.text()).toContain('Page 2')
    expect(wrapper.text()).not.toContain('Page 1')
  })
  it('cancels a pending section when it is closed and shows field-only data without a table', async () => {
    let resolve!: (value: TraceSectionResult) => void
    const load = vi
      .fn<
        (
          id: string,
          page: number,
          size: number,
          signal?: AbortSignal,
        ) => Promise<TraceSectionResult>
      >()
      .mockImplementation(
        () =>
          new Promise<TraceSectionResult>((done) => {
            resolve = done
          }),
      )
    const wrapper = mount(TraceDetailSection, {
      props: {
        section: { id: 'checks', label: 'Measurements', kind: 'Fields', pageSize: 20 },
        tableKey: 'trace-field-only',
        load,
      },
      global: { plugins: plugins() },
    })
    wrappers.push(wrapper)
    await flushPromises()
    resolve({ ...page(), kind: 'Fields', items: [], fields: [{ label: 'Reading', value: '12.3' }] })
    await flushPromises()
    expect(wrapper.text()).toContain('12.3')
    expect(wrapper.find('.app-data-table').exists()).toBe(false)
    wrapper.unmount()
    expect(load.mock.calls[0]![3]?.aborted).toBe(true)
  })
  it('reports counts beyond safe table pagination instead of rounding them', async () => {
    const wrapper = mount(TraceDetailSection, {
      props: {
        section: { id: 'checks', label: 'Measurements', kind: 'Table', pageSize: 20 },
        tableKey: 'trace-section-count-limit',
        load: async () => ({ ...page(), totalCount: '9007199254740993' }),
      },
      global: { plugins: plugins() },
    })
    wrappers.push(wrapper)
    await flushPromises()
    expect(wrapper.text()).toContain('exceeds the paging limit')
    expect(wrapper.find('.app-data-table').exists()).toBe(false)
  })
})
