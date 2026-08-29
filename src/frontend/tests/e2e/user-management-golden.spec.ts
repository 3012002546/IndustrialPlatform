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

test('用户管理黄金页:共享结构、键盘路径与窄窗口', async ({ page }) => {
  await login(page)
  await page.setViewportSize({ width: 1280, height: 720 })
  await page.goto('/pc/identity/users')

  await expect(page).toHaveURL(/\/pc\/identity\/users$/)
  await expect(page.getByTestId('identity-users-page')).toBeVisible({ timeout: 60_000 })
  await expect(page.getByRole('heading', { level: 1, name: '用户管理' })).toBeVisible()
  await expect(page.getByTestId('identity-users-query')).toBeVisible()
  await expect(page.getByTestId('app-data-table')).toBeVisible()
  await expect(page.getByRole('button', { name: '新建用户' })).toBeVisible()

  const submit = page.getByTestId('query-panel-submit')
  await submit.focus()
  await expect(submit).toBeFocused()
  await page.keyboard.press('Shift+Tab')
  await expect(page.getByTestId('query-panel-reset')).toBeFocused()

  await page.getByTestId('app-data-table-query-toggle').click()
  await expect(page.getByTestId('app-data-table-header-filter-loginName')).toBeVisible()
  await page.getByTestId('app-data-table-header-filter-loginName').focus()
  await expect(page.getByTestId('app-data-table-header-filter-loginName')).toBeFocused()

  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(
    true,
  )
  await page.screenshot({
    path: 'tests/e2e/screenshots/user-management-golden-1280x720.png',
    fullPage: true,
  })

  await page.setViewportSize({ width: 1440, height: 900 })
  await page.goto('/pc/identity/users')
  await expect(page.getByTestId('identity-users-page')).toBeVisible({ timeout: 60_000 })
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(
    true,
  )
  await page.screenshot({
    path: 'tests/e2e/screenshots/user-management-golden-1440x900.png',
    fullPage: true,
  })

  await page.setViewportSize({ width: 720, height: 900 })
  await page.goto('/pc/identity/users')
  await expect(page.getByTestId('identity-users-page')).toBeVisible({ timeout: 60_000 })
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(
    true,
  )
})
