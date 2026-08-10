/**
 * 全流程控制台验收(FE-010,§18.2):
 * 登录 → PC 首页 → PDA 首页 → Mobile 首页 → 我的 → 退出 全程
 * 不得出现 console error、page error 与未处理 Promise rejection;
 * 控制台日志不得出现 token / password / Authorization 等敏感凭据。
 */

import { expect, test, type Page } from '@playwright/test'

const VALID_USER = 'mock.admin'
const VALID_PASS = 'Mock@123456'

const SENSITIVE_PATTERNS = [/token/i, /password/i, /authorization/i, /Bearer/i]

async function fillLogin(page: Page): Promise<void> {
  await page.getByLabel('用户名').fill(VALID_USER)
  await page.getByLabel('密码', { exact: true }).fill(VALID_PASS)
}

async function login(page: Page): Promise<void> {
  await page.goto('/login')
  await fillLogin(page)
  await page.getByRole('button', { name: '登录' }).click()
  await expect(page).toHaveURL(/\/pc\/home/)
}

test('登录→三端→我的→退出全程无 console error / page error / 敏感日志', async ({ page }) => {
  const consoleErrors: string[] = []
  const consoleTexts: string[] = []
  const pageErrors: string[] = []

  page.on('console', (msg) => {
    consoleTexts.push(msg.text())
    if (msg.type() === 'error') consoleErrors.push(msg.text())
  })
  page.on('pageerror', (err) => pageErrors.push(err.message))

  // 完整关键路径:登录 → 三端 → 我的 → 退出
  await login(page)
  await page.goto('/pc/home')
  await expect(page.getByRole('heading', { name: '业务指标将在后续阶段接入' })).toBeVisible()
  await page.goto('/pda/home')
  await expect(page.getByRole('heading', { name: '现场任务将在业务阶段接入' })).toBeVisible()
  await page.goto('/mobile/home')
  await expect(page.getByRole('heading', { name: '业务功能将在后续阶段接入' })).toBeVisible()
  await page.goto('/mobile/my')
  await expect(page.getByRole('heading', { name: '我的' })).toBeVisible()
  await page.getByRole('button', { name: '退出登录' }).click()
  await expect(page).toHaveURL(/\/login/)

  // §18.2:不得出现 console error / page error(未处理 rejection 以 console error 呈现)
  expect(consoleErrors, `console error:\n${consoleErrors.join('\n')}`).toEqual([])
  expect(pageErrors, `page error:\n${pageErrors.join('\n')}`).toEqual([])

  // 敏感凭据不得进入日志(§19)
  for (const pattern of SENSITIVE_PATTERNS) {
    expect(consoleTexts.join('\n')).not.toMatch(pattern)
  }
})
