/* eslint-disable @typescript-eslint/no-explicit-any */
import { test, expect } from '@playwright/test'
const state = (page: import('@playwright/test').Page) =>
  page.evaluate(() => (window as any).reportFixture)
for (const [mode, query] of [
  ['single', ''],
  ['simple', '?bundle=Simple'],
  ['normal', '?bundle=ElectronicRecord'],
  ['driven', '?bundle=ElectronicRecord&driven'],
]) {
  test(
    mode + ' edits nested JSON and preserves the complete saved configuration',
    async ({ page }) => {
      await page.goto(
        '/tests/fixtures/report-designer.html' + query + (query ? '&' : '?') + 'json-data',
      )
      await page.getByRole('button', { name: '数据与参数', exact: true }).click()
      const dialog = page.getByRole('dialog')
      await expect(dialog.getByRole('tab', { name: '数据实例', exact: true })).toBeVisible()
      await expect(dialog.locator('.dashboard-structured-editor')).toHaveCount(0)
      const value = [
        {
          order: '测试工单',
          bom: [{ code: 'A', quantity: 0 }],
          operations: [{ basic: [{ name: '混配' }], records: [] }],
          passed: false,
          optional: null,
          extension: { text: '保留' },
        },
      ]
      const textarea = dialog.locator('textarea').first()
      await textarea.fill('{')
      await page.getByTestId('form-drawer-close').click()
      await expect(dialog.getByRole('alert')).toContainText('常量格式无效')
      await expect(dialog).toBeVisible()
      await expect(textarea).toHaveValue('{')
      await textarea.fill(JSON.stringify(value))
      await dialog.getByRole('button', { name: '校验并格式化', exact: true }).first().click()
      await expect(textarea).toHaveValue(JSON.stringify(value, null, 2))
      await dialog.getByRole('tab', { name: '参数', exact: true }).click()
      await dialog.getByRole('tab', { name: '数据实例', exact: true }).click()
      await expect(textarea).toHaveValue(JSON.stringify(value, null, 2))
      await page.getByTestId('form-drawer-close').click()
      await expect(dialog).toHaveCount(0)
      await page.getByRole('button', { name: '保存草稿', exact: true }).click()
      const result = await state(page),
        expected = structuredClone(result.original)
      expected.instances[0].value = value
      expect(result.saved).toEqual(expected)
      await page.getByRole('button', { name: '数据与参数', exact: true }).click()
      expect(
        JSON.parse(await page.getByRole('dialog').locator('textarea').first().inputValue()),
      ).toEqual(value)
      await page.getByRole('dialog').locator('textarea').first().fill('invalid')
      await page.getByRole('button', { name: '还原未应用修改', exact: true }).first().click()
      await page.getByTestId('form-drawer-close').click()
      await page.getByRole('button', { name: '保存草稿', exact: true }).click()
      expect((await state(page)).saved).toEqual(expected)
    },
  )
}
test('full photoresist snapshot stays in one text editor and round trips unchanged', async ({
  page,
}) => {
  await page.setViewportSize({ width: 1366, height: 900 })
  await page.goto('/tests/fixtures/report-designer.html?photoresist')
  await page.getByRole('button', { name: '数据与参数', exact: true }).click()
  const dialog = page.getByRole('dialog')
  await expect(dialog.locator('textarea')).toHaveCount(1)
  const original = (await state(page)).original
  expect(JSON.parse(await dialog.locator('textarea').inputValue())).toEqual(
    original.instances[0].value,
  )
  await expect(dialog.locator('.el-collapse-item__header').first()).not.toContainText('DEMO-R01')
  await dialog.getByRole('button', { name: '校验并格式化' }).click()
  await page.screenshot({ path: '../../.run/report-data-editor/photoresist-json.png' })
  await dialog.getByRole('tab', { name: '集合绑定', exact: true }).click()
  await expect(dialog.getByText('工单集合', { exact: true })).toBeVisible()
  await page.screenshot({ path: '../../.run/report-data-editor/collection-bindings.png' })
  await page.getByTestId('form-drawer-close').click()
  await page.getByRole('button', { name: '保存草稿', exact: true }).click()
  expect((await state(page)).saved).toEqual(original)
})
test('English dark mode data editor fits a narrow surface and preserves parameter types', async ({
  page,
}) => {
  await page.setViewportSize({ width: 900, height: 800 })
  await page.goto('/tests/fixtures/report-designer.html?english&dark')
  await page.getByRole('button', { name: 'Data and parameters', exact: true }).click()
  const dialog = page.getByRole('dialog')
  await expect(dialog.getByRole('button', { name: 'Validate & format' })).toBeVisible()
  await dialog.getByRole('tab', { name: 'Parameters', exact: true }).click()
  await dialog.getByRole('button', { name: 'Add parameter', exact: true }).click()
  await expect(dialog.getByRole('combobox', { name: 'Parameter type', exact: true })).toBeVisible()
  await page.screenshot({ path: '../../.run/report-data-editor/english-dark.png' })
  const size = await dialog.evaluate((el) => ({ width: el.clientWidth, scroll: el.scrollWidth }))
  expect(size.scroll).toBeLessThanOrEqual(size.width + 1)
})
