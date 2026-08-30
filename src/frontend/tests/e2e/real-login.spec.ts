/**
 * 真实 Identity 登录与权限集成 E2E(PF-01 §14 TASK-PF01-007)。
 * 经 Gateway(5080)→ Identity(5041)→ 云端 PG/Redis 全链路真实登录,不以 Mock 证明集成。
 * 前置:后端已运行,测试账号已种子(幂等,密码仅供本次验收,测试后清理):
 *   e2e.admin   / E2e!Admin@2026  — SYSTEM_ADMIN,全部 17 权限
 *   e2e.limited / E2e!Admin@2026  — 仅 platform.home.view
 * 运行:`pnpm exec playwright test -c playwright.real.config.ts`
 *
 * 覆盖(§14 预期输出 1/3):真实登录前预挂载主题、登录后用户偏好绑定、
 * 刷新/注销/用户切换无主题串用与受保护壳闪烁;真实 permissions 同时驱动
 * Router Guard(直接路由 403)、功能树菜单隐藏与业务标签恢复;会话键隔离。
 */

import { expect, test, type Page } from '@playwright/test'

export const E2E_ADMIN = 'e2e.admin'
export const E2E_LIMITED = 'e2e.limited'
export const E2E_PASSWORD = 'E2e!Admin@2026'

function usersMainTable(page: Page) {
  return page.locator('.vxe-table--main-wrapper')
}

const AUTH_HTTP_KEY = 'industrial-platform.auth.http.v1'
const AUTH_MOCK_KEY = 'industrial-platform.auth.mock.v1'

async function gotoLogin(page: Page): Promise<void> {
  await page.goto('/login')
  await expect(page.getByTestId('login-username')).toBeVisible()
  await expect(page.getByTestId('login-password')).toBeVisible()
}

/** 真实登录:等待进入 PC 壳(首呼 bcrypt+JWT 可能数秒,放宽超时)。 */
async function login(page: Page, username: string): Promise<void> {
  await gotoLogin(page)
  await page.getByTestId('login-username').fill(username)
  await page.getByTestId('login-password').fill(E2E_PASSWORD)
  await page.getByTestId('login-submit').click()
  await expect(page).toHaveURL(/\/pc\/home/, { timeout: 60_000 })
  await expect(page.locator('.ip-pc-layout')).toBeVisible()
}

/** 通过用户菜单退出登录,断言回到登录页且无受保护壳残留。 */
async function logout(page: Page): Promise<void> {
  await page.getByTestId('user-menu').click()
  await page.getByRole('menuitem', { name: '退出登录' }).click()
  await expect(page).toHaveURL(/\/login/, { timeout: 60_000 })
  await expect(page.getByTestId('login-username')).toBeVisible()
  await expect(page.locator('.ip-pc-layout')).toHaveCount(0)
}

/** 打开主题入口并经真实 radio 设置配色/明暗(与 visual-matrix 同款交互)。 */
async function applyTheme(page: Page, palette: string, mode: string): Promise<void> {
  await page.getByTestId('theme-control-trigger').click()
  await expect(page.locator('.theme-control__panel')).toBeVisible()
  await page.getByTestId(`theme-palette-${palette}`).check()
  await page.getByTestId(`theme-mode-${mode}`).check()
  await expect(page.locator('html')).toHaveAttribute('data-ip-palette', palette)
  // 关闭面板不依赖 Escape 焦点(配色切换重渲染可能让焦点落到 body),改以触发钮 toggle 关闭。
  await page.getByTestId('theme-control-trigger').click()
  await expect(page.locator('.theme-control__panel')).not.toBeVisible()
}

