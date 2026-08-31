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

  it('keeps VXE private structure selectors and class checks out of the table component', () => {
    const component = readFileSync(
      resolve(process.cwd(), 'src/components/management/AppDataTable.vue'),
      'utf8',
    )
    const script = component.match(/<script setup[\s\S]*?<\/script>/)?.[0] ?? ''

    expect(script).not.toMatch(/\.vxe-table--header-wrapper|\.vxe-table-custom-wrapper/)
    expect(script).not.toMatch(/\.vxe-table--column\.fixed--hidden|\.vxe-header--row/)
    expect(script).not.toMatch(/body--wrapper|classList\.contains\(['"]body--wrapper['"]\)/)
  })

  it('exposes named table-structure operations from the adapter', () => {
    const adapter = readFileSync(
      resolve(process.cwd(), 'src/components/management/appDataTable/vxeDomAdapter.ts'),
      'utf8',
    )

    expect(adapter).toMatch(/findVxeHeaderTables/)
    expect(adapter).toMatch(/findVxeHeaderRow/)
    expect(adapter).toMatch(/findVxeActiveCustomPanel/)
    expect(adapter).toMatch(/markVxeDuplicateColumnsDecorative/)
  })
})
