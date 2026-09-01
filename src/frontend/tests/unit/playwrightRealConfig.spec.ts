import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

describe('playwright.real config', () => {
  it('uses UnifiedHost on 5041 as the default API endpoint', async () => {
    const source = readFileSync(resolve(process.cwd(), 'playwright.real.config.ts'), 'utf8')

    expect(source).toContain(
      "const realApiBaseUrl = process.env.PF03_REAL_API_BASE_URL ?? 'http://localhost:5041'",
    )
    expect(source).toContain('VITE_API_BASE_URL: realApiBaseUrl')
  })
})
