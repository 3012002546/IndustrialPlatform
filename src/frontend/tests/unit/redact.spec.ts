import { describe, expect, it } from 'vitest'

import { redactHeaders, redactSensitive } from '@/api/redact'

describe('redactHeaders', () => {
  it('redacts credential headers and keeps the rest', () => {
    expect(
      redactHeaders({
        Authorization: 'Bearer secret',
        'X-Api-Key': 'key',
        Cookie: 'sid=1',
        'X-Request-Id': 'req-1',
      }),
    ).toEqual({
      Authorization: '[REDACTED]',
      'X-Api-Key': '[REDACTED]',
      Cookie: '[REDACTED]',
      'X-Request-Id': 'req-1',
    })
  })
})

describe('redactSensitive', () => {
  it('redacts nested sensitive values recursively', () => {
    const out = redactSensitive({
      user: 'alice',
      password: 'pw',
      refreshToken: 'rt',
      roles: ['admin'],
      meta: { accessToken: 'at' },
    }) as Record<string, unknown>
    expect(out.password).toBe('[REDACTED]')
    expect(out.refreshToken).toBe('[REDACTED]')
    expect((out.meta as Record<string, unknown>).accessToken).toBe('[REDACTED]')
    expect(out.user).toBe('alice')
    expect(out.roles).toEqual(['admin'])
  })

  it('redacts sensitive values inside arrays', () => {
    const out = redactSensitive([{ password: 'pw' }, 'ok']) as unknown[]
    expect(out[0]).toEqual({ password: '[REDACTED]' })
    expect(out[1]).toBe('ok')
  })

  it('leaves primitives untouched', () => {
    expect(redactSensitive(42)).toBe(42)
    expect(redactSensitive('plain')).toBe('plain')
    expect(redactSensitive(null)).toBeNull()
    expect(redactSensitive(undefined)).toBeUndefined()
  })
})
