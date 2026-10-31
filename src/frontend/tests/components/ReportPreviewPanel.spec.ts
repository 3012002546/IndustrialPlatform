import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import ElementPlus from 'element-plus'
import { afterEach, expect, it, vi } from 'vitest'
import ReportPreviewPanel from '@/components/studio/report/ReportPreviewPanel.vue'
import { registerStudioApi, type StudioApi } from '@/api/studio'

afterEach(() => vi.useRealTimers())
it('does not let an old queued response replace a new inline preview with the same id', async () => {
  let resolve!: (value: unknown) => void
  const read = vi.fn(
    () =>
      new Promise((done) => {
        resolve = done
      }),
  )
  registerStudioApi({ reportPreviewState: read, client: {} } as unknown as StudioApi)
  const wrapper = mount(ReportPreviewPanel, {
    props: { previewNId: 'same' },
    global: { plugins: [createPinia(), ElementPlus] },
  })
  try {
    await flushPromises()
    await wrapper.setProps({
      preview: { status: 'Succeeded', pages: [{ pageNo: 1, html: 'New inline page' }] },
    })
    resolve({ status: 'Succeeded', pages: [{ pageNo: 1, html: 'Stale queued page' }] })
    await flushPromises()
    expect(wrapper.get('iframe').attributes('srcdoc')).toContain('New inline page')
    expect(wrapper.get('iframe').attributes('srcdoc')).not.toContain('Stale queued page')
  } finally {
    wrapper.unmount()
  }
})
it('shows directly returned pages without polling or file requests, including after a long design session', async () => {
  vi.useFakeTimers()
  const read = vi.fn(),
    blob = vi.fn()
  registerStudioApi({ reportPreviewState: read, client: { getBlob: blob } } as unknown as StudioApi)
  const wrapper = mount(ReportPreviewPanel, {
    props: {
      preview: {
        status: 'Succeeded',
        pages: [
          { pageNo: 1, html: '<p>Direct first page</p>' },
          { pageNo: 2, html: '<p>Direct second page</p>' },
        ],
      },
    },
    global: { plugins: [createPinia(), ElementPlus] },
  })
  try {
    await flushPromises()
    expect(wrapper.find('iframe').attributes('srcdoc')).toContain('Direct first page')
    expect(wrapper.find('iframe').attributes('sandbox')).toBe('')
    wrapper.findComponent({ name: 'ElSelect' }).vm.$emit('update:modelValue', 2)
    await flushPromises()
    expect(wrapper.find('iframe').attributes('srcdoc')).toContain('Direct second page')
    await vi.advanceTimersByTimeAsync(12 * 60 * 60 * 1000)
    expect(wrapper.find('iframe').attributes('srcdoc')).toContain('Direct second page')
    expect(read).not.toHaveBeenCalled()
    expect(blob).not.toHaveBeenCalled()
  } finally {
    wrapper.unmount()
  }
})
it('clears a failed preview error for a new run and ignores a stale request failure', async () => {
  let rejectOld!: (reason: Error) => void
  const read = vi
    .fn()
    .mockRejectedValueOnce(new Error('Security store unavailable'))
    .mockImplementationOnce(
      () =>
        new Promise((_resolve, reject) => {
          rejectOld = reject
        }),
    )
    .mockResolvedValue({ status: 'Succeeded', pages: [{ pageNo: 1, contentUrl: '/new-page' }] })
  registerStudioApi({
    reportPreviewState: read,
    client: { getBlob: async () => ({ text: async () => '<p>Fresh preview</p>' }) },
  } as unknown as StudioApi)
  const wrapper = mount(ReportPreviewPanel, {
    props: { previewNId: 'failed' },
    global: { plugins: [createPinia(), ElementPlus] },
  })
  try {
    await flushPromises()
    expect(wrapper.text()).toContain('Security store unavailable')
    await wrapper.setProps({ previewNId: 'slow' })
    await flushPromises()
    expect(wrapper.text()).not.toContain('Security store unavailable')
    await wrapper.setProps({ previewNId: 'new' })
    await flushPromises()
    rejectOld(new Error('Old request timed out'))
    await flushPromises()
    expect(wrapper.find('iframe').attributes('srcdoc')).toContain('Fresh preview')
    expect(wrapper.text()).not.toContain('Old request timed out')
  } finally {
    wrapper.unmount()
  }
})
it('keeps an old preview visible through a long design session and clears it on access denial', async () => {
  vi.useFakeTimers()
  const read = vi.fn().mockResolvedValue({
    status: 'Succeeded',
    expiresOn: '2020-01-01T00:00:00Z',
    pages: [{ pageNo: 1, contentUrl: '/page' }],
  })
  registerStudioApi({
    reportPreviewState: read,
    client: { getBlob: async () => ({ text: async () => '<p>Long design session</p>' }) },
  } as unknown as StudioApi)
  const wrapper = mount(ReportPreviewPanel, {
    props: { previewNId: 'preview' },
    global: { plugins: [createPinia(), ElementPlus] },
  })
  try {
    await flushPromises()
    expect(wrapper.find('iframe').attributes('srcdoc')).toContain('Long design session')
    await vi.advanceTimersByTimeAsync(12 * 60 * 60 * 1000)
    expect(wrapper.find('iframe').attributes('srcdoc')).toContain('Long design session')
    read.mockRejectedValue(new Error('Access revoked'))
    await vi.advanceTimersByTimeAsync(30000)
    expect(wrapper.find('iframe').exists()).toBe(false)
    expect(wrapper.text()).toContain('Access revoked')
  } finally {
    wrapper.unmount()
  }
})
