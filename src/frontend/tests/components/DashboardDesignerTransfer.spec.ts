import { mount, flushPromises, type VueWrapper } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import ElementPlus from 'element-plus'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import DashboardDesignerPage from '@/pages/pc/studio/dashboard/DashboardDesignerPage.vue'
import { initialDashboardDocument } from '@/pages/pc/studio/dashboard/model'
import sample from '@/pages/pc/studio/dashboard/samples/semiconductor-photoresist.dashboard.json'
import { downloadDashboardDesign } from '@/components/studio/dashboard/designFile'

vi.mock('@/components/studio/dashboard/designFile', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/components/studio/dashboard/designFile')>()),
  downloadDashboardDesign: vi.fn(),
}))

const api = vi.hoisted(() => ({
  draft: vi.fn(),
  registry: vi.fn(),
  capabilities: vi.fn(),
  list: vi.fn(),
  validate: vi.fn(),
}))
vi.mock('@/api/studio', () => ({ getStudioApi: () => api }))
vi.mock('@/stores/authStore', () => ({ useAuthStore: () => ({ hasPermission: () => true }) }))
let wrapper: VueWrapper
const resource = {
  nId: 'current-resource',
  name: '原看板',
  kind: 'Dashboard',
  optimisticVersion: '1',
  concurrencyVersion: '1',
}
async function click(text: string) {
  const button = [...document.querySelectorAll('button')].find(
    (button) => button.textContent?.trim() === text,
  )
  expect(button, text).toBeDefined()
  button!.click()
  await flushPromises()
}
async function choose(text: string) {
  const input = document.querySelector('[data-testid="dashboard-design-file"]') as HTMLInputElement
  const file = new File([text], 'test.dashboard.json', { type: 'application/json' })
  Object.defineProperty(file, 'text', { value: async () => text })
  Object.defineProperty(input, 'files', { value: [file], configurable: true })
  input.dispatchEvent(new Event('change', { bubbles: true }))
  await flushPromises()
}
const componentCount = () => document.querySelectorAll('.dashboard-editor-component').length

beforeEach(async () => {
  vi.clearAllMocks()
  api.draft.mockResolvedValue({ resource, draftDocument: initialDashboardDocument() })
  api.registry.mockResolvedValue({ components: [] })
  api.capabilities.mockResolvedValue({
    modules: [{ key: 'Dashboard', installed: true, enabled: true }],
  })
  api.list.mockResolvedValue({ items: [], totalCount: '0' })
  api.validate.mockResolvedValue({ valid: true, diagnostics: [] })
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/design/:nId', component: DashboardDesignerPage }],
  })
  await router.push('/design/current-resource')
  wrapper = mount(DashboardDesignerPage, {
    attachTo: document.body,
    global: { plugins: [createPinia(), router, ElementPlus] },
  })
  await flushPromises()
})
afterEach(() => {
  wrapper.unmount()
  document.body.innerHTML = ''
})

describe('dashboard design import surface', () => {
  it('edits structured sample data without changing numeric types and exports the current unsaved design', async () => {
    await click('导入设计')
    await choose(JSON.stringify(sample))
    await click('应用到当前草稿')
    await click('在制批次（合成演示数据）')
    await click('数据绑定')
    await click('数据来源')
    const rowsEditor = wrapper.get('.dashboard-structured-editor[data-kind="rows"]')
    await rowsEditor.get('input[value="json"]').setValue(true)
    const value = rowsEditor.get('textarea')
    await value.setValue('[{"batch":"CHANGED","volume":0}]')
    await click('导出设计')
    const exported = vi.mocked(downloadDashboardDesign).mock.calls.at(-1)![1]
    expect(
      exported.components.find((component) => component.id === 'batch-table')?.bindings[0]?.source
        .value,
    ).toEqual([{ batch: 'CHANGED', volume: 0 }])
    expect(api.draft).toHaveBeenCalledTimes(2)
  })
  it('keeps the original canvas when parsing or authoritative server validation fails', async () => {
    await click('导入设计')
    await choose('{bad')
    expect(document.body.textContent).toContain('不是有效的看板设计')
    expect(componentCount()).toBe(2)
    expect(api.validate).not.toHaveBeenCalled()
    await choose(JSON.stringify(sample))
    api.validate.mockResolvedValueOnce({
      valid: false,
      diagnostics: [{ path: 'instances.shared', message: '目标数据集不可用' }],
    })
    await click('应用到当前草稿')
    expect(document.body.textContent).toContain('目标数据集不可用')
    expect(componentCount()).toBe(2)
  })

  it('imports the actual file into the existing resource as one undoable draft change', async () => {
    await click('导入设计')
    await choose(JSON.stringify(sample))
    expect(document.body.textContent).toContain('24 个组件')
    await click('应用到当前草稿')
    expect(api.validate).toHaveBeenCalledWith('current-resource', sample.document)
    expect(componentCount()).toBe(24)
    expect(wrapper.text()).toContain('原看板')
    await click('撤销')
    expect(componentCount()).toBe(2)
    await click('恢复')
    expect(componentCount()).toBe(24)
  })
})