test('真实登录链路:错误密码统一错误且不进入受保护壳,正确密码进入 PC 壳并绑定用户作用域', async ({
  page,
}) => {
  await gotoLogin(page)

  // 错误密码:后端 ID_AUTH_INVALID_CREDENTIALS 归一为业务错误,页面显示统一文案
  await page.getByTestId('login-username').fill(E2E_ADMIN)
  await page.getByTestId('login-password').fill('Wrong!Password1')
  await page.getByTestId('login-submit').click()
  await expect(page.getByText('用户名或密码错误。')).toBeVisible({ timeout: 60_000 })
  await expect(page).toHaveURL(/\/login/)
  await expect(page.locator('.ip-pc-layout')).toHaveCount(0)

  // 正确密码:进入 PC 壳;新用户无偏好 → 绑定产品默认(工业青 / system→Chromium light)
  await page.getByTestId('login-password').fill(E2E_PASSWORD)
  await page.getByTestId('login-submit').click()
  await expect(page).toHaveURL(/\/pc\/home/, { timeout: 60_000 })
  await expect(page.locator('.ip-pc-layout')).toBeVisible()
  await expect(page.locator('html')).toHaveAttribute('data-ip-palette', 'industrial-cyan')
  await expect(page.locator('html')).toHaveAttribute('data-ip-color-mode', 'light')

  // 真实会话键隔离:http 键存在、mock 键不存在
  const sessionKeys = await page.evaluate(() => Object.keys(sessionStorage))
  expect(sessionKeys).toContain(AUTH_HTTP_KEY)
  expect(sessionKeys).not.toContain(AUTH_MOCK_KEY)
})

test('真实登录前预挂载主题(bootstrap 暗色先于应用),受保护壳无闪烁', async ({ page }) => {
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
  await expect(page.getByTestId('login-username')).toBeVisible()
  // 首帧暗色 bootstrap 已生效(登录页即暗色),且无受保护壳
  await expect(page.locator('html')).toHaveAttribute('data-ip-color-mode', 'dark')
  await expect(page.locator('html')).toHaveAttribute('data-ip-palette', 'technology-blue')
  const pageBg = await page.evaluate(() =>
    getComputedStyle(document.documentElement).getPropertyValue('--ip-color-bg-page').trim(),
  )
  expect(pageBg).toBe('#111827')
  await expect(page.locator('.ip-pc-layout')).toHaveCount(0)

  // 登录后绑定用户作用域(新用户无偏好 → 产品默认),无受保护壳残留
  await login(page, E2E_ADMIN)
  await expect(page.locator('.ip-pc-layout')).toBeVisible()
  await expect(page.locator('html')).toHaveAttribute('data-ip-palette', 'industrial-cyan')
})

test('登录后用户偏好绑定:主题持久化到用户作用域,刷新与重登均保持', async ({ page }) => {
  await login(page, E2E_ADMIN)
  await applyTheme(page, 'technology-blue', 'dark')
  await expect(page.locator('html')).toHaveAttribute('data-ip-palette', 'technology-blue')
  await expect(page.locator('html')).toHaveAttribute('data-ip-color-mode', 'dark')

  // 刷新:会话恢复 + 用户偏好重新绑定,主题不回到默认
  await page.reload()
  await expect(page).toHaveURL(/\/pc\/home/)
  await expect(page.locator('html')).toHaveAttribute('data-ip-palette', 'technology-blue')
  await expect(page.locator('html')).toHaveAttribute('data-ip-color-mode', 'dark')

  // 注销 → 重登同一用户:同一作用域偏好仍被绑定
  await logout(page)
  await login(page, E2E_ADMIN)
  await expect(page.locator('html')).toHaveAttribute('data-ip-palette', 'technology-blue')
  await expect(page.locator('html')).toHaveAttribute('data-ip-color-mode', 'dark')
})

test('用户切换无主题串用:各用户偏好作用域隔离', async ({ page }) => {
  await login(page, E2E_ADMIN)
  await applyTheme(page, 'technology-blue', 'dark')
  await expect(page.locator('html')).toHaveAttribute('data-ip-palette', 'technology-blue')
  await logout(page)

  // 受限用户首次登录:作用域独立 → 产品默认,不继承管理员偏好
  await login(page, E2E_LIMITED)
  await expect(page.locator('html')).toHaveAttribute('data-ip-palette', 'industrial-cyan')
  await expect(page.locator('html')).not.toHaveAttribute('data-ip-palette', 'technology-blue')
})

