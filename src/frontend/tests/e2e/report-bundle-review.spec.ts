/* eslint-disable @typescript-eslint/no-explicit-any */
import { test, expect } from '@playwright/test'
const fixture = (page: import('@playwright/test').Page) =>
  page.evaluate(
    () =>
      (
        window as unknown as {
          reportFixture: {
            calls: string[]
            original: Record<string, any>
            saved: Record<string, any>
            previewInput: unknown
            exportInput: unknown
          }
        }
      ).reportFixture,
  )

for (const mode of ['Simple', 'ElectronicRecord', 'ElectronicRecord&driven'])
  test(`excludes query templates in ${mode} without changing saved configuration`, async ({
    page,
  }) => {
    await page.goto('/tests/fixtures/report-designer.html?bundle=' + mode + '&query-candidate')
    await page.locator('.report-bundle-flow button').first().click()
    await right(page).getByRole('combobox', { name: '子模板', exact: true }).click()
    await expect(page.getByRole('option', { name: '工序检验记录', exact: true })).toBeVisible()
    await expect(page.getByRole('option', { name: '查询报表候选', exact: true })).toHaveCount(0)
    await page.getByRole('option', { name: '工序检验记录', exact: true }).click()
    await page.getByRole('button', { name: '保存草稿', exact: true }).click()
    expect((await fixture(page)).saved).toEqual((await fixture(page)).original)
  })

for (const english of [false, true])
  test(`uses generic record scopes and preserves legacy values (${english ? 'en' : 'zh'})`, async ({
    page,
  }) => {
    await page.goto(
      '/tests/fixtures/report-designer.html?bundle=ElectronicRecord&driven' +
        (english ? '&english' : ''),
    )
    await page.locator('.report-bundle-flow button').first().click()
    const scope = right(page).getByRole('combobox', {
      name: english ? 'Assembly position' : '拼接位置',
      exact: true,
    })
    await scope.press('Enter')
    await page
      .getByRole('option', {
        name: english
          ? 'Master record header (once per master record)'
          : '主记录头部（每条主记录一次）',
        exact: true,
      })
      .click()
    await page
      .getByRole('button', { name: english ? 'Save draft' : '保存草稿', exact: true })
      .click()
    const expected = structuredClone((await fixture(page)).original)
    expected.assembly.templates[0].scope = 'WorkOrder'
    expect((await fixture(page)).saved).toEqual(expected)
    await scope.press('Enter')
    await page
      .getByRole('option', {
        name: english
          ? 'Child record body (once per child record)'
          : '子记录主体（每条子记录一次）',
        exact: true,
      })
      .click()
    await page
      .getByRole('button', { name: english ? 'Save draft' : '保存草稿', exact: true })
      .click()
    expect((await fixture(page)).saved).toEqual((await fixture(page)).original)
    await expect(right(page)).not.toContainText(english ? 'work order' : '工单头部')
  })
const right = (page: import('@playwright/test').Page) => page.locator('.studio-sidebar').last()
for (const mode of ['Simple', 'ElectronicRecord', 'ElectronicRecord&driven'])
  test(`selects a readable published template version in ${mode}`, async ({ page }) => {
    await page.goto('/tests/fixtures/report-designer.html?bundle=' + mode)
    await page.locator('.report-bundle-flow button').first().click()
    const selector = right(page).getByRole('combobox', { name: '子模板版本', exact: true })
    await expect(selector).toBeEnabled()
    await expect(right(page)).toContainText('子模板更新后不会自动切换')
    await expect(right(page)).not.toContainText('revision-child')
    await selector.press('Enter')
    await expect(page.getByRole('option', { name: /V3.*2026.*调整表头/ })).toBeVisible()
    await page.getByRole('option', { name: /V2.*2026.*初始发布版本/ }).click()
    await page.getByRole('button', { name: '保存草稿', exact: true }).click()
    const expected = structuredClone((await fixture(page)).original)
    const entries =
      mode === 'Simple'
        ? expected.items
        : expected.assembly[mode.includes('driven') ? 'templates' : 'rules']
    entries[0].revisionNId = 'revision-child-v2'
    expect((await fixture(page)).saved).toEqual(expected)
    await expect(page.locator('.report-bundle-flow button').first()).toContainText('子模板版本 V2')
    for (const name of ['数据与映射', '排版', '基础'])
      await right(page).getByRole('tab', { name, exact: true }).click()
    await expect(selector).toBeEnabled()
    await selector.press('Enter')
    await expect(page.getByRole('option', { name: /V2.*2026.*初始发布版本/ })).toBeVisible()
    await page.keyboard.press('Escape')
    expect(
      (await fixture(page)).calls.filter((call) => call.startsWith('revisions:')),
    ).toHaveLength(1)
    await page.getByRole('button', { name: '保存草稿', exact: true }).click()
    expect((await fixture(page)).saved).toEqual(expected)
  })
