import { test, expect, type Page } from '@playwright/test'
import type { Json } from '../../src/api/studio'

type Fixture = { calls: string[]; saved: Json; original: Json }
const state = (page: Page) =>
  page.evaluate(() => (window as unknown as { reportFixture: Fixture }).reportFixture)
const right = (page: Page) => page.locator('.studio-sidebar').last()
async function open(page: Page, large = false) {
  await page.goto(`/tests/fixtures/report-designer.html${large ? '?large' : ''}`)
  await expect(page.locator('.report-control')).toHaveCount(2)
  await expect.poll(async () => (await state(page)).calls.length).toBe(1)
}

test('control selection is local, quick and leaves the document and undo history unchanged', async ({
  page,
}) => {
  const errors: string[] = []
  page.on('pageerror', (e) => errors.push(e.message))
  await page.setViewportSize({ width: 1600, height: 1000 })
  await open(page, true)
  const times: number[] = []
  for (const index of [0, 1, 0, 1]) {
    const start = Date.now()
    await page.locator('.report-control').nth(index).click()
    await expect(right(page).locator('.report-selection-title')).toHaveText(index ? '表格' : '标签')
    await page.evaluate(
      () => new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve))),
    )
    times.push(Date.now() - start)
  }
  expect(Math.max(...times)).toBeLessThan(1000)
  await expect(page.getByRole('button', { name: '撤销', exact: true })).toBeDisabled()
  await page.getByRole('button', { name: '保存草稿', exact: true }).click()
  const value = await state(page)
  expect(value.saved).toEqual(value.original)
  expect(value.calls).toEqual(['contract'])
  expect(errors).toEqual([])
  console.log('selection milliseconds:', times)
})

test('large table edits one column with searchable virtual field options', async ({ page }) => {
  await page.setViewportSize({ width: 1600, height: 1000 })
  await open(page, true)
  await page.locator('.report-control').nth(1).click()
  const start = Date.now()
  await right(page).getByRole('tab', { name: '内容与绑定' }).click()
  await expect(page.locator('.report-column-editor')).toHaveCount(1)
  expect(Date.now() - start).toBeLessThan(1000)
  expect(await page.locator('*').count()).toBeLessThan(2000)
  const fields = page
    .locator('.report-column-editor')
    .getByRole('combobox', { name: '字段路径', exact: true })
  await fields.click()
  await fields.fill('field1999')
  await page.getByRole('option', { name: 'field1999', exact: true }).click()
  await page.getByRole('button', { name: '保存草稿', exact: true }).click()
  const saved = (await state(page)).saved
  expect(saved.bands[0].controls[1].table.columns[0].value.path).toEqual(['field1999'])
  expect(saved.bands[0].controls[1].table.columns).toHaveLength(8)
})

test('generic palette, page bands, group pairs and undo/save round trip', async ({ page }) => {
  await page.setViewportSize({ width: 1600, height: 1000 })
  await open(page)
  await expect(page.getByRole('button', { name: /CMP/ })).toHaveCount(0)
  await expect(page.getByRole('button', { name: '组装计划', exact: true })).toHaveCount(0)
  await expect(page.locator('.report-control-palette button')).toHaveCount(9)
  await page
    .locator('.report-control-palette')
    .getByRole('button', { name: '复选框', exact: true })
    .press('Enter')
  await expect(right(page).locator('.report-selection-title')).toHaveText('复选框')
  await page.getByRole('button', { name: '撤销', exact: true }).click()
  await expect(page.locator('.report-control')).toHaveCount(2)
  await page.getByRole('button', { name: '恢复', exact: true }).click()
  await expect(page.locator('.report-control')).toHaveCount(3)
  for (const label of ['页眉区段', '报表页眉', '分组头区段', '报表页脚', '页脚区段']) {
    await page.getByRole('button', { name: '页面设置', exact: true }).click()
    await right(page).getByRole('button', { name: label, exact: true }).click()
  }
  await page.getByRole('button', { name: '保存草稿', exact: true }).click()
  const saved = (await state(page)).saved
  expect(saved.bands.map((b: Json) => b.kind)).toEqual([
    'PageHeader',
    'ReportHeader',
    'GroupHeader',
    'Detail',
    'GroupFooter',
    'ReportFooter',
    'PageFooter',
  ])
  expect(saved.bands[2].group.pairedBandId).toBe(saved.bands[4].id)
  expect(saved.bands[4].group.pairedBandId).toBe(saved.bands[2].id)
  const check = saved.bands[3].controls[2]
  expect(check.type).toBe('CheckBox')
  expect(check.value.value).toBe(false)
})

