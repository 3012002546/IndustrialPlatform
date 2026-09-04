import { expect, test } from '@playwright/test'

test('无 SystemData 管理权限直达 PC 管理页被路由守卫阻断', async ({ page }) => {
  await page.goto('/login')
  await page.getByLabel('用户名').fill('mock.admin')
  await page.getByLabel('密码', { exact: true }).fill('Mock@123456')
  await page.getByTestId('login-submit').click()
  await expect(page).toHaveURL(/\/pc\/home/)

  await page.goto('/pc/systemdata/organizations')
  await expect(page).toHaveURL(/\/403/)
  await expect(page.getByRole('heading', { level: 1, name: '无权限' })).toBeVisible()
})

test('最大权限账号可进入 SystemData 管理页且显示默认 Mock 错误态', async ({ page }) => {
  const permissions = [
    'systemdata.organization.view',
    'systemdata.organization.create',
    'systemdata.organization.update',
    'systemdata.organization.move',
    'systemdata.organization.status',
    'systemdata.assignment.view',
    'systemdata.assignment.manage',
    'systemdata.navigation.view',
    'systemdata.navigation.manage',
    'systemdata.navigation.publish',
    'systemdata.navigation.rollback',
    'systemdata.feature.view',
    'systemdata.feature.manage',
    'systemdata.service-catalog.view',
    'systemdata.service-catalog.manage',
    'systemdata.theme-policy.view',
    'systemdata.theme-policy.manage',
    'systemdata.service-initialization.view',
    'systemdata.service-initialization.register',
    'systemdata.service-initialization.plan',
    'systemdata.service-initialization.apply',
    'systemdata.service-initialization.approve',
    'systemdata.service-initialization.backup',
    'systemdata.service-initialization.cancel',
  ]
  await page.route('**/systemdata/api/v1/**', async (route) => {
    await route.fulfill({
      status: 503,
      contentType: 'application/json',
      body: JSON.stringify({
        success: false,
        code: 'DEFAULT_MOCK_API_UNAVAILABLE',
        message: '默认 Mock 基线不提供 SystemData 管理数据',
        data: null,
        traceId: 'trace-default-mock-baseline',
      }),
    })
  })
  await page.addInitScript(
    ({ permissions: granted }) => {
      sessionStorage.setItem(
        'industrial-platform.auth.mock.v1',
        JSON.stringify({
          version: 1,
          session: {
            accessToken: 'mock-e2e-access',
            refreshToken: 'mock-e2e-refresh',
            expiresAt: new Date(Date.now() + 3_600_000).toISOString(),
            user: {
              userId: 'mock-e2e-admin',
              username: 'mock.admin',
              displayName: 'Mock E2E 最大权限账号',
              tenantId: 'dev-tenant',
              roles: ['admin'],
              permissions: granted,
              mustChangePassword: false,
            },
          },
        }),
      )
    },
    { permissions },
  )

  async function assertRouteShell(path: string, title: string): Promise<void> {
    await page.goto(path)
    await expect(page).toHaveURL(new RegExp(`${path.replaceAll('/', '\\/')}$`))
    await expect(page.getByTestId('systemdata-admin-page')).toBeVisible()
    await expect(page.getByRole('heading', { level: 1, name: title })).toBeVisible()
    await expect(page.locator('.systemdata-admin-frame')).toBeVisible()
    await expect(page.getByTestId('systemdata-record-count')).toBeVisible()
    await expect(page.locator('.systemdata-page-actions .el-button').first()).toBeVisible()

    const geometry = await page.locator('.systemdata-admin-frame').evaluate((element) => {
      const frame = element as HTMLElement
      const rect = frame.getBoundingClientRect()
      const style = getComputedStyle(frame)
      return {
        width: rect.width,
        viewportWidth: window.innerWidth,
        borderRadius: style.borderRadius,
        documentScrollWidth: document.documentElement.scrollWidth,
      }
    })
    expect(geometry.width).toBeGreaterThan(0)
    expect(geometry.width).toBeLessThanOrEqual(geometry.viewportWidth + 1)
    expect(geometry.borderRadius).not.toBe('0px')
    expect(geometry.documentScrollWidth).toBeLessThanOrEqual(geometry.viewportWidth + 1)
  }

  await assertRouteShell('/pc/systemdata/organizations', '行政组织与岗位')
  await expect(page.locator('.app-error-alert')).toBeVisible()
  await expect(page.locator('.systemdata-admin-content')).toHaveCount(0)
})
