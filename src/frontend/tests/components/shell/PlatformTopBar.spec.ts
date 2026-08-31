/**
 * PlatformTopBar 组件测试(PF-01 §6.1):
 * 三段结构(brand/search/right)、具名槽渲染、空槽抑制、
 * 固定高度与渐变背景 Token 消费、顶栏为 flex 布局。
 */

import { mount, type VueWrapper } from '@vue/test-utils'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

import PlatformTopBar from '@/components/shell/PlatformTopBar.vue'

const SLOT_CONTENT: Record<string, string> = {
  brand: '品牌区',
  context: '上下文区',
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
    expect(wrapper.get('.ip-topbar__context').text()).toContain('上下文区')
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

  it('采用视觉交接约定的固定高度与三段 flex 布局', async () => {
    const foundation = readFileSync(resolve(process.cwd(), 'src/styles/foundation.css'), 'utf8')
    expect(foundation).toContain('--ip-shell-topbar-height: 56px')
    expect(foundation).toContain('--ip-shell-toolrail-width: 72px')
    expect(foundation).toContain('--ip-shell-tabs-height: 38px')

    const sourceModules = import.meta.glob('../../../src/components/shell/*.vue', {
      query: '?raw',
      import: 'default',
    })
    const source = (await sourceModules['../../../src/components/shell/PlatformTopBar.vue']!()) as string
    expect(source).toMatch(/\.ip-topbar\s*\{[\s\S]*?display:\s*flex;/)
  })

  it('右区按实际内容宽度布局,不以 overflow hidden 裁切可交互工具', async () => {
    const sourceModules = import.meta.glob('../../../src/components/shell/*.vue', {
      query: '?raw',
      import: 'default',
    })
    const source = (await sourceModules['../../../src/components/shell/PlatformTopBar.vue']!()) as string
    expect(source).toMatch(/\.ip-topbar__right\s*\{[\s\S]*?width:\s*max-content;[\s\S]*?min-width:\s*max-content;[\s\S]*?overflow:\s*visible;/)
    expect(source).toMatch(/\.ip-topbar__actions\s*\{[\s\S]*?flex:\s*0\s+0\s+auto;[\s\S]*?min-width:\s*max-content;[\s\S]*?overflow:\s*visible;/)
  })

  it('提供基于左右实际占用测量的搜索布局,避免真实工具被搜索覆盖', async () => {
    const sourceModules = import.meta.glob('../../../src/components/shell/*.vue', {
      query: '?raw',
      import: 'default',
    })
    const source = (await sourceModules['../../../src/components/shell/PlatformTopBar.vue']!()) as string
    expect(source).toContain('ResizeObserver')
    expect(source).toContain('availableStart')
    expect(source).toContain('availableEnd')
    expect(source).toMatch(/ref=\"headerRef\"/)
    expect(source).toMatch(/:style=\"searchStyle\"/)
  })

  it('搜索使用参与布局的中间轨道,动作组贴近右侧用户区', async () => {
    const sourceModules = import.meta.glob('../../../src/components/shell/*.vue', {
      query: '?raw',
      import: 'default',
    })
    const source = (await sourceModules['../../../src/components/shell/PlatformTopBar.vue']!()) as string
    expect(source).toMatch(/\.ip-topbar\s*\{[\s\S]*?display:\s*grid;/)
    expect(source).toMatch(/\.ip-topbar\s*\{[\s\S]*?grid-template-columns:\s*minmax\(0,\s*1fr\)\s+max-content;/)
    expect(source).toMatch(/\.ip-topbar__search\s*\{[\s\S]*?position:\s*absolute;/)
    expect(source).toMatch(/\.ip-topbar__actions\s*\{[\s\S]*?flex:\s*0\s+0\s+auto;[\s\S]*?margin-left:\s*auto;/)
  })

  it('搜索中间轨道不以固定负边距制造左右区域重叠', async () => {
    const sourceModules = import.meta.glob('../../../src/components/shell/*.vue', {
      query: '?raw',
      import: 'default',
    })
    const source = (await sourceModules['../../../src/components/shell/PlatformTopBar.vue']!()) as string
    expect(source).not.toMatch(/\.ip-topbar__search\s*\{[\s\S]*?margin-left:\s*-/)
    expect(source).toMatch(/\.ip-topbar__left\s*\{[\s\S]*?min-width:\s*0;/)
  })

  it('窄屏按实际三段占位收缩搜索而不是覆盖右侧工具', async () => {
    const sourceModules = import.meta.glob('../../../src/components/shell/*.vue', {
      query: '?raw',
      import: 'default',
    })
    const source = (await sourceModules['../../../src/components/shell/PlatformTopBar.vue']!()) as string
    expect(source).toMatch(/@media\s*\(max-width:\s*1440px\)[\s\S]*?\.ip-topbar\s*\{[\s\S]*?grid-template-columns:/)
    expect(source).toMatch(/@media\s*\(min-width:\s*1600px\)/)
  })
})
