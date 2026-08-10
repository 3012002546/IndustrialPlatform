import { expect, test } from '@playwright/test'

test('application shell loads on the preview build', async ({ page }) => {
  await page.goto('/')

  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Industrial Platform')
})
