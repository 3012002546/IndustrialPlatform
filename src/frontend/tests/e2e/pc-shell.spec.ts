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

async function expectHeaderReadable(page: Page): Promise<void> {
  const viewport = page.viewportSize()
  expect(viewport).not.toBeNull()
  const header = page.locator('header.ip-topbar')
  const brand = page.locator('.ip-topbar__brand')
  const context = page.getByTestId('tenant-context')
  const search = page.getByTestId('command-search')
  const searchInput = search.locator('input')
  const right = page.locator('.ip-topbar__right')
  const user = page.getByTestId('user-menu')
  const theme = page.getByRole('button', { name: '主题' })

  await expect(page.locator('.ip-pc-brand .ip-brand__name')).toHaveCount(0)
  await expect(context).toHaveCSS('white-space', 'nowrap')
  await expect(user).toHaveCSS('white-space', 'nowrap')

  for (const locator of [header, brand, context, search, right, user]) {
    const box = await locator.boundingBox()
    expect(box).not.toBeNull()
    expect(box!.x).toBeGreaterThanOrEqual(0)
    expect(box!.x + box!.width).toBeLessThanOrEqual(viewport!.width)
  }
  const searchBox = await search.boundingBox()
  expect(searchBox).not.toBeNull()
  expect(searchBox!.width).toBeGreaterThanOrEqual(160)
  const userBox = await user.boundingBox()
  expect(userBox).not.toBeNull()
  expect(userBox!.width).toBeGreaterThanOrEqual(120)
  const headerBox = await header.boundingBox()
  const searchInputBox = await searchInput.boundingBox()
  const themeBox = await theme.boundingBox()
  expect(headerBox).not.toBeNull()
  expect(searchInputBox).not.toBeNull()
  expect(themeBox).not.toBeNull()
  expect(
    Math.abs(
      searchInputBox!.x + searchInputBox!.width / 2 - (headerBox!.x + headerBox!.width / 2),
    ),
  ).toBeLessThanOrEqual(2)
  expect(userBox!.x - (themeBox!.x + themeBox!.width)).toBeLessThanOrEqual(12)
}

test('四区结构:顶栏、工具轨、功能树与主内容区', async ({ page }) => {
  await login(page)
  await expect(page.locator('header.ip-topbar')).toBeVisible()
  await expect(page.getByRole('navigation', { name: '平台分组' })).toBeVisible()
  await expect(page.getByRole('navigation', { name: '工作台', exact: true })).toBeVisible()
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

test('更多入口常驻且菜单可选择真实一级分组', async ({ page }) => {
  await login(page)
  const more = page.getByTestId('toolrail-more')
  await expect(more).toBeVisible()
  await more.click()
  const menu = page.getByTestId('toolrail-more-menu')
  await expect(menu).toContainText('工作台')
  await expect(menu).toContainText('系统管理')
  await expect(menu.locator('svg').first()).toHaveCSS('width', '18px')
  await expect(menu.locator('svg').first()).toHaveCSS('height', '18px')
  await menu.getByRole('menuitem', { name: '系统管理' }).click()
  await expect(page.getByRole('navigation', { name: '系统管理' })).toBeVisible()
})

test('点击和 Ctrl+K 都打开同一个全局搜索并聚焦', async ({ page }) => {
  await login(page)
  const search = page.getByTestId('command-search')
  const input = search.locator('input')
  await input.click()
  await expect(input).toBeFocused()
  const results = page.locator('#platform-command-search-results')
  await expect(page.getByTestId('command-search-result').first()).toBeVisible()
  const resultsBox = await results.boundingBox()
  const viewport = page.viewportSize()
  expect(resultsBox).not.toBeNull()
  expect(viewport).not.toBeNull()
  expect(resultsBox!.x).toBeGreaterThanOrEqual(0)
  expect(resultsBox!.x + resultsBox!.width).toBeLessThanOrEqual(viewport!.width)
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(
    true,
  )
  await page.keyboard.press('Escape')
  await expect(input).toHaveAttribute('aria-expanded', 'false')
  await page.keyboard.press('Control+K')
  await expect(input).toBeFocused()
  await expect(input).toHaveAttribute('aria-expanded', 'true')
  await expect(search.getByTestId('command-search-shortcut')).toHaveText('Ctrl+K')
})

test('功能树收起/展开:保留图标入口且 aria-expanded 翻转', async ({ page }) => {
  await login(page)
  const toggle = page.getByTestId('function-tree-toggle')
  await expect(toggle).toHaveAttribute('aria-expanded', 'true')
  await toggle.click()
  await expect(toggle).toHaveAttribute('aria-expanded', 'false')
  await expect(toggle).toBeVisible()
  await expect(page.locator('nav.ip-function-tree')).toHaveCSS('width', '52px')
  await expect(page.locator('#ip-function-tree-list')).toBeVisible()
  await expect(page.locator('.ip-function-tree__label')).toHaveCount(0)
  await toggle.click()
  await expect(toggle).toHaveAttribute('aria-expanded', 'true')
  await expect(page.locator('#ip-function-tree-list')).toBeVisible()
})

test('两个 PC 目标视口无横向滚动并保存外壳截图', async ({ page }, testInfo) => {
  await page.setViewportSize({ width: 1280, height: 720 })
  await login(page)
  await expect(page.getByRole('link', { name: '首页' })).toBeVisible()
  await expectHeaderReadable(page)
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(
    true,
  )
  await page.screenshot({
    path: testInfo.outputPath('pc-shell-1280x720.png'),
    fullPage: true,
  })

  await page.setViewportSize({ width: 1440, height: 900 })
  await page.goto('/pc/home')
  await expect(page.getByRole('link', { name: '首页' })).toBeVisible()
  await expectHeaderReadable(page)
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(
    true,
  )
  await page.screenshot({
    path: testInfo.outputPath('pc-shell-1440x900.png'),
    fullPage: true,
  })
})
