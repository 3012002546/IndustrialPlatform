import { expect, test, type Locator, type Page, type TestInfo } from '@playwright/test'

type FixtureState = 'normal' | 'empty' | 'loading' | 'error' | 'disabled'
type NavigationScenario =
  'normal' | 'no-add' | 'add-skipped' | 'mixed' | 'blocked' | 'conflict' | 'validation'
type SystemDataKind =
  | 'organizations'
  | 'assignments'
  | 'navigation'
  | 'features'
  | 'services'
  | 'themes'
  | 'service-initialization'

const pages: readonly { kind: SystemDataKind; path: string; title: string }[] = [
  { kind: 'organizations', path: '/pc/systemdata/organizations', title: '行政组织与岗位' },
  { kind: 'assignments', path: '/pc/systemdata/assignments', title: '用户任职' },
  { kind: 'navigation', path: '/pc/systemdata/navigation', title: '菜单管理' },
  { kind: 'features', path: '/pc/systemdata/features', title: '功能开关' },
  { kind: 'services', path: '/pc/systemdata/services', title: '服务目录' },
  { kind: 'themes', path: '/pc/systemdata/themes', title: '租户主题策略' },
  {
    kind: 'service-initialization',
    path: '/pc/systemdata/service-initialization',
    title: '服务初始化编排',
  },
]

function fixtureUrl(
  path: string,
  state: FixtureState = 'normal',
  options: {
    locale?: 'zh-CN' | 'en-US'
    palette?: string
    mode?: string
    density?: string
    navigationScenario?: NavigationScenario
  } = {},
): string {
  const params = new URLSearchParams({ path, state })
  if (options.locale !== undefined) params.set('locale', options.locale)
  if (options.palette !== undefined) params.set('palette', options.palette)
  if (options.mode !== undefined) params.set('mode', options.mode)
  if (options.density !== undefined) params.set('density', options.density)
  if (options.navigationScenario !== undefined)
    params.set('navigationScenario', options.navigationScenario)
  return `/__systemdata_fixture__?${params.toString()}`
}

async function openFixture(
  page: Page,
  path: string,
  state: FixtureState = 'normal',
  options: {
    locale?: 'zh-CN' | 'en-US'
    palette?: string
    mode?: string
    density?: string
    navigationScenario?: NavigationScenario
  } = {},
): Promise<void> {
  await page.goto(fixtureUrl(path, state, options))
  await expect(page).toHaveURL(new RegExp(`${path.replaceAll('/', '\\/')}$`))
}

async function assertFrame(page: Page, title: string): Promise<void> {
  await expect(page.getByTestId('systemdata-admin-page')).toBeVisible()
  await expect(page.getByRole('heading', { level: 1, name: title, exact: true })).toBeVisible()
  await expect(page.locator('.systemdata-admin-frame')).toBeVisible()
  await expect(page.getByTestId('systemdata-record-count')).toBeVisible()
  await assertActionIcon(page.getByTestId('systemdata-refresh'))
}

async function assertActionIcon(button: Locator): Promise<void> {
  const icon = button.locator('.systemdata-page-action-icon')
  await expect(icon).toHaveCount(1)
  await expect(icon.locator('svg')).toHaveCSS('width', '14px')
  await expect(icon.locator('svg')).toHaveCSS('height', '14px')
}

async function assertViewport(page: Page, width: number, height: number): Promise<void> {
  await expect
    .poll(async () =>
      page.evaluate(() => ({ width: window.innerWidth, height: window.innerHeight })),
    )
    .toEqual({ width, height })
}

