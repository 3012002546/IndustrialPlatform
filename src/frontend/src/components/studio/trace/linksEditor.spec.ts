import { expect, it, vi } from 'vitest'
import { createPinia } from 'pinia'
import { flushPromises, shallowMount } from '@vue/test-utils'
import TraceLinksEditor from './TraceLinksEditor.vue'
import { rowBinding } from './model'
const api = vi.hoisted(() => ({ linkSources: vi.fn(), linkResources: vi.fn() }))
vi.mock('@/api/studioTrace', () => ({ getTraceApi: () => api }))

it.each(['external', 'resource'])(
  'keeps the %s choices usable when the other catalog fails',
  async (available) => {
    const unavailable = new Error('Catalog unavailable')
    if (available === 'external') {
      api.linkSources.mockResolvedValue({
        external: [{ key: 'mes', embeddable: true, allowedParameters: [] }],
        mes: [],
      })
      api.linkResources.mockRejectedValue(unavailable)
    } else {
      api.linkSources.mockRejectedValue(unavailable)
      api.linkResources.mockResolvedValue({
        items: [
          {
            kind: 'Report',
            resourceNId: 'report',
            revisionNId: 'v1',
            revisionNo: '1',
            name: 'Inspection',
            parameters: [],
          },
        ],
      })
    }
    const wrapper = shallowMount(TraceLinksEditor, {
      props: {
        modelValue: [
          {
            id: 'external',
            kind: 'External',
            label: 'MES',
            value: rowBinding(),
            openMode: 'Modal',
          },
          { id: 'resource', kind: 'Resource', label: 'Report', openMode: 'Modal' },
        ],
      },
      global: {
        plugins: [createPinia()],
        stubs: {
          ElSelect: { template: '<div><slot /></div>' },
          ElOption: { props: ['label'], template: '<span>{{ label }}</span>' },
          ElAlert: { props: ['title'], template: '<aside>{{ title }}</aside>' },
          ElInput: true,
          ElButton: true,
        },
      },
    })
    try {
      await flushPromises()
      expect(wrapper.get('aside').text()).toContain(unavailable.message)
      expect(wrapper.findAll('span').map((el) => el.text())).toContain(
        available === 'external' ? 'mes' : 'Inspection · v1 · Report',
      )
    } finally {
      wrapper.unmount()
    }
  },
)
