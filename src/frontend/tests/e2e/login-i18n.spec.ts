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
        const panel = document.querySelector<HTMLElement>('[data-testid="login-method-panel"]')
        const username = document.querySelector<HTMLElement>('[data-testid="login-username"]')
        const password = document.querySelector<HTMLElement>('[data-testid="login-password"]')
        const submit = document.querySelector<HTMLElement>('[data-testid="login-submit"]')
        const brand = document.querySelector<HTMLElement>('.login-card__header-row .ip-brand__image')
        const localeIcon = document.querySelector<HTMLElement>('.login-card__locale .ip-locale-control')
        const cardRect = card?.getBoundingClientRect()
        const menuRect = localeMenu?.getBoundingClientRect()
        const panelRect = panel?.getBoundingClientRect()
        const controlRects = [username, password, submit].map((element) => element?.getBoundingClientRect())
        const panelOverlapsControl = controlRects.some((rect) => {
          if (!rect || !panelRect) return false
          return rect.left < panelRect.right && rect.right > panelRect.left && rect.top < panelRect.bottom && rect.bottom > panelRect.top
        })
        const parseRgb = (value: string): [number, number, number] => {
          const channels = value.match(/\d+(?:\.\d+)?/g)?.map(Number) ?? []
          return [channels[0] ?? 0, channels[1] ?? 0, channels[2] ?? 0]
        }
        const relativeLuminance = (value: string): number => {
          const toLinear = (channel: number): number => {
            const normalized = channel / 255
            return normalized <= 0.03928 ? normalized / 12.92 : ((normalized + 0.055) / 1.055) ** 2.4
          }
          const [red, green, blue] = parseRgb(value)
          return toLinear(red) * 0.2126 + toLinear(green) * 0.7152 + toLinear(blue) * 0.0722
        }
        const cardBackground = card ? getComputedStyle(card).backgroundColor : 'rgb(0, 0, 0)'
        const localeColor = localeIcon ? getComputedStyle(localeIcon).color : 'rgb(0, 0, 0)'
        const cardLuminance = relativeLuminance(cardBackground)
        const localeLuminance = relativeLuminance(localeColor)
        return {
          scrollWidth: document.documentElement.scrollWidth,
          scrollHeight: document.documentElement.scrollHeight,
          innerWidth: window.innerWidth,
          innerHeight: window.innerHeight,
          cardLeft: cardRect?.left ?? -1,
          cardRight: cardRect?.right ?? Number.MAX_SAFE_INTEGER,
          menuLeft: menuRect?.left ?? 0,
          menuRight: menuRect?.right ?? window.innerWidth,
          panelTop: panelRect?.top ?? -1,
          panelBottom: panelRect?.bottom ?? -1,
          controlsBottom: Math.max(...controlRects.map((rect) => rect?.bottom ?? -1)),
          panelOverlapsControl,
          panelRight: panelRect?.right ?? Number.MAX_SAFE_INTEGER,
          brandFilter: brand ? getComputedStyle(brand).filter : '',
          localeContrast: (Math.max(cardLuminance, localeLuminance) + 0.05) / (Math.min(cardLuminance, localeLuminance) + 0.05),
        }
      })
      expect(geometry.scrollWidth).toBeLessThanOrEqual(geometry.innerWidth)
      expect(geometry.cardLeft).toBeGreaterThanOrEqual(0)
      expect(geometry.cardRight).toBeLessThanOrEqual(geometry.innerWidth)
      expect(geometry.menuLeft).toBeGreaterThanOrEqual(0)
      expect(geometry.menuRight).toBeLessThanOrEqual(geometry.innerWidth)
      expect(geometry.brandFilter).toMatch(mode === 'dark' ? /invert/ : /none|brightness\(1\)/)
      if (mode === 'dark') expect(geometry.localeContrast).toBeGreaterThanOrEqual(4.5)
      if (viewport.width <= 520) {
        expect(geometry.panelOverlapsControl).toBe(false)
        expect(geometry.panelTop).toBeGreaterThanOrEqual(geometry.controlsBottom - 1)
        expect(geometry.panelRight).toBeLessThanOrEqual(geometry.innerWidth)
        expect(geometry.panelBottom).toBeLessThanOrEqual(geometry.scrollHeight)
        expect(geometry.scrollHeight).toBeGreaterThanOrEqual(geometry.innerHeight)
        await page.getByTestId('login-method-panel-close').scrollIntoViewIfNeeded()
        await expect(page.getByTestId('login-method-panel-close')).toBeInViewport()
      }
    })
  }
}

test('390×844 登录卡在中文与英文下保持视口中心', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await page.goto('/login')
  await expect(page.locator('.login-card')).toBeVisible()

  const assertCentered = async (): Promise<void> => {
    const geometry = await page.locator('.login-card').evaluate((element) => {
      const rect = element.getBoundingClientRect()
      return {
        centerX: rect.left + rect.width / 2,
        centerY: rect.top + rect.height / 2,
        viewportCenterX: window.innerWidth / 2,
        viewportCenterY: window.innerHeight / 2,
      }
    })
    expect(Math.abs(geometry.centerX - geometry.viewportCenterX)).toBeLessThanOrEqual(2)
    expect(Math.abs(geometry.centerY - geometry.viewportCenterY)).toBeLessThanOrEqual(2)
  }

  await expect(page.locator('html')).toHaveAttribute('lang', 'zh-CN')
  await assertCentered()

  await page.locator('.ip-locale-control').click()
  await page.getByRole('option', { name: 'English' }).click()
  await expect(page.locator('html')).toHaveAttribute('lang', 'en-US')
  await assertCentered()
})
