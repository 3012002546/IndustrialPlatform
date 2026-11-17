import { expect, test } from '@playwright/test'
import type { SheetCapture } from '../../src/components/management/querySpreadsheet/types'

declare global {
  interface Window {
    sheetFixture: { capture(): Promise<SheetCapture> }
  }
}

// Uses the production component and SDK; no global hooks are included in application builds.
for (const columns of [8, 10, 30, 60]) {
  test(`dynamic ${columns} fields: editing, charts, export and print`, async ({
    page,
    context,
  }) => {
    await context.grantPermissions(['clipboard-read', 'clipboard-write'])
    await page.goto(`/tests/fixtures/query-spreadsheet.html?columns=${columns}`)
    await expect(page.getByRole('status')).toContainText('215 /')
    await expect(page.getByRole('status')).not.toContainText('临时编辑')
    await expect(page.locator('.query-sheet-canvas canvas:visible').first()).toBeVisible()
    await page.evaluate(async () => {
      await document.fonts.ready
      await new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)))
    })
    await expect(page.getByRole('tab', { name: '图表', exact: true })).toBeVisible()
    await page.getByRole('button', { name: '开启临时编辑', exact: true }).click()
    const box = (await page.locator('canvas[id^="univer-sheet-main-canvas_"]').boundingBox())!
    await expect(page.getByRole('button', { name: '刷新', exact: true })).toBeEnabled()
    const address = page.locator('.query-sheet-canvas input').first()
    await address.fill('B2')
    await address.press('Enter')
    await page.evaluate(() => navigator.clipboard.writeText('777'))
    await page.keyboard.press('Control+V')
    await expect(page.getByRole('status')).toContainText('临时编辑')
    if (columns === 60) {
      await page.mouse.click(box.x + 178, box.y + 39)
      await page.getByText('全选', { exact: true }).waitFor()
      await page.getByRole('checkbox').nth(3).locator('..').click()
      await page.getByRole('button', { name: '确认', exact: true }).click()
      await page.getByText('全选', { exact: true }).waitFor({ state: 'hidden' })
    }
    const before = await page.evaluate(() => window.sheetFixture.capture())
    if (columns === 60) {
      expect(before.filters).toHaveLength(1)
      expect(before.hidden).toHaveLength(72)
    }
    await page.getByRole('tab', { name: '图表', exact: true }).click()
    await expect(page.locator('.query-chart svg[role=img]')).toBeVisible()
    await expect(page.getByRole('dialog', { name: '图表', exact: true })).toHaveCount(0)
    await page.getByRole('button', { name: '柱状图', exact: true }).click()
    await expect(page.locator('.query-chart svg[role=img] rect')).toHaveCount(
      columns === 60 ? 2 : 3,
    )
    await page.getByRole('tab', { name: '表格', exact: true }).click()
    await expect(page.locator('.query-chart')).toBeHidden()
    const after = await page.evaluate(() => window.sheetFixture.capture())
    expect(after.snapshot.id).toBe(before.snapshot.id)
    expect(after.snapshot.sheets.result!.cellData).toEqual(before.snapshot.sheets.result!.cellData)
    expect(after.filters).toEqual(before.filters)
    expect(after.hidden).toEqual(before.hidden)
    const downloaded = page.waitForEvent('download')
    await page.getByRole('button', { name: '下载筛选结果 XLSX', exact: true }).click()
    expect((await downloaded).suggestedFilename()).toBe('动态查询验证.xlsx')
    await page.getByRole('button', { name: '打印筛选结果', exact: true }).click()
    await expect(page.frameLocator('.query-print-frame').locator('thead th')).toHaveCount(
      columns <= 9 ? columns : columns - 1 + Math.ceil((columns - 1) / 8),
    )
    await expect(page.frameLocator('.query-print-frame').locator('tbody tr')).toHaveCount(
      (215 - before.hidden.filter((r) => r <= 215).length) *
        (columns <= 9 ? 1 : Math.ceil((columns - 1) / 8)),
    )
    await expect(page.frameLocator('.query-print-frame').locator('tbody tr').first()).toContainText(
      '777',
    )
  })
}

