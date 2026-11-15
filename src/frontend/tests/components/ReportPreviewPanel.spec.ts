import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import ElementPlus from 'element-plus'
import { afterEach, expect, it, vi } from 'vitest'
import { inspectPage } from '@/components/studio/report/previewPhysicalPage'
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
    pages: [
      { pageNo: 1, contentUrl: '/page' },
      { pageNo: 2, contentUrl: '/other' },
    ],
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
    expect(wrapper.get('button[aria-label="下一页"]').attributes('disabled')).toBeDefined()
    wrapper.findComponent({ name: 'ElSelect' }).vm.$emit('update:modelValue', 2)
    await flushPromises()
    expect(wrapper.find('iframe').exists()).toBe(false)
    expect(wrapper.text()).toContain('Access revoked')
  } finally {
    wrapper.unmount()
  }
})

it('keeps matches within the selected physical page even when both child reports restart at 1', async () => {
  const pageHtml = (size: string, body: string) =>
    `<section class="pagedjs_page" data-page-number="1" style="${size}"><footer>1 / 1</footer>${body}</section>`
  const portrait = pageHtml('--pagedjs-width:210mm;--pagedjs-height:297mm', '<p>shared</p>')
  const landscape = pageHtml(
    '--pagedjs-width:297mm;--pagedjs-height:210mm',
    '<p>sh<b>ared</b></p><p>shared</p><table><tr><td>sh</td><td>ared</td></tr></table>',
  )
  const createUrl = vi.fn(() => 'blob:report-test')
  const revoke = vi.fn()
  vi.stubGlobal(
    'URL',
    class extends URL {
      static createObjectURL = createUrl
      static revokeObjectURL = revoke
    },
  )
  registerStudioApi({ client: {} } as unknown as StudioApi)
  const wrapper = mount(ReportPreviewPanel, {
    props: {
      preview: {
        status: 'Succeeded',
        pages: [
          { pageNo: 1, html: portrait },
          { pageNo: 2, html: landscape },
        ],
      },
    },
    global: { plugins: [createPinia(), ElementPlus] },
  })
  try {
    await wrapper.get('button[aria-label="在当前页查找"]').trigger('click')
    await wrapper.get('input[placeholder="在当前页查找"]').setValue('shared')
    expect(wrapper.get('[role="status"]').text()).toBe('1 / 1')
    wrapper.findAllComponents({ name: 'ElSelect' })[0]!.vm.$emit('update:modelValue', 2)
    await flushPromises()
    expect(wrapper.get('[role="status"]').text()).toBe('1 / 2')
    const next = wrapper.findAll('button').find((b) => b.text() === '下一处')!
    await next.trigger('click')
    expect(wrapper.get('[role="status"]').text()).toBe('2 / 2')
    expect(wrapper.get('iframe').attributes('sandbox')).toBe('')
    expect(wrapper.get('iframe').attributes('src')).toMatch(/-1$/)
    expect(inspectPage(landscape, 'shared')).toMatchObject({ width: '297mm', height: '210mm' })
  } finally {
    wrapper.unmount()
    vi.unstubAllGlobals()
  }
  expect(revoke).toHaveBeenCalledTimes(createUrl.mock.calls.length)
})

it('navigates a long document while mounting only the visible sheets', async () => {
  registerStudioApi({ client: {} } as unknown as StudioApi)
  const pages = Array.from({ length: 83 }, (_, i) => ({
    pageNo: i + 1,
    html: '<p>Sheet ' + (i + 1) + '</p>',
  }))
  const wrapper = mount(ReportPreviewPanel, {
    props: { preview: { status: 'Succeeded', pages } },
    global: { plugins: [createPinia(), ElementPlus] },
  })
  const click = async (name: string) => {
    await wrapper.get('button[aria-label="' + name + '"]').trigger('click')
    await flushPromises()
  }
  try {
    await flushPromises()
    expect(wrapper.findAll('iframe')).toHaveLength(1)
    await click('下一页')
    expect(wrapper.get('iframe').attributes('srcdoc')).toContain('Sheet 2')
    await click('连续全部页')
    await click('末页')
    expect(wrapper.get('[data-physical-page="83"] iframe').attributes('srcdoc')).toContain(
      'Sheet 83',
    )
    expect(wrapper.findAll('iframe').length).toBeLessThan(5)
    await click('首页')
    expect(wrapper.get('button[aria-label="上一页"]').attributes('disabled')).toBeDefined()
  } finally {
    wrapper.unmount()
  }
})

it('shows renderer diagnostics in the preview and clears them for a new report', async () => {
  registerStudioApi({ client: {} } as unknown as StudioApi)
  const wrapper = mount(ReportPreviewPanel, {
    props: {
      preview: {
        status: 'Succeeded',
        pages: [{ pageNo: 1, html: '<p>Original report</p>' }],
        diagnostics: [
          {
            code: 'REPORT_FONT_SUBSTITUTED',
            requested: 'Missing font, sans-serif',
            unavailable: ['Missing font'],
            fallback: 'sans-serif',
          },
          { code: 'LAYOUT_WARNING', message: '<img src=x onerror=alert(1)> Layout warning' },
        ],
      },
    },
    global: { plugins: [createPinia(), ElementPlus] },
  })
  try {
    await flushPromises()
    expect(wrapper.get('.preview-diagnostics summary').text()).toContain('预览诊断')
    expect(wrapper.get('.preview-diagnostics').text()).toContain('字体不可用：Missing font')
    expect(wrapper.get('.preview-diagnostics').text()).toContain('已替换为：sans-serif')
    expect(wrapper.find('.preview-diagnostics img').exists()).toBe(false)
    expect(wrapper.get('iframe').attributes('sandbox')).toBe('')
    await wrapper.setProps({
      preview: {
        status: 'Succeeded',
        pages: [{ pageNo: 1, html: '<p>New report</p>' }],
        diagnostics: [],
      },
    })
    await flushPromises()
    expect(wrapper.find('.preview-diagnostics').exists()).toBe(false)
    expect(wrapper.get('iframe').attributes('srcdoc')).toContain('New report')
  } finally {
    wrapper.unmount()
  }
})
