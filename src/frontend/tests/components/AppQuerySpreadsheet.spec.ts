import { defineComponent, h, onMounted } from 'vue'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia } from 'pinia'
import ElementPlus from 'element-plus'
import { afterEach, expect, it, vi } from 'vitest'
import AppQuerySpreadsheet from '@/components/management/AppQuerySpreadsheet.vue'
import type { QueryPage, QueryRequest } from '@/components/management/querySpreadsheet/types'

vi.mock('@/components/management/querySpreadsheet/SheetCanvas.vue', () => ({
  default: defineComponent({
    emits: ['ready'],
    setup(_, { expose, emit }) {
      expose({ replace: state.replace })
      onMounted(() => emit('ready'))
      return () => h('div')
    },
  }),
}))
const state = vi.hoisted(() => ({ replace: vi.fn() }))
const pages: ReturnType<typeof mount>[] = []
afterEach(() => {
  pages.forEach((p) => p.unmount())
  pages.length = 0
  vi.clearAllMocks()
})
function render(loader: (request: QueryRequest) => Promise<QueryPage>) {
  const page = mount(AppQuerySpreadsheet, {
    props: { columns: [{ field: 'x', title: 'X' }], loader },
    global: { plugins: [createPinia(), ElementPlus] },
  })
  pages.push(page)
  return page
}
const result = (x: string): QueryPage => ({
  items: [{ x }],
  total: 1,
  pageIndex: 1,
  pageSize: 10000,
})
it('cancels superseded queries and ignores their late responses', async () => {
  let resolveOld!: (value: QueryPage) => void
  const loader = vi
    .fn<(request: QueryRequest) => Promise<QueryPage>>()
    .mockReturnValueOnce(
      new Promise((resolve) => {
        resolveOld = resolve
      }),
    )
    .mockResolvedValueOnce(result('new'))
  const page = render(loader)
  await flushPromises()
  const api = page.vm as unknown as { reload(): Promise<void> }
  await api.reload()
  expect(loader.mock.calls[0]![0].signal.aborted).toBe(true)
  resolveOld(result('old'))
  await flushPromises()
  expect(state.replace).toHaveBeenCalledTimes(1)
  expect(state.replace).toHaveBeenLastCalledWith([{ x: 'new' }])
})
it('retains previous data on invalid responses and aborts on disposal', async () => {
  const loader = vi
    .fn<(request: QueryRequest) => Promise<QueryPage>>()
    .mockResolvedValueOnce(result('valid'))
    .mockResolvedValueOnce({ ...result('bad'), pageIndex: 9 })
  const page = render(loader)
  await flushPromises()
  await (page.vm as unknown as { reload(): Promise<void> }).reload()
  expect(state.replace).toHaveBeenCalledTimes(1)
  expect(page.text()).toContain('原结果已保留')
  page.unmount()
  expect(loader.mock.calls[1]![0].signal.aborted).toBe(true)
})

it('commits query metadata only after a valid worksheet replacement', async () => {
  const accepted = vi.fn()
  state.replace.mockRejectedValueOnce(new Error('engine failed'))
  const page = render(async () => ({ ...result('ok'), accepted }))
  await flushPromises()
  expect(accepted).not.toHaveBeenCalled()
  await (page.vm as unknown as { reload(): Promise<void> }).reload()
  expect(accepted).toHaveBeenCalledTimes(1)
})
