import { expect, test } from '@playwright/test'

const VIEWPORTS = [
  { width: 1440, height: 900 },
  { width: 1280, height: 720 },
  { width: 390, height: 844 },
  { width: 360, height: 800 },
] as const

for (const viewport of VIEWPORTS) {
  for (const mode of ['light', 'dark'] as const) {
    test(`登录页英文 ${viewport.width}×${viewport.height} ${mode} 不溢出且保留状态`, async ({ page }) => {
      await page.setViewportSize(viewport)
      await page.goto('/login')
      await page.evaluate((colorMode) => {
        document.documentElement.setAttribute('data-ip-color-mode', colorMode)
      }, mode)

      await page.getByTestId('login-username').fill('saved-user')
      await page.getByTestId('login-password').fill('saved-password')
      await page.getByTestId('login-method-toggle').click()
      await page.locator('.ip-locale-control').click()
      await page.locator('.ip-locale-control__menu [role="option"]').filter({ hasText: 'English' }).click()

      await expect(page.locator('html')).toHaveAttribute('lang', 'en-US')
      await expect(page).toHaveTitle(/Sign in/)
      await expect(page.getByRole('heading', { name: 'Sign in' })).toBeVisible()
      await expect(page.getByRole('heading', { name: 'Sign-in method', exact: true })).toBeVisible()
      await expect(page.getByText('Enterprise sign-in (SSO)')).toBeVisible()
      await expect(page.getByTestId('login-username')).toHaveValue('saved-user')
      await expect(page.getByTestId('login-password')).toHaveValue('saved-password')
      await expect(page).toHaveURL(/\/login$/)
      await expect(page.getByText('登录方式')).not.toBeVisible()

      const geometry = await page.evaluate(() => {
        const card = document.querySelector<HTMLElement>('.login-card')
        const localeMenu = document.querySelector<HTMLElement>('.ip-locale-control__menu')
        const cardRect = card?.getBoundingClientRect()
        const menuRect = localeMenu?.getBoundingClientRect()
        return {
          scrollWidth: document.documentElement.scrollWidth,
          innerWidth: window.innerWidth,
          cardLeft: cardRect?.left ?? -1,
          cardRight: cardRect?.right ?? Number.MAX_SAFE_INTEGER,
          menuLeft: menuRect?.left ?? 0,
          menuRight: menuRect?.right ?? window.innerWidth,
        }
      })
      expect(geometry.scrollWidth).toBeLessThanOrEqual(geometry.innerWidth)
      expect(geometry.cardLeft).toBeGreaterThanOrEqual(0)
      expect(geometry.cardRight).toBeLessThanOrEqual(geometry.innerWidth)
      expect(geometry.menuLeft).toBeGreaterThanOrEqual(0)
      expect(geometry.menuRight).toBeLessThanOrEqual(geometry.innerWidth)
    })
  }
}
