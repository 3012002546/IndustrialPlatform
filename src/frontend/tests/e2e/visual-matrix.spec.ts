/**
 * 主题/密度/三端像素回归矩阵(PF-01 §12.3):
 * - PC 核心外壳:/pc/home × 3 配色 × 2 有效明暗 × 2 密度 = 12 状态(1280×720)。
 * - PDA 现场壳:/pda/home × 3 配色 × 2 明暗 × 2 朝向(480×800/800×480)= 12 状态。
 * - Mobile 壳:/mobile/home × 3 配色 × 2 明暗 × 2 宽度(360×800/390×844)= 12 状态。
 * - UiBaselinePage:/pc/ui-baseline(DEV-only)× 3 配色 × 2 明暗 = 6 状态,
 *   覆盖查询/树表/表单抽屉/Loading/Empty/Error/Permission/Degraded 通用组件。
 * system 模式等价于对应有效模式,已在 theme.spec.ts 断言 DOM Token 等价。
 * 所有断言经真实主题入口 radio 设置,阈值由 playwright.config.ts 全局固定 1%。
 */

import { expect, test, type Page } from '@playwright/test'

const VALID_USER = 'mock.admin'
const VALID_PASS = 'Mock@123456'

const PALETTES = ['industrial-cyan', 'technology-blue', 'neutral-gray'] as const
const EFFECTIVE_MODES = ['light', 'dark'] as const
const DENSITIES = ['comfortable', 'compact'] as const

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

/** 打开主题入口并通过真实 radio 设置主题,随后关闭面板,保证截图不残留面板。 */
async function applyTheme(
  page: Page,
  palette: string,
  mode: string,
  density: string | null = null,
): Promise<void> {
  await page.getByTestId('theme-control-trigger').click()
  await expect(page.locator('.theme-control__panel')).toBeVisible()
  await page.getByTestId(`theme-palette-${palette}`).check()
  await page.getByTestId(`theme-mode-${mode}`).check()
  if (density !== null) {
    await page.getByTestId(`theme-density-${density}`).check()
  }
  await page.keyboard.press('Escape')
  await expect(page.locator('.theme-control__panel')).not.toBeVisible()
  await expect(page.locator('html')).toHaveAttribute('data-ip-palette', palette)
  await expect(page.locator('html')).toHaveAttribute('data-ip-color-mode', mode)
}

// ── PC 核心外壳:3 × 2 × 2 = 12 ──────────────────────────────────────────────
for (const palette of PALETTES) {
  for (const mode of EFFECTIVE_MODES) {
    for (const density of DENSITIES) {
      test(`PC /pc/home ${palette} ${mode} ${density} 1280×720 截图`, async ({ page }) => {
        await login(page)
        await page.setViewportSize({ width: 1280, height: 720 })
        await page.goto('/pc/home')
        await expect(page.getByRole('heading', { level: 1 })).toBeVisible()
        await applyTheme(page, palette, mode, density)
        await expect(page.locator('html')).toHaveAttribute('data-ip-density', density)
        await expect(page).toHaveScreenshot(`pc-home-${palette}-${mode}-${density}-1280x720.png`, {
          fullPage: true,
        })
      })
    }
  }
}

// ── PDA 现场壳:3 × 2 × 2 朝向 = 12 ──────────────────────────────────────────
const PDA_VIEWPORTS = [
  { width: 480, height: 800 },
  { width: 800, height: 480 },
] as const

for (const palette of PALETTES) {
  for (const mode of EFFECTIVE_MODES) {
    for (const viewport of PDA_VIEWPORTS) {
      test(`PDA /pda/home ${palette} ${mode} ${viewport.width}×${viewport.height} 截图`, async ({
        page,
      }) => {
        await login(page)
        await page.setViewportSize({ width: viewport.width, height: viewport.height })
        await page.goto('/pda/home')
        await expect(page.getByRole('heading', { name: '现场任务将在业务阶段接入' })).toBeVisible()
        await applyTheme(page, palette, mode)
        await expect(page).toHaveScreenshot(
          `pda-home-${palette}-${mode}-${viewport.width}x${viewport.height}.png`,
          { fullPage: true },
        )
      })
    }
  }
}

