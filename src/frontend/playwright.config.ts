import { defineConfig, devices } from '@playwright/test'

export default defineConfig({
  testDir: './tests/e2e',
  // 真实登录/Identity 页面联合验收(PF-01 §14)需要 authMode=http + 真实后端,
  // 由 playwright.real.config.ts 单独运行,不参与 Mock 基线与 CI 全量。
  testIgnore: [
    '**/real-login.spec.ts',
    '**/identity-pages.spec.ts',
    '**/user-management-golden.spec.ts',
    '**/systemdata-admin-visual.spec.ts',
  ],
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  // 本地 E2E 走 Vite dev server(非生产),冷编译 + 多 worker 并发全页加载会触发
  // 瞬时超时,故本地也保留 1 次重试、并限制 worker 数与测试超时(CI 更严:workers 1/重试 2)。
  retries: process.env.CI ? 2 : 1,
  timeout: 60_000,
  ...(process.env.CI ? { workers: 1 } : { workers: 3 }),
  reporter: [['html', { open: 'never' }]],
  // 像素回归基线统一落在 tests/e2e/snapshots/ 下;阈值 1% 全局生效,不得逐用例放宽(§12.3)。
  snapshotPathTemplate: '{testDir}/snapshots/{testFilePath}/{arg}{ext}',
  expect: {
    toHaveScreenshot: { maxDiffPixelRatio: 0.01 },
  },
  use: {
    baseURL: 'http://localhost:4173',
    trace: 'on-first-retry',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  // Mock 基线仅用于测试;生产构建禁止 mock(loadRuntimeConfig 抛错),故 E2E 跑 Vite dev
  // server(非生产模式),并显式声明 VITE_AUTH_MODE=mock(产品默认已是 http,不得依赖默认值)。
  webServer: {
    command: 'pnpm dev --port 4173 --strictPort --configLoader runner',
    url: 'http://localhost:4173',
    reuseExistingServer: !process.env.CI,
    timeout: 60_000,
    env: {
      VITE_AUTH_MODE: 'mock',
    },
  },
})
