import { expect, test } from '@playwright/test'

test('navigation default import sends one request while the Teleport footer is busy', async ({
  page,
}) => {
  await page.goto(
    '/__systemdata_fixture__?path=%2Fpc%2Fsystemdata%2Fnavigation&state=normal&navigationScenario=normal',
  )
  await expect(page).toHaveURL(/\/pc\/systemdata\/navigation$/)
  await expect(page.getByTestId('systemdata-navigation-defaults')).toBeVisible()

  await page.getByTestId('systemdata-navigation-defaults').click()
  await expect(page.getByTestId('systemdata-navigation-defaults-preview')).toBeVisible()

  let requestCount = 0
  let releaseRequest!: () => void
  const requestGate = new Promise<void>((resolve) => {
    releaseRequest = resolve
  })
  await page.route('**/systemdata/api/v1/navigation/defaults/import', async (route) => {
    requestCount += 1
    await requestGate
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        success: true,
        code: 'OK',
        message: 'OK',
        data: { draftRevision: 13, items: [] },
        traceId: 'trace-pf02-submit-lock',
      }),
    })
  })

  const confirm = page.getByTestId('systemdata-navigation-defaults-confirm')
  await expect(confirm).toBeEnabled()
  await confirm.evaluate((element) => {
    const button = element as HTMLButtonElement
    button.click()
    button.click()
    button.click()
  })

  await expect.poll(() => requestCount).toBe(1)
  await expect(confirm).toBeDisabled()
  releaseRequest()
  await expect(page.getByTestId('systemdata-navigation-defaults-preview')).toHaveCount(0)
  await page.unroute('**/systemdata/api/v1/navigation/defaults/import')
})
