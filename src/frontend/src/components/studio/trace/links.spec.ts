import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { createStudioApi, registerStudioApi } from '@/api/studio'
import type { HttpClient } from '@/api/httpClient'
import TraceLinkModal from './TraceLinkModal.vue'
import type { TraceModalLink } from '@/api/studioTrace'
import TraceLinkPreviewPage from '@/pages/pc/studio/trace/TraceLinkPreviewPage.vue'
import { createMemoryHistory, createRouter } from 'vue-router'
import { studioScreenRoutes } from '@/router/studioRoutes'
const wrappers: VueWrapper[] = []
const originalCreate = URL.createObjectURL,
  originalRevoke = URL.revokeObjectURL
beforeEach(() => {
  URL.createObjectURL = vi.fn(() => 'blob:authorized-file')
  URL.revokeObjectURL = vi.fn()
})
afterEach(() => {
  wrappers.splice(0).forEach((w) => w.unmount())
  URL.createObjectURL = originalCreate
  URL.revokeObjectURL = originalRevoke
  vi.useRealTimers()
  vi.restoreAllMocks()
})
function setup(link: TraceModalLink) {
  const getBlob = vi
    .fn<(path: string, options?: { signal?: AbortSignal }) => Promise<Blob>>()
    .mockResolvedValue(new Blob(['file'], { type: 'application/pdf' }))
  registerStudioApi(createStudioApi({ getBlob } as unknown as HttpClient))
  const wrapper = mount(TraceLinkModal, {
    props: { link },
    global: {
      plugins: [createPinia()],
      stubs: {
        AppFormDrawer: { template: '<section><slot name="header-actions" /><slot /></section>' },
        ElAlert: { props: ['title'], template: '<p>{{ title }}</p>' },
        ElButton: {
          props: ['disabled'],
          template: '<button :disabled="disabled"><slot /></button>',
        },
      },
    },
  })
  wrappers.push(wrapper)
  return { wrapper, getBlob }
}
const fileLink = (): TraceModalLink => ({
  kind: 'File',
  modalUrl: '/api/v1/studio/trace/model/link-content?ticket=opaque',
  newTabUrl: '/screen/trace-link?nId=model&ticket=opaque',
  embeddable: true,
  expiresOn: new Date(Date.now() + 30000).toISOString(),
})
describe('trace current-page link ownership', () => {
  it('retrieves a File with the authenticated platform client, preserves MIME and revokes its object URL on close', async () => {
    const { wrapper, getBlob } = setup(fileLink())
    await flushPromises()
    expect(getBlob).toHaveBeenCalledWith(
      '/api/v1/studio/trace/model/link-content?ticket=opaque',
      expect.objectContaining({ signal: expect.any(AbortSignal) }),
    )
    expect(wrapper.get('iframe').attributes('src')).toBe('blob:authorized-file')
    expect(wrapper.get('iframe').attributes('sandbox')).toBe('')
    const blob = vi.mocked(URL.createObjectURL).mock.calls[0]![0] as Blob
    expect(blob.type).toBe('application/pdf')
    await wrapper.setProps({ link: null })
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:authorized-file')
    expect(wrapper.find('iframe').exists()).toBe(false)
  })
  it('does not fetch cross-origin File URLs or execute javascript URLs', async () => {
    const { wrapper, getBlob } = setup({
      ...fileLink(),
      modalUrl: 'https://unregistered.invalid/file',
    })
    await flushPromises()
    expect(getBlob).not.toHaveBeenCalled()
    expect(wrapper.find('iframe').exists()).toBe(false)
    await wrapper.setProps({
      link: { ...fileLink(), kind: 'External', modalUrl: 'javascript:alert(1)' },
    })
    expect(wrapper.find('iframe').exists()).toBe(false)
  })
  it('keeps a registered External URL in the sandbox and offers an explicit browser fallback even when embedding is denied', async () => {
    const open = vi.spyOn(window, 'open').mockReturnValue(null)
    const link = {
      ...fileLink(),
      kind: 'External',
      modalUrl: 'https://registered-mes.invalid/detail?id=encoded',
    }
    const { wrapper, getBlob } = setup(link)
    await flushPromises()
    expect(wrapper.get('iframe').attributes('src')).toBe(link.modalUrl)
    expect(wrapper.get('iframe').attributes('sandbox')).not.toContain('allow-popups')
    expect(wrapper.get('iframe').attributes('sandbox')).not.toContain('allow-top-navigation')
    expect(getBlob).not.toHaveBeenCalled()
    await wrapper.setProps({ link: { ...link, embeddable: false } })
    expect(wrapper.find('iframe').exists()).toBe(false)
    expect(wrapper.find('a').exists()).toBe(false)
    expect(open).not.toHaveBeenCalled()
    await wrapper.get('[data-testid="trace-link-new-tab"]').trigger('click')
    expect(open).toHaveBeenCalledWith(link.modalUrl, '_blank', 'noopener,noreferrer')
    expect(wrapper.emitted('close')).toBeUndefined()
  })
  it('never acquires an expired file link', async () => {
    const { wrapper, getBlob } = setup({ ...fileLink(), expiresOn: '2000-01-01T00:00:00Z' })
    await flushPromises()
    expect(getBlob).not.toHaveBeenCalled()
    expect(wrapper.find('iframe').exists()).toBe(false)
    expect(wrapper.get('[data-testid="trace-link-new-tab"]').attributes('disabled')).toBeDefined()
  })

  it('opens a trusted standalone File page with tab authentication and disconnects its opener before navigation', async () => {
    const tab = { opener: window, location: { replace: vi.fn() } }
    const open = vi.spyOn(window, 'open').mockReturnValue(tab as unknown as Window)
    const { wrapper } = setup(fileLink())
    await flushPromises()
    await wrapper.get('[data-testid="trace-link-new-tab"]').trigger('click')
    expect(open).toHaveBeenCalledWith('about:blank', '_blank')
    expect(tab.opener).toBeNull()
    expect(tab.location.replace).toHaveBeenCalledWith(window.location.origin + fileLink().newTabUrl)
    expect(wrapper.emitted('close')).toBeUndefined()
    await wrapper.setProps({ link: null })
    expect(tab.location.replace).toHaveBeenCalledTimes(1)
  })

  it('rejects a File browser URL aimed at a different origin, path or ticket', async () => {
    const { wrapper } = setup({
      ...fileLink(),
      newTabUrl: 'https://evil.test/screen/trace-link?ticket=opaque',
    })
    await flushPromises()
    expect(wrapper.get('button').attributes('disabled')).toBeDefined()
    await wrapper.setProps({
      link: { ...fileLink(), newTabUrl: '/screen/trace-link?ticket=other' },
    })
    expect(wrapper.get('button').attributes('disabled')).toBeDefined()
    await wrapper.setProps({ link: { ...fileLink(), newTabUrl: '/pc/studio/trace?ticket=opaque' } })
    expect(wrapper.get('button').attributes('disabled')).toBeDefined()
  })

  it('renders standalone files independently and reloads through the authenticated API on refresh', async () => {
    const { wrapper: modal, getBlob } = setup(fileLink())
    await flushPromises()
    let count = 0
    vi.mocked(URL.createObjectURL).mockImplementation(() => `blob:standalone-${++count}`)
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: '/screen/trace-link', component: TraceLinkPreviewPage }],
    })
    await router.push({
      path: '/screen/trace-link',
      query: {
        nId: 'model',
        ticket: 'opaque',
        expiresOn: new Date(Date.now() + 300000).toISOString(),
      },
    })
    const mountPreview = () =>
      mount(TraceLinkPreviewPage, {
        global: {
          plugins: [createPinia(), router],
          stubs: { ElAlert: { props: ['title'], template: '<p>{{title}}</p>' } },
        },
      })
    const preview = mountPreview()
    wrappers.push(preview)
    await flushPromises()
    expect(preview.get('iframe').attributes('src')).toBe('blob:standalone-1')
    await modal.setProps({ link: null })
    expect(URL.revokeObjectURL).not.toHaveBeenCalledWith('blob:standalone-1')
    expect(preview.get('iframe').attributes('src')).toBe('blob:standalone-1')
    preview.unmount()
    wrappers.splice(wrappers.indexOf(preview), 1)
    const refreshed = mountPreview()
    wrappers.push(refreshed)
    await flushPromises()
    expect(getBlob).toHaveBeenCalledTimes(3)
    expect(refreshed.get('iframe').attributes('src')).toBe('blob:standalone-2')
    const record = studioScreenRoutes.find((r) => r.name === 'studio-trace-link-preview')
    expect(record?.meta).toMatchObject({
      requiresAuth: true,
      permission: 'studio.trace.run',
      workspace: 'none',
      traceLinkScreen: true,
    })
  })

  it('does not fetch an expired standalone preview and drops a pending response after navigation', async () => {
    const getBlob = vi.fn<(path: string, options?: { signal?: AbortSignal }) => Promise<Blob>>()
    registerStudioApi(createStudioApi({ getBlob } as unknown as HttpClient))
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: '/screen/trace-link', component: TraceLinkPreviewPage }],
    })
    await router.push({
      path: '/screen/trace-link',
      query: { nId: 'model', ticket: 'opaque', expiresOn: '2000-01-01T00:00:00Z' },
    })
    const preview = mount(TraceLinkPreviewPage, {
      global: {
        plugins: [createPinia(), router],
        stubs: { ElAlert: { props: ['title'], template: '<p>{{ title }}</p>' } },
      },
    })
    wrappers.push(preview)
    await flushPromises()
    expect(getBlob).not.toHaveBeenCalled()
    expect(preview.text()).toContain('链接已过期')
    let resolve!: (blob: Blob) => void
    getBlob.mockImplementationOnce(
      () =>
        new Promise((done) => {
          resolve = done
        }),
    )
    await router.push({
      path: '/screen/trace-link',
      query: {
        nId: 'model',
        ticket: 'opaque',
        expiresOn: new Date(Date.now() + 300000).toISOString(),
      },
    })
    await flushPromises()
    const signal = getBlob.mock.calls[0]?.[1]?.signal
    await router.push('/screen/trace-link')
    resolve(new Blob(['stale']))
    await flushPromises()
    expect(signal?.aborted).toBe(true)
    expect(preview.find('iframe').exists()).toBe(false)
    expect(URL.createObjectURL).not.toHaveBeenCalled()
  })

  it('disables the new browser action after a file read is rejected', async () => {
    const { wrapper, getBlob } = setup(fileLink())
    await flushPromises()
    getBlob.mockRejectedValueOnce(new Error('STUDIO_FORBIDDEN'))
    await wrapper.setProps({ link: { ...fileLink(), title: 'Denied' } })
    await flushPromises()
    expect(wrapper.find('iframe').exists()).toBe(false)
    expect(wrapper.get('button').attributes('disabled')).toBeDefined()
  })
})
