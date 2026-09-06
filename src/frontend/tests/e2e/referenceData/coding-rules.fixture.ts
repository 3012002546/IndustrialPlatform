import { expect, test } from '@playwright/test'

type CodingRule = {
  id: string
  nId: string
  name: string
  targetEntityNId: string
  template: string
  resetPolicy: string
  scopeType: string
  tenantNId: string | null
  revision: number
  status: string
  sourceRevision: number | null
  optimisticVersion: number
  concurrencyVersion: string
  createdOn: string
  lastUpdatedOn: string
  publishedOn: string | null
  publishedBy: string | null
  isFrozen: boolean
  isLocked: boolean
}

for (const scenario of [
  { width: 1366, height: 768, locale: 'zh-CN' },
  { width: 1920, height: 1080, locale: 'en-US' },
] as const) {
  test(`coding rule browser fixture ${scenario.width} ${scenario.locale}`, async ({
    page,
  }, testInfo) => {
    const failures: string[] = []
    page.on('pageerror', (error) => failures.push(error.message))
    page.on('console', (message) => {
      if (message.type() === 'error') failures.push(message.text())
    })
    await page.setViewportSize(scenario)

    const permissions = [
      'platform.home.view',
      ...['view', 'create', 'update', 'publish', 'disable', 'preview', 'generate'].map(
        (action) => `referencedata.coding-rule.${action}`,
      ),
    ]
    const user = {
      userNId: 'PF03-CODING-VISUAL',
      loginName: 'pf03.coding.visual',
      name: 'Coding rule operator',
      tenantNId: 'PF03-CODING-TENANT',
      roleNIds: [],
      permissionNIds: permissions,
      mustChangePassword: false,
    }
    await page.addInitScript(
      ({ user, locale }) => {
        sessionStorage.setItem(
          'industrial-platform.auth.http.v1',
          JSON.stringify({
            version: 1,
            session: {
              accessToken: 'fixture-token',
              refreshToken: 'fixture-refresh',
              expiresAt: new Date(Date.now() + 3_600_000).toISOString(),
              user: {
                userId: user.userNId,
                username: user.loginName,
                displayName: user.name,
                tenantId: user.tenantNId,
                roles: [],
                permissions: user.permissionNIds,
                mustChangePassword: false,
              },
            },
          }),
        )
        localStorage.setItem(
          'industrial-platform.locale.preferences.v1',
          JSON.stringify({
            locale,
            timeZone: 'Asia/Taipei',
            dateFormat: 'yyyy-MM-dd',
            numberLocale: locale,
            unitSystem: 'metric',
          }),
        )
      },
      { user, locale: scenario.locale },
    )

    const envelope = (data: unknown) => ({
      success: true,
      code: '200',
      message: 'success',
      data,
      traceId: 'pf03-coding-rule-browser-fixture',
    })
    await page.route('**/identity/api/v1/**', (route) => route.fulfill({ json: envelope(user) }))
    await page.route('**/systemdata/runtime/**', (route) => {
      const url = route.request().url()
      const data = url.includes('theme-policy')
        ? {
            policyRevision: 0,
            configured: false,
            degraded: false,
            allowedPalettes: ['industrial-cyan'],
            allowedModes: ['light', 'dark'],
            allowedPcDensities: ['comfortable'],
            defaultPalette: 'industrial-cyan',
            defaultMode: 'light',
            defaultPcDensity: 'comfortable',
          }
        : url.includes('features')
          ? { revision: 0, configured: false, degraded: false, items: [] }
          : { revision: 0, configured: false, degraded: false, nodes: [] }
      return route.fulfill({ json: envelope(data) })
    })

    const now = '2026-09-05T00:00:00Z'
    const admin = '/referencedata/api/v1/reference-data/admin/coding-rules'
    const runtime = '/referencedata/api/v1/reference-data/coding-rules'
    let rule: CodingRule | null = null
    let nextSequence = 1
    const idempotency = new Map<string, { request: string; code: string; sequence: number }>()
    const generationKeys: string[] = []

    function preview() {
      const current = rule!
      return {
        codingRuleNId: current.nId,
        ruleRevision: current.revision,
        sourceScope: current.scopeType,
        sourceTenantNId: current.tenantNId,
        code: `LOT-${user.tenantNId}-0001`,
        sampleSequence: 1,
        periodKey: 'ALL',
        previewedOn: now,
        consumesSequence: false,
      }
    }

    await page.route('**/referencedata/api/v1/reference-data/**', async (route) => {
      const request = route.request()
      const path = new URL(request.url()).pathname
      const method = request.method()

      if (path === admin && method === 'GET')
        return route.fulfill({
          json: envelope({
            items: rule ? [rule] : [],
            total: rule ? 1 : 0,
            pageIndex: 1,
            pageSize: 20,
          }),
        })

      if (path === admin && method === 'POST') {
        const body = request.postDataJSON()
        expect(body).toMatchObject({
          scopeType: 'Tenant',
          nId: 'LOT_FIXTURE',
          targetEntityNId: 'LOT',
          template: 'LOT-{TENANT}-{SEQ:4}',
          resetPolicy: 'Never',
        })
        rule = {
          id: 'ca8b233c-4910-4fad-86fc-d29d658dd250',
          ...body,
          nId: body.nId.toUpperCase(),
          targetEntityNId: body.targetEntityNId.toUpperCase(),
          tenantNId: user.tenantNId,
          revision: 1,
          status: 'Draft',
          sourceRevision: null,
          optimisticVersion: 1,
          concurrencyVersion: 'ce07b421-cd46-43f9-b030-a007c6250a97',
          createdOn: now,
          lastUpdatedOn: now,
          publishedOn: null,
          publishedBy: null,
          isFrozen: false,
          isLocked: false,
        }
        return route.fulfill({ status: 201, json: envelope(rule) })
      }

      if (rule && path === `${admin}/${rule.id}` && method === 'GET')
        return route.fulfill({ json: envelope(rule) })

      if (rule && path === `${admin}/${rule.id}/preview` && method === 'POST') {
        expect(request.postDataJSON()).toEqual({ factoryId: null })
        return route.fulfill({ json: envelope(preview()) })
      }

      if (rule && path === `${admin}/${rule.id}/publish` && method === 'POST') {
        expect(request.postDataJSON()).toMatchObject({
          expectedOptimisticVersion: rule.optimisticVersion,
          expectedConcurrencyVersion: rule.concurrencyVersion,
        })
        rule = {
          ...rule,
          status: 'Published',
          publishedOn: now,
          publishedBy: user.userNId,
          optimisticVersion: rule.optimisticVersion + 1,
          concurrencyVersion: '2c360efc-644a-465f-aad4-f034280584c0',
        }
        return route.fulfill({ json: envelope(rule) })
      }

      if (rule && path === `${runtime}/${rule.nId}/preview` && method === 'POST') {
        expect(request.postDataJSON()).toEqual({
          sourceScope: 'Tenant',
          sourceTenantNId: user.tenantNId,
          ruleRevision: 1,
          factoryId: null,
        })
        return route.fulfill({ json: envelope(preview()) })
      }

      if (rule && path === `${runtime}/${rule.nId}/generate` && method === 'POST') {
        const key = request.headers()['idempotency-key']
        expect(key).toBeTruthy()
        generationKeys.push(key!)
        const requestBody = request.postData()!
        const existing = idempotency.get(key!)
        if (existing) {
          expect(existing.request).toBe(requestBody)
          return route.fulfill({
            json: envelope({
              codingRuleNId: rule.nId,
              ruleRevision: rule.revision,
              code: existing.code,
              sequence: existing.sequence,
              periodKey: 'ALL',
              generatedOn: now,
            }),
          })
        }
        const sequence = nextSequence++
        const code = `LOT-${user.tenantNId}-${String(sequence).padStart(4, '0')}`
        idempotency.set(key!, { request: requestBody, code, sequence })
        return route.fulfill({
          json: envelope({
            codingRuleNId: rule.nId,
            ruleRevision: rule.revision,
            code,
            sequence,
            periodKey: 'ALL',
            generatedOn: now,
          }),
        })
      }

      return route.fulfill({ status: 404, json: envelope(null) })
    })

    const labels =
      scenario.locale === 'zh-CN'
        ? { publish: '发布', published: '已发布' }
        : { publish: 'Publish', published: 'Published' }

    await page.goto('/pc/system/reference-data/coding-rules')
    const root = page.getByTestId('coding-rules-page')
    const more = root.locator('[data-testid="coding-rule-more"]:visible')
    await expect(root).toBeVisible()
    const table = root.locator('.app-data-table').first()
    await expect(table.locator('.vxe-cell--radio')).toHaveCount(0)
    await expect(table.locator('.app-data-table__selection-summary')).toHaveCount(0)
    for (const tool of ['query-toggle', 'sort', 'group', 'export']) {
      await expect(table.getByTestId(`app-data-table-${tool}`)).toBeVisible()
    }
    await root.getByTestId('coding-rule-create').click()
    const editor = page.getByRole('dialog').filter({ has: page.getByTestId('coding-rule-save') })
    await expect(editor.getByText('{YYYY} {MM} {DD} {TENANT} {FACTORY} {SEQ:n}')).toBeVisible()
    await expect(editor.getByTestId('coding-rule-factory')).toBeDisabled()
    await editor.getByTestId('coding-rule-nid').fill('LOT_FIXTURE')
    await editor.getByTestId('coding-rule-name').fill('Fixture lot rule')
    await editor.getByTestId('coding-rule-target').fill('LOT')
    await editor.getByTestId('coding-rule-template').fill('LOT-{TENANT}-{SEQ:4}')
    await editor.screenshot({
      path: testInfo.outputPath(`coding-rule-editor-${scenario.width}.png`),
      animations: 'disabled',
    })
    await editor.getByTestId('coding-rule-save').click()
    await expect(editor).not.toBeVisible()

    await expect(more).toBeVisible()
    await more.click()
    await expect(page.locator('[data-testid="coding-rule-tools"]:visible')).toBeVisible()
    await page.locator('[data-testid="coding-rule-tools"]:visible').click()
    let tools = page
      .getByRole('dialog')
      .filter({ has: page.getByTestId('coding-rule-preview-draft') })
    await tools.getByTestId('coding-rule-preview-draft').click()
    await expect(tools.getByTestId('coding-rule-preview-result')).toContainText(
      `LOT-${user.tenantNId}-0001`,
    )
    await tools.getByTestId('form-drawer-close').click()

    await expect(more).toBeVisible()
    await more.click()
    await expect(page.locator('[data-testid="coding-rule-publish"]:visible')).toBeVisible()
    await page.locator('[data-testid="coding-rule-publish"]:visible').click()
    const confirm = page.locator('.el-message-box')
    await expect(confirm).toBeVisible()
    await confirm.getByRole('button', { name: labels.publish, exact: true }).click()
    await expect(root.locator('.app-data-table').first()).toContainText(labels.published)

    await expect(more).toBeVisible()
    await more.click()
    await expect(page.locator('[data-testid="coding-rule-tools"]:visible')).toBeVisible()
    await page.locator('[data-testid="coding-rule-tools"]:visible').click()
    tools = page
      .getByRole('dialog')
      .filter({ has: page.getByTestId('coding-rule-preview-runtime') })
    await tools.getByTestId('coding-rule-preview-runtime').click()
    await expect(tools.getByTestId('coding-rule-preview-result')).toContainText(
      `LOT-${user.tenantNId}-0001`,
    )
    await tools.getByTestId('coding-rule-idempotency-key').fill('fixture-same-key')
    await tools.getByTestId('coding-rule-generate').click()
    await expect(tools.getByTestId('coding-rule-generated-result')).toContainText(
      `LOT-${user.tenantNId}-0001`,
    )
    await tools.getByTestId('coding-rule-generate').click()
    await expect.poll(() => generationKeys.length).toBe(2)
    expect(generationKeys).toEqual(['fixture-same-key', 'fixture-same-key'])
    expect(nextSequence).toBe(2)

    const pcMain = page.locator('.ip-pc-main')
    const mainMetrics = await pcMain.evaluate((element) => ({
      scrollLeft: element.scrollLeft,
      scrollWidth: element.scrollWidth,
      clientWidth: element.clientWidth,
    }))
    expect(mainMetrics.scrollLeft, JSON.stringify(mainMetrics)).toBe(0)
    expect(mainMetrics.scrollWidth, JSON.stringify(mainMetrics)).toBeLessThanOrEqual(
      mainMetrics.clientWidth + 1,
    )
    await page.screenshot({
      path: testInfo.outputPath(`coding-rules-${scenario.width}.png`),
      fullPage: true,
      animations: 'disabled',
    })
    expect(
      await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1),
    ).toBe(true)
    expect(failures).toEqual([])
  })
}
