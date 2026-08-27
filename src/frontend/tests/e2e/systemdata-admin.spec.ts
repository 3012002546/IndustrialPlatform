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

test('最大权限账号可进入 SystemData 七个 PC 管理入口且无新增 Vue 警告', async ({ page }) => {
  const permissions = [
    'systemdata.organization.view',
    'systemdata.assignment.view',
    'systemdata.navigation.view',
    'systemdata.feature.view',
    'systemdata.service-catalog.view',
    'systemdata.theme-policy.view',
    'systemdata.service-initialization.view',
  ]
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

  const vueWarnings: string[] = []
  page.on('console', (message) => {
    if (message.type() === 'warning' && /Vue warn/i.test(message.text()))
      vueWarnings.push(message.text())
  })
  page.on('pageerror', (error) => {
    if (/Vue warn/i.test(error.message)) vueWarnings.push(error.message)
  })

  const pages = [
    ['/pc/systemdata/organizations', '行政组织与岗位'],
    ['/pc/systemdata/assignments', '用户任职'],
    ['/pc/systemdata/navigation', '导航与资源发布'],
    ['/pc/systemdata/features', '功能开关'],
    ['/pc/systemdata/services', '服务目录'],
    ['/pc/systemdata/themes', '租户主题策略'],
    ['/pc/systemdata/service-initialization', '服务初始化编排'],
  ] as const
  for (const [path, title] of pages) {
    await page.goto(path)
    await expect(page).toHaveURL(new RegExp(`${path.replaceAll('/', '\\/')}$`))
    await expect(page.getByTestId('systemdata-admin-page')).toBeVisible()
    await expect(page.getByRole('heading', { level: 1, name: title })).toBeVisible()
  }
  expect(vueWarnings).toEqual([])
})
