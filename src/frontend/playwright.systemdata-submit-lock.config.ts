import { tmpdir } from 'node:os'
import { join } from 'node:path'

import { defineConfig, devices } from '@playwright/test'

export default defineConfig({
  testDir: './tests/e2e',
  testMatch: '**/systemdata-submit-lock-network.spec.ts',
  fullyParallel: false,
  forbidOnly: true,
  retries: 0,
  workers: 1,
  timeout: 90_000,
  reporter: 'list',
  outputDir: join(tmpdir(), 'pf02-systemdata-submit-lock-playwright'),
  use: {
    baseURL: 'http://127.0.0.1:4187',
    trace: 'retain-on-failure',
    viewport: { width: 2048, height: 1090 },
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'], viewport: { width: 2048, height: 1090 } },
    },
  ],
  webServer: {
    command: 'node tests/visual-systemdata/server.mjs --port 4187',
    url: 'http://127.0.0.1:4187/__systemdata_fixture__/status',
    reuseExistingServer: false,
    timeout: 60_000,
  },
})
