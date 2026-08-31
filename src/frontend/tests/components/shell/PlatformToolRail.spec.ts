/**
 * PlatformToolRail 组件测试(PF-01 §6.2):
 * 渲染分组按钮、当前组选中语义、点击切换 emit、键盘按钮与 Tooltip/aria 名称。
 */

import { mount, type VueWrapper } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { defineComponent } from 'vue'

import { normalizeNavigationGroups } from '@/components/navigation/navigation'
import PlatformToolRail from '@/components/shell/PlatformToolRail.vue'
import type { NavigationGroup } from '@/components/navigation/types'
import { useLocalizationStore } from '@/stores/localizationStore'

const IconA = defineComponent({ name: 'IconA', template: '<span>★</span>' })
const IconB = defineComponent({ name: 'IconB', template: '<span>◆</span>' })

const GROUPS: readonly NavigationGroup[] = [
  { id: 'workspace', label: '工作台', icon: IconA, items: [] },
  { id: 'system', label: '系统管理', icon: IconB, items: [] },
]

function mountRail(activeGroupId = 'workspace'): VueWrapper {
  return mount(PlatformToolRail, {
    props: { groups: GROUPS, activeGroupId },
  })
}

describe('PlatformToolRail', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('渲染每个分组的图标按钮,带 aria-label 名称', () => {
    const wrapper = mountRail()
    const buttons = wrapper.findAll('button.ip-toolrail__button:not(.ip-toolrail__more-button)')
    expect(buttons.length).toBe(GROUPS.length)
    expect(buttons[0]?.attributes('aria-label')).toBe('工作台')
    expect(buttons[1]?.attributes('aria-label')).toBe('系统管理')
  })

  it('当前分组按钮标记 aria-current=page 与 aria-pressed', () => {
    const wrapper = mountRail('system')
    const active = wrapper.get('[aria-current="page"]')
    expect(active.attributes('aria-label')).toBe('系统管理')
    expect(active.attributes('aria-pressed')).toBe('true')
  })

  it('当前组与非当前组均使用 title 提示(可读名称,不依赖 Emoji)', () => {
    const wrapper = mountRail()
    for (const button of wrapper.findAll('button')) {
      expect(button.attributes('title')).toBeTruthy()
    }
  })

  it('点击非当前组发出 update:activeGroupId', async () => {
    const wrapper = mountRail('workspace')
    const system = wrapper.findAll('button')[1]!
    await system.trigger('click')
    expect(wrapper.emitted('update:activeGroupId')).toEqual([['system']])
  })

  it('点击当前组不重复 emit', async () => {
    const wrapper = mountRail('workspace')
    const workspace = wrapper.findAll('button')[0]!
    await workspace.trigger('click')
    expect(wrapper.emitted('update:activeGroupId')).toBeUndefined()
  })

  it('导航语义:nav aria-label 为平台分组;按钮为原生 button 可键盘激活', async () => {
    const wrapper = mountRail()
    expect(wrapper.get('nav').attributes('aria-label')).toBe('平台分组')
    const first = wrapper.findAll('button')[0]!
    await first.trigger('focus')
    expect((first.element as HTMLButtonElement).tagName).toBe('BUTTON')
  })

  it('compact 模式保留图标入口但隐藏分组文字', () => {
    const wrapper = mount(PlatformToolRail, {
      props: { groups: GROUPS, activeGroupId: 'workspace', mode: 'compact' },
    })
    expect(wrapper.get('nav').classes()).toContain('ip-toolrail--compact')
    expect(wrapper.findAll('.ip-toolrail__label')).toHaveLength(0)
    expect(wrapper.findAll('.ip-toolrail__icon')).toHaveLength(GROUPS.length)
  })

  it('语言切换即时更新一级分组、更多菜单与 aria 文案', async () => {
    const pinia = createPinia()
    setActivePinia(pinia)
    const localization = useLocalizationStore()
    const groups = normalizeNavigationGroups([
      { ...GROUPS[0]!, items: [] },
      { ...GROUPS[1]!, items: [] },
    ])
    localization.setLocale('en-US', null)
    const wrapper = mount(PlatformToolRail, {
      props: { groups, activeGroupId: 'workspace' },
      global: { plugins: [pinia] },
    })

    expect(wrapper.findAll('.ip-toolrail__button')[0]?.attributes('aria-label')).toBe('Workspace')
    expect(wrapper.find('nav').attributes('aria-label')).toBe('Platform groups')

    localization.setLocale('zh-CN', null)
    await wrapper.vm.$nextTick()
    expect(wrapper.findAll('.ip-toolrail__button')[0]?.attributes('aria-label')).toBe('工作台')
    expect(wrapper.find('nav').attributes('aria-label')).toBe('平台分组')
  })

  it('更多菜单中的溢出分组也跟随语言切换', async () => {
    let triggerResize: (() => void) | undefined
    vi.stubGlobal(
      'ResizeObserver',
      class {
        constructor(private readonly callback: ResizeObserverCallback) {
          triggerResize = () =>
            this.callback(
              [{ contentRect: { height: 74 } } as ResizeObserverEntry],
              this as unknown as ResizeObserver,
            )
        }

        observe(): void {}

        disconnect(): void {}
      },
    )
    const pinia = createPinia()
    setActivePinia(pinia)
    const localization = useLocalizationStore()
    const groups = normalizeNavigationGroups([
      ...GROUPS,
      { id: 'extra', label: '额外', icon: IconA, items: [] },
    ])
    const wrapper = mount(PlatformToolRail, {
      props: { groups, activeGroupId: 'workspace' },
      global: { plugins: [pinia] },
    })
    triggerResize?.()
    await wrapper.vm.$nextTick()

    expect(wrapper.find('[data-testid="toolrail-more"]').exists()).toBe(true)
    await wrapper.get('[data-testid="toolrail-more"]').trigger('click')
    localization.setLocale('en-US', null)
    await wrapper.vm.$nextTick()
    expect(wrapper.get('[data-testid="toolrail-more-menu"]').text()).toContain('System management')

    localization.setLocale('zh-CN', null)
    await wrapper.vm.$nextTick()
    expect(wrapper.get('[data-testid="toolrail-more-menu"]').text()).toContain('系统管理')
  })

  it('没有溢出分组时仍显示带图标的更多入口并列出现有授权分组', async () => {
    const wrapper = mountRail()

    const more = wrapper.get('[data-testid="toolrail-more"]')
    expect(more.find('svg.ip-toolrail__more-icon').exists()).toBe(true)
    await more.trigger('click')

    expect(wrapper.get('[data-testid="toolrail-more-menu"]').text()).toContain('工作台')
    expect(wrapper.get('[data-testid="toolrail-more-menu"]').text()).toContain('系统管理')
  })

  it('更多菜单组图标使用统一小尺寸而不受 SVG intrinsic size 撑大', async () => {
    const source = await import('@/components/shell/PlatformToolRail.vue?raw')

    expect(source.default).toMatch(
      /\.ip-toolrail__more-menu-item\s+svg\s*\{[\s\S]*?width:\s*18px;[\s\S]*?height:\s*18px;[\s\S]*?flex:\s*0\s+0\s+18px;/,
    )
  })

})
