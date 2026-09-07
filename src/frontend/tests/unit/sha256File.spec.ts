import { describe, expect, it } from 'vitest'

import { sampleFingerprint, sha256Bytes } from '@/utils/sha256File'

describe('file integrity helpers', () => {
  it('computes the standard SHA-256 for bytes', () => {
    expect(sha256Bytes(new TextEncoder().encode('abc'))).toBe('ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad')
  })

  it('uses a stable sample fingerprint for discovery and changes when content changes', async () => {
    const first = new File(['industrial-platform'], 'a.txt', { type: 'text/plain' })
    const same = new File(['industrial-platform'], 'copy.txt', { type: 'text/plain' })
    const changed = new File(['industrial-platform!'], 'a.txt', { type: 'text/plain' })

    const fingerprint = await sampleFingerprint(first)
    expect(fingerprint).toMatch(/^sample-v1:[0-9a-f]{64}$/)
    await expect(sampleFingerprint(same)).resolves.toBe(fingerprint)
    await expect(sampleFingerprint(changed)).resolves.not.toBe(fingerprint)
  })
})
