import { expect, test } from '@playwright/test'
import type {
  DynamicConfiguration,
  DynamicField,
  DynamicRecord,
} from '../../../src/api/referenceData/dynamicTypes'

for (const scenario of [
  { width: 1366, height: 768, locale: 'zh-CN' },
  { width: 1920, height: 1080, locale: 'en-US' },
] as const) {
  test(`dynamic property browser fixture ${scenario.width} ${scenario.locale}`, async ({
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
        (action) => `referencedata.dynamic-property.${action}`,
      ),
    ]
    const user = {
      userNId: 'PF03-DYNAMIC-VISUAL',
      loginName: 'pf03.dynamic.visual',
      name: 'Dynamic property operator',
      tenantNId: 'PF03-DYNAMIC-TENANT',
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
      traceId: 'pf03-dynamic-browser-fixture',
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

    const field = (
      input: Omit<DynamicField, 'id' | 'hasHadValue' | 'wasPublished'>,
    ): DynamicField => ({
      id: `field-${input.nId.toLowerCase()}`,
      hasHadValue: false,
      wasPublished: false,
      ...input,
    })
    const now = '2026-09-05T00:00:00Z'
    let definition: DynamicConfiguration | null = null
    let records: DynamicRecord[] = []
    const writes: string[] = []
    let runtimeSchemaRead = false
    let runtimeRecordsRead = false

    function summary() {
      const current = definition!
      return { ...current, fieldCount: current.fields.length }
    }
    function defaults(type: DynamicField['dataType']) {
      const shared = {
        minLength: null,
        maxLength: null,
        minValueJson: null,
        maxValueJson: null,
        minValue: null,
        maxValue: null,
        scale: null,
        pattern: null,
        dictionaryNId: null,
        referenceTarget: null,
        description: null,
      }
      if (type === 'Decimal')
        return {
          ...shared,
          defaultValueJson: '999999999999999999.1234567890',
          defaultValue: 999999999999999999.123456789,
        }
      if (type === 'Boolean') return { ...shared, defaultValueJson: 'false', defaultValue: false }
      return { ...shared, defaultValueJson: '""', defaultValue: '' }
    }

    await page.route('**/referencedata/api/v1/reference-data/**', async (route) => {
      const request = route.request()
      const url = new URL(request.url())
      const method = request.method()
      const path = url.pathname

      if (path.endsWith('/dynamic-properties/configurations/EQUIPMENT_PROFILE/schema')) {
        runtimeSchemaRead = true
        return route.fulfill({
          json: envelope({
            definitionId: definition!.id,
            nId: definition!.nId,
            name: definition!.name,
            fields: definition!.fields,
            sourceScope: 'Tenant',
            sourceTenantNId: user.tenantNId,
            revision: definition!.revision,
            publishedOn: definition!.publishedOn,
          }),
        })
      }
      if (
        path.endsWith('/dynamic-properties/configurations/EQUIPMENT_PROFILE/records') &&
        method === 'GET'
      ) {
        runtimeRecordsRead = true
        expect(url.searchParams.get('revision')).toBe('1')
        expect(url.searchParams.get('sourceScope')).toBe('Tenant')
        expect(url.searchParams.get('sourceTenantNId')).toBe(user.tenantNId)
        return route.fulfill({
          json: envelope({ items: records, total: records.length, pageIndex: 1, pageSize: 20 }),
        })
      }

      const admin = '/referencedata/api/v1/reference-data/admin/dynamic-properties/configurations'
      if (path === admin && method === 'GET') {
        return route.fulfill({
          json: envelope({
            items: definition ? [summary()] : [],
            total: definition ? 1 : 0,
            pageIndex: 1,
            pageSize: 20,
          }),
        })
      }
      if (path === admin && method === 'POST') {
        const raw = request.postData()!
        writes.push(raw)
        const body = JSON.parse(raw)
        expect(body.fields).toEqual([])
        definition = {
          id: 'dynamic-definition-1',
          nId: body.nId,
          name: body.name,
          description: body.description,
          scopeType: body.scopeType,
          tenantNId: user.tenantNId,
          revision: 1,
          status: 'Draft',
          fields: [],
          recordCount: 0,
          valueCount: 0,
          optimisticVersion: 1,
          concurrencyVersion: 'dynamic-token-1',
          lastUpdatedOn: now,
          publishedOn: null,
          publishedBy: null,
          isFrozen: false,
          isLocked: false,
        }
        return route.fulfill({ json: envelope(definition) })
      }
      if (path === `${admin}/dynamic-definition-1` && method === 'GET')
        return route.fulfill({ json: envelope(definition) })
      if (path === `${admin}/dynamic-definition-1` && method === 'PUT') {
        const raw = request.postData()!
        writes.push(raw)
        const body = JSON.parse(raw)
        expect(body.expectedOptimisticVersion).toBe(definition!.optimisticVersion)
        definition = {
          ...definition!,
          name: body.name,
          description: body.description,
          fields: body.fields.map(
            (item: {
              nId: string
              name: string
              dataType: DynamicField['dataType']
              required: boolean
              enabled: boolean
              sort: number
            }) =>
              field({
                ...item,
                ...defaults(item.dataType),
              }),
          ),
          optimisticVersion: definition!.optimisticVersion + 1,
          concurrencyVersion: `dynamic-token-${definition!.optimisticVersion + 1}`,
          lastUpdatedOn: now,
        }
        return route.fulfill({ json: envelope(definition) })
      }
      if (path === `${admin}/dynamic-definition-1/records` && method === 'GET') {
        return route.fulfill({
          json: envelope({ items: records, total: records.length, pageIndex: 1, pageSize: 20 }),
        })
      }
      if (path === `${admin}/dynamic-definition-1/records` && method === 'POST') {
        const raw = request.postData()!
        writes.push(raw)
        const body = JSON.parse(raw)
        expect(body.expectedOptimisticVersion).toBe(definition!.optimisticVersion)
        const record: DynamicRecord = {
          id: 'dynamic-record-1',
          nId: body.nId,
          name: body.name,
          category: body.category,
          sort: body.sort,
          enabled: body.enabled,
          values: body.values,
          valuesJson: {
            AMOUNT: '999999999999999999.1234567890',
            ENABLED: 'false',
            EMPTY_TEXT: '""',
          },
          revision: 1,
          isFrozen: false,
          isLocked: false,
        }
        records = [record]
        definition = {
          ...definition!,
          recordCount: 1,
          valueCount: 3,
          optimisticVersion: definition!.optimisticVersion + 1,
          concurrencyVersion: `dynamic-token-${definition!.optimisticVersion + 1}`,
        }
        return route.fulfill({
          json: envelope({
            record,
            optimisticVersion: definition.optimisticVersion,
            concurrencyVersion: definition.concurrencyVersion,
          }),
        })
      }
      if (path === `${admin}/dynamic-definition-1/publication-check` && method === 'GET') {
        return route.fulfill({
          json: envelope({
            previousRevision: null,
            addedFields: ['AMOUNT', 'ENABLED', 'EMPTY_TEXT'],
            changedFields: [],
            disabledFields: [],
            recordCount: 1,
            valueCount: 3,
            errors: [],
          }),
        })
      }
      if (path === `${admin}/dynamic-definition-1/publish` && method === 'POST') {
        const body = request.postDataJSON()
        expect(body.expectedOptimisticVersion).toBe(definition!.optimisticVersion)
        definition = {
          ...definition!,
          status: 'Published',
          optimisticVersion: definition!.optimisticVersion + 1,
          concurrencyVersion: `dynamic-token-${definition!.optimisticVersion + 1}`,
          publishedOn: now,
          publishedBy: user.userNId,
          fields: definition!.fields.map((item) => ({ ...item, wasPublished: true })),
        }
        records = records.map((item) => ({ ...item, isFrozen: true }))
        return route.fulfill({ json: envelope(definition) })
      }
      return route.fulfill({ status: 404, json: envelope(null) })
    })

    const labels =
      scenario.locale === 'zh-CN'
        ? {
            create: '新建动态配置',
            nId: '业务标识',
            name: '名称',
            save: '保存',
            addField: '新增字段',
            dataType: '数据类型',
            defaultValue: '默认值',
            unset: '未配置',
            decimal: '十进制数',
            boolean: '布尔值',
            string: '字符串',
            records: '配置记录',
            addRecord: '新增记录',
            publishCheck: '发布校验',
            publish: '发布',
            falseValue: '否（false）',
            noFields: '此定义还没有字段。',
          }
        : {
            create: 'New dynamic configuration',
            nId: 'Business ID',
            name: 'Name',
            save: 'Save',
            addField: 'Add field',
            dataType: 'Data type',
            defaultValue: 'Default value',
            unset: 'Not configured',
            decimal: 'Decimal',
            boolean: 'Boolean',
            string: 'String',
            records: 'Configuration records',
            addRecord: 'Add record',
            publishCheck: 'Publication check',
            publish: 'Publish',
            falseValue: 'False',
            noFields: 'This definition has no fields.',
          }

    await page.goto('/pc/system/reference-data/dynamic-properties')
    const root = page.getByTestId('reference-data-dynamic-properties')
    const directory = root.locator('.dynamic-property-master')
    await expect(root).toBeVisible()
    await expect(directory.locator('.vxe-cell--radio')).toHaveCount(0)
    await expect(directory.locator('.app-data-table__selection-summary')).toHaveCount(0)
    const directoryWidth = await directory.evaluate(
      (element) => element.getBoundingClientRect().width,
    )
    expect(directoryWidth).toBeGreaterThanOrEqual(240)
    expect(directoryWidth).toBeLessThanOrEqual(300)
    await page.getByTestId('dynamic-property-create').click()
    let dialog = page
      .getByRole('dialog')
      .filter({ has: page.getByTestId('dynamic-definition-save') })
    await dialog.getByTestId('dynamic-definition-nid').fill('EQUIPMENT_PROFILE')
    await dialog.getByTestId('dynamic-definition-name').fill('Equipment profile')
    await dialog.getByTestId('dynamic-definition-save').click()
    await expect(page.getByTestId('dynamic-definition-save')).toHaveCount(0)
    const fieldSurface = root
      .locator('.dynamic-property-detail .el-tabs__content .app-data-table__surface')
      .first()
    await expect(fieldSurface).toBeVisible()
    await expect(root.getByText(labels.noFields, { exact: true })).toBeVisible()
    expect(
      await fieldSurface.evaluate((element) => element.getBoundingClientRect().height),
    ).toBeGreaterThan(0)

    async function addField(
      nId: string,
      name: string,
      typeLabel: string,
      configure: (drawer: ReturnType<typeof page.getByRole>) => Promise<void>,
    ) {
      await page.getByRole('button', { name: labels.addField, exact: true }).click()
      const drawer = page
        .getByRole('dialog')
        .filter({ has: page.getByTestId('dynamic-field-save') })
      await drawer.getByRole('textbox', { name: labels.nId }).fill(nId)
      await drawer.getByTestId('dynamic-field-name').fill(name)
      await drawer
        .getByRole('combobox', { name: labels.dataType, exact: true })
        .click({ force: true })
      await page.getByRole('option', { name: typeLabel, exact: true }).click()
      await configure(drawer)
      await drawer.getByTestId('dynamic-field-save').click()
      await expect(page.getByTestId('dynamic-field-save')).toHaveCount(0)
    }

    await addField('AMOUNT', 'Precision amount', labels.decimal, async (drawer) => {
      await drawer.getByText(labels.unset, { exact: true }).click()
      await drawer
        .getByRole('textbox', { name: labels.defaultValue, exact: true })
        .fill('999999999999999999.1234567890')
    })
    await addField('ENABLED', 'Enabled flag', labels.boolean, async (drawer) => {
      await drawer.getByText(labels.unset, { exact: true }).click()
      await drawer
        .getByRole('combobox', { name: labels.defaultValue, exact: true })
        .click({ force: true })
      await page.getByRole('option', { name: labels.falseValue, exact: true }).click()
    })
    await addField('EMPTY_TEXT', 'Empty text', labels.string, async (drawer) => {
      await drawer.getByText(labels.unset, { exact: true }).click()
    })
    expect(
      await fieldSurface.evaluate((element) => element.getBoundingClientRect().height),
    ).toBeGreaterThan(100)
    await expect(fieldSurface.locator('.vxe-body--row').first()).toBeVisible()

    expect(writes.some((raw) => raw.includes('"defaultValue":999999999999999999.1234567890'))).toBe(
      true,
    )
    expect(writes.some((raw) => raw.includes('"defaultValue":false'))).toBe(true)
    expect(writes.some((raw) => raw.includes('"defaultValue":""'))).toBe(true)

    await page.getByRole('tab', { name: labels.records, exact: true }).click()
    const recordSurface = root
      .locator('.dynamic-property-detail .el-tabs__content .app-data-table__surface')
      .nth(1)
    await expect(recordSurface).toBeVisible()
    expect(
      await recordSurface.evaluate((element) => element.getBoundingClientRect().height),
    ).toBeGreaterThan(0)
    await page.getByRole('button', { name: labels.addRecord, exact: true }).click()
    dialog = page.getByRole('dialog').filter({ has: page.getByTestId('dynamic-record-save') })
    await dialog.getByTestId('dynamic-record-nid').fill('PRESS_01')
    for (const [fieldName, value] of [
      ['Precision amount', '999999999999999999.1234567890'],
      ['Empty text', ''],
    ] as const) {
      const editor = dialog.locator('.el-form-item').filter({ hasText: fieldName })
      await editor.getByText(labels.unset, { exact: true }).click()
      await editor.getByRole('textbox', { name: fieldName, exact: true }).fill(value)
    }
    const booleanEditor = dialog.locator('.el-form-item').filter({ hasText: 'Enabled flag' })
    await booleanEditor.getByText(labels.unset, { exact: true }).click()
    await booleanEditor
      .getByRole('combobox', { name: 'Enabled flag', exact: true })
      .click({ force: true })
    await page.getByRole('option', { name: labels.falseValue, exact: true }).click()
    await dialog.screenshot({
      path: testInfo.outputPath(`dynamic-record-editor-${scenario.width}.png`),
      animations: 'disabled',
    })
    await dialog.getByTestId('dynamic-record-save').click()
    await expect(page.getByTestId('dynamic-record-save')).toHaveCount(0)
    expect(
      await recordSurface.evaluate((element) => element.getBoundingClientRect().height),
    ).toBeGreaterThan(0)
    await expect(recordSurface.locator('.vxe-body--row').first()).toBeVisible()

    const recordWrite = writes.find((raw) => raw.includes('"nId":"PRESS_01"'))!
    expect(recordWrite).toContain('"AMOUNT":999999999999999999.1234567890')
    expect(recordWrite).toContain('"ENABLED":false')
    expect(recordWrite).toContain('"EMPTY_TEXT":""')

    await page.getByRole('button', { name: labels.publishCheck, exact: true }).click()
    dialog = page.getByRole('dialog').filter({ has: page.getByTestId('dynamic-publish-confirm') })
    await expect(dialog.getByText('AMOUNT', { exact: false })).toBeVisible()
    await dialog.getByTestId('dynamic-publish-confirm').click()
    await expect(page.getByTestId('dynamic-publish-confirm')).toHaveCount(0)

    const runtime = await page.evaluate(
      async ({ tenantNId }) => {
        const schemaResponse = await fetch(
          '/referencedata/api/v1/reference-data/dynamic-properties/configurations/EQUIPMENT_PROFILE/schema',
        )
        const schema = (await schemaResponse.json()).data
        const params = new URLSearchParams({
          pageIndex: '1',
          pageSize: '20',
          revision: String(schema.revision),
          sourceScope: schema.sourceScope,
          sourceTenantNId: tenantNId,
        })
        const recordsResponse = await fetch(
          `/referencedata/api/v1/reference-data/dynamic-properties/configurations/EQUIPMENT_PROFILE/records?${params}`,
        )
        return { schema, records: (await recordsResponse.json()).data }
      },
      { tenantNId: user.tenantNId },
    )
    expect(runtime.schema.definitionId).toBe('dynamic-definition-1')
    expect(runtime.schema.revision).toBe(1)
    expect(runtime.records.items[0].valuesJson).toEqual({
      AMOUNT: '999999999999999999.1234567890',
      ENABLED: 'false',
      EMPTY_TEXT: '""',
    })
    expect(runtimeSchemaRead).toBe(true)
    expect(runtimeRecordsRead).toBe(true)

    const pcMain = page.locator('.ip-pc-main')
    const mainMetrics = await pcMain.evaluate((element) => ({
      scrollLeft: element.scrollLeft,
      scrollWidth: element.scrollWidth,
      clientWidth: element.clientWidth,
    }))
    const metricsMessage = JSON.stringify(mainMetrics)
    expect(mainMetrics, metricsMessage).toMatchObject({ scrollLeft: 0 })
    expect(mainMetrics.scrollWidth, metricsMessage).toBeLessThanOrEqual(mainMetrics.clientWidth + 1)
    await page.screenshot({
      path: testInfo.outputPath(`dynamic-properties-${scenario.width}.png`),
      fullPage: true,
      animations: 'disabled',
    })
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
    expect(failures).toEqual([])
  })
}
