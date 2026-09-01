/**
 * 主题行为 E2E(PF-01 §12.3 关键行为):
 * 主题入口三端共享且即时生效;配色/明暗/密度切换改写根节点 data-ip-* 属性并持久化;
 * system 模式跟随 OS 明暗且等价于对应有效模式;首帧暗色 bootstrap 快照先于应用生效;
 * PC 壳固定尺寸不随密度变化而内容控件高度变化;200% 缩放核心操作仍可用。
 * Playwright 经 Vite dev server 提供应用(同 pc.spec.ts)。
 */

import { expect, test, type Page } from '@playwright/test'

const VALID_USER = 'mock.admin'
const VALID_PASS = 'Mock@123456'

async function fillLogin(page: Page): Promise<void> {
  await page.getByLabel('用户名').fill(VALID_USER)
  await page.getByLabel('密码', { exact: true }).fill(VALID_PASS)
}

async function login(page: Page): Promise<void> {
  await page.goto('/login')
  await fillLogin(page)
  await page.getByTestId('login-submit').click()
  await expect(page).toHaveURL(/\/pc\/home/)
}

async function openTheme(page: Page): Promise<void> {
  await page.getByTestId('theme-control-trigger').click()
  await expect(page.locator('.theme-control__panel')).toBeVisible()
}

/** 通过主题入口的真实 radio 设置主题(即时生效并持久化)。 */
async function setTheme(
  page: Page,
  kind: 'palette' | 'mode' | 'density',
  value: string,
): Promise<void> {
  await page.getByTestId(`theme-${kind}-${value}`).check()
}

test('PC 主题入口渲染配色/明暗/密度三组选项', async ({ page }) => {
  await login(page)
  await openTheme(page)
  await expect(page.getByText('配色', { exact: true })).toBeVisible()
  await expect(page.getByText('明暗模式', { exact: true })).toBeVisible()
  await expect(page.getByText('密度', { exact: true })).toBeVisible()
  for (const palette of ['industrial-cyan', 'technology-blue', 'neutral-gray']) {
    await expect(page.getByTestId(`theme-palette-${palette}`)).toBeVisible()
  }
  for (const mode of ['light', 'dark', 'system']) {
    await expect(page.getByTestId(`theme-mode-${mode}`)).toBeVisible()
  }
  for (const density of ['comfortable', 'compact']) {
    await expect(page.getByTestId(`theme-density-${density}`)).toBeVisible()
  }
})

test('切换配色立即改写根节点属性并持久化', async ({ page }) => {
  await login(page)
  await openTheme(page)
  await setTheme(page, 'palette', 'technology-blue')
  await expect(page.locator('html')).toHaveAttribute('data-ip-palette', 'technology-blue')
  await page.keyboard.press('Escape')

  // 刷新后用户偏好被重新绑定,配色不回到默认
  await page.reload()
  await expect(page).toHaveURL(/\/pc\/home/)
  await expect(page.locator('html')).toHaveAttribute('data-ip-palette', 'technology-blue')
})

test('顶栏背景跟随配色,明暗与刷新不覆盖既有渐变', async ({ page }) => {
  await login(page)
  await openTheme(page)
  await setTheme(page, 'palette', 'industrial-cyan')
  const cyanBackground = await page
    .locator('header.ip-topbar')
    .evaluate((el) => getComputedStyle(el).backgroundImage)

  await setTheme(page, 'palette', 'technology-blue')
  const blueBackground = await page
    .locator('header.ip-topbar')
    .evaluate((el) => getComputedStyle(el).backgroundImage)
  expect(cyanBackground).toContain('linear-gradient')
  expect(blueBackground).toContain('linear-gradient')
  expect(blueBackground).not.toBe(cyanBackground)

  await setTheme(page, 'mode', 'dark')
  await expect(page.locator('html')).toHaveAttribute('data-ip-color-mode', 'dark')
  await expect(page.locator('header.ip-topbar')).toHaveCSS('background-image', blueBackground)
  await page.keyboard.press('Escape')
  await page.reload()

  await expect(page.locator('html')).toHaveAttribute('data-ip-palette', 'technology-blue')
  await expect(page.locator('html')).toHaveAttribute('data-ip-color-mode', 'dark')
  await expect(page.locator('header.ip-topbar')).toHaveCSS('background-image', blueBackground)
})

test('切换明暗立即生效并持久化', async ({ page }) => {
  await login(page)
  await openTheme(page)
  await setTheme(page, 'mode', 'dark')
  await expect(page.locator('html')).toHaveAttribute('data-ip-theme-mode', 'dark')
  await expect(page.locator('html')).toHaveAttribute('data-ip-color-mode', 'dark')
  await page.keyboard.press('Escape')

  await page.reload()
  await expect(page).toHaveURL(/\/pc\/home/)
  await expect(page.locator('html')).toHaveAttribute('data-ip-color-mode', 'dark')
})

