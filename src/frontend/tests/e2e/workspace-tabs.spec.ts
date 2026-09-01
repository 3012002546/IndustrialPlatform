/**
 * PC 工作区标签 E2E(PF-01 §7.9/§10.1):
 * 12→13 SPA 阻断并展示上限对话框、整页直达被阻断路由兜底工作台+对话框、
 * 对话框复用现有标签、关闭后打开被阻断的新页面、刷新后从存储恢复业务标签、
 * 无权限业务标签恢复时被 prune 丢弃。
 * 依赖 DEV 专用沙箱路由 `/pc/dev/workspace-tabs?slot=N`(Vite dev server 提供,生产不含);
 * 沙箱页内「槽 N」链接提供 SPA 导航入口。沙箱页标签标题统一为路由 meta「工作区沙箱」,
 * 身份断言走页面内容(沙箱 · N)与计数。
 */

import { expect, test, type Page } from '@playwright/test'

const VALID_USER = 'mock.admin'
const VALID_PASS = 'Mock@123456'
/** mock.admin 会话 scope(与 MockAuthGateway 的 user 一致)。 */
const TABS_KEY = 'industrial-platform.pc.tabs.v1:dev-tenant:mock-admin-0001'

async function fillLogin(page: Page): Promise<void> {
  await page.getByLabel('用户名').fill(VALID_USER)
  await page.getByLabel('密码', { exact: true }).fill(VALID_PASS)
}

async function login(page: Page): Promise<void> {
  await page.goto('/login')
  await fillLogin(page)
  await page.getByTestId('login-submit').click()
  await expect(page).toHaveURL(/\/pc\/home/)
}

/** 逐个打开业务标签(全量刷新以走真实守卫恢复 + 登记)。 */
async function openSlots(page: Page, slots: number[]): Promise<void> {
  for (const slot of slots) {
    await page.goto(`/pc/dev/workspace-tabs?slot=${slot}`)
    await expect(page.getByTestId('workspace-tabs-sandbox')).toContainText(`沙箱 · ${slot}`)
  }
}

test('第 13 个业务标签被阻断并展示上限对话框', async ({ page }) => {
  await login(page)
  await openSlots(page, [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11])
  // 固定工作台 + 12 业务标签
  await expect(page.getByRole('tab')).toHaveCount(13)

  // SPA 内点击「槽 12」:导航被阻断,停留 slot=11 页面
  await page.getByRole('link', { name: '槽 12' }).click()
  await expect(page.getByTestId('workspace-tabs-sandbox')).toContainText('沙箱 · 11')
  await expect(page.getByText('业务标签已达上限')).toBeVisible()
  await expect(page.getByRole('tab')).toHaveCount(13)
})

test('整页直达被阻断路由:兜底固定工作台并展示上限对话框', async ({ page }) => {
  await login(page)
  await openSlots(page, [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11])
  await page.goto('/pc/dev/workspace-tabs?slot=12')
  // 初始导航被阻断 → 守卫兜底到 /pc/home,对话框仍展示
  await expect(page).toHaveURL(/\/pc\/home/)
  await expect(page.getByText('业务标签已达上限')).toBeVisible()
  await expect(page.getByRole('tab')).toHaveCount(13)
})

test('上限对话框复用现有标签', async ({ page }) => {
  await login(page)
  await openSlots(page, [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11])
  await page.getByRole('link', { name: '槽 12' }).click()
  await expect(page.getByText('业务标签已达上限')).toBeVisible()
  // 默认选中第一个业务标签(slot=0),复用后导航到该标签
  await page.getByRole('button', { name: '复用选中标签' }).click()
  await expect(page.getByTestId('workspace-tabs-sandbox')).toContainText('沙箱 · 0')
  await expect(page.getByText('业务标签已达上限')).toBeHidden()
  await expect(page.getByRole('tab')).toHaveCount(13)
})

test('上限对话框关闭选中后打开被阻断的新页面', async ({ page }) => {
  await login(page)
  await openSlots(page, [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11])
  await page.getByRole('link', { name: '槽 12' }).click()
  await expect(page.getByText('业务标签已达上限')).toBeVisible()
  await page.getByRole('button', { name: '关闭选中后打开' }).click()
  // 被阻断的 slot=12 现在打开;释放一个槽位后业务标签仍为 12
  await expect(page.getByTestId('workspace-tabs-sandbox')).toContainText('沙箱 · 12')
  await expect(page.getByText('业务标签已达上限')).toBeHidden()
  await expect(page.getByRole('tab')).toHaveCount(13)
})

test('刷新后从存储恢复业务标签', async ({ page }) => {
  await login(page)
  await page.goto('/pc/dev/workspace-tabs?slot=3')
  await expect(page.getByTestId('workspace-tabs-sandbox')).toContainText('沙箱 · 3')
  await expect(page.getByRole('tab')).toHaveCount(2)

  await page.reload()
  // 恢复 slot=3 标签并保持激活
  await expect(page.getByTestId('workspace-tabs-sandbox')).toContainText('沙箱 · 3')
  await expect(page.getByRole('tab')).toHaveCount(2)
  await expect(page.getByRole('tab', { name: '工作区沙箱' })).toHaveAttribute(
    'aria-selected',
    'true',
  )

  // 关闭恢复的业务标签 → 回到固定工作台
  await page.getByRole('button', { name: '关闭 工作区沙箱' }).click()
  await expect(page.getByRole('tab')).toHaveCount(1)
  await expect(page).toHaveURL(/\/pc\/home/)
})

test('无权限业务标签在恢复时被 prune 丢弃', async ({ page }) => {
  await page.addInitScript((key) => {
    localStorage.setItem(
      key,
      JSON.stringify({
        version: 1,
        tabs: [
          {
            id: 'pc-home',
            title: '工作台',
            kind: 'fixed',
            route: { name: 'pc-home', params: {}, query: {} },
            reloadVersion: 0,
          },
          {
            id: 'identity-users',
            title: '用户管理',
            kind: 'business',
            route: { name: 'identity-users', params: {}, query: {} },
            reloadVersion: 1,
          },
          {
            id: 'workspace-tabs-sandbox&q=slot=5',
            title: '工作区沙箱',
            kind: 'business',
            route: { name: 'workspace-tabs-sandbox', params: {}, query: { slot: '5' } },
            reloadVersion: 1,
          },
        ],
        activeTabId: 'identity-users',
        updatedAt: '2026-08-12T00:00:00.000Z',
      }),
    )
  }, TABS_KEY)
  await login(page)
  // mock.admin 无 identity.user.view:用户管理标签被丢弃,沙箱标签保留
  await expect(page.getByRole('tab', { name: '用户管理' })).toHaveCount(0)
  await expect(page.getByRole('tab', { name: '工作区沙箱' })).toHaveCount(1)
  await expect(page.getByRole('tab')).toHaveCount(2)
})