test('真实权限驱动 Router Guard:管理员直达管理页,受限用户菜单隐藏且直达路由 403', async ({
  page,
}) => {
  await login(page, E2E_ADMIN)
  // 管理员:系统管理分组功能树列出管理项
  await page.getByRole('button', { name: '系统管理' }).click()
  await expect(page.getByRole('link', { name: '用户管理', exact: true })).toBeVisible()
  await expect(page.getByRole('link', { name: '角色权限', exact: true })).toBeVisible()

  // 直达管理页:真实数据渲染
  await page.goto('/pc/identity/users')
  await expect(page).toHaveURL(/\/pc\/identity\/users/)
  await expect(usersMainTable(page).getByText(E2E_ADMIN, { exact: true })).toBeVisible({
    timeout: 60_000,
  })
  await logout(page)

  // 受限用户:功能树不渲染越权项(菜单隐藏)……
  await login(page, E2E_LIMITED)
  await page.getByRole('button', { name: '系统管理' }).click()
  await expect(page.getByRole('link', { name: '用户管理' })).toHaveCount(0)

  // ……但菜单隐藏不替代直接路由 403(§14 预期输出 3)
  await page.goto('/pc/identity/users')
  await expect(page).toHaveURL(/\/403/)
  await expect(page.getByRole('heading', { name: '无权限' })).toBeVisible()
  await expect(page.getByText('无权访问')).toBeVisible()
})

test('业务标签恢复:管理员刷新恢复业务标签,受限用户不携带越权标签', async ({ page }) => {
  await login(page, E2E_ADMIN)
  await page.goto('/pc/identity/users')
  await expect(page).toHaveURL(/\/pc\/identity\/users/)
  await expect(usersMainTable(page).getByText(E2E_ADMIN, { exact: true })).toBeVisible({
    timeout: 60_000,
  })
  await expect(page.locator('.ip-pc-tabs')).toContainText('用户管理')

  // 刷新:标签恢复,仍停留在业务标签页
  await page.reload()
  await expect(page).toHaveURL(/\/pc\/identity\/users/)
  await expect(page.locator('.ip-pc-tabs')).toContainText('用户管理')
  await logout(page)

  // 受限用户作用域独立:仅有固定工作台,无越权业务标签
  await login(page, E2E_LIMITED)
  await expect(page.locator('.ip-pc-tabs')).toContainText('工作台')
  await expect(page.locator('.ip-pc-tabs')).not.toContainText('用户管理')
})

test('真实生产操作壳在 1280/1440 视口保持紧凑顶栏与完整卡片区', async ({ page }, testInfo) => {
  await page.setViewportSize({ width: 1280, height: 720 })
  await login(page, E2E_ADMIN)
  await page.getByRole('button', { name: '生产操作' }).click()
  await expect(page).toHaveURL(/\/pc\/operation/)
  await expect(page.getByTestId('operation-user-menu').locator('svg')).toHaveCSS('width', '18px')
  await expect(page.getByTestId('operation-user-menu').locator('svg')).toHaveCSS('height', '18px')

  const topbar = await page.locator('.ip-operation-topbar').boundingBox()
  const userMenu = await page.getByTestId('operation-user-menu').boundingBox()
  expect(topbar).not.toBeNull()
  expect(userMenu).not.toBeNull()
  expect(topbar!.height).toBeLessThanOrEqual(64)
  expect(userMenu!.height).toBeLessThanOrEqual(40)
  expect(userMenu!.x + userMenu!.width).toBeLessThanOrEqual(1280)
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true)
  await page.screenshot({ path: testInfo.outputPath('real-operation-1280x720.png'), fullPage: true })

  await page.setViewportSize({ width: 1440, height: 900 })
  await expect(page.locator('.pc-operation-card')).toHaveCount(9)
  const lastCard = await page.locator('.pc-operation-card').last().boundingBox()
  expect(lastCard).not.toBeNull()
  expect(lastCard!.y + lastCard!.height).toBeLessThanOrEqual(900)
  expect(await page.evaluate(() => document.documentElement.scrollHeight <= window.innerHeight)).toBe(true)
  await page.screenshot({ path: testInfo.outputPath('real-operation-1440x900.png'), fullPage: true })
})

test('注销干净回登录页且控制台无敏感令牌/密码日志', async ({ page }) => {
  const consoleTexts: string[] = []
  page.on('console', (msg) => consoleTexts.push(msg.text()))
  page.on('pageerror', (error) => consoleTexts.push(`pageerror:${error.message}`))

  await login(page, E2E_ADMIN)
  await logout(page)
  await expect(page.locator('.ip-pc-layout')).toHaveCount(0)
  await expect(page.getByTestId('login-username')).toBeVisible()

  // 敏感数据不落控制台:密码明文、access/refresh 令牌、英文 password 键
  const joined = consoleTexts.join('\n')
  expect(joined).not.toContain(E2E_PASSWORD)
  expect(joined).not.toContain('accessToken')
  expect(joined).not.toContain('refreshToken')
  expect(joined).not.toContain('"password"')
})
