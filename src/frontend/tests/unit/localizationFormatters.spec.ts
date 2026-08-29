import { describe, expect, it } from 'vitest'

import { formatDateTime, formatNumber } from '@/localization/formatters'

describe('localization formatters', () => {
  it('formats a timestamp with an explicit timezone instead of slicing ISO text', () => {
    const value = formatDateTime('2026-01-02T03:04:05.000Z', {
      locale: 'en-US',
      timeZone: 'America/Los_Angeles',
    })
    expect(value).toContain('01/01/2026')
    expect(value).toContain('7:04 PM')
  })

  it('formats numbers using the requested independent number locale', () => {
    expect(formatNumber(1234567.89, 'en-US')).toBe('1,234,567.89')
    expect(formatNumber(1234567.89, 'zh-CN')).toBe('1,234,567.89')
  })

  it('rejects an invalid timezone rather than silently changing it', () => {
    expect(() => formatDateTime('2026-01-02T03:04:05.000Z', {
      locale: 'en-US',
      timeZone: 'Not/AZone',
    })).toThrow(RangeError)
  })
})
