/**
 * AppFormDrawer 组件测试(PF-01 §7.10):
 * 显隐、submit/cancel/update:modelValue、busy 防重、Escape、
 * 焦点归还触发点与 PC 尺寸/手持全宽。
 * jsdom 中 Vue Teleport 不落到 body,用自定义 teleport stub 渲染槽内容。
 */

import { mount, type VueWrapper } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent } from 'vue'

import AppFormDrawer from '@/components/management/AppFormDrawer.vue'
import { useDeviceStore } from '@/stores/deviceStore'

/**
 * AppFormDrawer 用 <script setup> 直接 import ElFocusTrap,模板编译为局部绑定,
 * 绕过 resolveComponent,VTU 全局 stubs 无法替换(jsdom 下真实 ElFocusTrap
 * 渲染为 [object Object])。故顶层 mock 深路径模块,把该导入替换为渲染槽的 stub。
 * (element-plus 主入口运行时与类型均不导出 ElFocusTrap,组件按内部子路径导入。)
 */
vi.mock('element-plus/es/components/focus-trap/index', async () => {
  const { defineComponent } = await import('vue')
  return {
    ElFocusTrap: defineComponent({
      name: 'ElFocusTrapStub',
      template: '<div><slot /></div>',
    }),
    default: defineComponent({
      name: 'ElFocusTrapStub',
      template: '<div><slot /></div>',
    }),
  }
})

/** jsdom 不支持真实 Teleport;stub 在组件原位渲染槽内容便于断言。 */
const TeleportStub = defineComponent({
  name: 'TeleportStub',
  props: { to: { type: String, required: true }, disabled: Boolean },
  template: '<div><slot /></div>',
})

const wrappers: VueWrapper[] = []

type DrawerProps = InstanceType<typeof AppFormDrawer>['$props']

function mountDrawer(props: DrawerProps, slots: Record<string, string> = {}): VueWrapper {
  const pinia = createPinia()
  setActivePinia(pinia)
  const wrapper = mount(AppFormDrawer, {
    props,
    slots,
    global: {
      plugins: [pinia],
      stubs: { teleport: TeleportStub },
    },
  })
  wrappers.push(wrapper)
  return wrapper
}

describe('AppFormDrawer', () => {
  beforeEach(() => {
    localStorage.clear()
  })

  afterEach(() => {
    wrappers.splice(0).forEach((w) => w.unmount())
    document.body.innerHTML = ''
    localStorage.clear()
  })

  it('modelValue=false 时不渲染对话框', () => {
    const wrapper = mountDrawer({ modelValue: false, title: '编辑' })
    expect(wrapper.find('[role="dialog"]').exists()).toBe(false)
  })

  it('modelValue=true 渲染对话框并带标题/ARIA;默认槽内容可见', () => {
    const wrapper = mountDrawer(
      { modelValue: true, title: '新建用户' },
      { default: '<label>姓名<input /></label>' },
    )
    const dialog = wrapper.get('[role="dialog"]')
    expect(dialog.attributes('aria-modal')).toBe('true')
    expect(dialog.attributes('aria-labelledby')).toBeTruthy()
    expect(wrapper.get('.app-form-drawer__title').text()).toBe('新建用户')
    expect(wrapper.find('input').exists()).toBe(true)
  })

  it('提交发出 submit', async () => {
    const wrapper = mountDrawer({ modelValue: true, title: '表单' })
    await wrapper.get('[data-testid="form-drawer-submit"]').trigger('click')
    expect(wrapper.emitted('submit')).toEqual([[]])
  })

  it('busy 时提交按钮禁用且不重复触发 submit', async () => {
    const wrapper = mountDrawer({ modelValue: true, title: '表单', busy: true })
    const submit = wrapper.get('[data-testid="form-drawer-submit"]')
    expect((submit.element as HTMLButtonElement).disabled).toBe(true)
    expect(submit.text()).toContain('提交中')
    await submit.trigger('click')
    expect(wrapper.emitted('submit')).toBeUndefined()
  })

  it('取消按钮发出 cancel 与 update:modelValue=false', async () => {
    const wrapper = mountDrawer({ modelValue: true, title: '表单' })
    await wrapper.get('[data-testid="form-drawer-cancel"]').trigger('click')
    expect(wrapper.emitted('cancel')).toEqual([[]])
    expect(wrapper.emitted('update:modelValue')).toEqual([[false]])
  })

  it('关闭按钮发出 cancel 与 update:modelValue=false', async () => {
    const wrapper = mountDrawer({ modelValue: true, title: '表单' })
    await wrapper.get('[data-testid="form-drawer-close"]').trigger('click')
    expect(wrapper.emitted('cancel')).toEqual([[]])
    expect(wrapper.emitted('update:modelValue')).toEqual([[false]])
  })

  it('Escape 键关闭', async () => {
    const wrapper = mountDrawer({ modelValue: true, title: '表单' })
    await wrapper.get('[role="dialog"]').trigger('keydown', { key: 'Escape' })
    expect(wrapper.emitted('cancel')).toEqual([[]])
    expect(wrapper.emitted('update:modelValue')).toEqual([[false]])
  })

  it('关闭后焦点归还打开前元素', async () => {
    const trigger = document.createElement('button')
    document.body.appendChild(trigger)
    trigger.focus()
    const wrapper = mountDrawer({ modelValue: false, title: '表单' })
    await wrapper.setProps({ modelValue: true })
    await wrapper.setProps({ modelValue: false })
    expect(document.activeElement).toBe(trigger)
  })

  it('PC 尺寸档位 narrow/medium/wide 映射宽度类', () => {
    const narrow = mountDrawer({ modelValue: true, title: 't', size: 'narrow' })
    expect(narrow.get('.app-form-drawer').classes()).toContain('app-form-drawer--narrow')
    const wide = mountDrawer({ modelValue: true, title: 't', size: 'wide' })
    expect(wide.get('.app-form-drawer').classes()).toContain('app-form-drawer--wide')
    const medium = mountDrawer({ modelValue: true, title: 't' })
    expect(medium.get('.app-form-drawer').classes()).toContain('app-form-drawer--medium')
  })

  it('PC 顶部切换模态/抽屉并记住最近选择', async () => {
    const wrapper = mountDrawer({ modelValue: true, title: '表单', storageKey: 'test-form-mode' })
    expect(wrapper.get('.app-form-drawer').classes()).toContain('app-form-drawer--drawer')
    await wrapper.get('[data-testid="form-surface-mode-toggle"]').trigger('click')
    expect(wrapper.get('.app-form-drawer').classes()).toContain('app-form-drawer--modal')
    expect(localStorage.getItem('test-form-mode')).toBe('modal')
    wrapper.unmount()

    const reopened = mountDrawer({ modelValue: true, title: '表单', storageKey: 'test-form-mode' })
    await new Promise((resolve) => setTimeout(resolve))
    expect(reopened.get('.app-form-drawer').classes()).toContain('app-form-drawer--modal')
  })

  it('PDA/Mobile 抽屉全宽(手持修饰类)', async () => {
    for (const terminal of ['pda', 'mobile'] as const) {
      const pinia = createPinia()
      setActivePinia(pinia)
      useDeviceStore().setOverride(terminal)
      const wrapper = mount(AppFormDrawer, {
        props: { modelValue: true, title: 't', size: 'narrow' },
        global: {
          plugins: [pinia],
          stubs: { teleport: TeleportStub },
        },
      })
      wrappers.push(wrapper)
      expect(wrapper.get('.app-form-drawer__panel').classes()).toContain(
        'app-form-drawer__panel--handheld',
      )
    }
  })
})