function cssRgb(value: string): [number, number, number] {
  const match = value.match(/rgba?\((\d+),\s*(\d+),\s*(\d+)/)
  if (match !== null) return [Number(match[1]), Number(match[2]), Number(match[3])]
  const srgbMatch = value.match(/color\(srgb\s+([\d.]+)\s+([\d.]+)\s+([\d.]+)/)
  if (srgbMatch !== null) {
    return [
      Math.round(Number(srgbMatch[1]) * 255),
      Math.round(Number(srgbMatch[2]) * 255),
      Math.round(Number(srgbMatch[3]) * 255),
    ]
  }
  const oklabMatch = value.match(/oklab\(([-\d.]+)\s+([-\d.]+)\s+([-\d.]+)/)
  if (oklabMatch !== null) {
    const lightness = Number(oklabMatch[1])
    const a = Number(oklabMatch[2])
    const b = Number(oklabMatch[3])
    const l = lightness + 0.3963377774 * a + 0.2158037573 * b
    const m = lightness - 0.1055613458 * a - 0.0638541728 * b
    const s = lightness - 0.0894841775 * a - 1.291485548 * b
    const l3 = l * l * l
    const m3 = m * m * m
    const s3 = s * s * s
    const linearToSrgb = (channel: number): number =>
      channel <= 0.0031308
        ? 12.92 * channel
        : 1.055 * Math.pow(Math.max(channel, 0), 1 / 2.4) - 0.055
    return [
      Math.round(
        Math.max(
          0,
          Math.min(1, linearToSrgb(4.0767416621 * l3 - 3.3077115913 * m3 + 0.2309699292 * s3)),
        ) * 255,
      ),
      Math.round(
        Math.max(
          0,
          Math.min(1, linearToSrgb(-1.2684380046 * l3 + 2.6097574011 * m3 - 0.3413193965 * s3)),
        ) * 255,
      ),
      Math.round(
        Math.max(
          0,
          Math.min(1, linearToSrgb(-0.0041960863 * l3 - 0.7034186147 * m3 + 1.707614701 * s3)),
        ) * 255,
      ),
    ]
  }
  throw new Error(`Expected an RGB color, received ${value}`)
}

function contrastRatio(
  foreground: [number, number, number],
  background: [number, number, number],
): number {
  const luminance = ([r, g, b]: [number, number, number]): number => {
    const channel = (value: number): number => {
      const normalized = value / 255
      return normalized <= 0.03928
        ? normalized / 12.92
        : Math.pow((normalized + 0.055) / 1.055, 2.4)
    }
    return 0.2126 * channel(r) + 0.7152 * channel(g) + 0.0722 * channel(b)
  }
  const lighter = Math.max(luminance(foreground), luminance(background))
  const darker = Math.min(luminance(foreground), luminance(background))
  return (lighter + 0.05) / (darker + 0.05)
}

async function assertNormalPage(page: Page, kind: SystemDataKind): Promise<void> {
  const content = page.locator('.systemdata-admin-content')
  await expect(content).toBeVisible()

  if (kind === 'organizations') {
    const layout = page.locator('.systemdata-organizations-layout')
    await expect(layout).toBeVisible()
    await assertActionIcon(page.getByTestId('systemdata-organizations-new'))
    const masterList = layout.locator('.organization-master-list')
    await expect(masterList).toBeVisible()
    await expect(masterList.getByRole('treeitem').first()).toBeVisible()
    await masterList.getByTestId('organization-card-org-platform').click()
    await expect(layout.locator('.systemdata-organization-context')).toContainText('平台运营中心')
    await expect(layout.getByRole('cell', { name: /平台运营负责人/ })).toBeVisible()
    await expect(layout.locator('.app-tree-table__content .app-data-table')).toBeVisible()
    const newPosition = page.getByTestId('systemdata-positions-new')
    await expect(newPosition).toBeVisible()
    await expect(newPosition).toBeEnabled()
    await newPosition.click()
    await expect(page.getByRole('dialog')).toBeVisible()
    await page.getByTestId('form-drawer-close').click()
    await expect(page.getByRole('dialog')).toHaveCount(0)
    await expect(
      layout
        .locator('.app-tree-table__content')
        .getByRole('button', { name: '编辑', exact: true })
        .first(),
    ).toBeVisible()
    await expect(
      layout
        .locator('.app-tree-table__content')
        .getByRole('button', { name: '停用', exact: true })
        .first(),
    ).toBeVisible()
    return
  }

  if (kind === 'assignments') {
    const query = page.locator('.systemdata-assignment-query')
    await expect(query.locator('.app-query-panel__body--grid')).toBeVisible()
    await query.getByRole('textbox', { name: 'Identity 用户搜索', exact: true }).fill('PF02')
    await query.getByRole('button', { name: '搜索用户', exact: true }).click()
    await expect(query.getByRole('button', { name: /PF02 视觉验收用户/ })).toBeVisible()
    await query.getByRole('button', { name: /PF02 视觉验收用户/ }).click()
    await expect(query).toContainText('已选择：PF02 视觉验收用户')
    await expect(page.locator('.app-data-table')).toBeVisible()
    await expect(page.getByText('平台运营负责人', { exact: true })).toBeVisible()
    await assertActionIcon(page.getByRole('button', { name: '新建任职', exact: true }))
    await expect(page.locator('.app-data-table').getByText('当前', { exact: true })).toBeVisible()
    await expect(page.locator('.app-data-table').getByText('Current', { exact: true })).toHaveCount(
      0,
    )
    return
  }

  if (kind === 'navigation') {
    const table = page.locator('.app-data-table')
    await expect(table).toBeVisible()
    await expect(table.locator('.app-data-table__toolbar-title')).toContainText('草稿树')
    await expect(page.getByTestId('app-data-table-tree-expand-all')).toBeVisible()
    await expect(page.getByTestId('app-data-table-tree-collapse-all')).toBeVisible()
    await expect(page.getByTestId('systemdata-navigation-preview')).toBeVisible()
    await expect(page.getByTestId('systemdata-navigation-new-first-level')).toBeVisible()
    await expect(page.getByTestId('systemdata-navigation-publish')).toBeVisible()
    await page.getByTestId('systemdata-navigation-preview').click()
    await expect(page.getByRole('dialog')).toBeVisible()
    await expect(page.getByTestId('systemdata-navigation-runtime-preview')).toBeVisible()
    await page.getByTestId('systemdata-navigation-preview-close').click()
    await expect(page.getByRole('dialog')).toHaveCount(0)
    return
  }

  if (kind === 'features') {
    const table = page.locator('.app-data-table')
    await expect(table).toBeVisible()
    await expect(table.getByText('feature-systemdata-ui', { exact: true })).toBeVisible()
    await expect(table.getByRole('button', { name: '覆盖', exact: true }).first()).toBeVisible()
    await table.getByRole('button', { name: '覆盖', exact: true }).first().click()
    await expect(page.getByRole('dialog')).toBeVisible()
    await expect(page.getByRole('heading', { level: 2, name: '功能覆盖' })).toBeVisible()
    const impactCheckbox = page.getByRole('dialog').locator('.el-checkbox')
    await expect(impactCheckbox).toBeVisible()
    await expect(page.getByTestId('form-drawer-submit')).toBeEnabled()
    await impactCheckbox.click()
    await expect(page.getByTestId('form-drawer-submit')).toBeEnabled()
    await page.getByTestId('form-drawer-close').click()
    return
  }

  if (kind === 'services') {
    const groups = page.locator('.systemdata-service-group')
    await expect(groups).toHaveCount(2)
    await expect(groups.nth(0).getByRole('heading', { level: 2, name: '平台服务' })).toBeVisible()
    await expect(groups.nth(1).getByRole('heading', { level: 2, name: '外部服务' })).toBeVisible()
    await expect(groups.nth(0).locator('.app-data-table')).toBeVisible()
    await expect(groups.nth(1).locator('.app-data-table')).toBeVisible()
    await expect(groups.getByText('MES 生产执行系统', { exact: true })).toBeVisible()
    await expect(page.getByRole('button', { name: '新建外部服务', exact: true })).toBeVisible()
    await assertActionIcon(page.getByRole('button', { name: '新建外部服务', exact: true }))
    return
  }

  if (kind === 'themes') {
    const editor = page.locator('.systemdata-theme-editor')
    await expect(editor).toBeVisible()
    await expect(editor.getByRole('heading', { level: 2, name: '允许配色' })).toBeVisible()
    await expect(editor.getByRole('checkbox', { name: '工业青', exact: true })).toBeChecked()
    await expect(editor.getByRole('checkbox', { name: '科技蓝', exact: true })).toBeChecked()
    await expect(editor.getByText('industrial-cyan', { exact: true })).toHaveCount(0)
    await expect(editor.getByText('technology-blue', { exact: true })).toHaveCount(0)
    await expect(
      page.getByRole('button', { name: '保存策略并重新获取', exact: true }),
    ).toBeEnabled()
    return
  }

  const panel = page.locator('#systemdata-init-panel-registrations')
  await expect(panel).toBeVisible()
  await expect(panel.getByText('identity-core', { exact: true })).toBeVisible()
  const registerButton = panel.getByRole('button', { name: '注册 / 重注册', exact: true })
  await expect(registerButton).toBeVisible()
  await assertActionIcon(registerButton)
  expect(
    await registerButton.evaluate((element) => element.getBoundingClientRect().width),
  ).toBeLessThan(320)
  await panel.locator('.vxe-body--row .vxe-radio--icon').first().click()

  await page.locator('#systemdata-init-tab-seedsets').click()
  await expect(page.locator('#systemdata-init-panel-seedsets')).toBeVisible()
  await expect(page.locator('.systemdata-init-seed')).toContainText('identity-permissions')

  await page.locator('#systemdata-init-tab-plans').click()
  await expect(page.locator('#systemdata-init-panel-plans')).toBeVisible()
  const createPlanButton = page.getByRole('button', { name: '生成计划', exact: true })
  await assertActionIcon(createPlanButton)
  expect(
    await createPlanButton.evaluate((element) => element.getBoundingClientRect().width),
  ).toBeLessThan(320)
  await expect(page.locator('.systemdata-init-plan-index')).toContainText('plan-visual-001')
  await page.locator('.systemdata-init-plan-index button').first().click()
  await expect(page.locator('.systemdata-init-plan-detail')).toContainText('identity-core')

  await page.locator('#systemdata-init-tab-operations').click()
  await expect(page.locator('#systemdata-init-panel-operations')).toBeVisible()
  await expect(page.locator('#systemdata-init-panel-operations')).toContainText(
    'operation-visual-001',
  )
  await page
    .locator('#systemdata-init-panel-operations .vxe-body--row .vxe-radio--icon')
    .first()
    .click()
  await expect(page.locator('.systemdata-init-operation-detail')).toContainText('trace-visual-001')

  await page.locator('#systemdata-init-tab-environment').click()
  await expect(page.locator('#systemdata-init-panel-environment')).toBeVisible()
  await expect(page.locator('#systemdata-init-panel-environment')).toContainText('development')
  await page.locator('#systemdata-init-tab-registrations').click()
  await expect(page.locator('#systemdata-init-panel-registrations')).toBeVisible()
}

async function assertNoDocumentOverflow(page: Page): Promise<void> {
  const geometry = await page.evaluate(() => ({
    viewportWidth: window.innerWidth,
    documentScrollWidth: document.documentElement.scrollWidth,
    bodyScrollWidth: document.body.scrollWidth,
  }))
  expect(geometry.documentScrollWidth).toBeLessThanOrEqual(geometry.viewportWidth + 1)
  expect(geometry.bodyScrollWidth).toBeLessThanOrEqual(geometry.viewportWidth + 1)
}

async function assertCanReachBottom(page: Page, target: Locator): Promise<void> {
  const scrollRegion = page.locator('.systemdata-admin-page')
  await expect(scrollRegion).toHaveAttribute('tabindex', '0')
  const initial = await scrollRegion.evaluate((element) => ({
    clientHeight: element.clientHeight,
    scrollHeight: element.scrollHeight,
    scrollTop: element.scrollTop,
  }))
  expect(initial.scrollHeight).toBeGreaterThan(initial.clientHeight + 1)

  const scrollBox = await scrollRegion.boundingBox()
  if (scrollBox === null) throw new Error('Expected SystemData scroll region')
  await page.mouse.move(scrollBox.x + scrollBox.width - 24, scrollBox.y + scrollBox.height - 24)
  await page.mouse.wheel(0, initial.scrollHeight * 2)
  await expect
    .poll(async () => scrollRegion.evaluate((element) => element.scrollTop))
    .toBeGreaterThan(0)

  await scrollRegion.focus()
  await page.keyboard.press('End')
  await expect
    .poll(async () =>
      scrollRegion.evaluate(
        (element) => element.scrollTop + element.clientHeight >= element.scrollHeight - 1,
      ),
    )
    .toBe(true)
  await expect(target).toBeInViewport()
}

async function screenshot(page: Page, testInfo: TestInfo, name: string): Promise<void> {
  await page.screenshot({ path: testInfo.outputPath(`${name}.png`), fullPage: true })
}

async function viewportScreenshot(page: Page, testInfo: TestInfo, name: string): Promise<void> {
  await page.screenshot({ path: testInfo.outputPath(`${name}.png`) })
}

async function effectiveBackground(locator: Locator): Promise<string> {
  return locator.evaluate((element) => {
    let current: HTMLElement | null = element as HTMLElement
    while (current !== null) {
      const background = getComputedStyle(current).backgroundColor
      if (background !== 'rgba(0, 0, 0, 0)') return background
      current = current.parentElement
    }
    return 'rgba(0, 0, 0, 0)'
  })
}

test('七个 SystemData 管理页在本地 Mock 正常数据下逐页渲染', async ({ page }, testInfo) => {
  for (const item of pages) {
    await openFixture(page, item.path)
    await assertViewport(page, 2048, 1090)
    await assertFrame(page, item.title)
    await expect(page.locator('.systemdata-page-actions .el-button').first()).toBeVisible()
    await assertNormalPage(page, item.kind)
    await assertNoDocumentOverflow(page)
    await screenshot(page, testInfo, `normal-${item.kind}`)
  }

  const status = await page.request.get('/__systemdata_fixture__/status')
  const payload = (await status.json()) as { writes: number; unknown: number }
  expect(payload.writes).toBe(0)
  expect(payload.unknown).toBe(0)
})

test('菜单默认导入、深层权限叶子与验证在隔离 HTTP 夹具中可复核', async ({ page }) => {
  await openFixture(page, '/pc/systemdata/navigation', 'normal', {
    navigationScenario: 'no-add',
  })
  await assertFrame(page, '菜单管理')
  await page.getByTestId('systemdata-navigation-defaults').click()
  const noAddPreview = page.getByTestId('systemdata-navigation-defaults-preview')
  await expect(noAddPreview).toContainText('已跳过')
  await expect(noAddPreview).toContainText('可新增 0')
  await expect(page.getByTestId('systemdata-navigation-defaults-confirm')).toBeDisabled()
  await page.getByTestId('systemdata-navigation-defaults-cancel').click()

  await openFixture(page, '/pc/systemdata/navigation', 'normal', {
    navigationScenario: 'add-skipped',
  })
  await page.getByTestId('systemdata-navigation-defaults').click()
  const addSkippedPreview = page.getByTestId('systemdata-navigation-defaults-preview')
  await expect(addSkippedPreview).toContainText('可新增 1')
  await expect(addSkippedPreview).toContainText('已跳过 2')
  await expect(page.getByTestId('systemdata-navigation-defaults-confirm')).toBeEnabled()
  await page.getByTestId('systemdata-navigation-defaults-confirm').click()
  await expect(page.getByRole('dialog')).toHaveCount(0)

  await openFixture(page, '/pc/systemdata/navigation', 'normal', {
    navigationScenario: 'mixed',
  })
  await page.getByTestId('systemdata-navigation-defaults').click()
  const mixedPreview = page.getByTestId('systemdata-navigation-defaults-preview')
  await expect(mixedPreview).toContainText('可新增 1')
  await expect(mixedPreview).toContainText('已跳过 1')
  await expect(mixedPreview).toContainText('已阻断 1')
  await expect(page.getByTestId('systemdata-navigation-defaults-confirm')).toBeDisabled()
  const blockedImport = await page.evaluate(async () => {
    const response = await fetch('/systemdata/api/v1/navigation/defaults/import', {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ expectedDraftRevision: 12 }),
    })
    return { status: response.status, body: (await response.json()) as { code: string } }
  })
  expect(blockedImport.status).toBe(422)
  expect(blockedImport.body.code).toBe('NAVIGATION_DEFAULT_IMPORT_BLOCKED')
  await page.getByTestId('systemdata-navigation-defaults-cancel').click()

  await openFixture(page, '/pc/systemdata/navigation', 'normal', {
    navigationScenario: 'conflict',
  })
  await page.getByTestId('systemdata-navigation-defaults').click()
  await page.getByTestId('systemdata-navigation-defaults-confirm').click()
  await expect(page.getByTestId('systemdata-navigation-defaults-preview')).toContainText(
    '草稿已被其他操作更新',
  )

  await openFixture(page, '/pc/systemdata/navigation', 'normal', {
    navigationScenario: 'normal',
  })
  const table = page.locator('.app-data-table')
  await table.getByTestId('app-data-table-tree-expand-all').click()
  await expect(table).toContainText('平台诊断目录')
  await expect(table).toContainText('平台配置诊断')
  await expect(table).toContainText('刷新平台诊断')
  await expect(table).toContainText('导出平台诊断')
  await table.getByTestId('app-data-table-quick-search').fill('刷新平台诊断')
  await table.getByTestId('app-data-table-tree-expand-all').click()
  await expect(table).toContainText('平台诊断目录')
  await expect(table).toContainText('平台配置诊断')
  await expect(table).toContainText('刷新平台诊断')
  await expect(table).not.toContainText('导出平台诊断')

  await openFixture(page, '/pc/systemdata/navigation', 'normal', {
    navigationScenario: 'validation',
  })
  await page.getByTestId('systemdata-navigation-validate').click()
  const validation = page.locator('details.systemdata-validation')
  await expect(validation).toBeVisible()
  await expect(validation).toContainText('平台诊断目录 / 平台配置诊断')
  await expect(validation).toContainText('resource-platform-refresh')
  await expect(validation).toContainText('resource-platform-export')
  await expect(validation).toContainText('module-systemdata')

  const status = await page.request.get('/__systemdata_fixture__/status')
  const payload = (await status.json()) as {
    writes: number
    unknown: number
    requests: Array<{ operation?: string; result?: string }>
  }
  expect(payload.writes).toBe(0)
  expect(payload.unknown).toBe(0)
  expect(
    payload.requests.filter(
      (request) =>
        request.operation === 'default-import' && request.result === 'accepted-in-memory',
    ),
  ).toHaveLength(1)
  expect(
    payload.requests.filter(
      (request) => request.operation === 'default-import' && request.result === 'blocked',
    ),
  ).toHaveLength(1)
  expect(
    payload.requests.filter(
      (request) => request.operation === 'default-import' && request.result === 'conflict',
    ),
  ).toHaveLength(1)
  expect(payload.requests.filter((request) => request.operation === 'validate')).toHaveLength(1)
})

test('组织页在多个 CSS 视口下可滚动到底部且不产生页面横向溢出', async ({ page }, testInfo) => {
  const viewports = [
    { name: '1280x720', width: 1280, height: 720 },
    { name: '1440x900', width: 1440, height: 900 },
    { name: '1024x720-css', width: 1024, height: 720 },
    { name: '1024x545-css', width: 1024, height: 545 },
  ] as const

  for (const viewport of viewports) {
    await page.setViewportSize({ width: viewport.width, height: viewport.height })
    await openFixture(page, '/pc/systemdata/organizations')
    await assertViewport(page, viewport.width, viewport.height)
    await assertFrame(page, '行政组织与岗位')
    const layout = page.locator('.systemdata-organizations-layout')
    await expect(layout).toBeVisible()
    const masterList = layout.locator('.organization-master-list')
    await expect(masterList).toBeVisible()
    await expect(masterList.getByRole('treeitem').first()).toBeVisible()
    await assertNoDocumentOverflow(page)
    if (viewport.width === 1024) {
      await expect(layout).toHaveCSS('flex-direction', 'column')
      await masterList.getByTestId('organization-card-org-platform').click()
      await expect(layout.locator('.systemdata-organization-context')).toContainText('平台运营中心')
      await expect(layout.getByRole('cell', { name: /平台运营负责人/ })).toBeVisible()
      await assertCanReachBottom(
        page,
        layout
          .locator('.app-tree-table__content .app-data-table__actions-column')
          .getByRole('button', { name: '停用', exact: true })
          .first(),
      )
      expect(
        await layout
          .locator('.app-tree-table__tree .organization-master-list__card')
          .first()
          .evaluate((card) => {
            const cardRect = card.getBoundingClientRect()
            const treeRect = card.closest('.app-tree-table__tree')?.getBoundingClientRect()
            return (
              treeRect !== undefined &&
              cardRect.top >= treeRect.top &&
              cardRect.bottom <= treeRect.bottom
            )
          }),
      ).toBe(true)
    }
    await screenshot(page, testInfo, `organizations-${viewport.name}`)
    if (viewport.name === '1024x545-css') {
      await viewportScreenshot(page, testInfo, 'organizations-1024x545-css-after-scroll')
    }
  }
})

test('SystemData 服务与导航页在受限高度下可用滚轮和键盘到达底部', async ({ page }, testInfo) => {
  await page.setViewportSize({ width: 1280, height: 720 })
  await openFixture(page, '/pc/systemdata/services')
  await assertViewport(page, 1280, 720)
  await assertFrame(page, '服务目录')
  await assertCanReachBottom(
    page,
    page.locator('.systemdata-service-group').nth(1).locator('.app-data-table__footer'),
  )
  await viewportScreenshot(page, testInfo, 'services-1280x720-after-scroll')

  await openFixture(page, '/pc/systemdata/navigation')
  await assertViewport(page, 1280, 720)
  await assertFrame(page, '菜单管理')
  // The collapsed tree fits; expand real nodes before exercising overflow scrolling.
  await expect(page.locator('.app-data-table__footer')).toBeInViewport()
  await page.getByTestId('app-data-table-tree-expand-all').click()
  await expect(page.locator('.app-data-table')).toContainText('导出平台诊断')
  await assertCanReachBottom(page, page.locator('.app-data-table__footer'))
  await viewportScreenshot(page, testInfo, 'navigation-1280x720-after-scroll')
})

test('SystemData 空态、未选择、加载、错误、禁用、表单和确认态保持可见且可操作', async ({
  page,
}, testInfo) => {
  await page.setViewportSize({ width: 2048, height: 1090 })

  await openFixture(page, '/pc/systemdata/features', 'empty')
  await assertFrame(page, '功能开关')
  await expect(page.locator('.app-empty-state')).toBeVisible()
  await expect(page.locator('.app-data-table')).toHaveCount(0)
  await screenshot(page, testInfo, 'state-empty-features')

  await openFixture(page, '/pc/systemdata/organizations', 'normal')
  await assertFrame(page, '行政组织与岗位')
  await expect(page.locator('.systemdata-organization-context')).toHaveCount(0)
  await expect(page.getByTestId('organization-selection-clear')).toHaveCount(0)
  await screenshot(page, testInfo, 'state-no-selection-organizations')

  await openFixture(page, '/pc/systemdata/organizations', 'loading')
  await expect(page.getByTestId('app-loading-state')).toBeVisible()
  await expect(page.getByTestId('systemdata-refresh')).toBeDisabled()
  await screenshot(page, testInfo, 'state-loading-organizations')
  await expect(page.locator('.systemdata-admin-content')).toBeVisible({ timeout: 30_000 })

  await openFixture(page, '/pc/systemdata/themes', 'error')
  await assertFrame(page, '租户主题策略')
  const themeError = page.locator('.app-error-alert')
  await expect(themeError).toBeVisible()
  // The frame keeps its content mounted on errors so context and drafts are not lost.
  await expect(page.locator('.systemdata-admin-content')).toHaveAttribute('aria-busy', 'false')
  await expect(page.locator('.systemdata-theme-editor')).toBeVisible()
  const retryResponse = page.waitForResponse(
    (response) =>
      response.url().endsWith('/systemdata/api/v1/theme-policy') &&
      response.request().method() === 'GET',
  )
  await themeError.getByRole('button', { name: '重试', exact: true }).click()
  expect((await retryResponse).status()).toBe(503)
  await expect(themeError).toBeVisible()
  await screenshot(page, testInfo, 'state-error-themes')

  await openFixture(page, '/pc/systemdata/features', 'disabled')
  await assertFrame(page, '功能开关')
  await expect(page.getByTestId('app-loading-state')).toBeVisible()
  await expect(page.getByTestId('systemdata-refresh')).toBeDisabled()
  await screenshot(page, testInfo, 'state-disabled-features')
  await expect(page.locator('.systemdata-admin-content')).toBeVisible({ timeout: 30_000 })

  await openFixture(page, '/pc/systemdata/organizations')
  await assertFrame(page, '行政组织与岗位')
  await page.getByTestId('systemdata-organizations-new').click()
  await expect(page.getByRole('dialog')).toBeVisible()
  await page.getByTestId('form-drawer-submit').click()
  await expect(page.getByRole('dialog').getByRole('alert')).toContainText('组织名称不能为空')
  await screenshot(page, testInfo, 'state-form-validation-organizations')
  await page.getByTestId('form-drawer-close').click()

  await openFixture(page, '/pc/systemdata/navigation')
  await assertFrame(page, '菜单管理')
  await page.getByTestId('systemdata-navigation-publish').click()
  await expect(page.locator('.el-message-box')).toBeVisible()
  await page.locator('.el-message-box').getByRole('button', { name: '取消', exact: true }).click()
  await expect(page.locator('.el-message-box')).toHaveCount(0)
  await screenshot(page, testInfo, 'state-confirm-cancel-navigation')

  const status = await page.request.get('/__systemdata_fixture__/status')
  const payload = (await status.json()) as { writes: number; unknown: number }
  expect(payload.writes).toBe(0)
  expect(payload.unknown).toBe(0)
})

test('SystemData English/dark/compact and keyboard focus controls are usable', async ({
  page,
}, testInfo) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await openFixture(page, '/pc/systemdata/themes', 'normal', {
    locale: 'en-US',
    palette: 'technology-blue',
    mode: 'dark',
    density: 'compact',
  })
  await assertFrame(page, 'Tenant theme policy')
  await expect(page.locator('html')).toHaveAttribute('lang', 'en-US')
  await expect(page.locator('html')).toHaveAttribute('data-ip-color-mode', 'dark')
  await expect(page.locator('html')).toHaveAttribute('data-ip-density', 'compact')
  await expect(page.locator('.systemdata-theme-editor')).toContainText('Allowed palettes')
  const themeReadability = await page.locator('.systemdata-theme-editor').evaluate((editor) => {
    const selectedLabel = editor.querySelector<HTMLElement>(
      '.el-checkbox.is-checked .el-checkbox__label',
    )
    if (selectedLabel === null) throw new Error('Expected a selected theme checkbox label')
    return {
      foreground: getComputedStyle(selectedLabel).color,
      background: getComputedStyle(editor).backgroundColor,
    }
  })
  expect(
    contrastRatio(cssRgb(themeReadability.foreground), cssRgb(themeReadability.background)),
  ).toBeGreaterThanOrEqual(4.5)
  const densityLabel = page
    .locator('.systemdata-theme-editor .el-form-item__label')
    .filter({ hasText: 'Allowed PC densities' })
  expect(
    await densityLabel.evaluate((element) => element.getBoundingClientRect().width),
  ).toBeGreaterThanOrEqual(160)
  await screenshot(page, testInfo, 'themes-en-dark-compact-1440x900')

  await openFixture(page, '/pc/systemdata/features', 'normal', {
    locale: 'en-US',
    palette: 'technology-blue',
    mode: 'dark',
    density: 'compact',
  })
  await assertViewport(page, 1440, 900)
  await assertFrame(page, 'Feature flags')
  const tableReadability = await page.locator('.app-data-table').evaluate((table) => {
    const bodyWrapper = table.querySelector<HTMLElement>('.vxe-table--body-wrapper')
    const headerWrapper = table.querySelector<HTMLElement>('.vxe-table--header-wrapper')
    const bodyCell = table.querySelector<HTMLElement>('.vxe-body--column')
    if (bodyWrapper === null || headerWrapper === null || bodyCell === null) {
      throw new Error('Expected VXE table surface')
    }
    return {
      bodyBackground: getComputedStyle(bodyWrapper).backgroundColor,
      headerBackground: getComputedStyle(headerWrapper).backgroundColor,
      bodyColor: getComputedStyle(bodyCell).color,
    }
  })
  expect(cssRgb(tableReadability.bodyBackground)).not.toEqual([255, 255, 255])
  expect(cssRgb(tableReadability.headerBackground)).not.toEqual([255, 255, 255])
  expect(
    contrastRatio(cssRgb(tableReadability.bodyColor), cssRgb(tableReadability.bodyBackground)),
  ).toBeGreaterThanOrEqual(4.5)
  await screenshot(page, testInfo, 'features-en-dark-compact-1440x900')

  const themeTrigger = page.getByTestId('theme-control-trigger')
  await themeTrigger.focus()
  await expect(themeTrigger).toBeFocused()
  await page.keyboard.press('Enter')
  await expect(page.getByTestId('theme-mode-dark')).toBeVisible()
  await page.getByTestId('theme-mode-dark').focus()
  await page.keyboard.press('Escape')
  await expect(page.getByTestId('theme-mode-dark')).toHaveCount(0)

  await openFixture(page, '/pc/systemdata/service-initialization')
  await assertFrame(page, '服务初始化编排')
  const seedTab = page.locator('#systemdata-init-tab-seedsets')
  await page
    .locator('#systemdata-init-panel-registrations .vxe-body--row .vxe-radio--icon')
    .first()
    .click()
  await page.locator('#systemdata-init-tab-registrations').focus()
  await page.keyboard.press('ArrowRight')
  await expect(seedTab).toBeFocused()
  await expect(seedTab).toHaveAttribute('aria-selected', 'true')
  await expect(page.locator('#systemdata-init-panel-seedsets')).toBeVisible()
  await viewportScreenshot(page, testInfo, 'initialization-seedsets-1440x900')
  await page.keyboard.press('End')
  await expect(page.locator('#systemdata-init-tab-environment')).toBeFocused()
  await expect(page.locator('#systemdata-init-tab-environment')).toHaveAttribute(
    'aria-selected',
    'true',
  )
  await expect(page.locator('#systemdata-init-panel-environment')).toBeVisible()
  await viewportScreenshot(page, testInfo, 'initialization-environment-1440x900')

  await page.locator('#systemdata-init-tab-plans').click()
  await expect(page.locator('#systemdata-init-panel-plans')).toBeVisible()
  await viewportScreenshot(page, testInfo, 'initialization-plans-1440x900')
  await page.locator('.systemdata-init-plan-index button').first().click()
  await expect(page.locator('.systemdata-init-plan-detail')).toContainText('identity-core')
  await assertCanReachBottom(page, page.locator('.systemdata-init-plan-detail'))
  await viewportScreenshot(page, testInfo, 'initialization-plan-detail-1440x900-after-scroll')

  await page.locator('#systemdata-init-tab-operations').click()
  await expect(page.locator('#systemdata-init-panel-operations')).toBeVisible()
  await page
    .locator('#systemdata-init-panel-operations .vxe-body--row .vxe-radio--icon')
    .first()
    .click()
  await expect(page.locator('.systemdata-init-operation-detail')).toContainText('trace-visual-001')
  await viewportScreenshot(page, testInfo, 'initialization-operations-1440x900')
})