for (const [mode, query] of [
  ['single', ''],
  ['simple', '?bundle=Simple'],
  ['normal', '?bundle=ElectronicRecord'],
  ['driven', '?bundle=ElectronicRecord&driven'],
]) {
  test(mode + ' shares full preview and all draft download formats', async ({ page }) => {
    await page.goto('/tests/fixtures/report-designer.html' + query)
    await page.getByRole('button', { name: '预览', exact: true }).click()
    await expect(page.frameLocator('iframe').getByText('Engine page fixture')).toBeVisible()
    await expect(page.locator('iframe')).toHaveAttribute('sandbox', '')
    await expect.poll(async () => (await page.getByRole('dialog').boundingBox())?.y).toBe(0)
    const bounds = await page.getByRole('dialog').boundingBox()
    expect(bounds?.x).toBe(0)
    expect(bounds?.y).toBe(0)
    for (const [label, format, extension] of [
      ['PDF', 'Pdf', 'pdf'],
      ['HTML', 'Html', 'html'],
      ['Excel (.xlsx)', 'Xlsx', 'xlsx'],
      ['Excel 97–2003 (.xls)', 'Xls', 'xls'],
      ['Word (.docx)', 'Docx', 'docx'],
      ['RTF', 'Rtf', 'rtf'],
      ['MHT', 'Mht', 'mht'],
      ['文本（制表符分列）', 'Text', 'txt'],
      ['CSV', 'Csv', 'csv'],
      ['PNG 物理页（ZIP）', 'Png', 'zip'],
      ['JPEG 物理页（ZIP）', 'Jpeg', 'zip'],
    ]) {
      await page.getByRole('combobox', { name: '输出方式', exact: true }).press('Enter')
      await page.getByRole('option', { name: label!, exact: true }).click()
      const pending = page.waitForEvent('download')
      await page.getByRole('button', { name: '下载草稿', exact: true }).click()
      expect((await pending).suggestedFilename()).toBe(
        'preview-' + format!.toLowerCase() + '.' + extension,
      )
      expect((await fixture(page)).exportInput).toEqual((await fixture(page)).previewInput)
    }
    await page.getByRole('button', { name: '关闭预览' }).click()
    expect((await fixture(page)).saved).toBeNull()
  })
}
test('bundle names load without waiting for unrelated datasets; inspector groups preserve save', async ({
  page,
}) => {
  await page.goto('/tests/fixtures/report-designer.html?bundle=Simple&slow-datasets')
  await expect(page.locator('.report-bundle-flow')).toContainText('工序检验记录')
  await page.locator('.report-bundle-flow button').first().click()
  await expect(right(page).getByRole('tab', { name: '基础', exact: true })).toBeVisible()
  expect((await fixture(page)).calls).not.toContain('child-contract')
  for (const name of ['数据与映射', '排版', '基础'])
    await right(page).getByRole('tab', { name: name!, exact: true }).click()
  await page.getByRole('button', { name: '保存草稿', exact: true }).click()
  expect((await fixture(page)).saved).toEqual((await fixture(page)).original)
})
for (const driven of [false, true])
  test(
    'grouped ' + (driven ? 'driven' : 'normal') + ' properties save without losing hidden fields',
    async ({ page }) => {
      await page.goto(
        '/tests/fixtures/report-designer.html?bundle=ElectronicRecord' + (driven ? '&driven' : ''),
      )
      await page.locator('.report-bundle-flow button').first().click()
      expect((await fixture(page)).calls).not.toContain('child-contract')
      await expect(right(page).locator('fieldset')).toHaveCount(0)
      await right(page).getByRole('tab', { name: '数据与映射', exact: true }).click()
      await expect(right(page).locator('fieldset').first()).toBeVisible()
      if (!driven)
        await expect.poll(async () => (await fixture(page)).calls).toContain('child-contract')
      await right(page).getByRole('tab', { name: '排版', exact: true }).click()
      await right(page).getByRole('combobox', { name: '实例开始位置', exact: true }).press('Enter')
      await page.getByRole('option', { name: '新页开始', exact: true }).click()
      await right(page).getByRole('tab', { name: '基础', exact: true }).click()
      await page.getByRole('button', { name: '保存草稿', exact: true }).click()
      const value = await fixture(page),
        original = structuredClone(value.original)
      original.assembly[driven ? 'templates' : 'rules'][0].breakBefore = 'Page'
      expect(value.saved).toEqual(original)
      if (driven) expect(value.calls).not.toContain('child-contract')
    },
  )

test('bundle property groups fit the shared light and dark inspector', async ({ page }) => {
  await page.setViewportSize({ width: 1600, height: 1000 })
  for (const dark of [false, true]) {
    await page.goto(
      '/tests/fixtures/report-designer.html?bundle=ElectronicRecord&driven' +
        (dark ? '&dark&english' : ''),
    )
    await page.locator('.report-bundle-flow button').first().click()
    for (const [name, group] of dark
      ? [
          ['Basic', 'basic'],
          ['Data & mappings', 'data'],
          ['Layout', 'layout'],
        ]
      : [
          ['基础', 'basic'],
          ['数据与映射', 'data'],
          ['排版', 'layout'],
        ]) {
      await right(page).getByRole('tab', { name: name!, exact: true }).click()
      const size = await right(page).evaluate((el) => ({
        width: el.clientWidth,
        scroll: el.scrollWidth,
      }))
      expect(size.scroll).toBeLessThanOrEqual(size.width + 1)
      await page.screenshot({
        path:
          '../../.run/designer-review/inspector-' + (dark ? 'dark-' : 'light-') + group + '.png',
      })
    }
  }
})
