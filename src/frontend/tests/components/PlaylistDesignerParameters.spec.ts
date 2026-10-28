import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { createMemoryHistory, createRouter, RouterView } from 'vue-router'
import ElementPlus, { ElMessageBox } from 'element-plus'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import PlaylistDesignerPage from '@/pages/pc/studio/dashboard/PlaylistDesignerPage.vue'
import { initialPlaylistDocument, type PlaylistDocument } from '@/pages/pc/studio/dashboard/model'
import type { Json } from '@/api/studio'

const api = vi.hoisted(() => ({ draft: vi.fn(), list: vi.fn(), revision: vi.fn(), save: vi.fn() }))
vi.mock('@/api/studio', () => ({ getStudioApi: () => api }))

let wrapper: VueWrapper
let original: PlaylistDocument
const resource = { nId: 'playlist-1', name: '轮播测试', kind: 'Playlist' }
const pageMapping = (target: string, ...path: string[]) => ({
  target,
  source: { from: 'PageParam', path, nullPolicy: 'Error' },
})

beforeEach(async () => {
  vi.clearAllMocks()
  original = {
    ...initialPlaylistDocument(),
    parameters: ['line', 'line2', 'unused'].map((name) => ({
      name,
      type: 'String',
      required: false,
      nullable: true,
      source: 'Caller',
      defaultValue: '',
    })),
    items: [
      {
        id: 'first',
        dashboardNId: 'dashboard-1',
        revisionNId: 'rev-1',
        staySeconds: 20,
        enabled: true,
        label: '第一项',
        parameterMap: [
          pageMapping('first-line', 'line', 'id'),
          pageMapping('other-line', 'line2', 'line'),
          { target: 'constant', source: { from: 'Constant', value: 'line' } },
        ],
      },
      {
        id: 'second',
        dashboardNId: 'dashboard-1',
        revisionNId: 'rev-1',
        staySeconds: 30,
        enabled: false,
        label: '第二项',
        parameterMap: [pageMapping('second-line', 'line'), pageMapping('retained', 'line2')],
      },
    ],
  }
  api.draft.mockResolvedValue({ resource, draftDocument: structuredClone(original) })
  api.list.mockResolvedValue({ items: [], totalCount: '0' })
  api.revision.mockResolvedValue({ document: { parameters: [] } })
  api.save.mockImplementation(async (_, document) => ({ resource, draftDocument: document }))
  await mountDesigner()
})

async function mountDesigner() {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/design/:nId', component: PlaylistDesignerPage }],
  })
  await router.push('/design/playlist-1')
  wrapper = mount(RouterView, {
    attachTo: document.body,
    global: { plugins: [createPinia(), router, ElementPlus] },
  })
  await flushPromises()
}

afterEach(async () => {
  wrapper.unmount()
  ElMessageBox.close()
  await flushPromises()
  document.body.innerHTML = ''
})

async function removeParameter(index: number) {
  await wrapper.findAll('.playlist-parameter-row')[index]!.get('button').trigger('click')
  await flushPromises()
}
async function confirmRemoval(text: string) {
  const button = [...document.querySelectorAll('.el-message-box button')].find(
    (button) => button.textContent?.trim() === text,
  )
  expect(button).toBeDefined()
  ;(button as HTMLButtonElement).click()
  await flushPromises()
}
async function saveDocument(): Promise<PlaylistDocument> {
  const save = wrapper.findAll('button').find((button) => button.text().trim() === '保存')!
  await save.trigger('click')
  await flushPromises()
  expect(api.save).toHaveBeenCalledTimes(1)
  return JSON.parse(JSON.stringify(api.save.mock.calls[0]![1])) as PlaylistDocument
}
function expectNoDanglingMappings(document: PlaylistDocument) {
  const names = document.parameters.map((parameter) => parameter.name)
  for (const item of document.items) {
    for (const mapping of item.parameterMap as Json[]) {
      if (mapping.source.from === 'PageParam') expect(names).toContain(mapping.source.path[0])
    }
  }
}

describe('playlist shared parameter removal', () => {
  it('removes an unused parameter immediately and saves all playlist items and mappings intact', async () => {
    await removeParameter(2)
    expect(document.querySelector('.el-message-box')).toBeNull()
    expect(wrapper.findAll('.playlist-parameter-row')).toHaveLength(2)
    const saved = await saveDocument()
    expect(saved.parameters.map((parameter) => parameter.name)).toEqual(['line', 'line2'])
    expect(saved.items).toEqual(original.items)
    expectNoDanglingMappings(saved)
  })

  it('confirms before removing a referenced parameter and clears only its first-path PageParam mappings', async () => {
    await removeParameter(0)
    expect(wrapper.findAll('.playlist-parameter-row')).toHaveLength(3)
    await confirmRemoval('移除')
    const saved = await saveDocument()
    expect(saved.parameters.map((parameter) => parameter.name)).toEqual(['line2', 'unused'])
    expect(saved.items).toEqual(
      original.items.map((item) => ({
        ...item,
        parameterMap: item.parameterMap.filter(
          (mapping: Json) =>
            mapping.source.from !== 'PageParam' || mapping.source.path[0] !== 'line',
        ),
      })),
    )
    expectNoDanglingMappings(saved)
  })

  it('keeps the parameter and every mapping when the removal confirmation is cancelled', async () => {
    await removeParameter(0)
    await confirmRemoval('取消')
    expect(wrapper.findAll('.playlist-parameter-row')).toHaveLength(3)
    const saved = await saveDocument()
    expect(saved).toEqual(original)
    expectNoDanglingMappings(saved)
  })
})

