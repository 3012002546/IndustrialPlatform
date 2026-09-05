import { tmpdir } from 'node:os'
import { join } from 'node:path'

import { defineConfig, devices } from '@playwright/test'

/** UnifiedHost 默认入口的真实 SystemData 管理 CRUD 验收配置。 */
export default defineConfig({
  testDir: './tests/e2e',
  testMatch: ['**/systemdata-real.spec.ts'],
  fullyParallel: false,
  workers: 1,
  timeout: 90_000,
  outputDir: join(tmpdir(), 'ip-pf02-playwright-results'),
  reporter: [
    ['html', { open: 'never', outputFolder: join(tmpdir(), 'ip-pf02-playwright-report') }],
  ],
  use: { baseURL: 'http://localhost:4173', trace: 'on-first-retry' },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: {
    command: 'pnpm dev --configLoader runner --port 4173 --strictPort',
    url: 'http://localhost:4173',
    reuseExistingServer: true,
    timeout: 60_000,
    env: {
      VITE_AUTH_MODE: 'http',
      VITE_API_BASE_URL: 'http://localhost:5041',
      VITE_REQUEST_TIMEOUT_MS: '60000',
      VITE_CACHE_DIR: join(tmpdir(), 'ip-pf02-vite-cache'),
    },
  },
})
