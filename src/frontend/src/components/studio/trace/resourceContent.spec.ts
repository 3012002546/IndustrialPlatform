import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { createPinia } from 'pinia'
import TraceResourceContent from './TraceResourceContent.vue'

const state = vi.hoisted(() => ({ get: vi.fn() }))
vi.mock('@/api/studio', () => ({ getStudioApi: () => ({ client: { get: state.get } }) }))
vi.mock('@/pages/pc/studio/report/ReportRuntimePage.vue', () => ({
  __esModule: true,
  default: {
    name: 'ReportContent',
    props: {
      resourceNId: String,
      revisionNId: String,
      initialParameters: Object,
      embedded: Boolean,
    },
    template:
      '<article>{{ resourceNId }} / {{ revisionNId }} / {{ initialParameters.recordKey }}</article>',
  },
}))
let wrapper: VueWrapper
const link = () => ({
  kind: 'Resource',
  modalUrl: '/screen/trace-resource?nId=trace&revisionNId=model-v1&ticket=signed',
  expiresOn: new Date(Date.now() + 300000).toISOString(),
  embeddable: true,
})
beforeEach(() => {
  vi.useFakeTimers()
  state.get
    .mockReset()
    .mockResolvedValue({
      kind: 'Report',
      resourceNId: 'inspection',
      revisionNId: 'fixed-v2',
      parameters: { recordKey: 'row-3' },
    })
})
afterEach(() => {
  wrapper?.unmount()
  vi.useRealTimers()
})
function render() {
  wrapper = mount(TraceResourceContent, {
    props: { link: link() },
    global: {
      plugins: [createPinia()],
      stubs: { ElAlert: { props: ['title'], template: '<p>{{ title }}</p>' } },
    },
  })
}
it('passes the authorized fixed revision and typed row parameters to existing report content', async () => {
  render()
  await flushPromises()
  expect(state.get).toHaveBeenCalledWith(
    '/api/v1/studio/trace/trace/resource-content?revisionNId=model-v1&ticket=signed',
    expect.objectContaining({ signal: expect.any(AbortSignal) }),
  )
  expect(wrapper.text()).toContain('inspection / fixed-v2 / row-3')
  expect(wrapper.findComponent({ name: 'ReportContent' }).props('embedded')).toBe(true)
  expect(wrapper.find('iframe').exists()).toBe(false)
  state.get.mockRejectedValueOnce(new Error('Permission revoked'))
  await vi.advanceTimersByTimeAsync(30000)
  await flushPromises()
  expect(wrapper.text()).toContain('Permission revoked')
  expect(wrapper.find('article').exists()).toBe(false)
})
it('does not let a late authorized target replace a newer failed link', async () => {
  let resolve!: (value: unknown) => void
  state.get.mockImplementationOnce(
    () =>
      new Promise((done) => {
        resolve = done
      }),
  )
  render()
  await flushPromises()
  state.get.mockRejectedValueOnce(new Error('New target denied'))
  await wrapper.setProps({
    link: {
      ...link(),
      modalUrl: '/screen/trace-resource?nId=trace&revisionNId=model-v1&ticket=new',
    },
  })
  await flushPromises()
  resolve({ kind: 'Report', resourceNId: 'old', revisionNId: 'v1', parameters: {} })
  await flushPromises()
  expect(wrapper.text()).toContain('New target denied')
  expect(wrapper.find('article').exists()).toBe(false)
})
