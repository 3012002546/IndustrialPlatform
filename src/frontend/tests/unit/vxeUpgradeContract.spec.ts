import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

describe('VXE upgrade boundary', () => {
  it('keeps private VXE DOM traversal in the adapter module', () => {
    const component = readFileSync(
      resolve(process.cwd(), 'src/components/management/AppDataTable.vue'),
      'utf8',
    )
    const adapter = readFileSync(
      resolve(process.cwd(), 'src/components/management/appDataTable/vxeDomAdapter.ts'),
      'utf8',
    )

    expect(component).not.toMatch(/\.querySelector(?:All)?\s*\(/)
    expect(component).not.toMatch(/\.closest\s*\(/)
    expect(adapter).toMatch(/querySelector/)
    expect(adapter).toMatch(/closest/)
  })
})
