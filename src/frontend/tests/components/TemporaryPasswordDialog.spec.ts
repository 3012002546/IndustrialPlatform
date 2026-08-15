/**
 * 一次性临时密码弹窗组件测试(TASK-ID-021,§29A.4/§29A.7):
 * - 打开时仅展示一次临时密码,不进入任何持久化存储;
 * - 复制调用 Clipboard API 写入密码;
 * - 关闭(按钮/程序)即不可逆清空,再次打开需调用方重新传入。
 * jsdom 中 Vue Teleport 不落到 body,用自定义 teleport stub 渲染槽内容(同 AppFormDrawer)。
 */

import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import ElementPlus from 'element-plus'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent } from 'vue'

import TemporaryPasswordDialog from '@/pages/pc/identity/components/TemporaryPasswordDialog.vue'

// el-dialog 内部使用 ElFocusTrap(子路径导入),jsdom 下渲染异常,替换为渲染槽的 stub。
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

const TeleportStub = defineComponent({
  name: 'TeleportStub',
  props: { to: { type: String, required: true }, disabled: Boolean },
  template: '<div><slot /></div>',
})

const PASSWORD = 'Tmp!Pass123'

const wrappers: VueWrapper[] = []

function mountDialog(props: {
  modelValue: boolean
  password: string
  title?: string
  description?: string
}): VueWrapper {
  const wrapper = mount(TemporaryPasswordDialog, {
    props,
    global: {
      plugins: [ElementPlus],
      stubs: { teleport: TeleportStub },
    },
  })
  wrappers.push(wrapper)
  return wrapper
}

describe('TemporaryPasswordDialog', () => {
  beforeEach(() => {
    Object.defineProperty(navigator, 'clipboard', {
      value: { writeText: vi.fn().mockResolvedValue(undefined) },
      configurable: true,
    })
  })

  afterEach(() => {
    wrappers.splice(0).forEach((w) => w.unmount())
    document.body.innerHTML = ''
    sessionStorage.clear()
    localStorage.clear()
    vi.restoreAllMocks()
  })

  it('打开时展示临时密码一次,且不写入任何 Storage', async () => {
    const wrapper = mountDialog({ modelValue: true, password: PASSWORD })
    await flushPromises()

    const code = wrapper.get('[data-testid="temporary-password"]')
    expect(code.text()).toBe(PASSWORD)
    // 密码绝不进入持久化存储。
    expect(sessionStorage.length).toBe(0)
    expect(localStorage.length).toBe(0)
    expect(sessionStorage.getItem('anything')).toBeNull()
  })

  it('展示说明文字并提示仅显示一次', async () => {
    const wrapper = mountDialog({
      modelValue: true,
      password: PASSWORD,
      description: '用户「alice」创建成功',
    })
    await flushPromises()

    expect(wrapper.text()).toContain('用户「alice」创建成功')
    expect(wrapper.text()).toContain('仅显示这一次')
  })

  it('复制将临时密码写入剪贴板', async () => {
    const writeText = navigator.clipboard?.writeText as ReturnType<typeof vi.fn>
    const wrapper = mountDialog({ modelValue: true, password: PASSWORD })
    await flushPromises()

    await wrapper.get('[data-testid="temporary-password-copy"]').trigger('click')
    await flushPromises()

    expect(writeText).toHaveBeenCalledWith(PASSWORD)
  })

  it('点击「我已保存,关闭」后不可逆清除:透传关闭事件且密码不再渲染', async () => {
    const wrapper = mountDialog({ modelValue: true, password: PASSWORD })
    await flushPromises()
    expect(wrapper.get('[data-testid="temporary-password"]').text()).toBe(PASSWORD)

    await wrapper.get('[data-testid="temporary-password-confirm"]').trigger('click')
    await flushPromises()

    // 向父组件透传关闭;父组件同步 modelValue=false 后密码被清空。
    const emitted = wrapper.emitted('update:modelValue')
    expect(emitted?.at(-1)).toEqual([false])

    await wrapper.setProps({ modelValue: false })
    await flushPromises()
    // el-dialog 关闭后内容可能仍驻留 DOM(隐藏),但密码必须已不可逆清空。
    expect(wrapper.find('[data-testid="temporary-password"]').text()).toBe('')
    expect(wrapper.text()).not.toContain(PASSWORD)
  })

  it('父组件程序关闭同样清空密码(不可逆)', async () => {
    const wrapper = mountDialog({ modelValue: true, password: PASSWORD })
    await flushPromises()
    expect(wrapper.text()).toContain(PASSWORD)

    await wrapper.setProps({ modelValue: false })
    await flushPromises()

    expect(wrapper.find('[data-testid="temporary-password"]').text()).toBe('')
    expect(wrapper.text()).not.toContain(PASSWORD)
  })
})