test('desktop panes, narrow panel switching and a single data drawer', async ({ page }) => {
  await page.setViewportSize({ width: 1366, height: 900 })
  await open(page)
  const leftBox = (await page.locator('.studio-sidebar').first().boundingBox())!
  const rightBox = (await right(page).boundingBox())!
  const canvasBox = (await page.locator('.studio-viewport').boundingBox())!
  expect(leftBox.x + leftBox.width).toBeLessThanOrEqual(canvasBox.x + 1)
  expect(canvasBox.x + canvasBox.width).toBeLessThanOrEqual(rightBox.x + 1)
  await page.getByRole('button', { name: '数据与参数', exact: true }).click()
  await expect(page.getByRole('dialog')).toHaveCount(1)
  await expect(page.getByRole('dialog')).toHaveAccessibleName('数据与参数')
  await page.keyboard.press('Escape')
  await expect(page.getByRole('dialog')).toHaveCount(0)
  await page.screenshot({ path: '../../.run/pf09-report-designer-desktop.png', fullPage: true })
  await page.setViewportSize({ width: 900, height: 900 })
  await page.reload()
  await expect(page.locator('.report-control')).toHaveCount(2)
  await page.locator('.report-control').first().click()
  await expect(right(page)).toBeVisible()
  await expect(page.locator('.studio-editor')).toHaveClass(/left-hidden/)
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(900)
  await page.screenshot({ path: '../../.run/pf09-report-designer-narrow.png', fullPage: true })
})

test('slow auxiliary lists do not block canvas selection or save', async ({ page }) => {
  await page.goto('/tests/fixtures/report-designer.html?slow-options')
  await expect(page.getByText('正在读取数据来源列表，可继续编辑画布…')).toBeVisible()
  await page.locator('.report-control').first().click()
  await expect(right(page).locator('.report-selection-title')).toHaveText('标签')
  await expect(page.getByRole('button', { name: '保存草稿', exact: true })).toBeEnabled()
  await page.getByRole('button', { name: '保存草稿', exact: true }).click()
  expect((await state(page)).saved).toEqual((await state(page)).original)
})

test('appearance is mounted on demand and edits the selected shared style', async ({ page }) => {
  await open(page)
  await page.locator('.report-control').first().click()
  await expect(right(page).locator('.el-color-picker')).toHaveCount(0)
  await right(page).getByRole('tab', { name: '外观', exact: true }).click()
  await expect(right(page).locator('.el-color-picker')).toHaveCount(3)
  const size = right(page).getByRole('spinbutton').first()
  await size.fill('16')
  await size.press('Tab')
  await page.getByRole('button', { name: '保存草稿', exact: true }).click()
  expect((await state(page)).saved.styles[0].fontSizePt).toBe(16)
  await right(page).getByRole('tab', { name: '基础', exact: true }).click()
  await expect(right(page).locator('.el-color-picker')).toHaveCount(0)
})

test('English and dark theme retain the control palette and paper inspector', async ({ page }) => {
  await page.setViewportSize({ width: 1600, height: 1000 })
  await page.goto('/tests/fixtures/report-designer.html?english&dark')
  await expect(page.locator('.report-control-palette button')).toHaveCount(9)
  await expect(
    page.locator('.report-control-palette').getByRole('button', { name: 'Check box', exact: true }),
  ).toBeVisible()
  await page.getByRole('button', { name: 'Page settings', exact: true }).click()
  await expect(right(page).getByRole('button', { name: 'Page header', exact: true })).toBeVisible()
  await expect(right(page).locator('.report-selection-title')).toHaveText('Report page')
  await page.screenshot({ path: '../../.run/pf09-report-designer-dark-en.png', fullPage: true })
})

test('paper stays centered through zoom and panel changes, with dashboard toolbar grouping', async ({
  page,
}) => {
  await page.setViewportSize({ width: 1920, height: 1080 })
  await open(page)
  const centered = async () => {
    const viewport = (await page.locator('.studio-viewport').boundingBox())!
    const paper = (await page.locator('.report-paper').boundingBox())!
    return Math.abs(paper.x + paper.width / 2 - viewport.x - viewport.width / 2)
  }
  await expect.poll(centered).toBeLessThan(2)
  const header = page.locator('.app-page__actions')
  for (const name of ['返回', '撤销', '恢复', '预览', '保存草稿', '发布'])
    await expect(header.getByRole('button', { name, exact: true })).toBeVisible()
  const toolbar = page.locator('.report-canvas-toolbar')
  const buttons = await toolbar.getByRole('button').all()
  const ys = await Promise.all(buttons.map(async (button) => (await button.boundingBox())!.y))
  expect(Math.max(...ys) - Math.min(...ys)).toBeLessThan(2)
  await toolbar.getByRole('button', { name: '100%', exact: true }).click()
  await expect.poll(centered).toBeLessThan(2)
  await toolbar.getByRole('button', { name: '收起控件', exact: true }).click()
  await expect.poll(centered).toBeLessThan(2)
  await toolbar.getByRole('button', { name: '收起属性', exact: true }).click()
  await expect.poll(centered).toBeLessThan(2)
  await toolbar.getByRole('button', { name: '展开控件', exact: true }).click()
  await toolbar.getByRole('button', { name: '展开属性', exact: true }).click()
  await toolbar.getByRole('button', { name: '适应画布', exact: true }).click()
  await expect.poll(centered).toBeLessThan(2)
  await page.getByRole('button', { name: '保存草稿', exact: true }).click()
  expect((await state(page)).saved).toEqual((await state(page)).original)
})

