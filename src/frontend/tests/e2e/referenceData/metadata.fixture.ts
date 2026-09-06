import { expect, test } from '@playwright/test'

type AttributeRequest = {
  nId: string
  name: string
  dataType: string
  required: boolean
  isArray: boolean
  enabled: boolean
  sort: number
  defaultValue: string | null
  minLength: number | null
  maxLength: number | null
  minValue: number | null
  maxValue: number | null
  pattern: string | null
  dictionaryNId: string | null
  referenceTarget: string | null
  precision: number | null
  scale: number | null
  unitDimensionNId: string | null
  defaultUnitNId: string | null
  unitRevision: number | null
  unitSourceScope: string | null
  unitSourceTenantNId: string | null
  description: string | null
}

type Attribute = AttributeRequest & {
  id: string
  wasPublished: boolean
  isFrozen: boolean
  isLocked: boolean
}

type Schema = {
  id: string
  nId: string
  name: string
  description: string | null
  scopeType: string
  tenantNId: string | null
  revision: number
  status: string
  sourceRevision: number | null
  attributes: Attribute[]
  optimisticVersion: number
  concurrencyVersion: string
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
  test(`metadata definition publish and fixed history ${scenario.width} ${scenario.locale}`, async ({
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
        (action) => `referencedata.metadata.${action}`,
      ),
      'referencedata.unit-of-measure.view',
      'referencedata.platform.manage',
    ]
    const user = {
      userNId: 'PF03-METADATA-VISUAL',
      loginName: 'pf03.metadata.visual',
      name: 'Metadata operator',
      tenantNId: 'PF03-METADATA-TENANT',
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
      traceId: 'pf03-metadata-browser-fixture',
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
    const admin = '/referencedata/api/v1/reference-data/admin/metadata-schemas'
    const runtime = '/referencedata/api/v1/reference-data/metadata-schemas'
    const unitRuntime = '/referencedata/api/v1/reference-data/units-of-measure'
    let schema: Schema | null = null
    const writes: string[] = []
    let fixedHistoryRead = false
    let currentRead = false

    function summary() {
      const current = schema!
      return { ...current, attributes: undefined, attributeCount: current.attributes.length }
    }

    function runtimeSchema(includeDisabled: boolean) {
      const current = schema!
      const runtimeAttributes = current.attributes
        .filter((attribute) => includeDisabled || attribute.enabled)
        .map((attribute) => {
          const runtimeAttribute = { ...attribute } as Partial<Attribute>
          delete runtimeAttribute.id
          delete runtimeAttribute.wasPublished
          delete runtimeAttribute.isFrozen
          delete runtimeAttribute.isLocked
          return runtimeAttribute as AttributeRequest
        })
      if (includeDisabled)
        runtimeAttributes.push({
          nId: 'LEGACY',
          name: 'Legacy attribute',
          dataType: 'String',
          required: false,
          isArray: false,
          enabled: false,
          sort: 99,
          defaultValue: '',
          minLength: null,
          maxLength: null,
          minValue: null,
          maxValue: null,
          pattern: null,
          dictionaryNId: null,
          referenceTarget: null,
          precision: null,
          scale: null,
          unitDimensionNId: null,
          defaultUnitNId: null,
          unitRevision: null,
          unitSourceScope: null,
          unitSourceTenantNId: null,
          description: null,
        })
      return {
        nId: current.nId,
        name: current.name,
        description: current.description,
        attributes: runtimeAttributes,
        sourceScope: current.scopeType,
        sourceTenantNId: current.tenantNId,
        revision: current.revision,
        publishedOn: current.publishedOn,
      }
    }

    await page.route('**/referencedata/api/v1/reference-data/**', async (route) => {
      const request = route.request()
      const url = new URL(request.url())
      const method = request.method()
      const path = url.pathname

      if (path === `${unitRuntime}/dimensions` && method === 'GET') {
        return route.fulfill({
          json: envelope({
            items: [
              {
                nId: 'MASS',
                name: 'Mass',
                sourceScope: 'Tenant',
                sourceTenantNId: user.tenantNId,
                revision: 4,
                publishedOn: now,
                isSystemDefined: false,
                conversionKind: 'Ratio',
                baseUnitNId: 'KG',
                unitCount: 2,
              },
            ],
            total: 1,
            pageIndex: 1,
            pageSize: 100,
          }),
        })
      }

      if (path === `${unitRuntime}/dimensions/MASS/revisions/4` && method === 'GET') {
        return route.fulfill({
          json: envelope({
            nId: 'MASS',
            name: 'Mass',
            description: null,
            sourceScope: 'Tenant',
            sourceTenantNId: user.tenantNId,
            revision: 4,
            status: 'Published',
            sourceRevision: null,
            publishedOn: now,
            isSystemDefined: false,
            conversionKind: 'Ratio',
            baseUnitNId: 'KG',
            units: [
              {
                nId: 'KG',
                name: 'Kilogram',
                symbol: 'kg',
                factorToBase: '1',
                offsetToBase: '0',
                decimalPlaces: 3,
                roundingMode: 'ToEven',
                enabled: true,
                sort: 0,
              },
            ],
          }),
        })
      }

      if (path === admin && method === 'GET') {
        return route.fulfill({
          json: envelope({
            items: schema ? [summary()] : [],
            total: schema ? 1 : 0,
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
          attributes: AttributeRequest[]
        }
        expect(body.scopeType).toBe('Tenant')
        expect(body.attributes).toHaveLength(1)
        expect(body.attributes[0]).toMatchObject({
          nId: 'WEIGHT',
          dataType: 'Decimal',
          defaultValue: '0.000000000001',
          precision: 28,
          scale: 12,
          unitDimensionNId: 'MASS',
          defaultUnitNId: 'KG',
          unitRevision: 4,
          unitSourceScope: 'Tenant',
          unitSourceTenantNId: user.tenantNId,
          dictionaryNId: null,
          referenceTarget: null,
        })
        schema = {
          id: '17660ae5-afec-4c90-8e09-f41d2ab26493',
          nId: body.nId.toUpperCase(),
          name: body.name,
          description: body.description,
          scopeType: body.scopeType,
          tenantNId: user.tenantNId,
          revision: 1,
          status: 'Draft',
          sourceRevision: null,
          attributes: body.attributes.map((attribute, index) => ({
            ...attribute,
            id: `5c25b32a-ff14-42bb-b7ac-0fed04ea3c0${index}`,
            nId: attribute.nId.toUpperCase(),
            wasPublished: false,
            isFrozen: false,
            isLocked: false,
          })),
          optimisticVersion: 1,
          concurrencyVersion: '67cfc43d-fbb7-4a5d-a693-c8b396df1606',
          lastUpdatedOn: now,
          publishedOn: null,
          publishedBy: null,
          isFrozen: false,
          isLocked: false,
        }
        return route.fulfill({ status: 201, json: envelope(schema) })
      }

      if (schema && path === `${admin}/${schema.id}` && method === 'GET')
        return route.fulfill({ json: envelope(schema) })

      if (schema && path === `${admin}/${schema.id}/publication-check` && method === 'GET') {
        return route.fulfill({
          json: envelope({
            previousRevision: null,
            addedAttributes: ['WEIGHT'],
            tightenedAttributes: [],
            relaxedAttributes: [],
            disabledAttributes: [],
            incompatibleChanges: [],
            errors: [],
          }),
        })
      }

      if (schema && path === `${admin}/${schema.id}/publish` && method === 'POST') {
        const body = request.postDataJSON()
        expect(body.expectedOptimisticVersion).toBe(schema.optimisticVersion)
        expect(body.expectedConcurrencyVersion).toBe(schema.concurrencyVersion)
        schema = {
          ...schema,
          status: 'Published',
          publishedOn: now,
          publishedBy: user.userNId,
          optimisticVersion: schema.optimisticVersion + 1,
          concurrencyVersion: '3fccf063-feef-4346-8eea-d579d0fec35f',
          attributes: schema.attributes.map((attribute) => ({
            ...attribute,
            wasPublished: true,
          })),
        }
        return route.fulfill({ json: envelope(schema) })
      }

      const revision = path.match(/\/metadata-schemas\/([^/]+)\/revisions\/(\d+)$/)
      if (schema && revision && method === 'GET') {
        fixedHistoryRead = true
        expect(decodeURIComponent(revision[1]!)).toBe('EQUIPMENT_FIXTURE')
        expect(revision[2]).toBe('1')
        expect(url.searchParams.get('sourceScope')).toBe('Tenant')
        expect(url.searchParams.get('sourceTenantNId')).toBe(user.tenantNId)
        return route.fulfill({ json: envelope(runtimeSchema(true)) })
      }

      if (schema && path === `${runtime}/EQUIPMENT_FIXTURE` && method === 'GET') {
        currentRead = true
        return route.fulfill({ json: envelope(runtimeSchema(false)) })
      }

      return route.fulfill({ status: 404, json: envelope(null) })
    })

    const labels =
      scenario.locale === 'zh-CN'
        ? {
            decimal: '十进制数',
            tenant: '租户',
            publish: '发布',
            published: '已发布',
            current: '当前有效定义',
          }
        : {
            decimal: 'Decimal',
            tenant: 'Tenant',
            publish: 'Publish',
            published: 'Published',
            current: 'Current effective definition',
          }

    await page.goto('/pc/system/reference-data/metadata')
    const root = page.getByTestId('reference-data-metadata')
    const more = root.locator('[data-testid="metadata-more"]:visible')
    await expect(root).toBeVisible()
    const directory = root.locator('.metadata-master')
    await expect(directory.locator('.vxe-cell--radio')).toHaveCount(0)
    await expect(directory.locator('.app-data-table__selection-summary')).toHaveCount(0)
    const directoryWidth = await directory.evaluate(
      (element) => element.getBoundingClientRect().width,
    )
    expect(directoryWidth).toBeGreaterThanOrEqual(240)
    expect(directoryWidth).toBeLessThanOrEqual(300)
    await root.getByTestId('metadata-schema-create').click()
    const editor = page
      .getByRole('dialog')
      .filter({ has: page.getByTestId('metadata-schema-save') })
    await editor.getByTestId('metadata-schema-nid').fill('EQUIPMENT_FIXTURE')
    await editor.getByTestId('metadata-schema-name').fill('Equipment fixture')
    await editor.getByTestId('metadata-attribute-nid-0').fill('WEIGHT')
    await editor.getByTestId('metadata-attribute-name-0').fill('Weight')
    await editor.getByTestId('metadata-attribute-type-0').click({ force: true })
    await page.getByRole('option', { name: labels.decimal, exact: true }).click()
    await editor.getByTestId('metadata-attribute-precision-0').locator('input').fill('28')
    await editor.getByTestId('metadata-attribute-scale-0').locator('input').fill('12')
    await editor.getByTestId('metadata-attribute-unit-dimension-0').click()
    await page.getByRole('option', { name: /MASS/ }).click()
    await expect(
      editor.getByTestId('metadata-attribute-unit-revision-0').locator('input'),
    ).toHaveValue('4')
    await expect(editor.getByTestId('metadata-attribute-unit-source-0')).toContainText(
      labels.tenant,
    )
    await expect(editor.getByTestId('metadata-attribute-unit-source-tenant-0')).toHaveValue(
      user.tenantNId,
    )
    await editor.getByTestId('metadata-attribute-default-unit-0').click()
    await page.getByRole('option', { name: /Kilogram \(KG/ }).click()
    await editor.getByTestId('metadata-attribute-default-configured-0').click()
    await editor.getByTestId('metadata-attribute-default-0').fill('0.000000000001')
    await editor.screenshot({
      path: testInfo.outputPath(`metadata-editor-${scenario.width}.png`),
      animations: 'disabled',
    })
    await editor.getByTestId('metadata-schema-save').click()
    await expect(editor).not.toBeVisible()
    expect(writes).toHaveLength(1)
    expect(writes[0]).toContain('"defaultValue":"0.000000000001"')

    await directory.locator('.vxe-body--row').first().locator('.vxe-body--column').first().click()
    await expect(directory.locator('.vxe-body--row').first()).toHaveAttribute(
      'aria-current',
      'true',
    )
    const attributeSurface = root.locator(
      '.metadata-detail-panel .el-tabs__content .app-data-table__surface',
    )
    await expect(attributeSurface).toBeVisible()
    expect(
      await attributeSurface.evaluate((element) => element.getBoundingClientRect().height),
    ).toBeGreaterThan(100)
    await expect(more).toBeVisible()
    await more.click()
    await expect(page.locator('[data-testid="metadata-publication-open"]:visible')).toBeVisible()
    await page.locator('[data-testid="metadata-publication-open"]:visible').click()
    const publication = page
      .getByRole('dialog')
      .filter({ has: page.getByTestId('metadata-publish') })
    await expect(publication.getByTestId('metadata-publish-confirm')).toContainText('WEIGHT')
    await publication.getByTestId('metadata-publish').click()
    await expect(root.locator('.app-data-table').first()).toContainText(labels.published)

    await expect(more).toBeVisible()
    await more.click()
    await expect(page.locator('[data-testid="metadata-runtime-open"]:visible')).toBeVisible()
    await page.locator('[data-testid="metadata-runtime-open"]:visible').click()
    const runtimeViewer = page
      .getByRole('dialog')
      .filter({ has: page.getByTestId('metadata-runtime-mode') })
    await expect(runtimeViewer).toContainText('LEGACY')
    await expect(runtimeViewer.getByTestId('metadata-runtime-source')).toContainText(labels.tenant)
    await expect(runtimeViewer.getByTestId('metadata-runtime-revision')).toContainText('1')
    expect(fixedHistoryRead).toBe(true)
    await runtimeViewer.getByTestId('metadata-runtime-mode').click({ force: true })
    await page.getByRole('option', { name: labels.current, exact: true }).click()
    await expect.poll(() => currentRead).toBe(true)
    await expect(runtimeViewer).not.toContainText('LEGACY')

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
      path: testInfo.outputPath(`metadata-${scenario.width}.png`),
      fullPage: true,
      animations: 'disabled',
    })
    expect(
      await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1),
    ).toBe(true)
    expect(failures).toEqual([])
  })
}
