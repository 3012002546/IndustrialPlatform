/**
 * 六类目标视口统一验收(FE-010,§20.2「三端」):
 * 登录后显式访问各终端首页,断言关键内容可见、无横向滚动、无关键裁切,
 * 并保存全部六张目标视口截图(与各终端任务 E2E 使用同一批规范文件名)。
 * 另验证 Mobile safe-area Token 消费:注入 --ip-safe-area-bottom 覆盖后,
 * 底部导航 padding-bottom 必须随之变化(证明安全区适配接线)。
 * Playwright 经 Vite dev server 提供应用(同 pc/pda/mobile.spec.ts)。
 */

import { expect, test, type Page } from '@playwright/test'

const VALID_USER = 'mock.admin'
const VALID_PASS = 'Mock@123456'

async function fillLogin(page: Page): Promise<void> {
  await page.getByLabel('用户名').fill(VALID_USER)
  // 密码字段标签与「显示密码」切换按钮 aria-label 子串冲突,需精确匹配输入框
  await page.getByLabel('密码', { exact: true }).fill(VALID_PASS)
}

async function login(page: Page): Promise<void> {
  await page.goto('/login')
  await fillLogin(page)
  await page.getByRole('button', { name: '登录' }).click()
  await expect(page).toHaveURL(/\/pc\/home/)
}

/** 每终端首页的关键内容锚点(heading),用于断言无关键遮挡/裁切。 */
const VIEWPORTS = [
  { name: 'pc', size: [1280, 720], path: '/pc/home', heading: '快速开始' },
  { name: 'pc', size: [1440, 900], path: '/pc/home', heading: '快速开始' },
  { name: 'pda', size: [480, 800], path: '/pda/home', heading: '现场任务将在业务阶段接入' },
  { name: 'pda', size: [800, 480], path: '/pda/home', heading: '现场任务将在业务阶段接入' },
  { name: 'mobile', size: [360, 800], path: '/mobile/home', heading: '业务功能将在后续阶段接入' },
  { name: 'mobile', size: [390, 844], path: '/mobile/home', heading: '业务功能将在后续阶段接入' },
] as const

for (const { name, size, path, heading } of VIEWPORTS) {
  test(`${name} ${size[0]}×${size[1]} 视口:关键内容可见、无横向滚动、像素回归并保存截图`, async ({
    page,
  }) => {
    await login(page)
    await page.setViewportSize({ width: size[0], height: size[1] })
    await page.goto(path)
    // 关键内容(空状态标题)可见 → 无关键遮挡/裁切
    await expect(page.getByRole('heading', { name: heading })).toBeVisible()
    // 无非预期横向滚动(§20.2 三端)
    expect(
      await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth),
    ).toBe(true)
    // 像素回归基线(默认主题,阈值 1% 全局生效);PC 1440×900 仅在本处覆盖
    await expect(page).toHaveScreenshot(`${name}-home-${size[0]}x${size[1]}.png`, {
      fullPage: true,
    })
    // 证据截图(与历史规范文件名一致,供文档引用)
    await page.screenshot({
      path: `tests/e2e/screenshots/${name}-home-${size[0]}x${size[1]}.png`,
      fullPage: true,
    })
  })
}

test('Mobile safe-area Token 消费:底部导航 padding-bottom 随 --ip-safe-area-bottom 变化', async ({
  page,
}) => {
  await login(page)
  await page.setViewportSize({ width: 360, height: 800 })
  await page.goto('/mobile/home')
  await expect(page.getByRole('heading', { name: '业务功能将在后续阶段接入' })).toBeVisible()

  const before = await page
    .locator('nav.ip-mobile-nav')
    .evaluate((el) => getComputedStyle(el).paddingBottom)
  expect(before).toBe('0px') // 非设备模拟下 env(safe-area-inset-bottom)=0,兜底 0px

  // 注入 safe-area 覆盖,证明布局消费 Token(而不是魔法值)
  await page.addStyleTag({ content: ':root { --ip-safe-area-bottom: 34px; }' })
  const after = await page
    .locator('nav.ip-mobile-nav')
    .evaluate((el) => getComputedStyle(el).paddingBottom)
  expect(after).toBe('34px')
})