// ── Mobile 壳:3 × 2 × 2 宽度 = 12 ───────────────────────────────────────────
const MOBILE_VIEWPORTS = [
  { width: 360, height: 800 },
  { width: 390, height: 844 },
] as const

for (const palette of PALETTES) {
  for (const mode of EFFECTIVE_MODES) {
    for (const viewport of MOBILE_VIEWPORTS) {
      test(`Mobile /mobile/home ${palette} ${mode} ${viewport.width}×${viewport.height} 截图`, async ({
        page,
      }) => {
        await login(page)
        await page.setViewportSize({ width: viewport.width, height: viewport.height })
        await page.goto('/mobile/home')
        await expect(page.getByRole('heading', { name: '业务功能将在后续阶段接入' })).toBeVisible()
        await applyTheme(page, palette, mode)
        await expect(page).toHaveScreenshot(
          `mobile-home-${palette}-${mode}-${viewport.width}x${viewport.height}.png`,
          { fullPage: true },
        )
      })
    }
  }
}

// ── UiBaselinePage:3 × 2 × 2 = 12 状态,覆盖通用组件视觉与 PC 密度变化 ───────
// PC 核心外壳(/pc/home)固定尺寸不随 density 变化(§12.3);密度对内容控件的高度
// 影响由本页查询/按钮等控件承载,故 UiBaselinePage 矩阵纳入密度维度做视觉回归。
for (const palette of PALETTES) {
  for (const mode of EFFECTIVE_MODES) {
    for (const density of DENSITIES) {
      test(`UiBaseline /pc/ui-baseline ${palette} ${mode} ${density} 1280×720 截图`, async ({
        page,
      }) => {
        await login(page)
        await page.setViewportSize({ width: 1280, height: 720 })
        await page.goto('/pc/ui-baseline')
        // §12.3:基线页必须覆盖查询/树表/表单抽屉/Loading/Empty/Error/Permission/Degraded
        await expect(page.getByTestId('baseline-query')).toBeVisible()
        await expect(page.getByTestId('baseline-tree-table')).toBeVisible()
        await expect(page.getByTestId('baseline-drawer-trigger')).toBeVisible()
        await expect(page.getByTestId('app-loading-state')).toBeVisible()
        await expect(page.getByRole('heading', { name: '暂无数据' })).toBeVisible()
        await expect(page.getByRole('heading', { name: '加载失败' })).toBeVisible()
        await expect(page.getByTestId('app-permission-state')).toBeVisible()
        await expect(page.getByTestId('app-degraded-state')).toBeVisible()
        await applyTheme(page, palette, mode, density)
        await expect(page.locator('html')).toHaveAttribute('data-ip-density', density)
        await expect(page).toHaveScreenshot(
          `ui-baseline-${palette}-${mode}-${density}-1280x720.png`,
          { fullPage: true },
        )
      })
    }
  }
}

test('UiBaseline 表单抽屉:焦点进入、Escape 关闭并归还焦点(表单抽屉契约)', async ({ page }) => {
  await login(page)
  await page.setViewportSize({ width: 1280, height: 720 })
  await page.goto('/pc/ui-baseline')
  await expect(page.getByTestId('baseline-drawer-trigger')).toBeVisible()

  await page.getByTestId('baseline-drawer-trigger').click()
  await expect(page.getByTestId('form-drawer-backdrop')).toBeVisible()
  await expect(page.getByRole('heading', { name: '样例表单' })).toBeVisible()

  // 点击抽屉内输入框使焦点进入抽屉,再按 Escape 关闭(抽屉 onKeydown 只响应面板内键盘事件)
  await page.getByLabel('名称').click()
  await expect(page.getByLabel('名称')).toBeFocused()

  // Escape 关闭,焦点归还触发按钮(§7.10 focus trap)
  await page.keyboard.press('Escape')
  await expect(page.getByTestId('form-drawer-backdrop')).not.toBeVisible()
  await expect(page.getByTestId('baseline-drawer-trigger')).toBeFocused()
})
