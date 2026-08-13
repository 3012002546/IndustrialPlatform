/**
 * AppTreeTableLayout 组件测试(PF-01 §7.10):
 * tree/content 双栏 aria 标签、四个槽与树宽档位。
 */

import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'

import AppTreeTableLayout from '@/components/management/AppTreeTableLayout.vue'

function slots() {
  return {
    tree: '<ul aria-label="tree-list"><li>设备分组</li></ul>',
    toolbar: '<button type="button">筛选</button>',
    default: '<table aria-label="rows"><tbody></tbody></table>',
    pagination: '<button type="button">下一页</button>',
  }
}

describe('AppTreeTableLayout', () => {
  it('树侧栏带 aria-label;内容区带 contentLabel', () => {
    const wrapper = mount(AppTreeTableLayout, {
      props: { treeLabel: '功能树', contentLabel: '内容区' },
      slots: slots(),
    })
    expect(wrapper.get('.app-tree-table__tree').attributes('aria-label')).toBe('功能树')
    expect(wrapper.get('.app-tree-table__content').attributes('aria-label')).toBe('内容区')
  })

  it('渲染 tree/toolbar/default/pagination 四个槽', () => {
    const wrapper = mount(AppTreeTableLayout, {
      props: { treeLabel: '树', contentLabel: '区' },
      slots: slots(),
    })
    expect(wrapper.find('[aria-label="tree-list"]').exists()).toBe(true)
    expect(wrapper.get('.app-tree-table__toolbar').text()).toContain('筛选')
    expect(wrapper.find('[aria-label="rows"]').exists()).toBe(true)
    expect(wrapper.get('.app-tree-table__pagination').text()).toContain('下一页')
  })

  it('treeWidth 默认 medium,可切 narrow', () => {
    const medium = mount(AppTreeTableLayout, {
      props: { treeLabel: '树', contentLabel: '区' },
    })
    expect(medium.get('.app-tree-table__tree').classes()).toContain('app-tree-table__tree--medium')
    const narrow = mount(AppTreeTableLayout, {
      props: { treeLabel: '树', contentLabel: '区', treeWidth: 'narrow' },
    })
    expect(narrow.get('.app-tree-table__tree').classes()).toContain('app-tree-table__tree--narrow')
  })
})
