/**
 * 真实 Identity 集成 E2E 配置(PF-01 §14 TASK-PF01-007)。
 * 与 Mock 基线(playwright.config.ts)分开:webServer 以 VITE_AUTH_MODE=http 启动,
 * 前置后端(Gateway 5080 → Identity 5041 → 云端 PG/Redis)已运行且测试账号已种子。
 * 运行:`pnpm exec playwright test -c playwright.real.config.ts`
 *
 * 注意:CORS 仅允许 5173/4173,故复用 4173;运行前须确保 4173 无残留 dev server
 * (reuseExistingServer 若复用 Mock 模式服务器会导致断言失败)。
 */

import { defineConfig, devices } from '@playwright/test'

export default defineConfig({
  testDir: './tests/e2e',
  testMatch: ['**/real-login.spec.ts', '**/identity-pages.spec.ts'],
  // 共享真实后端与云端库,严格串行(workers=1)降低瞬时慢首呼/共享状态影响。
  // 并行登录同一账号会触发 Identity 登录成功更新的乐观并发冲突(ConcurrencyException→500),故必须串行。
  fullyParallel: false,
  retries: process.env.CI ? 1 : 1,
  timeout: 90_000,
  workers: 1,
  reporter: [['html', { open: 'never', outputFolder: 'playwright-report-real' }]],
  // 与 Mock 基线同一套像素回归目录与阈值(§17 视觉基线契约)。
  snapshotPathTemplate: '{testDir}/snapshots/{testFilePath}/{arg}{ext}',
  expect: {
    toHaveScreenshot: { maxDiffPixelRatio: 0.01 },
  },
  use: {
    baseURL: 'http://localhost:4173',
    trace: 'on-first-retry',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: {
    command: 'pnpm dev --port 4173 --strictPort',
    url: 'http://localhost:4173',
    reuseExistingServer: false,
    timeout: 60_000,
    env: {
      VITE_AUTH_MODE: 'http',
      VITE_API_BASE_URL: 'http://localhost:5080',
      // 云端 PG/Redis 经 Tailscale 中继路径 RTT 较高(数百 ms),拥塞峰值下登录可达数十秒。
      // 默认请求超时 10s 会误切断真实后端,故放宽到 60s(见 §14 已知限制)。
      VITE_REQUEST_TIMEOUT_MS: '60000',
    },
  },
})
