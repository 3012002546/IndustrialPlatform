/**
 * PC 平台外壳 E2E(PF-01 §6.1~6.3、§7.8):
 * 四区结构(顶栏/工具轨/功能树/主内容)、跳过链接首焦点、工具轨分组切换联动功能树、
 * 功能树收起/展开(ThemeStore)、mock 账号无 identity.* 权限时系统分组功能树为空、
 * 两个 PC 目标视口无横向滚动并保存外壳截图。
 * Playwright 经 Vite dev server 提供应用(同 pc.spec.ts)。
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

test('四区结构:顶栏、工具轨、功能树与主内容区', async ({ page }) => {
  await login(page)
  await expect(page.locator('header.ip-topbar')).toBeVisible()
  await expect(page.getByRole('navigation', { name: '平台分组' })).toBeVisible()
  await expect(page.getByRole('navigation', { name: '工作台' })).toBeVisible()
  await expect(page.locator('main#main-content')).toBeVisible()
  // 工作台分组授权项(首页)渲染
  await expect(page.getByRole('link', { name: '首页' })).toBeVisible()
})

test('跳过链接是首个可聚焦元素,Enter 跳到主内容', async ({ page }) => {
  await login(page)
  await page.keyboard.press('Tab')
  const skip = page.getByRole('link', { name: '跳到主内容' })
  await expect(skip).toBeFocused()
  await page.keyboard.press('Enter')
  await expect(page.locator('main#main-content')).toBeFocused()
})

test('工具轨切换分组联动功能树;mock 无 identity 权限时系统分组为空', async ({ page }) => {
  await login(page)
  await page.getByRole('button', { name: '系统管理' }).click()
  await expect(page.getByRole('navigation', { name: '系统管理' })).toBeVisible()
  // 当前分组按钮标记 aria-current
  await expect(page.getByRole('button', { name: '系统管理' })).toHaveAttribute(
    'aria-current',
    'page',
  )
  // mock.admin 仅 platform.* 权限,identity.* 菜单全部被授权过滤
  await expect(page.locator('nav.ip-function-tree a.ip-function-tree__link')).toHaveCount(0)
})

test('功能树收起/展开:列表隐藏且 aria-expanded 翻转', async ({ page }) => {
  await login(page)
  const toggle = page.getByTestId('function-tree-toggle')
  await expect(toggle).toHaveAttribute('aria-expanded', 'true')
  await toggle.click()
  await expect(toggle).toHaveAttribute('aria-expanded', 'false')
  await expect(page.locator('#ip-function-tree-list')).toBeHidden()
  await toggle.click()
  await expect(toggle).toHaveAttribute('aria-expanded', 'true')
  await expect(page.locator('#ip-function-tree-list')).toBeVisible()
})

test('两个 PC 目标视口无横向滚动并保存外壳截图', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 720 })
  await login(page)
  await expect(page.getByRole('link', { name: '首页' })).toBeVisible()
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(
    true,
  )
  await page.screenshot({
    path: 'tests/e2e/screenshots/pc-shell-1280x720.png',
    fullPage: true,
  })

  await page.setViewportSize({ width: 1440, height: 900 })
  await page.goto('/pc/home')
  await expect(page.getByRole('link', { name: '首页' })).toBeVisible()
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(
    true,
  )
  await page.screenshot({
    path: 'tests/e2e/screenshots/pc-shell-1440x900.png',
    fullPage: true,
  })
})