test('design uses physical style and table widths, with the shared engine preview surface', async ({
  page,
}) => {
  await page.setViewportSize({ width: 1920, height: 1080 })
  await open(page)
  await expect(page.locator('.report-design-table th')).toHaveCount(8)
  const geometry = await page.locator('.report-paper').evaluate((paper) => ({
    width: getComputedStyle(paper).width,
    padding: getComputedStyle(paper).paddingLeft,
    fontSize: getComputedStyle(paper.querySelector('.report-control')!).fontSize,
    columnWidth: getComputedStyle(paper.querySelector('col')!).width,
  }))
  expect(geometry.width).toBe('630px')
  expect(geometry.padding).toBe('45px')
  expect(parseFloat(geometry.fontSize)).toBeCloseTo(((10 * 25.4) / 72) * 3, 2)
  expect(geometry.columnWidth).toBe('60px')
  await page.getByRole('button', { name: '预览', exact: true }).click()
  await expect(page.frameLocator('iframe').getByText('Engine page fixture')).toBeVisible()
  const dialog = (await page.getByRole('dialog').boundingBox())!
  expect(dialog.width).toBeGreaterThanOrEqual(1000)
  await page.keyboard.press('Escape')
  await page.goto('/tests/fixtures/report-designer.html?sample')
  await expect(page.locator('.report-control')).toHaveCount(9)
  await expect(page.locator('.report-control').first()).toHaveText('CMP批次生产汇总报表')
  const tableBottom = await page
    .locator('.report-design-table')
    .evaluate((table) => table.getBoundingClientRect().bottom)
  const footerTop = (await page.locator('.report-band').nth(2).boundingBox())!.y
  expect(tableBottom).toBeLessThanOrEqual(footerTop + 1)
  await page.screenshot({ path: '../../.run/pf09-report-layout-sample.png', fullPage: true })
})

test('rulers show physical units and all four margins without changing report coordinates', async ({
  page,
}) => {
  await page.setViewportSize({ width: 1920, height: 1080 })
  await open(page)
  const unit = page.getByRole('combobox', { name: '测量单位', exact: true })
  await expect(unit).toBeVisible()
  await expect(page.getByRole('img', { name: '报表标尺与上下左右页边距' })).toBeVisible()
  for (const side of ['top', 'right', 'bottom', 'left'])
    await expect(page.locator(`.report-margin-label[data-side="${side}"]`)).toContainText('15 mm')
  const before = await page.locator('.report-paper').boundingBox()
  for (const [label, value] of [
    ['厘米 (cm)', '1.5 cm'],
    ['英寸 (in)', '0.59 in'],
    ['像素 (96 dpi)', '56.69 px'],
    ['毫米 (mm)', '15 mm'],
  ]) {
    await page.locator('.report-ruler-unit-select .el-select__wrapper').click()
    await page.getByRole('option', { name: label!, exact: true }).click()
    await expect(page.locator('.report-margin-label[data-side="top"]')).toContainText(value!)
    expect(await page.locator('.report-paper').boundingBox()).toEqual(before)
  }
  await page.getByRole('button', { name: '保存草稿', exact: true }).click()
  expect((await state(page)).saved).toEqual((await state(page)).original)
  for (const [label, value, side] of [
    ['上边距 mm', '10', 'top'],
    ['右边距 mm', '20', 'right'],
    ['下边距 mm', '18', 'bottom'],
    ['左边距 mm', '25', 'left'],
  ]) {
    const input = right(page).locator('label').filter({ hasText: label! }).getByRole('spinbutton')
    await input.fill(value!)
    await input.press('Tab')
    await expect(page.locator(`.report-margin-label[data-side="${side}"]`)).toContainText(
      `${value} mm`,
    )
    await expect(page.locator(`rect[data-margin="${side}"]`)).toHaveAttribute(
      side === 'left' || side === 'right' ? 'width' : 'height',
      String(Number(value) * 3),
    )
  }
  await page.getByRole('button', { name: '100%', exact: true }).click()
  const alignment = await page.locator('.report-measured-canvas').evaluate((canvas) => {
    const paper = canvas.querySelector('.report-paper')!.getBoundingClientRect()
    const ruler = canvas.querySelector('.report-ruler-horizontal')!.getBoundingClientRect()
    return Math.abs(ruler.x - paper.x)
  })
  expect(alignment).toBeLessThan(3)
  await page.getByRole('button', { name: '适应画布', exact: true }).click()
  await page.screenshot({ path: '../../.run/pf09-report-rulers.png', fullPage: true })
})
