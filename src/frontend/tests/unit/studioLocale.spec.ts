import { readFileSync, readdirSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { expect, it } from 'vitest'
import { studioMessages } from '@/components/studio/shared/copy'
it('provides both locales for every literal Studio interface message', () => {
  const roots = ['../../src/components/studio/shared/', '../../src/pages/pc/studio/']
  const keys = new Set<string>()
  for (const root of roots) {
    const path = fileURLToPath(new URL(root, import.meta.url))
    for (const file of readdirSync(path).filter((name) => name.endsWith('.vue'))) {
      const source = readFileSync(path + '/' + file, 'utf8')
      for (const match of source.matchAll(/\bt(?:\.value)?\('([^']+)'\)/g)) keys.add(match[1]!)
    }
  }
  const missing: string[] = []
  for (const locale of ['zh-CN', 'en-US'] as const)
    for (const key of keys) if (!(key in studioMessages[locale])) missing.push(locale + ':' + key)
  expect(missing).toEqual([])
})
