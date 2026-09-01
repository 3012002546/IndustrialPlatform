/**
 * PDA 关键路径 E2E(FE-008,§16):
 * 显式 PDA 路由可达、现场任务空状态、无扫码/称量/工单等不可用业务按钮、
 * 48px 触控目标几何验收、横竖屏目标视口(480×800 / 800×480)无横向滚动并截图、
 * 键盘操作与退出。Playwright 经 Vite dev server 提供应用(同 pc.spec.ts)。
 */

import { expect, test, type Page } from '@playwright/test'

const VALID_USER = 'mock.admin'
const VALID_PASS = 'Mock@123456'

async function fillLogin(page: Page): Promise<void> {
  await page.getByLabel('用户名').fill(VALID_USER)
  // 密码字段标签与「显示密码」切换按钮 aria-label 子串冲突,需精确匹配输入框
  await page.getByLabel('密码', { exact: true }).fill(VALID_PASS)
}

async function login(page: Page): Promise<void> {
  await page.goto('/login')
  await fillLogin(page)
  await page.getByTestId('login-submit').click()
  await expect(page).toHaveURL(/\/pc\/home/)
}

test('显式 PDA 路由可达并渲染现场任务空状态', async ({ page }) => {
  await login(page)
  await page.goto('/pda/home')
  await expect(page).toHaveURL(/\/pda\/home/)
  // 标题与描述均含该文案,用 heading role 精确定位空状态标题(避免 strict mode 冲突)
  await expect(page.getByRole('heading', { name: '现场任务将在业务阶段接入' })).toBeVisible()
})

test('PDA 首页不出现扫码/称量/工单等不可用业务按钮', async ({ page }) => {
  await login(page)
  await page.goto('/pda/home')
  // 免责文案可提及暂缓能力,但不得渲染为可点击的按钮/链接(§16:无伪业务入口)
  for (const name of ['扫码', '称量', '工单']) {
    await expect(page.getByRole('button', { name })).toHaveCount(0)
    await expect(page.getByRole('link', { name })).toHaveCount(0)
  }
})

test('顶栏提供返回/首页/退出三个触控入口', async ({ page }) => {
  await login(page)
  await page.goto('/pda/home')
  await expect(page.getByTestId('back-button')).toBeVisible()
  await expect(page.getByTestId('home-button')).toBeVisible()
  await expect(page.getByTestId('logout-button')).toBeVisible()
})

test('48px 触控目标:返回/首页/主题/退出按钮几何尺寸均不小于 48×48', async ({ page }) => {
  await login(page)
  await page.goto('/pda/home')
  for (const testid of ['back-button', 'home-button', 'theme-control-trigger', 'logout-button']) {
    const box = await page.getByTestId(testid).boundingBox()
    expect(box).not.toBeNull()
    expect(box!.width).toBeGreaterThanOrEqual(48)
    expect(box!.height).toBeGreaterThanOrEqual(48)
  }
})

test('键盘操作:Tab 依次可达跳过链接与返回按钮,Enter 触发首页入口', async ({ page }) => {
  await login(page)
  await page.goto('/pda/home')
  await page.keyboard.press('Tab')
  await expect(page.getByRole('link', { name: '跳到主内容' })).toBeFocused()
  await page.keyboard.press('Tab')
  await expect(page.getByTestId('back-button')).toBeFocused()
  await page.getByTestId('home-button').focus()
  await page.keyboard.press('Enter')
  await expect(page).toHaveURL(/\/pda\/home/)
})

test('PDA 横竖屏目标视口无横向滚动并保存截图', async ({ page }) => {
  await login(page)

  // 竖屏 480×800
  await page.setViewportSize({ width: 480, height: 800 })
  await page.goto('/pda/home')
  await expect(page.getByRole('heading', { name: '现场任务将在业务阶段接入' })).toBeVisible()
  // 480px 宽度自动识别为 Mobile 断点(§11.1),但显式路由 meta.terminal='pda'
  // 是终端文案单事实源:无 override 时必须仍显示 PDA(PF-01 §7.11)。
  await expect(page.getByTestId('terminal-info')).toContainText('PDA')
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(
    true,
  )
  await page.screenshot({ path: 'tests/e2e/screenshots/pda-home-480x800.png', fullPage: true })

  // 横屏 800×480
  await page.setViewportSize({ width: 800, height: 480 })
  await page.goto('/pda/home')
  await expect(page.getByRole('heading', { name: '现场任务将在业务阶段接入' })).toBeVisible()
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(
    true,
  )
  await page.screenshot({ path: 'tests/e2e/screenshots/pda-home-800x480.png', fullPage: true })
})

test('PDA 退出登录回到登录页', async ({ page }) => {
  await login(page)
  await page.goto('/pda/home')
  await page.getByRole('button', { name: '退出登录' }).click()
  await expect(page).toHaveURL(/\/login/)
})
