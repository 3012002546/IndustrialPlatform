/**
 * Users 管理页视觉黄金样板(PF-03 §7):只走真实 Identity 数据，不创建本地假服务/假用户。
 * 使用 playwright.real.config.ts 执行；Mock 配置明确排除本文件。
 */

import { expect, test, type Page } from '@playwright/test'

const E2E_ADMIN = 'e2e.admin'
const E2E_PASSWORD = 'E2e!Admin@2026'

async function login(page: Page): Promise<void> {
  await page.goto('/login')
  await page.getByTestId('login-username').fill(E2E_ADMIN)
  await page.getByTestId('login-password').fill(E2E_PASSWORD)
  await page.getByTestId('login-submit').click()
  await expect(page).toHaveURL(/\/pc\/home/, { timeout: 60_000 })
}

test('用户管理黄金页:共享结构、键盘路径与窄窗口', async ({ page }, testInfo) => {
  await login(page)
  await page.setViewportSize({ width: 1280, height: 720 })
  await page.goto('/pc/identity/users')

  await expect(page).toHaveURL(/\/pc\/identity\/users$/)
  await expect(page.getByTestId('identity-users-page')).toBeVisible({ timeout: 60_000 })
  await expect(page.getByRole('heading', { level: 1, name: '用户管理' })).toBeVisible()
  await expect(page.getByTestId('identity-users-query')).toBeVisible()
  await expect(page.getByTestId('app-data-table')).toBeVisible()
  await expect(page.locator('.vxe-table--header-wrapper .vxe-header--column').first()).toHaveCSS(
    'height',
    '38px',
  )
  await expect(page.getByRole('button', { name: '新建用户' })).toBeVisible()
  await expect(page.getByTestId('identity-users-create').locator('svg')).toHaveCSS(
    'width',
    '14px',
  )
  await expect(page.getByTestId('identity-users-create').locator('svg')).toHaveCSS(
    'height',
    '14px',
  )

  const headerGeometry = await page.evaluate(() => {
    const search = document.querySelector<HTMLElement>('.ip-topbar__search')?.getBoundingClientRect()
    const mode = document.querySelector<HTMLElement>('[data-testid="pc-experience-mode-control"]')?.getBoundingClientRect()
    const user = document.querySelector<HTMLElement>('[data-testid="user-menu"]')?.getBoundingClientRect()
    const query = document.querySelector<HTMLElement>('[data-testid="identity-users-query"]')?.getBoundingClientRect()
    const content = document.querySelector<HTMLElement>('.ip-pc-content')?.getBoundingClientRect()
    const pager = document.querySelector<HTMLElement>('.app-data-table__pagination')?.getBoundingClientRect()
    return {
      searchRight: search?.right ?? 0,
      modeLeft: mode?.left ?? 0,
      userRight: user?.right ?? 0,
      queryHeight: query?.height ?? 0,
      contentBottom: content?.bottom ?? 0,
      pagerBottom: pager?.bottom ?? 0,
      viewportHeight: window.innerHeight,
      scrollHeight: document.documentElement.scrollHeight,
    }
  })
  expect(headerGeometry.searchRight).toBeLessThanOrEqual(headerGeometry.modeLeft)
  expect(headerGeometry.queryHeight).toBeLessThanOrEqual(96)
  expect(headerGeometry.contentBottom).toBeLessThanOrEqual(headerGeometry.viewportHeight)
  expect(headerGeometry.pagerBottom).toBeGreaterThan(0)
  expect(headerGeometry.pagerBottom).toBeLessThanOrEqual(headerGeometry.viewportHeight)
  expect(headerGeometry.scrollHeight).toBeLessThanOrEqual(headerGeometry.viewportHeight)
  expect(1280 - headerGeometry.userRight).toBeLessThanOrEqual(8)

  const submit = page.getByTestId('query-panel-submit')
  await submit.focus()
  await expect(submit).toBeFocused()
  await page.keyboard.press('Tab')
  await expect(page.getByTestId('query-panel-reset')).toBeFocused()

  await page.getByTestId('app-data-table-query-toggle').click()
  await expect(page.getByTestId('app-data-table-header-filter-loginName')).toBeVisible()
  await page.getByTestId('app-data-table-header-filter-loginName').focus()
  await expect(page.getByTestId('app-data-table-header-filter-loginName')).toBeFocused()

  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(
    true,
  )
  await page.screenshot({
    path: testInfo.outputPath('user-management-golden-1280x720.png'),
    fullPage: true,
  })

  await page.setViewportSize({ width: 1440, height: 900 })
  await page.goto('/pc/identity/users')
  await expect(page.getByTestId('identity-users-page')).toBeVisible({ timeout: 60_000 })
  await expect(page.locator('.vxe-table--header-wrapper .vxe-header--column').first()).toHaveCSS(
    'height',
    '38px',
  )
  const functionTree = page.locator('.ip-function-tree__list')
  const treeOverflow = await functionTree.evaluate((element) => ({
    clientWidth: element.clientWidth,
    scrollWidth: element.scrollWidth,
  }))
  expect(treeOverflow.scrollWidth).toBeLessThanOrEqual(treeOverflow.clientWidth)
  await expect(functionTree).toHaveCSS('overflow-x', 'hidden')
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(
    true,
  )
  await page.getByRole('button', { name: '语言' }).click()
  await page.getByRole('option', { name: 'English' }).click()
  await expect(page.locator('html')).toHaveAttribute('lang', 'en-US')
  const englishTreeOverflow = await functionTree.evaluate((element) => ({
    clientWidth: element.clientWidth,
    scrollWidth: element.scrollWidth,
  }))
  expect(englishTreeOverflow.scrollWidth).toBeLessThanOrEqual(englishTreeOverflow.clientWidth)
  await expect(functionTree).toHaveCSS('overflow-x', 'hidden')
  await expect(page.locator('.vxe-sort--asc-btn').first()).toHaveAttribute(
    'title',
    'Ascending order: lowest to highest',
  )
  await expect(page.locator('.vxe-sort--desc-btn').first()).toHaveAttribute(
    'title',
    'Descending order: highest to lowest',
  )
  await expect(page.locator('.vxe-table--header-wrapper .vxe-header--column').first()).toHaveCSS(
    'height',
    '38px',
  )
  await page.screenshot({
    path: testInfo.outputPath('user-management-golden-1440x900.png'),
    fullPage: true,
  })

  await page.setViewportSize({ width: 720, height: 900 })
  await page.goto('/pc/identity/users')
  await expect(page.getByTestId('identity-users-page')).toBeVisible({ timeout: 60_000 })
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(
    true,
  )
})
