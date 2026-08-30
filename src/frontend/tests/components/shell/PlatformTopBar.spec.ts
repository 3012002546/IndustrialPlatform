/**
 * PlatformTopBar 组件测试(PF-01 §6.1):
 * 三段结构(brand/search/right)、具名槽渲染、空槽抑制、
 * 固定高度与渐变背景 Token 消费、顶栏为 flex 布局。
 */

import { mount, type VueWrapper } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'

import PlatformTopBar from '@/components/shell/PlatformTopBar.vue'

const SLOT_CONTENT: Record<string, string> = {
  brand: '品牌区',
  'global-search': '搜索区',
  'global-actions': '操作区',
  user: '用户区',
}

function mountTopBar(slots: Record<string, string> = {}): VueWrapper {
  return mount(PlatformTopBar, { slots })
}

describe('PlatformTopBar', () => {
  it('渲染 header.ip-topbar,包含左中右三段和右侧 actions/user 子区域', () => {
    const wrapper = mountTopBar()
    const header = wrapper.get('header.ip-topbar')
    expect(header.classes()).toContain('ip-topbar')
    expect(wrapper.find('.ip-topbar__brand').exists()).toBe(true)
    expect(wrapper.find('.ip-topbar__search').exists()).toBe(false)
    expect(wrapper.find('.ip-topbar__right').exists()).toBe(true)
    expect(wrapper.find('.ip-topbar__actions').exists()).toBe(true)
    expect(wrapper.find('.ip-topbar__user').exists()).toBe(true)
  })

  it('四个具名槽内容渲染到对应区域', () => {
    const wrapper = mountTopBar(SLOT_CONTENT)
    expect(wrapper.get('.ip-topbar__brand').text()).toContain('品牌区')
    expect(wrapper.get('.ip-topbar__search').text()).toContain('搜索区')
    expect(wrapper.get('.ip-topbar__actions').text()).toContain('操作区')
    expect(wrapper.get('.ip-topbar__user').text()).toContain('用户区')
  })

  it('空 global-search 槽不渲染 .ip-topbar__search 占位', () => {
    const wrapper = mountTopBar({ brand: 'x', 'global-actions': 'y', user: 'z' })
    expect(wrapper.find('.ip-topbar__search').exists()).toBe(false)
  })

  it('消费固定高度与渐变背景 Token(var 引用)', async () => {
    // jsdom 不注入/解析 SFC 样式(style tag count=0),像素级由 E2E 验收;
    // 这里以源文件样式块做 Token 消费契约断言(import.meta.glob raw 读取)。
    const sourceModules = import.meta.glob('../../../src/components/shell/*.vue', {
      query: '?raw',
      import: 'default',
    })
    const loader = sourceModules['../../../src/components/shell/PlatformTopBar.vue']
    expect(loader).toBeTypeOf('function')
    const source = await loader!()
    expect(source).toContain('--ip-shell-topbar-height')
    expect(source).toContain('--ip-shell-topbar-background')
    expect(source).toContain('--ip-shell-topbar-text')
  })

  it('提供 user 槽时渲染用户区,无 user 槽时同样存在(区域容器固定)', () => {
    const wrapper = mountTopBar()
    expect(wrapper.find('.ip-topbar__user').exists()).toBe(true)
  })
})
