import { expect, test } from '@playwright/test'
import type { DictionaryDetail } from '../../../src/api/referenceData/types'

for (const scenario of [
  { width: 1366, height: 768, locale: 'zh-CN' },
  { width: 1920, height: 1080, locale: 'en-US' },
] as const) {
  test(`dictionary browser fixture ${scenario.width} ${scenario.locale}`, async ({
    page,
  }, testInfo) => {
    const failures: string[] = []
    page.on('pageerror', (error) => failures.push(error.message))
    await page.setViewportSize(scenario)
    const permissions = [
      'platform.home.view',
      ...['view', 'create', 'update', 'publish', 'disable'].map(
        (action) => `referencedata.dictionary.${action}`,
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
    let dictionary: DictionaryDetail = {
      id: '59314190-c8c5-418c-9378-6236952ee829',
      nId: 'ORDER_STATUS',
      name: scenario.locale === 'zh-CN' ? '工单状态' : 'Order status',
      description: null,
      scopeType: 'Tenant',
      tenantNId: user.tenantNId,
      revision: 1,
      status: 'Draft',
      optimisticVersion: 1,
      concurrencyVersion: '3b750b90-5384-4fef-8d29-ae8d2d4e8551',
      lastUpdatedOn: '2026-09-05T00:00:00Z',
      publishedBy: null,
      publishedOn: null,
      isFrozen: false,
      isLocked: false,
      items: [{ nId: 'OPEN', name: 'Open', description: null, sort: 0, enabled: true }],
    }
    const requests: string[] = []
    await page.route('**/referencedata/api/v1/reference-data/**', async (route) => {
      const request = route.request()
      const url = new URL(request.url())
      requests.push(`${request.method()} ${url.pathname}${url.search}`)
      if (url.pathname.endsWith('/publication-check'))
        return route.fulfill({
          json: envelope({
            previousRevision: null,
            addedItems: ['OPEN'],
            changedItems: [],
            disabledItems: [],
            errors: [],
          }),
        })
      if (url.pathname.endsWith('/publish')) {
        expect(request.postDataJSON()).toMatchObject({
          expectedOptimisticVersion: dictionary.optimisticVersion,
          expectedConcurrencyVersion: dictionary.concurrencyVersion,
        })
        dictionary = {
          ...dictionary,
          status: 'Published',
          optimisticVersion: dictionary.optimisticVersion + 1,
          publishedBy: user.userNId,
          publishedOn: new Date().toISOString(),
        }
        return route.fulfill({ json: envelope(dictionary) })
      }
      if (request.method() === 'PUT') {
        const body = request.postDataJSON()
        dictionary = { ...dictionary, ...body, optimisticVersion: dictionary.optimisticVersion + 1 }
        return route.fulfill({ json: envelope(dictionary) })
      }
      if (url.pathname.endsWith('/admin/dictionaries'))
        return route.fulfill({
          json: envelope({
            items: [
              {
                ...dictionary,
                enabledItemCount: dictionary.items.filter((item) => item.enabled).length,
              },
            ],
            total: 1,
            pageIndex: 1,
            pageSize: 25,
          }),
        })
      return route.fulfill({ json: envelope(dictionary) })
    })
    await page.goto('/pc/system/reference-data/dictionaries')
    const root = page.getByTestId('reference-data-dictionaries')
    await expect(root).toBeVisible()
    await expect(root.locator('.app-data-table').first()).toContainText('ORDER_STATUS')
    const directory = root.locator('.dictionary-directory')
    await expect(directory.locator('.vxe-body--row').first()).toBeVisible()
    await expect(directory.locator('.vxe-cell--radio')).toHaveCount(0)
    await expect(directory.locator('.app-data-table__selection-summary')).toHaveCount(0)
    const directoryWidth = await directory.evaluate(
      (element) => element.getBoundingClientRect().width,
    )
    expect(directoryWidth).toBeGreaterThanOrEqual(240)
    expect(directoryWidth).toBeLessThanOrEqual(300)
    expect(
      await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1),
    ).toBe(true)
    await directory.locator('.vxe-body--row').first().locator('.vxe-body--column').first().click()
    await expect(directory.locator('.vxe-body--row').first()).toHaveAttribute(
      'aria-current',
      'true',
    )
    const edit = scenario.locale === 'zh-CN' ? '编辑' : 'Edit'
    const editButton = root.getByRole('button', { name: edit, exact: true }).first()
    if (await editButton.isVisible()) await editButton.click()
    else {
      await root
        .getByRole('button', { name: scenario.locale === 'zh-CN' ? '更多' : 'More', exact: true })
        .first()
        .click()
      await page.getByRole('menuitem', { name: edit, exact: true }).click()
    }
    const dialog = page.getByRole('dialog').filter({ has: page.getByTestId('dictionary-save') })
    await expect(dialog).toBeVisible()
    await dialog.getByTestId('form-surface-mode-toggle').click()
    await expect(page.locator('.app-form-drawer--modal')).toBeVisible()
    await dialog
      .getByTestId('dictionary-name')
      .fill(scenario.locale === 'zh-CN' ? '工单状态（已更新）' : 'Updated order status')
    await page.screenshot({
      path: testInfo.outputPath(`dictionary-editor-${scenario.width}.png`),
      fullPage: true,
      animations: 'disabled',
    })
    await dialog.getByTestId('dictionary-save').click()
    await expect(dialog).not.toBeVisible()
    await directory.locator('.vxe-body--row').first().locator('.vxe-body--column').first().click()
    await expect(directory.locator('.vxe-body--row').first()).toHaveAttribute(
      'aria-current',
      'true',
    )
    await expect(root.getByTestId('dictionary-item-edit-OPEN')).toBeVisible()
    await root.getByTestId('dictionary-item-edit-OPEN').click()
    const itemDialog = page
      .getByRole('dialog')
      .filter({ has: page.getByTestId('dictionary-item-save') })
    await expect(itemDialog).toBeVisible()
    await expect(itemDialog.getByTestId('dictionary-name')).toHaveCount(0)
    await itemDialog
      .getByTestId('dictionary-item-name')
      .fill(scenario.locale === 'zh-CN' ? '开放（已更新）' : 'Open (updated)')
    await page.screenshot({
      path: testInfo.outputPath(`dictionary-item-editor-${scenario.width}.png`),
      fullPage: true,
      animations: 'disabled',
    })
    await itemDialog.getByTestId('dictionary-item-save').click()
    await expect(itemDialog).not.toBeVisible()
    await root
      .getByRole('button', { name: scenario.locale === 'zh-CN' ? '更多' : 'More', exact: true })
      .first()
      .click()
    await page
      .getByRole('menuitem', {
        name: scenario.locale === 'zh-CN' ? '发布' : 'Publish',
        exact: true,
      })
      .click()
    await expect(page.getByTestId('dictionary-publish-confirm')).toBeVisible()
    await page.getByTestId('dictionary-publish-confirm').click()
    await expect(root.locator('.app-data-table').first()).toContainText(
      scenario.locale === 'zh-CN' ? '已发布' : 'Published',
    )
    await root.locator('.dictionary-query-field input').first().fill('Updated')
    await root.getByTestId('query-panel-submit').click()
    await expect
      .poll(() => requests.some((request) => request.includes('keyword=Updated')))
      .toBe(true)
    await expect(page.locator('.el-message')).toHaveCount(0)
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
      path: testInfo.outputPath(`dictionary-list-${scenario.width}.png`),
      fullPage: true,
      animations: 'disabled',
    })
    expect(failures).toEqual([])
  })
}
