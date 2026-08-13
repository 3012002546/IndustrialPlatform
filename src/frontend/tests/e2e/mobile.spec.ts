/**
 * Mobile 关键路径 E2E(FE-009,§17):
 * 显式 Mobile 路由可达、底部导航 Tab 切换与高亮、我的页用户信息与退出、
 * 44px 触控目标几何验收、目标视口(360×800 / 390×844)无横向滚动并截图、
 * 键盘操作与无任务/消息/审批假入口。Playwright 经 Vite dev server 提供应用。
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
  await page.getByRole('button', { name: '登录' }).click()
  await expect(page).toHaveURL(/\/pc\/home/)
}

test('显式 Mobile 路由可达并渲染业务空状态', async ({ page }) => {
  await login(page)
  await page.goto('/mobile/home')
  await expect(page).toHaveURL(/\/mobile\/home/)
  await expect(page.getByRole('heading', { name: '业务功能将在后续阶段接入' })).toBeVisible()
})

test('底部导航只含首页/我的两个 Tab,当前 Tab 高亮', async ({ page }) => {
  await login(page)
  await page.goto('/mobile/home')
  await expect(page.getByRole('link', { name: '首页' })).toHaveAttribute('aria-current', 'page')
  await expect(page.getByRole('link', { name: '我的' })).not.toHaveAttribute('aria-current', 'page')

  await page.getByRole('link', { name: '我的' }).click()
  await expect(page).toHaveURL(/\/mobile\/my/)
  await expect(page.getByRole('link', { name: '我的' })).toHaveAttribute('aria-current', 'page')
  await expect(page.getByRole('link', { name: '首页' })).not.toHaveAttribute('aria-current', 'page')

  await page.getByRole('link', { name: '首页' }).click()
  await expect(page).toHaveURL(/\/mobile\/home/)
})

test('「我的」页展示当前用户并可从底部导航返回首页', async ({ page }) => {
  await login(page)
  await page.goto('/mobile/home')
  await page.getByRole('link', { name: '我的' }).click()
  await expect(page.getByRole('heading', { name: '我的' })).toBeVisible()
  await expect(page.getByTestId('display-name')).toContainText('Mock 演示账号')
  await expect(page.getByTestId('username')).toContainText('mock.admin')
  await page.getByRole('link', { name: '首页' }).click()
  await expect(page).toHaveURL(/\/mobile\/home/)
})

test('Mobile 首页不出现任务/消息/审批等可点击假入口', async ({ page }) => {
  await login(page)
  await page.goto('/mobile/home')
  for (const name of ['任务', '消息', '审批']) {
    await expect(page.getByRole('button', { name })).toHaveCount(0)
    await expect(page.getByRole('link', { name })).toHaveCount(0)
  }
})

test('44px 触控目标:主题入口、底部导航 Tab 与退出按钮几何高度均不小于 44', async ({ page }) => {
  await login(page)
  await page.goto('/mobile/my')
  const theme = await page.getByTestId('theme-control-trigger').boundingBox()
  expect(theme).not.toBeNull()
  expect(theme!.width).toBeGreaterThanOrEqual(44)
  expect(theme!.height).toBeGreaterThanOrEqual(44)
  for (const link of ['首页', '我的']) {
    const box = await page.getByRole('link', { name: link }).boundingBox()
    expect(box).not.toBeNull()
    expect(box!.height).toBeGreaterThanOrEqual(44)
  }
  const logout = await page.getByTestId('logout-button').boundingBox()
  expect(logout).not.toBeNull()
  expect(logout!.height).toBeGreaterThanOrEqual(44)
})

test('键盘操作:Tab 依次可达跳过链接、主题入口与底部导航,Enter 触发「我的」', async ({ page }) => {
  await login(page)
  await page.goto('/mobile/home')
  // 顶栏右区主题入口先于底部导航:跳过链接 → 主题入口 → 底部导航「首页」→「我的」
  await page.keyboard.press('Tab')
  await expect(page.getByRole('link', { name: '跳到主内容' })).toBeFocused()
  await page.keyboard.press('Tab')
  await expect(page.getByTestId('theme-control-trigger')).toBeFocused()
  await page.keyboard.press('Tab')
  await expect(page.getByRole('link', { name: '首页' })).toBeFocused()
  await page.keyboard.press('Tab')
  await expect(page.getByRole('link', { name: '我的' })).toBeFocused()
  await page.keyboard.press('Enter')
  await expect(page).toHaveURL(/\/mobile\/my/)
})

test('Mobile 目标视口无横向滚动并保存截图', async ({ page }) => {
  await login(page)

  // 360×800
  await page.setViewportSize({ width: 360, height: 800 })
  await page.goto('/mobile/home')
  await expect(page.getByRole('heading', { name: '业务功能将在后续阶段接入' })).toBeVisible()
  // 显式路由 meta.terminal='mobile' 是终端文案单事实源:无 override 时显示 Mobile(PF-01 §7.11)。
  await expect(page.getByTestId('terminal-info')).toContainText('Mobile')
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(
    true,
  )
  await page.screenshot({ path: 'tests/e2e/screenshots/mobile-home-360x800.png', fullPage: true })

  // 390×844
  await page.setViewportSize({ width: 390, height: 844 })
  await page.goto('/mobile/home')
  await expect(page.getByRole('heading', { name: '业务功能将在后续阶段接入' })).toBeVisible()
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(
    true,
  )
  await page.screenshot({ path: 'tests/e2e/screenshots/mobile-home-390x844.png', fullPage: true })
})

test('「我的」页退出登录回到登录页', async ({ page }) => {
  await login(page)
  await page.goto('/mobile/home')
  await page.getByRole('link', { name: '我的' }).click()
  await page.getByRole('button', { name: '退出登录' }).click()
  await expect(page).toHaveURL(/\/login/)
})
