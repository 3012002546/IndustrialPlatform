import { mergeConfig, defineConfig } from 'vitest/config'

import viteConfig from './vite.config.ts'

export default mergeConfig(
  viteConfig,
  defineConfig({
    test: {
      include: ['tests/unit/**/*.spec.ts', 'tests/components/**/*.spec.ts', 'tests/contract/**/*.spec.ts', 'src/**/*.spec.ts'],
      environment: 'jsdom',
      restoreMocks: true,
      coverage: {
        provider: 'v8',
        include: ['src/**'],
        exclude: ['src/main.ts', 'src/vite-env.d.ts'],
        reporter: ['text', 'html'],
        thresholds: {
          statements: 70,
          branches: 70,
          functions: 70,
          lines: 70,
        },
      },
    },
  }),
)
