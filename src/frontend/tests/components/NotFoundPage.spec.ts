/**
 * 404 页面组件测试(FE-007,§15.4):
 * 原始路径以纯文本展示(HTML 转义)、返回首页、返回上一页(无历史时回落首页)。
 */

import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import { createMemoryHistory, createRouter, createWebHistory, type Router } from 'vue-router'

import NotFoundPage from '@/pages/public/NotFoundPage.vue'
import { routes } from '@/router/routes'

interface NotFoundHarness {
  wrapper: VueWrapper
  router: Router
}

async function mountNotFound(initialPath: string): Promise<NotFoundHarness> {
  const router = createRouter({ history: createMemoryHistory(), routes })
  await router.push(initialPath)
  await router.isReady()
  const wrapper = mount(NotFoundPage, { global: { plugins: [router] } })
  return { wrapper, router }
}

describe('NotFoundPage', () => {
  it('以纯文本展示原始路径(Vue 插值自动 HTML 转义)', async () => {
    const { wrapper } = await mountNotFound('/no/such-page')
    const pathEl = wrapper.get('.not-found-page__path')
    expect(pathEl.text()).toContain('/no/such-page')
    // 无 v-html:路径为纯文本节点,不渲染为子元素
    expect(pathEl.element.childElementCount).toBe(0)
  })

  it('包含 HTML 特殊字符的路径被转义而非解析', async () => {
    const { wrapper } = await mountNotFound('/a<b')
    const pathEl = wrapper.get('.not-found-page__path')
    expect(pathEl.text()).toContain('/a<b')
    expect(pathEl.element.innerHTML).not.toContain('<b')
  })

  it('返回首页', async () => {
    const { wrapper, router } = await mountNotFound('/no-such-page')
    await wrapper.get('[data-testid="go-home"]').trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.name).toBe('root')
  })

  it('有历史时返回上一页(web history,与生产一致)', async () => {
    // memory history 的 back() 会导航到初始 "" entry,resolve("") 回落当前路由,
    // 无法表达"返回上一页";改用 jsdom 真实 web history 覆盖该行为。
    // 上一页用真实路由 /pc/home,popstate 为宏任务,waitFor 轮询导航完成。
    const router = createRouter({ history: createWebHistory(), routes })
    await router.push('/pc/home')
    await router.push('/no-such-page')
    await router.isReady()
    const wrapper = mount(NotFoundPage, { global: { plugins: [router] } })
    await wrapper.get('[data-testid="go-back"]').trigger('click')
    await vi.waitFor(() => {
      expect(router.currentRoute.value.fullPath).toBe('/pc/home')
    })
  })

  it('无历史时返回上一页回落为返回首页', async () => {
    // 首个导航用 replace:memory history 队列只剩一条 entry,state.back 恒为 undefined,
    // canGoBack() 判定无历史,回落返回首页(root)。
    const router = createRouter({ history: createMemoryHistory(), routes })
    await router.replace('/no-such-page')
    await router.isReady()
    const wrapper = mount(NotFoundPage, { global: { plugins: [router] } })
    await wrapper.get('[data-testid="go-back"]').trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.name).toBe('root')
  })
})