test('system 模式跟随 OS 明暗,有效模式等价于显式明暗', async ({ page }) => {
  await login(page)
  await openTheme(page)
  await setTheme(page, 'mode', 'system')
  await expect(page.locator('html')).toHaveAttribute('data-ip-theme-mode', 'system')

  // 模拟 OS dark:有效模式变为 dark,DOM 与显式 dark 等价
  await page.emulateMedia({ colorScheme: 'dark' })
  await expect(page.locator('html')).toHaveAttribute('data-ip-color-mode', 'dark')
  const darkPageBg = await page.evaluate(() =>
    getComputedStyle(document.documentElement).getPropertyValue('--ip-color-bg-page').trim(),
  )

  // 切回 OS light:有效模式回到 light
  await page.emulateMedia({ colorScheme: 'light' })
  await expect(page.locator('html')).toHaveAttribute('data-ip-color-mode', 'light')
  const lightPageBg = await page.evaluate(() =>
    getComputedStyle(document.documentElement).getPropertyValue('--ip-color-bg-page').trim(),
  )

  // 有效明暗必须真正驱动语义 Token(dark ≠ light 的 bg-page)
  expect(darkPageBg).not.toBe(lightPageBg)
})

test('密度切换:内容控件高度变化,PC 壳固定尺寸不变', async ({ page }) => {
  await login(page)
  const topbarBefore = await page
    .locator('header.ip-topbar')
    .evaluate((el) => el.getBoundingClientRect().height)

  await openTheme(page)
  const optionBefore = await page
    .locator('.theme-control__option')
    .first()
    .evaluate((el) => el.getBoundingClientRect().height)
  await setTheme(page, 'density', 'compact')
  await expect(page.locator('html')).toHaveAttribute('data-ip-density', 'compact')

  const optionAfter = await page
    .locator('.theme-control__option')
    .first()
    .evaluate((el) => el.getBoundingClientRect().height)
  const topbarAfter = await page
    .locator('header.ip-topbar')
    .evaluate((el) => el.getBoundingClientRect().height)

  // 内容控件(密度选项)高度随紧凑变小;PC 顶栏固定尺寸不随密度变化
  expect(optionAfter).toBeLessThan(optionBefore)
  expect(topbarAfter).toBe(topbarBefore)
})

test('首帧暗色 bootstrap 快照在应用挂载前生效,无明亮底色', async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem(
      'industrial-platform.ui.bootstrap.v1',
      JSON.stringify({
        version: 1,
        palette: 'technology-blue',
        mode: 'dark',
        density: 'comfortable',
      }),
    )
  })
  await page.goto('/login')
  await expect(page.getByLabel('用户名')).toBeVisible()

  // bootstrap 脚本同步写入根节点,先于 Vue 应用状态生效
  await expect(page.locator('html')).toHaveAttribute('data-ip-palette', 'technology-blue')
  await expect(page.locator('html')).toHaveAttribute('data-ip-theme-mode', 'dark')
  await expect(page.locator('html')).toHaveAttribute('data-ip-color-mode', 'dark')
  const colorScheme = await page.evaluate(
    () => getComputedStyle(document.documentElement).colorScheme,
  )
  expect(colorScheme).toBe('dark')
  // 暗色语义 Token 已激活:bg-page 为暗色值 #111827,而非明亮 #f3f4f6
  const pageBg = await page.evaluate(() =>
    getComputedStyle(document.documentElement).getPropertyValue('--ip-color-bg-page').trim(),
  )
  expect(pageBg).toBe('#111827')
})

test('200% 缩放:核心操作仍可用,内容不永久遮挡', async ({ page }) => {
  await login(page)
  await page.goto('/pc/home')
  // Chromium CSS zoom 模拟浏览器 200% 缩放(缩放布局而非仅画布)
  await page.evaluate(() => {
    document.documentElement.style.zoom = '2'
  })

  const trigger = page.getByTestId('theme-control-trigger')
  await expect(trigger).toBeVisible()
  await trigger.click() // 自动滚动到可见再点击
  await expect(page.locator('.theme-control__panel')).toBeVisible()
  // 缩放下面板内 radio 仍可操作(核心交互);选中后焦点进入面板,Escape 才能被面板处理
  await page.getByTestId('theme-mode-light').check()
  await expect(page.locator('html')).toHaveAttribute('data-ip-color-mode', 'light')
  await page.keyboard.press('Escape')
  await expect(page.locator('.theme-control__panel')).not.toBeVisible()

  // 内容区关键标题可滚动到视图,无不可恢复遮挡
  const heading = page.getByRole('heading', { level: 1 })
  await heading.scrollIntoViewIfNeeded()
  await expect(heading).toBeVisible()
})
