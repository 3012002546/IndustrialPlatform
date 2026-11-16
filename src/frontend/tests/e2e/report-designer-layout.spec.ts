import { test, expect } from '@playwright/test'
import { writeFileSync } from 'node:fs'
test('report canvas fits its actual workspace at different window sizes', async ({ page }) => {
  const results = []
  for (const mode of [
    '',
    '?bundle=Simple',
    '?bundle=ElectronicRecord',
    '?bundle=ElectronicRecord&driven',
  ])
    for (const [width, height] of [
      [1920, 1080],
      [1280, 720],
      [1024, 768],
    ]) {
      await page.setViewportSize({ width: width!, height: height! })
      await page.goto('/tests/fixtures/report-designer.html' + mode)
      await expect(page.locator('.studio-editor')).toBeVisible()
      await page.addStyleTag({
        content:
          'html,body {margin:0;height:100%;overflow:hidden} #app {display:flex; flex-direction:column; height:calc(100dvh - 120px); margin-top:120px; padding:16px; margin-left:280px; width:calc(100% - 280px); box-sizing:border-box; overflow:auto} #app > * {flex:1 1 auto;min-width:0} ',
      })
      const size = await page.evaluate(() => {
        const host = document.querySelector('#app')!,
          editor = document.querySelector('.studio-editor')!
        return {
          width: innerWidth,
          height: innerHeight,
          client: host.clientHeight,
          scroll: host.scrollHeight,
          editorBottom: editor.getBoundingClientRect().bottom,
          viewportBottom: innerHeight,
          canvasRight: document.querySelector('.studio-viewport')!.getBoundingClientRect().right,
          propertyLeft: document
            .querySelector('.studio-sidebar:last-child')!
            .getBoundingClientRect().left,
        }
      })
      results.push({ ...size, mode })
    }
  writeFileSync('../../.run/designer-layout/layout-measure.json', JSON.stringify(results, null, 2))
  for (const size of results) {
    expect(size.scroll).toBeLessThanOrEqual(size.client + 1)
    expect(size.editorBottom).toBeLessThanOrEqual(size.viewportBottom)
    if (size.width > 1024) expect(size.canvasRight).toBeLessThanOrEqual(size.propertyLeft + 1)
  }
})

test('draft and reports start together; datasets load only when their editor is opened', async ({
  page,
}) => {
  await page.goto(
    '/tests/fixtures/report-designer.html?bundle=ElectronicRecord&driven&slow-draft&late-child',
  )
  await expect(page.locator('.report-bundle-flow')).toContainText('工序检验记录')
  const calls = () =>
    page.evaluate(
      () =>
        (window as unknown as { reportFixture: { loadEvents: string[] } }).reportFixture.loadEvents,
    )
  const initial = await calls()
  expect(initial.indexOf('list:Report')).toBeLessThan(initial.indexOf('draft-end'))
  expect(initial).not.toContain('list:Dataset')
  expect(initial).toContain('report-page:2')
  await page.getByRole('button', { name: '数据与参数', exact: true }).click()
  await expect.poll(calls).toContain('list:Dataset')
  await page.keyboard.press('Escape')
  await page.getByRole('button', { name: '数据与参数', exact: true }).click()
  expect((await calls()).filter((x) => x === 'list:Dataset')).toHaveLength(1)
})

test('long bundle scrolls its unscaled canvas without moving the workspace header', async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await page.goto(
    '/tests/fixtures/report-designer.html?bundle=ElectronicRecord&driven&many-templates',
  )
  const viewport = page.locator('.studio-viewport')
  await expect(viewport.locator('button')).toHaveCount(100)
  await expect(viewport.locator('.studio-canvas')).toHaveCount(0)
  const header = await page.locator('.app-page__header').boundingBox()
  await viewport
    .getByRole('button', { name: '工序模板 100 ·', exact: false })
    .scrollIntoViewIfNeeded()
  await viewport.getByRole('button', { name: '工序模板 100 ·', exact: false }).click()
  expect(await viewport.evaluate((el) => el.scrollTop)).toBeGreaterThan(0)
  expect(await page.locator('.app-page__header').boundingBox()).toEqual(header)
  await expect(page.getByRole('tab', { name: '基础', exact: true })).toBeVisible()
  await page.getByRole('button', { name: '保存草稿', exact: true }).click()
  const unchanged = await page.evaluate(() => {
    const f = (window as unknown as { reportFixture: { saved: unknown; original: unknown } })
      .reportFixture
    return JSON.stringify(f.saved) === JSON.stringify(f.original)
  })
  expect(unchanged).toBe(true)
  await page.screenshot({ path: '../../.run/designer-layout/long-bundle.png' })
})
