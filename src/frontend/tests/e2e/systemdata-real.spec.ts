import { expect, test } from '@playwright/test'

const ADMIN = process.env.E2E_ADMIN ?? 'e2e.admin'
const PASSWORD = process.env.E2E_PASSWORD ?? 'E2e!Admin@2026'

test('UnifiedHost admin 完成组织与岗位真实 CRUD/状态闭环', async ({ page }, testInfo) => {
  const suffix = Date.now().toString()
  const organizationNId = `pf02-e2e-company-${suffix}`
  const positionNId = `pf02-e2e-position-${suffix}`
  const organizationName = `PF02 E2E 公司 ${suffix}`
  const positionName = `PF02 E2E 岗位 ${suffix}`
  let organizationCreated = false
  let positionCreated = false

  try {
    if (process.env.E2E_BYPASS_LOGIN === '1') {
    const response = await page.request.post('http://localhost:5041/identity/api/v1/auth/login', {
      data: { loginName: ADMIN, password: PASSWORD },
    })
    expect(response.ok()).toBeTruthy()
    const payload = (await response.json()).data
    await page.addInitScript(
      (session) => {
        sessionStorage.setItem(
          'industrial-platform.auth.http.v1',
          JSON.stringify({ version: 1, session }),
        )
      },
      {
        accessToken: payload.accessToken,
        refreshToken: payload.refreshToken,
        expiresAt: payload.expiresAt,
        user: {
          userId: payload.user.userNId,
          username: payload.user.loginName,
          displayName: payload.user.name,
          tenantId: payload.user.tenantNId,
          roles: payload.user.roleNIds,
          permissions: payload.user.permissionNIds,
        },
      },
    )
    await page.goto('/pc/home')
  } else {
    await page.goto('/login')
    await page.getByTestId('login-username').fill(ADMIN)
    await page.getByTestId('login-password').fill(PASSWORD)
    await page.getByTestId('login-submit').click()
  }
  await expect(page).toHaveURL(/\/pc\/home/, { timeout: 60_000 })

  await page.goto('/pc/systemdata/organizations')
  await expect(page.getByRole('heading', { level: 1, name: '行政组织与岗位' })).toBeVisible({
    timeout: 60_000,
  })

  await page.getByRole('button', { name: '新建组织' }).click()
  await page.getByRole('textbox', { name: '组织 NId', exact: true }).fill(organizationNId)
  await page.getByRole('textbox', { name: '组织名称', exact: true }).fill(organizationName)
  await page.getByRole('spinbutton', { name: '组织显示顺序', exact: true }).fill('0')
  await page.getByTestId('form-drawer-submit').click()
  await expect(page.getByRole('button', { name: organizationName })).toBeVisible({
    timeout: 60_000,
  })
  organizationCreated = true

  await page.getByRole('button', { name: organizationName }).click()
  await page.getByRole('button', { name: '新建岗位' }).click()
  await page.getByRole('textbox', { name: '岗位 NId', exact: true }).fill(positionNId)
  await page.getByRole('textbox', { name: '岗位名称', exact: true }).fill(positionName)
  await page.getByRole('spinbutton', { name: '岗位显示顺序', exact: true }).fill('1')
  await page.getByTestId('form-drawer-submit').click()
  await expect(page.getByText(positionName, { exact: true })).toBeVisible({ timeout: 60_000 })
  positionCreated = true

  // 编辑与状态操作走领域端点；清理使用停用而不是物理删除。
  await page.getByRole('button', { name: '编辑组织' }).click()
  await page
    .getByRole('textbox', { name: '组织名称', exact: true })
    .fill(`${organizationName} 已更新`)
  await page.getByTestId('form-drawer-submit').click()
  await expect(page.getByText(`${organizationName} 已更新`, { exact: true })).toBeVisible()

  const positionRow = page.getByRole('row').filter({ hasText: positionName })
  await positionRow.getByRole('button', { name: '停用' }).click()
  await expect(positionRow).toContainText('Inactive')
  await page.getByRole('button', { name: '停用组织' }).click()
  await expect(page.getByText('Inactive', { exact: true }).first()).toBeVisible()
  } finally {
    const cleanup = {
      exactOnly: true,
      organizationNId,
      positionNId,
      organizationCreated,
      positionCreated,
      statusCalls: [] as Array<{ target: string; status: number }>,
      physicalDelete: false,
      blocker:
        'SystemData 仅提供精确 status 端点，没有经安全备份/事务保护的物理删除端点；因此不执行 DELETE、不修改 is_deleted。',
    }
    try {
      const session = await page.evaluate(() => {
        const raw = sessionStorage.getItem('industrial-platform.auth.http.v1')
        if (!raw) return null
        try {
          const parsed = JSON.parse(raw) as { session?: { accessToken?: string }; accessToken?: string }
          return parsed.session?.accessToken ?? parsed.accessToken ?? null
        } catch {
          return null
        }
      })
      if (session && positionCreated) {
        const response = await page.evaluate(
          async ({ token, nId, suffix: cleanupSuffix }) =>
            fetch(`/systemdata/api/v1/positions/${encodeURIComponent(nId)}/status`, {
              method: 'PUT',
              headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' },
              body: JSON.stringify({ status: 'Inactive', reason: `PF-02 E2E cleanup ${cleanupSuffix}` }),
            }).then((result) => ({ status: result.status })),
          { token: session, nId: positionNId, suffix },
        )
        cleanup.statusCalls.push({ target: positionNId, status: response.status })
      }
      if (session && organizationCreated) {
        const response = await page.evaluate(
          async ({ token, nId, suffix: cleanupSuffix }) =>
            fetch(`/systemdata/api/v1/organizations/${encodeURIComponent(nId)}/status`, {
              method: 'PUT',
              headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' },
              body: JSON.stringify({ status: 'Inactive', reason: `PF-02 E2E cleanup ${cleanupSuffix}` }),
            }).then((result) => ({ status: result.status })),
          { token: session, nId: organizationNId, suffix },
        )
        cleanup.statusCalls.push({ target: organizationNId, status: response.status })
      }
    } catch (error) {
      cleanup.blocker = `${cleanup.blocker} 精确停用复核失败: ${error instanceof Error ? error.message : String(error)}`
    }
    await testInfo.attach('pf02-test-data-cleanup', {
      body: Buffer.from(JSON.stringify(cleanup, null, 2)),
      contentType: 'application/json',
    })
  }
})
