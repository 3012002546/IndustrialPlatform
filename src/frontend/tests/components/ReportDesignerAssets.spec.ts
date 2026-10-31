import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import ElementPlus from 'element-plus'
import { expect, it, vi } from 'vitest'
import ReportDesignerPage from '@/pages/pc/studio/report/ReportDesignerPage.vue'
import { initialReportDocument } from '@/components/studio/report/model'

const state = vi.hoisted(() => ({
  api: {
    draft: vi.fn(),
    list: vi.fn(),
    reportDraftContract: vi.fn(),
    reportAsset: vi.fn(),
    save: vi.fn(),
  },
}))
vi.mock('@/api/studio', () => ({ getStudioApi: () => state.api }))
vi.mock('vue-router', () => ({
  useRoute: () => ({ params: { nId: 'report' }, query: {} }),
  useRouter: () => ({}),
  onBeforeRouteLeave: vi.fn(),
}))
vi.mock('@/permissions', () => ({ PermissionGate: { template: '<div><slot /></div>' } }))
vi.mock('@/components/studio/shared/StudioFileField.vue', () => ({
  default: {
    template: `<button class="choose-image" @click="$emit('update:modelValue', { fileNId: 'image-file' })">Choose image</button>`,
    emits: ['update:modelValue'],
  },
}))

it('keeps attaching an image and its binding in one undoable edit', async () => {
  vi.stubGlobal(
    'ResizeObserver',
    class {
      observe() {}
      disconnect() {}
    },
  )
  const document = initialReportDocument()
  document.bands[0].controls = [
    {
      id: 'picture',
      type: 'Image',
      xMm: 0,
      yMm: 0,
      widthMm: 50,
      heightMm: 5,
      value: { from: 'Constant', value: '', nullPolicy: 'Error' },
    },
  ]
  const resource = {
    nId: 'report',
    kind: 'Report',
    name: 'Fixture',
    optimisticVersion: '1',
    concurrencyVersion: 'fixture',
  }
  state.api.draft.mockResolvedValue({ resource, draftDocument: document })
  state.api.list.mockResolvedValue({ items: [] })
  state.api.reportDraftContract.mockResolvedValue({})
  state.api.reportAsset.mockResolvedValue({ id: 'attached-image', referenceNId: 'image-reference' })
  state.api.save.mockImplementation(async (_resource, draft) => ({
    resource,
    draftDocument: structuredClone(JSON.parse(JSON.stringify(draft))),
  }))
  const page = mount(ReportDesignerPage, {
    global: {
      plugins: [createPinia(), ElementPlus],
      stubs: { AppPage: { template: '<main><slot name="actions"/><slot/></main>' } },
    },
  })
  const click = async (label: string) => {
    await page
      .findAll('button')
      .find((button) => button.text() === label)!
      .trigger('click')
    await flushPromises()
  }
  try {
    await flushPromises()
    await page.get('.report-control').trigger('click')
    await page
      .findAll('[role="tab"]')
      .find((tab) => tab.text() === '内容与绑定')!
      .trigger('click')
    await flushPromises()
    await page.get('.choose-image').trigger('click')
    await click('附加图片（扫描未完成可重试）')
    await click('撤销')
    await click('恢复')
    await click('保存草稿')
    expect(state.api.save.mock.lastCall![1].bands[0].controls[0].value.value).toBe('attached-image')
    expect(state.api.save.mock.lastCall![1].assets).toHaveLength(1)
  } finally {
    page.unmount()
    vi.unstubAllGlobals()
  }
})
