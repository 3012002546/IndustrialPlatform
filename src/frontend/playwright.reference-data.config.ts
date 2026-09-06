import { defineConfig, devices } from '@playwright/test'

// Browser interaction/visual fixtures only. These results do not prove live Gateway or database integration.
export default defineConfig({
  testDir: './tests/e2e/referenceData',
  testMatch: '**/*.fixture.ts',
  workers: 1,
  retries: 0,
  timeout: 60_000,
  reporter: 'list',
  outputDir: './test-results/reference-data',
  use: {
    baseURL: process.env.PF03_UI_BASE_URL ?? 'http://127.0.0.1:4273',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  ...(process.env.PF03_UI_EXTERNAL_SERVER === 'true'
    ? {}
    : {
        webServer: {
          command:
            'node node_modules/vite/bin/vite.js --configLoader runner --host 127.0.0.1 --port 4273 --strictPort --logLevel error',
          url: 'http://127.0.0.1:4273',
          reuseExistingServer: false,
          timeout: 60_000,
          env: {
            VITE_AUTH_MODE: 'http',
            VITE_API_BASE_URL: 'http://127.0.0.1:4273',
            VITE_CACHE_DIR: process.env.PF03_UI_CACHE_DIR ?? 'node_modules/.vite',
          },
        },
      }),
})
