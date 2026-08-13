/**
 * 业务标签上限对话框测试(PF-01 §7.9/§10.1):
 * pending 非空时展示标题/提示/现有业务标签,默认选中第一个;
 * 复用/关闭后打开/取消 三决议 emit 到 resolve;关闭(Esc/遮罩/×)统一按取消处理。
 * ElDialog 内容 teleport 到 body,按钮经 document.body 查询驱动。
 */

import { enableAutoUnmount, mount } from '@vue/test-utils'
import { ElDialog } from 'element-plus'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { nextTick } from 'vue'

import WorkspaceTabLimitDialog from '@/components/shell/WorkspaceTabLimitDialog.vue'
import { useWorkspaceTabsStore } from '@/stores/workspaceTabsStore'
import { MAX_BUSINESS_TABS } from '@/workspace'
import type { TabLimitResolution, WorkspaceRouteCandidate } from '@/workspace'

function sandboxCandidate(slot: number): WorkspaceRouteCandidate {
  return {
    id: `sandbox:${slot}`,
    title: `沙箱 ${slot}`,
    kind: 'business',
    route: { name: 'workspace-tabs-sandbox', params: {}, query: { slot: String(slot) } },
  }
}

function buttonByText(text: string): HTMLElement | undefined {
  return Array.from(document.body.querySelectorAll<HTMLButtonElement>('button')).find((b) =>
    b.textContent?.includes(text),
  )
}

enableAutoUnmount(afterEach)

describe('WorkspaceTabLimitDialog', () => {
  let pinia: ReturnType<typeof createPinia>
  let tabsStore: ReturnType<typeof useWorkspaceTabsStore>

  beforeEach(() => {
    pinia = createPinia()
    setActivePinia(pinia)
    localStorage.clear()
    document.body.innerHTML = ''
    tabsStore = useWorkspaceTabsStore()
    tabsStore.bindUser({ tenantId: 't1', userId: 'u1' })
    for (let i = 0; i < MAX_BUSINESS_TABS; i += 1) tabsStore.requestOpen(sandboxCandidate(i))
    tabsStore.requestOpen(sandboxCandidate(MAX_BUSINESS_TABS)) // 触发 pending
  })

  afterEach(() => {
    // enableAutoUnmount 已兜底;清理 teleport/inline 残留
    document.body.innerHTML = ''
  })

  async function mountDialog() {
    // ElDialog 默认内联渲染(非 append-to-body),attachTo body 使内容进入 document.body
    const wrapper = mount(WorkspaceTabLimitDialog, {
      global: { plugins: [pinia] },
      attachTo: document.body,
    })
    await nextTick()
    await nextTick()
    return wrapper
  }

  it('pending 非空时展示标题、提示与现有业务标签', async () => {
    await mountDialog()
    expect(document.body.textContent).toContain('业务标签已达上限')
    expect(document.body.textContent).toContain('12 个上限')
    for (let i = 0; i < 3; i += 1) {
      expect(document.body.textContent).toContain(`沙箱 ${i}`)
    }
  })

  it('复用选中标签 → resolve({ action: reuse })', async () => {
    const wrapper = await mountDialog()
    buttonByText('复用选中标签')!.click()
    await nextTick()
    const resolution = wrapper.emitted('resolve')?.[0]?.[0] as TabLimitResolution
    expect(resolution).toEqual({ action: 'reuse', tabId: tabsStore.businessTabs[0]?.id })
  })

  it('关闭选中后打开 → resolve({ action: close-and-open })', async () => {
    const wrapper = await mountDialog()
    buttonByText('关闭选中后打开')!.click()
    await nextTick()
    const resolution = wrapper.emitted('resolve')?.[0]?.[0] as TabLimitResolution
    expect(resolution).toEqual({
      action: 'close-and-open',
      tabId: tabsStore.businessTabs[0]?.id,
    })
  })

  it('取消 → resolve({ action: cancel })', async () => {
    const wrapper = await mountDialog()
    buttonByText('取消')!.click()
    await nextTick()
    const resolution = wrapper.emitted('resolve')?.[0]?.[0] as TabLimitResolution
    expect(resolution).toEqual({ action: 'cancel' })
  })

  it('pending 清空后对话框 modelValue 关闭(关闭过渡由 jsdom 不触发,断言绑定态)', async () => {
    const wrapper = await mountDialog()
    expect(wrapper.findComponent(ElDialog).props('modelValue')).toBe(true)
    tabsStore.resolvePending({ action: 'cancel' })
    await nextTick()
    expect(tabsStore.pending).toBeNull()
    expect(wrapper.findComponent(ElDialog).props('modelValue')).toBe(false)
  })
})
