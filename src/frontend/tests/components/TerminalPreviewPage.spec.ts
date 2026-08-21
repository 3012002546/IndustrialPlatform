import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { describe, expect, it } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'

import { makeAuthSession } from '../fixtures/session'
import TerminalPreviewPage from '@/pages/pc/TerminalPreviewPage.vue'
import { PERMISSIONS } from '@/permissions'
import { routes, ROUTE_NAMES } from '@/router/routes'
import { useAuthStore } from '@/stores/authStore'

async function mountPreview(
  permissions: string[] = [PERMISSIONS.platformPdaView, PERMISSIONS.platformMobileView],
) {
  const pinia = createPinia()
  setActivePinia(pinia)
  useAuthStore().adoptSession(makeAuthSession(permissions))
  const router = createRouter({ history: createMemoryHistory(), routes })
  await router.push({ name: ROUTE_NAMES.terminalPreview })
  await router.isReady()
  const wrapper = mount(TerminalPreviewPage, { global: { plugins: [pinia, router] } })
  return { wrapper, router }
}

describe('TerminalPreviewPage', () => {
  it('默认通过同源 iframe 打开真实 PDA 首页预览契约', async () => {
    const { wrapper } = await mountPreview()
    const frame = wrapper.get('[data-testid="terminal-preview-frame"]')
    expect(frame.attributes('src')).toContain('/pda/home?preview=iframe')
    expect(frame.attributes('title')).toContain('PDA')
    expect(wrapper.get('[data-testid="terminal-preview-size"]').text()).toContain('480 × 800')
    expect(wrapper.get('[data-testid="terminal-preview-device"]').attributes('style')).toContain(
      '--preview-height: 800px',
    )
    expect(wrapper.get('[data-testid="terminal-preview-frame"]').attributes('style')).toContain(
      'width: 480px',
    )
    expect(wrapper.get('[data-testid="terminal-preview-frame"]').attributes('style')).toContain(
      'height: 800px',
    )
    expect(wrapper.get('[data-testid="terminal-preview-device-slot"]').attributes('style')).toContain(
      '--preview-slot-height',
    )
    expect(wrapper.get('[data-testid="terminal-preview-device"]').attributes('style')).toContain(
      '--preview-scale:',
    )
  })

  it('仅展示当前账号有权测试的终端并以可用终端作为默认值', async () => {
    const { wrapper } = await mountPreview([PERMISSIONS.platformMobileView])

    expect(wrapper.find('[data-testid="terminal-preview-pda"]').exists()).toBe(false)
    expect(wrapper.get('[data-testid="terminal-preview-mobile"]').attributes('aria-pressed')).toBe(
      'true',
    )
    expect(wrapper.get('[data-testid="terminal-preview-frame"]').attributes('src')).toContain(
      '/mobile/home?preview=iframe',
    )
  })

  it('切换 Mobile 与常用尺寸会更新真实路由 iframe 和设备视口', async () => {
    const { wrapper } = await mountPreview()
    await wrapper.get('[data-testid="terminal-preview-mobile"]').trigger('click')
    await flushPromises()

    expect(wrapper.get('[data-testid="terminal-preview-frame"]').attributes('src')).toContain(
      '/mobile/home?preview=iframe',
    )
    await wrapper.get('[data-testid="terminal-preview-size-430x932"]').trigger('click')
    expect(wrapper.get('[data-testid="terminal-preview-size"]').text()).toContain('430 × 932')
    expect(wrapper.get('[data-testid="terminal-preview-device"]').attributes('style')).toContain(
      '430px',
    )
  })

  it('刷新重建 iframe,支持独立查看与返回 PC 工作台', async () => {
    const { wrapper, router } = await mountPreview()
    const before = wrapper
      .get('[data-testid="terminal-preview-frame"]')
      .attributes('data-frame-key')
    await wrapper.get('[data-testid="terminal-preview-refresh"]').trigger('click')
    expect(
      wrapper.get('[data-testid="terminal-preview-frame"]').attributes('data-frame-key'),
    ).not.toBe(before)
    expect(wrapper.get('[data-testid="terminal-preview-open"]').attributes('target')).toBe('_blank')
    await wrapper.get('[data-testid="terminal-preview-back"]').trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.name).toBe(ROUTE_NAMES.pcHome)
  })
})
