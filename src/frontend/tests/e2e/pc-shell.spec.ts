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
  const mode = page.getByTestId('pc-experience-mode-control')
  const theme = page.getByRole('button', { name: '主题' })
  const locale = page.locator('.ip-locale-control')
  const fullscreen = page.getByTestId('browser-fullscreen')
  const actions = page.locator('.ip-topbar__actions')

  await expect(page.locator('.ip-pc-brand .ip-brand__name')).toHaveCount(0)
  await expect(context).toHaveCSS('white-space', 'nowrap')
  await expect(user).toHaveCSS('white-space', 'nowrap')

  for (const locator of [header, brand, context, search, right, user, mode, theme, locale, fullscreen]) {
    await expect(locator).toBeVisible()
    const box = await locator.boundingBox()
    expect(box).not.toBeNull()
    expect(box!.x).toBeGreaterThanOrEqual(0)
    expect(box!.x + box!.width).toBeLessThanOrEqual(viewport!.width)
  }
  const searchBox = await search.boundingBox()
  expect(searchBox).not.toBeNull()
  expect(searchBox!.width).toBeGreaterThanOrEqual(160)
  const headerBox = await header.boundingBox()
  expect(headerBox).not.toBeNull()
  expect(searchBox!.y).toBeGreaterThanOrEqual(headerBox!.y)
  expect(searchBox!.y + searchBox!.height).toBeLessThanOrEqual(headerBox!.y + headerBox!.height)
  const userBox = await user.boundingBox()
  const modeBox = await mode.boundingBox()
  expect(userBox).not.toBeNull()
  expect(modeBox).not.toBeNull()
  expect(userBox!.width).toBeGreaterThanOrEqual(120)
  expect(modeBox!.x).toBeGreaterThanOrEqual(searchBox!.x + searchBox!.width)
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
  await expect(actions).not.toHaveCSS('overflow', 'hidden')

  for (const tool of await actions.locator('button').all()) {
    if (!(await tool.isVisible())) continue
    const toolBox = await tool.boundingBox()
    expect(toolBox).not.toBeNull()
    const hit = await tool.evaluate((element) => {
      const rect = element.getBoundingClientRect()
      const target = document.elementFromPoint(rect.left + rect.width / 2, rect.top + rect.height / 2)
      return target === element || element.contains(target)
    })
    expect(hit).toBe(true)
  }
}

async function expectHeaderToolsUsable(page: Page): Promise<void> {
  const viewport = page.viewportSize()
  expect(viewport).not.toBeNull()
  const header = page.locator('header.ip-topbar')
  const search = page.getByTestId('command-search')
  const searchInput = search.locator('input')
  const actions = page.locator('.ip-topbar__actions')
  const user = page.getByTestId('user-menu')
  const status = page.getByTestId('platform-service-status')

  for (const locator of [header, search, actions, user]) {
    await expect(locator).toBeVisible()
    const box = await locator.boundingBox()
    expect(box).not.toBeNull()
    expect(box!.x).toBeGreaterThanOrEqual(0)
    expect(box!.x + box!.width).toBeLessThanOrEqual(viewport!.width)
  }
  const headerBox = await header.boundingBox()
  const searchBox = await search.boundingBox()
  expect(headerBox).not.toBeNull()
  expect(searchBox).not.toBeNull()
  expect(searchBox!.width).toBeGreaterThan(0)
  expect(searchBox!.y).toBeGreaterThanOrEqual(headerBox!.y)
  expect(searchBox!.y + searchBox!.height).toBeLessThanOrEqual(headerBox!.y + headerBox!.height)
  await expect(actions).not.toHaveCSS('overflow', 'hidden')

  if (await status.isVisible()) {
    const statusBox = await status.boundingBox()
    expect(statusBox).not.toBeNull()
    expect(statusBox!.x + statusBox!.width).toBeLessThanOrEqual(viewport!.width)
  }

  for (const tool of await actions.locator('button').all()) {
    if (!(await tool.isVisible())) continue
    const toolBox = await tool.boundingBox()
    expect(toolBox).not.toBeNull()
    expect(toolBox!.width).toBeGreaterThan(0)
    expect(toolBox!.height).toBeGreaterThan(0)
    const hit = await tool.evaluate((element) => {
      const rect = element.getBoundingClientRect()
      const target = document.elementFromPoint(rect.left + rect.width / 2, rect.top + rect.height / 2)
      return target === element || element.contains(target)
    })
    expect(hit).toBe(true)
  }
  await searchInput.click()
  await expect(searchInput).toBeFocused()
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

test('宽屏搜索保持几何居中并完整提供长提示', async ({ page }) => {
  await page.setViewportSize({ width: 2048, height: 1090 })
  await login(page)
  await expectHeaderReadable(page)
  await expect(page.getByTestId('command-search').locator('input')).toHaveAttribute(
    'placeholder',
    '搜索已授权菜单、最近访问或快捷命令',
  )
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(
    true,
  )
})

test('窗口连续缩小时搜索与账号仍避让且不产生文档横向滚动', async ({ page }) => {
  await page.setViewportSize({ width: 1024, height: 768 })
  await login(page)
  await expectHeaderReadable(page)
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(
    true,
  )
})

test('英文、长账号与连续窄屏下搜索仍可键盘/点击且工具可命中', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 720 })
  await login(page)
  await page.getByRole('button', { name: '语言' }).click()
  await page.getByRole('option', { name: 'English' }).click()
  await expect(page.locator('html')).toHaveAttribute('lang', 'en-US')
  await page.locator('[data-testid="user-menu"] .ip-pc-user__name').evaluate((element) => {
    element.textContent = 'Long display account used for shell layout verification'
  })

  for (const width of [1280, 1200, 1024, 900]) {
    await page.setViewportSize({ width, height: 720 })
    await expectHeaderToolsUsable(page)
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(
      true,
    )
  }
})

test('功能树筛选无结果后收起仍能访问授权入口', async ({ page }) => {
  await login(page)
  const treeSearch = page.locator('.ip-function-tree__search')
  await treeSearch.fill('不存在的菜单')
  await expect(page.locator('nav.ip-function-tree a.ip-function-tree__link')).toHaveCount(0)
  await page.getByTestId('function-tree-toggle').click()
  await expect(page.locator('nav.ip-function-tree')).toHaveCSS('width', '52px')
  await expect(page.locator('nav.ip-function-tree a.ip-function-tree__link').first()).toBeVisible()
})

test('用户菜单使用紧凑的 Element Plus 面板与统一命令行高', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await login(page)
  await page.getByTestId('user-menu').click()
  const menu = page.locator('.ip-pc-user-popper')
  await expect(menu).toBeVisible()
  await expect(menu).toHaveCSS('width', '192px')
  const items = menu.locator('.el-dropdown-menu__item')
  await expect(items).toHaveCount(4)
  await expect(menu.locator('.ip-pc-user-menu__summary')).toContainText('Mock 演示账号')
  await expect(menu.locator('.ip-pc-user-menu__summary')).toContainText('mock.admin')
  await expect(menu.locator('.ip-pc-user-menu__summary')).toContainText('dev-tenant')
  for (const item of await items.all()) {
    await expect(item).toHaveCSS('height', '36px')
    await expect(item).toHaveCSS('font-size', '13px')
    await expect(item.locator('svg')).toHaveCSS('width', '16px')
    await expect(item.locator('svg')).toHaveCSS('height', '16px')
  }
  const menuBox = await menu.boundingBox()
  expect(menuBox).not.toBeNull()
  expect(menuBox!.x + menuBox!.width).toBeLessThanOrEqual(1440)
})