test('formula workspace, read-only switch, full-result filtering and frozen export agree', async ({
  page,
  context,
}) => {
  await context.grantPermissions(['clipboard-read', 'clipboard-write'])
  await page.goto('/tests/fixtures/query-spreadsheet.html?columns=8')
  await expect(page.getByRole('status')).toContainText('215 /')
  const name = page.locator('.query-sheet-canvas input').first()
  async function select(address: string) {
    await expect(page.getByRole('button', { name: '刷新', exact: true })).toBeEnabled()
    await name.fill(address)
    await name.press('Enter')
  }
  async function typeCell(address: string, value: string) {
    await select(address)
    await page.evaluate((text) => navigator.clipboard.writeText(text), value)
    await page.keyboard.press('Control+V')
  }
  await typeCell('B2', '999')
  expect(
    (await page.evaluate(() => window.sheetFixture.capture())).snapshot.sheets.result!
      .cellData![1]![1]!.v,
  ).toBe(0)
  await page.getByRole('button', { name: '开启临时编辑', exact: true }).click()
  await expect(page.getByRole('button', { name: '结束编辑', exact: true })).toBeVisible()
  await page.evaluate(
    () => new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve))),
  )
  await page.getByRole('button', { name: '追加 10 列', exact: true }).click()
  await page.getByRole('button', { name: '追加 30 行', exact: true }).click()
  await typeCell('I1', 'Computed')
  await typeCell('B216', '777')
  await typeCell('I216', '=B216*2')
  const beforeSort = await page.evaluate(() => window.sheetFixture.capture())
  expect(beforeSort.columns.at(-1)?.title).toBe('Computed')
  await page.getByRole('combobox', { name: '排序字段', exact: true }).press('ArrowDown')
  await expect(page.getByRole('option', { name: 'Computed', exact: true })).toBeVisible()
  await page.getByRole('option', { name: 'Computed', exact: true }).click()
  const edited = await page.evaluate(() => window.sheetFixture.capture())
  expect(edited.columns).toHaveLength(9)
  expect(edited.snapshot.sheets.result!.cellData![215]![8]!.v).toBe(1554)
  await page.getByRole('button', { name: '结束编辑', exact: true }).click()
  const retained = await page.evaluate(() => window.sheetFixture.capture())
  expect(retained.snapshot.id).toBe(edited.snapshot.id)
  expect(retained.snapshot.sheets.result!.cellData).toEqual(edited.snapshot.sheets.result!.cellData)
  // The last source record is beyond the old 200-row page boundary.
  await select('A1')
  const box = (await page.locator('canvas[id^="univer-sheet-main-canvas_"]').boundingBox())!
  await page.mouse.click(box.x + 178, box.y + 39)
  await page.getByRole('checkbox').nth(0).locator('..').click()
  await page.getByRole('checkbox').nth(3).locator('..').click()
  await page.getByRole('button', { name: '确认', exact: true }).click()
  const filtered = await page.evaluate(() => window.sheetFixture.capture())
  expect(filtered.hidden).not.toContain(215)
  await expect(page.getByRole('status')).toContainText('筛选后行数: 72')
  const downloadPromise = page.waitForEvent('download')
  await page.getByRole('button', { name: '下载筛选结果 XLSX', exact: true }).click()
  const download = await downloadPromise
  const ExcelJS = (await import('exceljs')).default
  const workbook = new ExcelJS.Workbook()
  await workbook.xlsx.readFile((await download.path())!)
  expect(workbook.worksheets[0]!.rowCount).toBe(73)
  expect(workbook.worksheets[0]!.getCell('I73').value).toBe(1554)
  await page.getByRole('tab', { name: '图表', exact: true }).click()
  await expect(page.locator('.query-chart')).toBeVisible()
  await expect(page.getByRole('button', { name: '刷新', exact: true })).toBeEnabled()
  await page.getByRole('combobox', { name: '数值字段', exact: true }).press('ArrowDown')
  await page.getByRole('option', { name: 'Computed', exact: true }).click()
  await expect(page.locator('.query-chart svg title')).toHaveText('组1: 1554')
  await page.getByRole('tab', { name: '表格', exact: true }).click()
  expect((await page.evaluate(() => window.sheetFixture.capture())).hidden).toEqual(filtered.hidden)
  await page.getByRole('combobox', { name: '排序字段', exact: true }).press('ArrowDown')
  await page.getByRole('option', { name: 'Computed', exact: true }).click()
  await page.getByRole('combobox', { name: '排序方向', exact: true }).press('ArrowDown')
  await page.getByRole('option', { name: '降序', exact: true }).click()
  await page.getByRole('button', { name: '排序全部结果', exact: true }).click()
  await expect(page.getByRole('button', { name: '刷新', exact: true })).toBeEnabled()
  const sorted = await page.evaluate(() => window.sheetFixture.capture())
  const firstVisible = Array.from({ length: sorted.rowCount }, (_, i) => i + 1).find(
    (r) => !sorted.hidden.includes(r),
  )!
  expect(sorted.snapshot.sheets.result!.cellData![firstVisible]![8]?.v).toBe(1554)
  expect(sorted.snapshot.sheets.result!.cellData![firstVisible]![1]?.v).toBe(777)
  await page.screenshot({ path: '../../.run/query-closeout-formula.png' })
})
