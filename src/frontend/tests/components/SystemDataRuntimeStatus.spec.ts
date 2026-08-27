import { createPinia, setActivePinia } from 'pinia'
import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'

import SystemDataRuntimeStatus from '@/components/systemData/SystemDataRuntimeStatus.vue'
import { useSystemDataRuntimeStore } from '@/stores/systemData/runtimeStore'

describe('SystemDataRuntimeStatus', () => {
  it('shows degraded capability and provides a retry action', async () => {
    const pinia = createPinia()
    setActivePinia(pinia)
    const store = useSystemDataRuntimeStore()
    store.degraded = true
    store.unavailable = true

    const wrapper = mount(SystemDataRuntimeStatus, { global: { plugins: [pinia] } })

    expect(wrapper.get('[data-testid="systemdata-runtime-degraded"]').text()).toContain('降级')
    expect(wrapper.get('button').text()).toContain('重试')
  })
})
