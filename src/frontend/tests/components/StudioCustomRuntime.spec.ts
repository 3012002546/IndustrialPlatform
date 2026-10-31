import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import ElementPlus from 'element-plus'
import { expect, it, vi } from 'vitest'
import { createStudioApi, registerStudioApi, type StudioRuntime } from '@/api/studio'
import type { HttpClient } from '@/api/httpClient'
import StudioCustomRuntime from '@/components/studio/shared/StudioCustomRuntime.vue'

it('allows retry when an embedded page never finishes loading', async () => {
  vi.useFakeTimers()
  const http = {
    post: vi.fn().mockResolvedValue({ mode: 'Link', url: 'https://external.test/page' }),
    get: vi.fn(),
    put: vi.fn(),
    delete: vi.fn(),
    getWithMeta: vi.fn(),
  } as unknown as HttpClient
  registerStudioApi(createStudioApi(http))
  const runtime: StudioRuntime = {
    resource: {
      nId: 'link',
      kind: 'LowCodePage',
      name: 'Synthetic link',
      isEnabled: true,
      publishedRevisionNId: 'rev',
      publishedRevisionNo: '1',
      hasDraftChanges: false,
      lastUpdatedOn: '',
      optimisticVersion: '1',
      concurrencyVersion: 'token',
    },
    revisionNId: 'rev',
    document: { mode: 'Link', instances: [], actions: [] },
    instances: {},
    diagnostics: [],
  }
  const wrapper = mount(StudioCustomRuntime, {
    props: { runtime },
    global: { plugins: [createPinia(), ElementPlus] },
  })
  try {
    await flushPromises()
    expect((wrapper.get('button').element as HTMLButtonElement).disabled).toBe(true)
    await vi.advanceTimersByTimeAsync(15000)
    expect((wrapper.get('button').element as HTMLButtonElement).disabled).toBe(false)
    expect(wrapper.text()).toContain('页面内容加载超时')
    await wrapper.get('button').trigger('click')
    await flushPromises()
    expect(http.post).toHaveBeenCalledTimes(2)
  } finally {
    wrapper.unmount()
    vi.useRealTimers()
  }
})
