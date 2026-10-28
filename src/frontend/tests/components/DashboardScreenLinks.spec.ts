import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { createPinia } from 'pinia'
import ElementPlus, { ElSelect } from 'element-plus'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import DashboardScreenLinks from '@/components/studio/dashboard/DashboardScreenLinks.vue'

const api = vi.hoisted(() => ({ list: vi.fn(), create: vi.fn(), revoke: vi.fn() }))
vi.mock('@/api/dashboardScreen', async (original) => ({
  ...(await original<typeof import('@/api/dashboardScreen')>()),
  listDashboardScreens: api.list,
  createDashboardScreen: api.create,
  revokeDashboardScreen: api.revoke,
}))
let wrapper: VueWrapper | undefined
beforeEach(() => {
  api.list.mockReset().mockResolvedValue([
    {
      id: 'permanent',
      revisionNId: 'rev-1',
      createdOn: '2026-10-27T00:00:00Z',
      expiresOn: null,
      revokedOn: null,
      followPublished: true,
      key: 'v1.' + 'b'.repeat(100),
    },
  ])
  api.create.mockReset().mockResolvedValue({
    id: 'new-link',
    key: 'a'.repeat(64),
    expiresOn: null,
    followPublished: true,
  })
  vi.stubGlobal(
    'ResizeObserver',
    class {
      observe() {}
      unobserve() {}
      disconnect() {}
    },
  )
})
afterEach(() => {
  wrapper?.unmount()
  wrapper = undefined
  document.body.innerHTML = ''
  vi.unstubAllGlobals()
})
async function open(kind: 'Dashboard' | 'Playlist' = 'Dashboard') {
  wrapper = mount(DashboardScreenLinks, {
    attachTo: document.body,
    props: {
      nId: 'dashboard-1',
      revisionNId: 'rev-1',
      parameters: { lineId: 'L01' },
      modelValue: false,
      resourceKind: kind,
    },
    global: { plugins: [createPinia(), ElementPlus] },
  })
  await wrapper.setProps({ modelValue: true })
  await flushPromises()
  return wrapper
}
async function create() {
  const button = [...document.querySelectorAll('button')].find(
    (button) => button.textContent?.trim() === '生成链接',
  )!
  button.click()
  await flushPromises()
}
describe('dashboard screen link policy', () => {
  it('defaults to unlimited and following the latest publication, with an honest refresh explanation', async () => {
    const view = await open()
    const selects = view.findAllComponents(ElSelect)
    expect(selects[0]!.props('modelValue')).toBe(0)
    expect(selects[1]!.props('modelValue')).toBe(true)
    expect(document.body.textContent).toContain('无限期')
    expect(document.body.textContent).toContain('保存草稿不影响大屏')
    expect(document.body.textContent).toContain('30秒')
    expect(document.body.textContent).not.toContain('1970')
    await create()
    expect(api.create).toHaveBeenCalledWith(
      'dashboard-1',
      'rev-1',
      { lineId: 'L01' },
      null,
      true,
      'Dashboard',
    )
  })
  it('still allows finite expiry and a pinned publication', async () => {
    const view = await open()
    const selects = view.findAllComponents(ElSelect)
    selects[0]!.vm.$emit('update:modelValue', 168)
    selects[1]!.vm.$emit('update:modelValue', false)
    await flushPromises()
    await create()
    expect(api.create).toHaveBeenCalledWith(
      'dashboard-1',
      'rev-1',
      { lineId: 'L01' },
      168,
      false,
      'Dashboard',
    )
    expect(document.body.textContent).toContain('后续发布调整不会影响它')
  })
  it('uses the same create, list and copy surface for playlist links', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined)
    vi.stubGlobal('navigator', { clipboard: { writeText } })
    await open('Playlist')
    expect(api.list).toHaveBeenCalledWith('dashboard-1', 'Playlist')
    await create()
    expect(api.create).toHaveBeenCalledWith(
      'dashboard-1',
      'rev-1',
      { lineId: 'L01' },
      null,
      true,
      'Playlist',
    )
    const copy = [...document.querySelectorAll('button')].find(
      (button) => button.textContent?.trim() === '复制',
    )!
    copy.click()
    await flushPromises()
    expect(writeText).toHaveBeenCalledWith(
      expect.stringContaining('/screen/playlists/new-link#key='),
    )
  })
  it('copies existing links after reopening and keeps revocations in history only', async () => {
    const existing = (await api.list())[0]
    api.list.mockResolvedValue([
      existing,
      { ...existing, id: 'revoked', revokedOn: '2026-10-27T02:00:00Z', key: null },
    ])
    const writeText = vi.fn().mockResolvedValue(undefined)
    vi.stubGlobal('navigator', { clipboard: { writeText } })
    const view = await open()
    expect(document.body.textContent).not.toContain('已撤销')
    const copy = [...document.querySelectorAll('button')].find(
      (button) => button.textContent?.trim() === '复制',
    )!
    copy.click()
    await flushPromises()
    expect(writeText).toHaveBeenCalledWith(
      expect.stringContaining('/screen/dashboards/permanent#key=v1.'),
    )
    await view.setProps({ modelValue: false })
    await view.setProps({ modelValue: true })
    await flushPromises()
    expect(
      [...document.querySelectorAll('button')].some(
        (button) => button.textContent?.trim() === '复制',
      ),
    ).toBe(true)
    const history = [...document.querySelectorAll('input[type="radio"]')].find(
      (input) => (input as HTMLInputElement).value === 'true',
    ) as HTMLInputElement
    history.click()
    await flushPromises()
    expect(document.body.textContent).toContain('已撤销')
    expect(document.body.textContent).toContain('撤销时间')
    expect(
      [...document.querySelectorAll('button')].some(
        (button) => button.textContent?.trim() === '复制',
      ),
    ).toBe(false)
    expect(
      [...document.querySelectorAll('button')].some(
        (button) => button.textContent?.trim() === '撤销',
      ),
    ).toBe(false)
  })
})
