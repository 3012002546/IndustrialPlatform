import { expect, test } from '@playwright/test'

async function login(page: import('@playwright/test').Page): Promise<void> {
  await page.goto('/login')
  await page.getByLabel('用户名').fill('mock.admin')
  await page.getByLabel('密码', { exact: true }).fill('Mock@123456')
  await page.getByRole('button', { name: '登录' }).click()
  await expect(page).toHaveURL(/\/pc\/home/)
}

test('双权限用户可以在管理壳与生产操作壳之间切换', async ({ page }) => {
  await login(page)
  await expect(page.getByTestId('pc-experience-mode-control')).toBeVisible()
  await page.getByRole('button', { name: '生产操作' }).click()
  await expect(page).toHaveURL(/\/pc\/operation/)
  await expect(page.locator('.ip-toolrail')).toHaveCount(0)
  await expect(page.locator('.ip-function-tree')).toHaveCount(0)
  await expect(page.locator('.ip-pc-tabs')).toHaveCount(0)
  await expect(page.locator('[data-operation-launcher]')).toHaveCount(9)
  await expect(page.locator('[aria-disabled="true"]')).toHaveCount(8)
  await expect(page.locator('[aria-disabled="true"] a')).toHaveCount(0)
  await expect(page.locator('.ip-operation-topbar .ip-brand .ip-brand__name')).toHaveCount(0)
  await expect(page.getByTestId('tenant-context')).toHaveCSS('white-space', 'nowrap')
  await expect(page.getByTestId('operation-user-menu')).toHaveCSS('white-space', 'nowrap')
  const viewport = page.viewportSize()
  expect(viewport).not.toBeNull()
  for (const locator of [
    page.locator('.ip-operation-topbar'),
    page.locator('.ip-operation-topbar__brand'),
    page.locator('.ip-operation-topbar__context'),
    page.locator('.ip-operation-topbar__right'),
    page.getByTestId('operation-user-menu'),
  ]) {
    const box = await locator.boundingBox()
    expect(box).not.toBeNull()
    expect(box!.x).toBeGreaterThanOrEqual(0)
    expect(box!.x + box!.width).toBeLessThanOrEqual(viewport!.width)
  }
  const operationUserBox = await page.getByTestId('operation-user-menu').boundingBox()
  expect(operationUserBox).not.toBeNull()
  expect(operationUserBox!.width).toBeGreaterThanOrEqual(120)
  const topbarColor = await page
    .locator('.ip-operation-topbar')
    .evaluate((element) => getComputedStyle(element).color)
  const operationUserColor = await page
    .getByTestId('operation-user-menu')
    .evaluate((element) => getComputedStyle(element).color)
  expect(operationUserColor).toBe(topbarColor)

  await page.screenshot({ path: 'tests/e2e/screenshots/pc-operation-1280x720.png', fullPage: true })
  await page.getByRole('button', { name: '管理' }).click()
  await expect(page).toHaveURL(/\/pc\/home/)
  await expect(page.getByRole('navigation', { name: '平台分组' })).toBeVisible()
})

test('生产操作模式随当前语言切换为英文且公共文案无中文回退', async ({ page }) => {
  await login(page)
  await page.getByLabel('语言').selectOption('en-US')
  await page.getByRole('button', { name: 'Operations' }).click()
  await expect(page).toHaveURL(/\/pc\/operation/)

  await expect(page.locator('html')).toHaveAttribute('lang', 'en-US')
  await expect(page.getByRole('heading', { name: 'Production operations' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Task execution' })).toBeVisible()
  await expect(page.getByTestId('operation-fullscreen')).toHaveAttribute(
    'aria-label',
    'Browser fullscreen',
  )
  await expect(page.locator('.pc-operation-home')).not.toContainText(/[\u3400-\u9fff]/)
})

test('无生产操作权限直达生产操作路由被拒绝', async ({ page }) => {
  await login(page)
  await page.evaluate(() => {
    const raw = sessionStorage.getItem('industrial-platform.auth.mock.v1')
    if (raw === null) return
    const stored = JSON.parse(raw) as {
      version: number
      session: { user: { permissions: string[] } }
    }
    stored.session.user.permissions = stored.session.user.permissions.filter(
      (permission) => permission !== 'platform.operation.view',
    )
    sessionStorage.setItem('industrial-platform.auth.mock.v1', JSON.stringify(stored))
  })
  await page.goto('/pc/operation')
  await expect(page).toHaveURL(/\/403/)
})
