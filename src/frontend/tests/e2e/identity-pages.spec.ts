/**
 * Identity 管理页面在 PC 壳下的三主题代表状态验收(PF-01 §14 TASK-PF01-007 预期输出 2)。
 * 真实数据(用户/角色/权限/审计)→ 新 PC 壳(顶栏/工具轨/功能树/标签)→ 三套配色截图。
 * 前置:后端已运行、e2e.admin(SYSTEM_ADMIN)已种子。
 * 运行:`pnpm exec playwright test -c playwright.real.config.ts`
 *
 * 覆盖:页面位于新 PC 壳(非裸路由)、真实数据无错误态、三套配色像素回归。
 * 模式维度(light/dark/system 等价)已由 theme.spec/visual-matrix.spec 覆盖,此处统一 light。
 */

import { expect, test, type Page } from '@playwright/test'

const E2E_ADMIN = 'e2e.admin'
const E2E_PASSWORD = 'E2e!Admin@2026'

const PALETTES = ['industrial-cyan', 'technology-blue', 'neutral-gray'] as const

const IDENTITY_PAGES = [
  { path: '/pc/identity/users', title: '用户管理', data: (p: Page) => p.getByText('e2e.admin') },
  {
    path: '/pc/identity/roles',
    title: '角色权限',
    data: (p: Page) => p.getByText('系统管理员', { exact: true }),
  },
  {
    path: '/pc/identity/permissions',
    title: '权限目录',
    data: (p: Page) => p.getByText(/共 \d+ 项/),
  },
  {
    path: '/pc/identity/audits',
    title: '登录审计',
    data: (p: Page) => p.getByRole('heading', { name: '加载失败' }),
  },
] as const

async function login(page: Page): Promise<void> {
  await page.goto('/login')
  await page.getByTestId('login-username').fill(E2E_ADMIN)
  await page.getByTestId('login-password').fill(E2E_PASSWORD)
  await page.getByTestId('login-submit').click()
  await expect(page).toHaveURL(/\/pc\/home/, { timeout: 60_000 })
}

/** 打开主题入口并经真实 radio 设置配色,随后关闭面板。 */
async function applyPalette(page: Page, palette: string): Promise<void> {
  await page.getByTestId('theme-control-trigger').click()
  await expect(page.locator('.theme-control__panel')).toBeVisible()
  await page.getByTestId(`theme-palette-${palette}`).check()
  await expect(page.locator('html')).toHaveAttribute('data-ip-palette', palette)
  // 关闭面板不依赖 Escape 焦点:配色切换会触发整站重渲染,单次 check 后焦点可能落到 body,
  // Escape 不再达面板 @keydown。改以触发钮 toggle 关闭,焦点无关、稳定(real-login 同款)。
  await page.getByTestId('theme-control-trigger').click()
  await expect(page.locator('.theme-control__panel')).not.toBeVisible()
}

for (const palette of PALETTES) {
  for (const pageDef of IDENTITY_PAGES) {
    test(`Identity ${pageDef.title} ${palette} 真实数据 · PC 壳三主题代表状态截图`, async ({
      page,
    }) => {
      await login(page)
      await page.setViewportSize({ width: 1280, height: 720 })
      await page.goto(pageDef.path)
      await expect(page).toHaveURL(pageDef.path)

      // 位于新 PC 壳:四层结构均在
      await expect(page.locator('.ip-pc-layout')).toBeVisible()
      await expect(page.locator('header.ip-topbar')).toBeVisible()
      await expect(page.locator('nav.ip-toolrail')).toBeVisible()
      await expect(page.locator('nav.ip-function-tree')).toBeVisible()
      await expect(page.locator('.ip-pc-tabs')).toContainText(pageDef.title)

      // 真实数据:无「加载失败」;用户/角色/权限页另断言真实行(审计页允许空表)
      await expect(page.getByRole('heading', { name: '加载失败' })).not.toBeVisible({
        timeout: 60_000,
      })
      if (pageDef.title !== '登录审计') {
        await expect(pageDef.data(page)).toBeVisible({ timeout: 60_000 })
      }

      await applyPalette(page, palette)
      // 审计页为实时数据(登录时间/IP 哈希/TraceId 逐次登录变化),像素基线必然漂移,
      // 故掩蔽数据表体与分页总数,仅对壳/配色等稳定区域做像素回归(其余页全量截图)。
      const shotOptions: { fullPage: boolean; mask?: Array<import('@playwright/test').Locator> } = {
        fullPage: true,
      }
      if (pageDef.title === '登录审计') {
        shotOptions.mask = [page.locator('.el-table__body'), page.locator('.el-pagination__total')]
      }
      await expect(page).toHaveScreenshot(
        `identity-${pageDef.path.split('/').pop()}-${palette}.png`,
        shotOptions,
      )
    })
  }
}
