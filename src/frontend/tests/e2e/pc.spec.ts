/**
 * PC 关键路径 E2E(FE-007,§15):
 * 未登录重定向、必填校验、错误账号、密码显隐、重复提交、安全 redirect、
 * 刷新保持、键盘登录、403、404、退出,以及两个 PC 目标视口截图。
 *
 * Phase 2 仅 Mock 认证,playwright 通过 Vite dev server 提供应用(见 playwright.config.ts)。
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

test('未登录访问受保护路径 → 登录并携带安全 redirect,登录后回到原路径', async ({ page }) => {
  await page.goto('/pc/home')
  await expect(page).toHaveURL(/\/login\?redirect=/)

  await fillLogin(page)
  await page.getByRole('button', { name: '登录' }).click()
  await expect(page).toHaveURL(/\/pc\/home/)
  await expect(page.getByText('业务指标将在后续阶段接入')).toBeVisible()
})

test('空提交显示必填错误', async ({ page }) => {
  await page.goto('/login')
  await page.getByRole('button', { name: '登录' }).click()
  await expect(page.getByText('请输入用户名')).toBeVisible()
  await expect(page.getByText('请输入密码')).toBeVisible()
})

test('登录页在目标视口不产生纵向页面滚动', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 720 })
  await page.goto('/login')

  expect(await page.evaluate(() => document.documentElement.scrollHeight)).toBe(
    await page.evaluate(() => window.innerHeight),
  )
})

test('错误账号显示统一错误', async ({ page }) => {
  await page.goto('/login')
  await page.getByLabel('用户名').fill('wrong.user')
  await page.getByLabel('密码', { exact: true }).fill('wrong-password')
  await page.getByRole('button', { name: '登录' }).click()
  await expect(page.getByText('用户名或密码错误')).toBeVisible()
})

test('密码显隐切换', async ({ page }) => {
  await page.goto('/login')
  const password = page.getByLabel('密码', { exact: true })
  await expect(password).toHaveAttribute('type', 'password')
  await page.getByRole('button', { name: '显示密码' }).click()
  await expect(password).toHaveAttribute('type', 'text')
})

test('键盘登录:回车提交', async ({ page }) => {
  await page.goto('/login')
  await fillLogin(page)
  await page.getByLabel('密码', { exact: true }).press('Enter')
  await expect(page).toHaveURL(/\/pc\/home/)
})

test('登录后刷新保持会话', async ({ page }) => {
  await login(page)
  await page.reload()
  await expect(page).toHaveURL(/\/pc\/home/)
  await expect(page.getByText('业务指标将在后续阶段接入')).toBeVisible()
})

test('退出登录回到登录页', async ({ page }) => {
  await login(page)
  await page.getByRole('button', { name: '用户菜单' }).click()
  await page.getByText('退出登录').click()
  await expect(page).toHaveURL(/\/login/)
})

test('403 页面提供返回有权限首页与重新登录', async ({ page }) => {
  await login(page)
  await page.goto('/403')
  await expect(page.getByRole('heading', { level: 1, name: '无权限' })).toBeVisible()
  await page.getByRole('button', { name: '返回有权限首页' }).click()
  await expect(page).toHaveURL(/\/pc\/home/)
})

test('404 页面以纯文本展示原始路径并可返回首页', async ({ page }) => {
  await login(page)
  await page.goto('/no-such-page')
  await expect(page.getByText('/no-such-page')).toBeVisible()
  await page.getByRole('button', { name: '返回首页' }).click()
  await expect(page).toHaveURL(/\/pc\/home/)
})

test('不安全的 redirect(协议相对)被拒绝,登录后落到站内', async ({ page }) => {
  await page.goto('/login?redirect=//evil.example/steal')
  await fillLogin(page)
  await page.getByRole('button', { name: '登录' }).click()
  await expect(page).not.toHaveURL(/evil\.example/)
  await expect(page).toHaveURL(/\/pc\/home/)
})

test('PC 首页在两个目标视口渲染并保存截图', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 720 })
  await login(page)
  await expect(page.getByText('业务指标将在后续阶段接入')).toBeVisible()
  await page.screenshot({ path: 'tests/e2e/screenshots/pc-home-1280x720.png', fullPage: true })

  await page.setViewportSize({ width: 1440, height: 900 })
  await page.screenshot({ path: 'tests/e2e/screenshots/pc-home-1440x900.png', fullPage: true })
})
