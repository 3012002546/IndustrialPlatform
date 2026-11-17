import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { beforeEach, expect, it, vi } from 'vitest'
import ReportRevisionSelect from '@/components/studio/report/ReportRevisionSelect.vue'

const api = vi.hoisted(() => ({ revisions: vi.fn() }))
vi.mock('@/api/studio', () => ({ getStudioApi: () => api }))
beforeEach(() => vi.resetAllMocks())
const version = (id: string, no: string) => ({
  nId: id,
  revisionNo: no,
  createdOn: '2026-11-17T01:00:00Z',
  releaseNote: '发布说明',
})
const create = () =>
  mount(ReportRevisionSelect, {
    props: { modelValue: 'old', reportNId: 'a' },
    global: {
      plugins: [createPinia()],
      stubs: {
        ElSelect: {
          props: ['modelValue', 'disabled'],
          emits: ['update:modelValue'],
          template: `<select :value="modelValue" :disabled="disabled" @change="$emit('update:modelValue', $event.target.value)"><slot/></select>`,
        },
        ElOption: {
          props: ['value', 'label'],
          template: '<option :value="value">{{ label }}</option>',
        },
        ElButton: { template: '<button><slot/></button>' },
      },
    },
  })

it('loads readable history without upgrading the bound version and emits the selected internal id', async () => {
  api.revisions.mockResolvedValue([version('latest', '3'), version('old', '2')])
  const wrapper = create()
  try {
    await flushPromises()
    expect(wrapper.get('select').element.value).toBe('old')
    expect(wrapper.get('select').text()).toContain('V3')
    expect(wrapper.get('select').text()).toContain('2026')
    expect(wrapper.get('select').text()).toContain('发布说明')
    expect(wrapper.emitted('update:modelValue')).toBeUndefined()
    await wrapper.get('select').setValue('latest')
    expect(wrapper.emitted('update:modelValue')).toEqual([['latest']])
    await wrapper.setProps({ disabled: true })
    expect(wrapper.get('select').attributes('disabled')).toBeDefined()
  } finally {
    wrapper.unmount()
  }
})

it('retains an unknown bound id on failure and allows retry without changing it', async () => {
  api.revisions.mockRejectedValueOnce(new Error('unavailable')).mockResolvedValueOnce([])
  const wrapper = create()
  try {
    await flushPromises()
    expect(wrapper.get('[role="alert"]').text()).toContain('已保留原绑定')
    expect(wrapper.get('select').element.value).toBe('old')
    expect(wrapper.text()).not.toContain('old')
    await wrapper.get('button').trigger('click')
    await flushPromises()
    expect(wrapper.get('select').element.value).toBe('old')
    expect(wrapper.text()).toContain('未在列表中找到')
    expect(wrapper.emitted('update:modelValue')).toBeUndefined()
  } finally {
    wrapper.unmount()
  }
})

it('ignores history from the previously selected template when requests finish out of order', async () => {
  let finish!: (rows: ReturnType<typeof version>[]) => void
  api.revisions
    .mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          finish = resolve
        }),
    )
    .mockResolvedValueOnce([version('b-version', '8')])
  const wrapper = create()
  try {
    await wrapper.setProps({ reportNId: 'b', modelValue: 'b-version' })
    await flushPromises()
    finish([version('a-version', '9')])
    await flushPromises()
    expect(wrapper.get('select').text()).toContain('V8')
    expect(wrapper.get('select').text()).not.toContain('V9')
    expect(wrapper.emitted('loaded')).toEqual([['b', [version('b-version', '8')]]])
    expect(wrapper.emitted('update:modelValue')).toBeUndefined()
  } finally {
    wrapper.unmount()
  }
})

it('reuses a visited template but reloads when its published version changes', async () => {
  api.revisions.mockResolvedValue([version('old', '2')])
  const wrapper = create()
  try {
    await flushPromises()
    await wrapper.setProps({ reportNId: 'b' })
    await flushPromises()
    await wrapper.setProps({ reportNId: 'a' })
    await flushPromises()
    expect(api.revisions.mock.calls).toEqual([['a'], ['b']])
    await wrapper.setProps({ publishedRevisionNId: 'new' })
    await flushPromises()
    expect(api.revisions.mock.calls).toEqual([['a'], ['b'], ['a']])
    expect(wrapper.get('select').element.value).toBe('old')
    expect(wrapper.emitted('update:modelValue')).toBeUndefined()
  } finally {
    wrapper.unmount()
  }
})