describe('playlist designer loading', () => {
  it('shows fresh draft fields and loads pinned parameters before the optional dashboard list completes', async () => {
    wrapper.unmount()
    vi.clearAllMocks()
    let finish!: (value: unknown) => void
    api.list.mockImplementation(
      () =>
        new Promise((resolve) => {
          finish = resolve
        }),
    )
    await mountDesigner()
    expect(wrapper.findAll('.playlist-item')).toHaveLength(2)
    expect(wrapper.text()).toContain('轮播测试')
    expect(api.revision).toHaveBeenCalledTimes(1)
    expect(
      wrapper
        .findAll('button')
        .find((b) => b.text() === '保存')!
        .attributes('disabled'),
    ).toBeUndefined()
    await removeParameter(2)
    finish({ items: [], totalCount: '0' })
    await flushPromises()
    expect(wrapper.findAll('.playlist-parameter-row')).toHaveLength(2)
    expect(api.draft).toHaveBeenCalledTimes(1)
    expect(api.revision).toHaveBeenCalledTimes(1)
  })

  it('deduplicates concurrent parameter reads for the same pinned dashboard revision', async () => {
    wrapper.unmount()
    vi.clearAllMocks()
    const document = JSON.parse(JSON.stringify(original))
    document.items[1].dashboardNId = document.items[0].dashboardNId
    document.items[1].revisionNId = document.items[0].revisionNId
    api.draft.mockResolvedValue({ resource, draftDocument: document })
    let finish!: (value: unknown) => void
    api.revision.mockImplementation(
      () =>
        new Promise((resolve) => {
          finish = resolve
        }),
    )
    await mountDesigner()
    expect(wrapper.findAll('.playlist-item')).toHaveLength(2)
    expect(api.revision).toHaveBeenCalledTimes(1)
    finish({ document: { parameters: [] } })
    await flushPromises()
    expect(
      wrapper
        .findAll('button')
        .find((b) => b.text() === '保存')!
        .attributes('disabled'),
    ).toBeUndefined()
  })

  it('keeps the draft editable after a list failure and retries only the optional list', async () => {
    wrapper.unmount()
    vi.clearAllMocks()
    api.list
      .mockRejectedValueOnce(new Error('看板列表暂不可用'))
      .mockResolvedValue({ items: [], totalCount: '0' })
    await mountDesigner()
    expect(wrapper.findAll('.playlist-item')).toHaveLength(2)
    expect(wrapper.text()).toContain('看板列表暂不可用')
    await wrapper
      .findAll('button')
      .find((b) => b.text() === '刷新')!
      .trigger('click')
    await flushPromises()
    expect(api.list).toHaveBeenCalledTimes(2)
    expect(api.draft).toHaveBeenCalledTimes(1)
    expect(wrapper.text()).not.toContain('看板列表暂不可用')
  })

  it('loads an unpinned target contract once the list supplies its published revision', async () => {
    wrapper.unmount()
    vi.clearAllMocks()
    const document = JSON.parse(JSON.stringify(original))
    document.items[0].revisionNId = ''
    api.draft.mockResolvedValue({ resource, draftDocument: document })
    let finish!: (value: unknown) => void
    api.list.mockImplementation(
      () =>
        new Promise((resolve) => {
          finish = resolve
        }),
    )
    await mountDesigner()
    expect(api.revision).toHaveBeenCalledTimes(1)
    finish({
      items: [
        {
          nId: document.items[0].dashboardNId,
          name: '看板',
          isEnabled: true,
          publishedRevisionNId: 'latest-revision',
        },
      ],
      totalCount: '1',
    })
    await flushPromises()
    expect(api.revision).toHaveBeenCalledWith(document.items[0].dashboardNId, 'latest-revision')
    expect(api.revision).toHaveBeenCalledTimes(2)
  })

  it('does not flash an empty editable playlist before the fresh draft arrives', async () => {
    wrapper.unmount()
    vi.clearAllMocks()
    let finish!: (value: unknown) => void
    api.draft.mockImplementation(
      () =>
        new Promise((resolve) => {
          finish = resolve
        }),
    )
    await mountDesigner()
    expect(wrapper.findAll('.playlist-item')).toHaveLength(0)
    expect(wrapper.text()).not.toContain('添加第一个看板')
    expect(wrapper.get('[role="status"]').text()).toContain('加载中')
    finish({ resource, draftDocument: JSON.parse(JSON.stringify(original)) })
    await flushPromises()
    expect(wrapper.findAll('.playlist-item')).toHaveLength(2)
    expect(wrapper.find('.playlist-initial-loading').exists()).toBe(false)
  })
})
