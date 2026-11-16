import { mount, flushPromises } from '@vue/test-utils'
import { createPinia } from 'pinia'
import ElementPlus from 'element-plus'
import { expect, it, vi } from 'vitest'
import ReportPreviewExport from '@/components/studio/report/ReportPreviewExport.vue'
import ReportFormatSelect from '@/components/studio/report/ReportFormatSelect.vue'
const state = vi.hoisted(() => ({ export: vi.fn(), download: vi.fn() }))
vi.mock('@/api/studio', () => ({ getStudioApi: () => ({ reportPreviewExport: state.export }) }))
vi.mock('@/permissions', () => ({ PermissionGate: { template: '<div><slot /></div>' } }))
vi.mock('@/components/management/download', () => ({ downloadBlob: state.download }))
function render(
  input = { kind: 'Report', document: { value: 'draft' }, parameters: { order: '001' } },
) {
  return mount(ReportPreviewExport, {
    props: { input },
    global: { plugins: [createPinia(), ElementPlus] },
  })
}
it('offers all formats and downloads the captured input once, with correct draft zip naming', async () => {
  state.export.mockReset()
  state.download.mockReset()
  let resolve!: (value: Blob) => void
  state.export.mockImplementation(
    () =>
      new Promise<Blob>((done) => {
        resolve = done
      }),
  )
  const page = render()
  try {
    expect(
      page.findComponent(ReportFormatSelect).findAllComponents({ name: 'ElOption' }),
    ).toHaveLength(11)
    page.findComponent(ReportFormatSelect).vm.$emit('update:modelValue', 'Png')
    await flushPromises()
    const button = page.get('button[aria-label="下载草稿"]')
    await button.trigger('click')
    await button.trigger('click')
    expect(state.export).toHaveBeenCalledTimes(1)
    expect(state.export.mock.calls[0]?.slice(0, 2)).toEqual([page.props('input'), 'Png'])
    const blob = new Blob(['zip'])
    resolve(blob)
    await flushPromises()
    expect(state.download).toHaveBeenCalledWith(blob, 'preview-png.zip')
  } finally {
    page.unmount()
  }
})
it('aborts and discards a late download when a new preview replaces it or closes', async () => {
  state.export.mockReset()
  state.download.mockReset()
  let resolve!: (value: Blob) => void
  state.export.mockImplementation(
    () =>
      new Promise<Blob>((done) => {
        resolve = done
      }),
  )
  const page = render()
  await page.get('button[aria-label="下载草稿"]').trigger('click')
  const signal = state.export.mock.calls[0]?.[2] as AbortSignal
  await page.setProps({ input: { kind: 'Report', document: { value: 'new' }, parameters: {} } })
  expect(signal.aborted).toBe(true)
  resolve(new Blob(['old']))
  await flushPromises()
  expect(state.download).not.toHaveBeenCalled()
  await page.get('button[aria-label="下载草稿"]').trigger('click')
  const second = state.export.mock.calls[1]?.[2] as AbortSignal
  page.unmount()
  expect(second.aborted).toBe(true)
  resolve(new Blob(['closed']))
  await flushPromises()
  expect(state.download).not.toHaveBeenCalled()
})
it('shows the export error and allows an explicit retry without replacing preview content', async () => {
  state.export.mockReset()
  state.download.mockReset()
  state.export
    .mockRejectedValueOnce(new Error('REPORT_XLSX_GRAPHIC_UNSUPPORTED'))
    .mockResolvedValue(new Blob(['ok']))
  const page = render()
  try {
    await page.get('button[aria-label="下载草稿"]').trigger('click')
    await flushPromises()
    expect(page.get('[role="alert"]').text()).toContain('REPORT_XLSX_GRAPHIC_UNSUPPORTED')
    await page.get('button[aria-label="下载草稿"]').trigger('click')
    await flushPromises()
    expect(page.find('[role="alert"]').exists()).toBe(false)
    expect(state.download).toHaveBeenCalledOnce()
  } finally {
    page.unmount()
  }
})
