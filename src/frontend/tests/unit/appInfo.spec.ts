import { describe, expect, it } from 'vitest'

import { getAppInfo } from '@/app/appInfo'

describe('appInfo', () => {
  it('returns the stable application metadata', () => {
    const info = getAppInfo()
    expect(info.name).toBe('Industrial Platform')
    expect(info.version).toBe('0.1.0')
    expect(info.description.length).toBeGreaterThan(0)
  })

  it('keeps the metadata snapshot stable', () => {
    expect(getAppInfo()).toMatchObject({
      name: 'Industrial Platform',
      version: '0.1.0',
    })
  })
})
