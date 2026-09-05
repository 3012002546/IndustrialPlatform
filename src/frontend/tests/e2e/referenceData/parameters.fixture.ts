import { expect, test } from '@playwright/test'
import type { ConfigurationDomain } from '../../../src/api/referenceData/parameterTypes'

for (const scenario of [
  { width: 1366, height: 768, locale: 'zh-CN' },
  { width: 1920, height: 1080, locale: 'en-US' },
] as const) {
  test(`parameter browser fixture ${scenario.width} ${scenario.locale}`, async ({
    page,
  }, testInfo) => {
    const failures: string[] = []
    page.on('pageerror', (error) => failures.push(error.message))
    await page.setViewportSize(scenario)
    const permissions = [
      'platform.home.view',
      ...['view', 'create', 'update', 'disable'].map(
        (action) => `referencedata.parameter.${action}`,
      ),
    ]
    const user = {
      userNId: 'PF03-VISUAL',
      loginName: 'pf03.visual',
      name: 'Reference data operator',
      tenantNId: 'PF03-VISUAL',
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
              expiresAt: new Date(Date.now() + 3600000).toISOString(),
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
      traceId: 'pf03-browser-fixture',
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

    let domain: ConfigurationDomain = {
      id: '40b8d6c8-994e-475d-af29-d872b86492ce',
      nId: 'TRACE',
      name: scenario.locale === 'zh-CN' ? '追溯配置' : 'Trace configuration',
      description: null,
      scopeType: 'Tenant',
      tenantNId: user.tenantNId,
      status: 'Active',
      revision: 5,
      optimisticVersion: 4,
      concurrencyVersion: 'token-4',
      lastUpdatedOn: '2026-09-05T00:00:00Z',
      isFrozen: false,
      isLocked: false,
      keys: [
        {
          id: 'amount-key',
          nId: 'AMOUNT',
          fullNId: 'TRACE.AMOUNT',
          name: scenario.locale === 'zh-CN' ? '称量精度' : 'Weighing amount',
          description: null,
          dataType: 'Decimal',
          valueMode: 'Single',
          value: 12.3,
          defaultValue: null,
          valueJson: '12.3000000000',
          defaultValueJson: null,
          isMandatory: false,
          isReadOnly: false,
          dictionaryNId: null,
          referenceTarget: null,
          status: 'Active',
          sort: 0,
          multiValues: [],
          hasHadValue: true,
          isFrozen: false,
          isLocked: false,
        },
        {
          id: 'sources-key',
          nId: 'SOURCES',
          fullNId: 'TRACE.SOURCES',
          name: scenario.locale === 'zh-CN' ? '允许来源' : 'Allowed sources',
          description: null,
          dataType: 'String',
          valueMode: 'Multi',
          value: null,
          defaultValue: null,
          valueJson: null,
          defaultValueJson: null,
          isMandatory: false,
          isReadOnly: false,
          dictionaryNId: null,
          referenceTarget: null,
          status: 'Active',
          sort: 1,
          multiValues: [
            {
              id: 'manual-value',
              nId: 'MANUAL',
              name: 'Manual',
              value: 'manual',
              valueJson: '"manual"',
              sort: 0,
              isDefault: true,
              enabled: true,
            },
          ],
          hasHadValue: true,
          isFrozen: false,
          isLocked: false,
        },
      ],
    }
    let historyCount = 0
    const writes: string[] = []
    await page.route('**/referencedata/api/v1/reference-data/**', async (route) => {
      const request = route.request()
      const url = new URL(request.url())
      if (url.pathname.endsWith('/history')) {
        historyCount++
        return route.fulfill({
          json: envelope({
            items: [
              {
                id: 'h1',
                appDomainId: domain.id,
                keyId: 'amount-key',
                objectType: 'Key',
                objectId: 'amount-key',
                fullNId: 'TRACE.AMOUNT',
                changeType: 'Updated',
                changeReason: 'Precision check',
                beforeSummary: '{"hasValue":true}',
                afterSummary: '{"hasValue":true}',
                revision: domain.revision,
                userNId: user.userNId,
                traceId: 'pf03-fixture',
                createdOn: domain.lastUpdatedOn,
              },
            ],
            total: 1,
            pageIndex: 1,
            pageSize: 20,
          }),
        })
      }
      if (url.pathname.endsWith('/keys/AMOUNT') && request.method() === 'GET') {
        const key = domain.keys[0]!
        return route.fulfill({
          json: envelope({
            appDomainNId: domain.nId,
            keyNId: key.nId,
            fullNId: key.fullNId,
            dataType: key.dataType,
            valueMode: key.valueMode,
            value: null,
            valueJson: key.valueJson,
            multiValues: [],
            usesDefaultValue: false,
            blocksInheritance: key.valueJson === null,
            sourceScope: 'Tenant',
            sourceTenantNId: user.tenantNId,
            revision: domain.revision,
            lastUpdatedOn: domain.lastUpdatedOn,
          }),
        })
      }
      if (request.method() === 'PUT') {
        const raw = request.postData()!
        writes.push(raw)
        const body = JSON.parse(raw)
        expect(body.expectedAppDomainOptimisticVersion).toBe(domain.optimisticVersion)
        if (url.pathname.endsWith('/amount-key')) {
          // The raw request is asserted below; JSON.parse here is just fixture state handling.
          const exact = raw.match(/"value":([^,}]+),"defaultValue"/)?.[1] ?? 'null'
          domain.keys[0] = {
            ...domain.keys[0]!,
            name: body.name,
            valueJson: exact === 'null' ? null : exact,
            value: body.value,
          }
        } else if (url.pathname.endsWith('/manual-value')) {
          domain.keys[1]!.multiValues[0] = {
            ...domain.keys[1]!.multiValues[0]!,
            name: body.name,
            value: body.value,
            valueJson: JSON.stringify(body.value),
            isDefault: body.isDefault,
            enabled: body.enabled,
          }
        }
        domain = {
          ...domain,
          revision: domain.revision + 1,
          optimisticVersion: domain.optimisticVersion + 1,
          concurrencyVersion: 'token-' + (domain.optimisticVersion + 1),
        }
        return route.fulfill({ json: envelope(domain) })
      }
      if (url.pathname.endsWith('/' + domain.id)) return route.fulfill({ json: envelope(domain) })
      return route.fulfill({
        json: envelope({
          items: [{ ...domain, keys: undefined, keyCount: domain.keys.length }],
          total: 1,
          pageIndex: 1,
          pageSize: 20,
        }),
      })
    })
    await page.goto('/pc/system/reference-data/parameters')
    await expect(page.getByTestId('reference-data-parameters')).toBeVisible()
    await page.locator('.parameter-master .vxe-cell--radio').first().click()
    await expect(page.getByTestId('parameter-create-key')).toBeVisible()
    const edit = scenario.locale === 'zh-CN' ? '编辑' : 'Edit'
    const more = scenario.locale === 'zh-CN' ? '更多' : 'More'
    const close = scenario.locale === 'zh-CN' ? '关闭' : 'Close'
    async function openKey(nId: string) {
      const index = nId === 'AMOUNT' ? 0 : 1
      await page
        .locator('.parameter-detail')
        .getByRole('button', { name: more, exact: true })
        .nth(index)
        .click()
      await page.getByRole('menuitem', { name: edit, exact: true }).click()
    }
    await openKey('AMOUNT')
    let dialog = page.getByRole('dialog').filter({ has: page.getByTestId('parameter-save') })
    await dialog.getByTestId('form-surface-mode-toggle').click()
    await dialog
      .getByRole('textbox', { name: scenario.locale === 'zh-CN' ? '配置值' : 'Value', exact: true })
      .fill('999999999999999999.1234567890')
    await dialog
      .getByRole('textbox', {
        name: scenario.locale === 'zh-CN' ? '变更原因' : 'Change reason',
        exact: true,
      })
      .fill('Precision check')
    await dialog.screenshot({
      path: testInfo.outputPath('parameter-editor-' + scenario.width + '.png'),
      animations: 'disabled',
    })
    await dialog.getByTestId('parameter-save').click()
    await expect(page.getByTestId('parameter-save')).toHaveCount(0)
    expect(writes[0]).toContain('"value":999999999999999999.1234567890')
    await openKey('SOURCES')
    dialog = page.getByRole('dialog').filter({ hasText: 'TRACE.SOURCES' })
    await dialog
      .getByRole('tab', {
        name: scenario.locale === 'zh-CN' ? '多值明细' : 'Value entries',
        exact: true,
      })
      .click()
    await dialog
      .locator('.vxe-body--row')
      .filter({ hasText: 'MANUAL' })
      .getByRole('button', { name: edit, exact: true })
      .click()
    dialog = page.getByRole('dialog').filter({ has: page.getByTestId('parameter-save') })
    await dialog
      .getByRole('textbox', { name: scenario.locale === 'zh-CN' ? '配置值' : 'Value', exact: true })
      .fill('manual-reviewed')
    await dialog
      .getByRole('textbox', {
        name: scenario.locale === 'zh-CN' ? '变更原因' : 'Change reason',
        exact: true,
      })
      .fill('Review source')
    await dialog.getByTestId('parameter-save').click()
    await expect(page.getByTestId('parameter-save')).toHaveCount(0)
    expect(writes[1]).toContain('"value":"manual-reviewed"')
    await page
      .locator('.parameter-context')
      .getByRole('button', {
        name: scenario.locale === 'zh-CN' ? '变更历史' : 'Change history',
        exact: true,
      })
      .click()
    await expect(
      page.getByRole('dialog').getByText('Precision check', { exact: true }),
    ).toBeVisible()
    expect(historyCount).toBeGreaterThan(0)
    await page.getByRole('dialog').getByRole('button', { name: close, exact: true }).last().click()
    await expect(page.locator('.el-message')).toHaveCount(0)
    await page.screenshot({
      path: testInfo.outputPath('parameter-list-' + scenario.width + '.png'),
      fullPage: true,
      animations: 'disabled',
    })
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
    expect(failures).toEqual([])
  })
}
