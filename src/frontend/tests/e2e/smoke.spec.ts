import { expect, test } from '@playwright/test'

test('app boots and redirects unauthenticated users to login', async ({ page }) => {
  await page.goto('/')

  await expect(page.getByRole('heading', { level: 1, name: '登录' })).toBeVisible()
})