test('Feature 暗色操作文字与 fixed-right 悬停背景保持可读且整行一致', async ({
  page,
}, testInfo) => {
  await page.setViewportSize({ width: 1024, height: 720 })
  await openFixture(page, '/pc/systemdata/features', 'normal', {
    locale: 'en-US',
    palette: 'technology-blue',
    mode: 'dark',
    density: 'compact',
  })
  await assertViewport(page, 1024, 720)
  await assertFrame(page, 'Feature flags')

  const table = page.locator('.app-data-table')
  const fixedRight = table.locator('.vxe-table--fixed-right-wrapper')
  await expect(fixedRight).toBeVisible()
  const mainRow = table
    .locator('.vxe-table--main-wrapper .vxe-table--body-wrapper .vxe-body--row')
    .first()
  const fixedRow = fixedRight.locator('.vxe-body--row').first()
  const mainCell = mainRow.locator('.vxe-body--column').first()
  const fixedActionCell = fixedRow.locator('.app-data-table__actions-column')
  const enabledAction = fixedActionCell.locator('.el-button:not(.is-disabled)').first()
  await expect(enabledAction).toBeVisible()

  const initialColor = await enabledAction.evaluate((element) => getComputedStyle(element).color)
  const initialSurface = await effectiveBackground(enabledAction)
  expect(contrastRatio(cssRgb(initialColor), cssRgb(initialSurface))).toBeGreaterThanOrEqual(4.5)

  await mainCell.hover()
  const [mainHoverSurface, fixedHoverSurface] = await Promise.all([
    effectiveBackground(mainRow.locator('.app-data-table__actions-column')),
    effectiveBackground(fixedActionCell),
  ])
  expect(mainHoverSurface).toBe(fixedHoverSurface)

  await enabledAction.hover()
  const hoveredColor = await enabledAction.evaluate((element) => getComputedStyle(element).color)
  const hoveredSurface = await effectiveBackground(enabledAction)
  expect(contrastRatio(cssRgb(hoveredColor), cssRgb(hoveredSurface))).toBeGreaterThanOrEqual(4.5)

  await enabledAction.focus()
  const focusedColor = await enabledAction.evaluate((element) => getComputedStyle(element).color)
  const focusedSurface = await effectiveBackground(enabledAction)
  expect(contrastRatio(cssRgb(focusedColor), cssRgb(focusedSurface))).toBeGreaterThanOrEqual(4.5)
  await viewportScreenshot(page, testInfo, 'features-dark-fixed-hover-1024x720')
})

test('同主题只读黄金页可在本地 fixture 中用于对照', async ({ page }, testInfo) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await openFixture(page, '/pc/identity/users', 'normal', {
    locale: 'en-US',
    palette: 'technology-blue',
    mode: 'dark',
    density: 'compact',
  })
  await expect(page.getByTestId('identity-users-page')).toBeVisible()
  await expect(
    page.getByRole('heading', { level: 1, name: 'User management', exact: true }),
  ).toBeVisible()
  await expect(page.getByTestId('identity-users-total')).toContainText('1')
  await expect(page.getByTestId('identity-users-query')).toBeVisible()
  await expect(page.getByText('PF02 视觉验收用户', { exact: true }).first()).toBeVisible()
  await expect(page.getByRole('button', { name: 'Details', exact: true })).toBeVisible()
  await expect(page.getByTestId('identity-users-create')).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Edit', exact: true })).toHaveCount(0)
  await assertNoDocumentOverflow(page)
  await screenshot(page, testInfo, 'golden-users-readonly-en-dark-compact-1440x900')
})
