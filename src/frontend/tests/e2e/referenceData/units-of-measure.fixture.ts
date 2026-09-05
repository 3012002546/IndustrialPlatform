import { expect, test } from '@playwright/test'

type UnitRequest = {
  nId: string
  name: string
  symbol: string
  factorToBase: string
  offsetToBase: string
  decimalPlaces: number
  roundingMode: string
  enabled: boolean
  sort: number
}

type Unit = UnitRequest & {
  id: string
  isFrozen: boolean
  isLocked: boolean
}

type Dimension = {
  id: string
  nId: string
  name: string
  description: string | null
  scopeType: string
  tenantNId: string | null
  revision: number
  status: string
  sourceRevision: number | null
  publishedOn: string | null
  publishedBy: string | null
  isSystemDefined: boolean
  conversionKind: string
  baseUnitNId: string
  units: Unit[]
  optimisticVersion: number
  concurrencyVersion: string
  lastUpdatedOn: string
  isFrozen: boolean
  isLocked: boolean
}

for (const scenario of [
  { width: 1366, height: 768, locale: 'zh-CN' },
  { width: 1920, height: 1080, locale: 'en-US' },
] as const) {
  test(`unit of measure browser fixture ${scenario.width} ${scenario.locale}`, async ({
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
      ...['view', 'create', 'update', 'publish', 'disable'].map(
        (action) => `referencedata.unit-of-measure.${action}`,
      ),
    ]
    const user = {
      userNId: 'PF03-UOM-VISUAL',
      loginName: 'pf03.uom.visual',
      name: 'Unit of measure operator',
      tenantNId: 'PF03-UOM-TENANT',
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
      traceId: 'pf03-uom-browser-fixture',
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
    const admin = '/referencedata/api/v1/reference-data/admin/units-of-measure/dimensions'
    const runtime = '/referencedata/api/v1/reference-data/units-of-measure'
    let dimension: Dimension | null = null
    const writes: string[] = []
    let conversionRead = false
    let fixedHistoryRead = false
    let currentRead = false

    function summary() {
      const current = dimension!
      return { ...current, units: undefined, unitCount: current.units.length }
    }

    function runtimeDimension() {
      const current = dimension!
      return {
        nId: current.nId,
        name: current.name,
        description: current.description,
        sourceScope: current.scopeType,
        sourceTenantNId: current.tenantNId,
        revision: current.revision,
        status: current.status,
        sourceRevision: current.sourceRevision,
        publishedOn: current.publishedOn,
        isSystemDefined: current.isSystemDefined,
        conversionKind: current.conversionKind,
        baseUnitNId: current.baseUnitNId,
        units: current.units.map((unit) => ({
          nId: unit.nId,
          name: unit.name,
          symbol: unit.symbol,
          factorToBase: unit.factorToBase,
          offsetToBase: unit.offsetToBase,
          decimalPlaces: unit.decimalPlaces,
          roundingMode: unit.roundingMode,
          enabled: unit.enabled,
          sort: unit.sort,
        })),
      }
    }

    await page.route('**/referencedata/api/v1/reference-data/**', async (route) => {
      const request = route.request()
      const url = new URL(request.url())
      const method = request.method()
      const path = url.pathname

      if (path === `${runtime}/convert` && method === 'POST') {
        conversionRead = true
        const body = request.postDataJSON()
        expect(body).toEqual({
          sourceScope: 'Tenant',
          sourceTenantNId: user.tenantNId,
          unitDimensionNId: 'LENGTH_FIXTURE',
          unitRevision: 1,
          fromUnitNId: 'M',
          toUnitNId: 'MM',
          value: '0',
        })
        return route.fulfill({
          json: envelope({
            unitDimensionNId: 'LENGTH_FIXTURE',
            unitRevision: 1,
            sourceScope: 'Tenant',
            sourceTenantNId: user.tenantNId,
            fromUnitNId: 'M',
            toUnitNId: 'MM',
            inputValue: '0',
            resultValue: '0',
            wasRounded: false,
            conversionSnapshot: {
              sourceFactorToBase: '1',
              sourceOffsetToBase: '0',
              targetFactorToBase: '0.001',
              targetOffsetToBase: '0',
              decimalPlaces: 3,
              roundingMode: 'ToEven',
            },
          }),
        })
      }

      if (path === `${runtime}/dimensions` && method === 'GET') {
        return route.fulfill({
          json: envelope({
            items: dimension?.status === 'Published' ? [summary()] : [],
            total: dimension?.status === 'Published' ? 1 : 0,
            pageIndex: 1,
            pageSize: 20,
          }),
        })
      }

      const revision = path.match(/\/dimensions\/([^/]+)\/revisions\/(\d+)$/)
      if (revision && method === 'GET') {
        fixedHistoryRead = true
        expect(decodeURIComponent(revision[1]!)).toBe('LENGTH_FIXTURE')
        expect(revision[2]).toBe('1')
        expect(url.searchParams.get('sourceScope')).toBe('Tenant')
        expect(url.searchParams.get('sourceTenantNId')).toBe(user.tenantNId)
        return route.fulfill({ json: envelope(runtimeDimension()) })
      }

      if (path === `${runtime}/dimensions/LENGTH_FIXTURE` && method === 'GET') {
        currentRead = true
        expect(url.searchParams.get('sourceScope')).toBe('Tenant')
        expect(url.searchParams.get('sourceTenantNId')).toBe(user.tenantNId)
        return route.fulfill({ json: envelope(runtimeDimension()) })
      }

      if (path === admin && method === 'GET') {
        return route.fulfill({
          json: envelope({
            items: dimension ? [summary()] : [],
            total: dimension ? 1 : 0,
            pageIndex: 1,
            pageSize: 20,
          }),
        })
      }

      if (path === admin && method === 'POST') {
        const raw = request.postData()!
        writes.push(raw)
        const body = JSON.parse(raw) as {
          scopeType: string
          nId: string
          name: string
          description: string | null
          conversionKind: string
          baseUnitNId: string
          units: UnitRequest[]
        }
        expect(body.conversionKind).toBe('Ratio')
        expect(body.units).toHaveLength(2)
        expect(body.units.every((unit) => unit.offsetToBase === '0')).toBe(true)
        expect(body.units.find((unit) => unit.nId.toUpperCase() === 'MM')?.factorToBase).toBe(
          '0.001000000000',
        )
        const base = body.units.find(
          (unit) => unit.nId.toUpperCase() === body.baseUnitNId.toUpperCase(),
        )
        expect(base).toMatchObject({ factorToBase: '1', offsetToBase: '0', enabled: true })
        dimension = {
          id: '113d661c-5af1-4ea6-a207-34d76a9229b5',
          nId: body.nId.toUpperCase(),
          name: body.name,
          description: body.description,
          scopeType: body.scopeType,
          tenantNId: user.tenantNId,
          revision: 1,
          status: 'Draft',
          sourceRevision: null,
          publishedOn: null,
          publishedBy: null,
          isSystemDefined: false,
          conversionKind: body.conversionKind,
          baseUnitNId: body.baseUnitNId.toUpperCase(),
          units: body.units.map((unit, index) => ({
            ...unit,
            id: `c289f794-5006-4100-8d4d-510f01eedf0${index}`,
            nId: unit.nId.toUpperCase(),
            isFrozen: false,
            isLocked: false,
          })),
          optimisticVersion: 1,
          concurrencyVersion: '6cbeb6a2-095d-4224-8b20-9ca1e35a6df0',
          lastUpdatedOn: now,
          isFrozen: false,
          isLocked: false,
        }
        return route.fulfill({ status: 201, json: envelope(dimension) })
      }

      if (dimension && path === `${admin}/${dimension.id}` && method === 'GET')
        return route.fulfill({ json: envelope(dimension) })

      if (dimension && path === `${admin}/${dimension.id}/publish` && method === 'POST') {
        const body = request.postDataJSON()
        expect(body.expectedOptimisticVersion).toBe(dimension.optimisticVersion)
        expect(body.expectedConcurrencyVersion).toBe(dimension.concurrencyVersion)
        dimension = {
          ...dimension,
          status: 'Published',
          publishedOn: now,
          publishedBy: user.userNId,
          optimisticVersion: dimension.optimisticVersion + 1,
          concurrencyVersion: '443931fa-1957-4462-80c4-f5df1e581ef1',
        }
        return route.fulfill({ json: envelope(dimension) })
      }

      return route.fulfill({ status: 404, json: envelope(null) })
    })

    const labels =
      scenario.locale === 'zh-CN'
        ? {
            ratio: '比例换算',
            publish: '发布',
            published: '已发布',
            current: '当前版本',
            fixed: '固定修订',
          }
        : {
            ratio: 'Ratio',
            publish: 'Publish',
            published: 'Published',
            current: 'Current revision',
            fixed: 'Fixed revision',
          }

    await page.goto('/pc/system/reference-data/units-of-measure')
    const root = page.getByTestId('reference-data-units-of-measure')
    const more = root.locator('[data-testid="unit-dimension-more"]:visible')
    await expect(root).toBeVisible()
    await root.getByTestId('unit-dimension-create').click()
    const dimensionEditor = page
      .getByRole('dialog')
      .filter({ has: page.getByTestId('unit-dimension-save') })
    await dimensionEditor.getByTestId('unit-dimension-nid').fill('LENGTH_FIXTURE')
    await dimensionEditor.getByTestId('unit-dimension-name').fill('Fixture length')
    await dimensionEditor.getByTestId('unit-dimension-conversion-kind').click({ force: true })
    await page.getByRole('option', { name: labels.ratio, exact: true }).click()

    const unitField = (name: string) => dimensionEditor.getByTestId(name).first()
    await unitField('unit-definition-nid-0').fill('M')
    await unitField('unit-definition-name-0').fill('Metre')
    await unitField('unit-definition-symbol-0').fill('m')
    await expect(unitField('unit-definition-offset-0')).toHaveValue('0')
    await expect(unitField('unit-definition-offset-0')).toBeDisabled()
    await unitField('unit-definition-decimals-0').locator('input').fill('3')

    await dimensionEditor.getByTestId('unit-definition-add').click()
    await unitField('unit-definition-nid-1').fill('MM')
    await unitField('unit-definition-name-1').fill('Millimetre')
    await unitField('unit-definition-symbol-1').fill('mm')
    await unitField('unit-definition-factor-1').fill('0.001000000000')
    await expect(unitField('unit-definition-offset-1')).toHaveValue('0')
    await expect(unitField('unit-definition-offset-1')).toBeDisabled()
    await unitField('unit-definition-decimals-1').locator('input').fill('3')
    await dimensionEditor.getByTestId('unit-dimension-base-unit').click({ force: true })
    await page.getByRole('option', { name: 'Metre (M)', exact: true }).click()
    await dimensionEditor.screenshot({
      path: testInfo.outputPath(`unit-dimension-editor-${scenario.width}.png`),
      animations: 'disabled',
    })
    await dimensionEditor.getByTestId('unit-dimension-save').click()
    await expect(dimensionEditor).not.toBeVisible()
    expect(writes).toHaveLength(1)
    expect(writes[0]).toContain('"factorToBase":"0.001000000000"')
    expect(writes[0]).toContain('"offsetToBase":"0"')

    await expect(more).toBeVisible()
    await more.click()
    await expect(page.locator('[data-testid="unit-dimension-publish"]:visible')).toBeVisible()
    await page.locator('[data-testid="unit-dimension-publish"]:visible').click()
    const publishConfirm = page.locator('.el-message-box')
    await expect(publishConfirm).toBeVisible()
    await publishConfirm.getByRole('button', { name: labels.publish, exact: true }).click()
    await expect(root.locator('.app-data-table').first()).toContainText(labels.published)

    await expect(more).toBeVisible()
    await more.click()
    await expect(page.locator('[data-testid="unit-conversion-open"]:visible')).toBeVisible()
    await page.locator('[data-testid="unit-conversion-open"]:visible').click()
    const conversion = page
      .getByRole('dialog')
      .filter({ has: page.getByTestId('unit-conversion-submit') })
    await conversion.getByText(labels.current, { exact: true }).click()
    await expect.poll(() => currentRead).toBe(true)
    await conversion.getByText(labels.fixed, { exact: true }).click()
    await conversion.getByTestId('unit-conversion-value').fill('0')
    await conversion.getByTestId('unit-conversion-submit').click()
    await expect(conversion.getByTestId('unit-conversion-result').locator('strong')).toHaveText('0')
    expect(conversionRead).toBe(true)
    await expect(conversion.getByTestId('unit-runtime-source')).toContainText(
      scenario.locale === 'zh-CN' ? '租户' : 'Tenant',
    )
    await expect(conversion.getByTestId('unit-runtime-revision')).toContainText('1')
    expect(fixedHistoryRead).toBe(true)

    await page.screenshot({
      path: testInfo.outputPath(`units-of-measure-${scenario.width}.png`),
      fullPage: true,
      animations: 'disabled',
    })
    expect(
      await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1),
    ).toBe(true)
    expect(failures).toEqual([])
  })
}
