import { describe, expect, it, vi } from 'vitest'
import type { HttpClient } from '@/api/httpClient'
import { createCodingRuleApi } from '@/api/referenceData/codingRules'

function client() {
  return {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    delete: vi.fn(),
    getWithMeta: vi.fn(),
  } as unknown as HttpClient
}

describe('CodingRule API contract', () => {
  it('uses the admin list, detail, versioned mutation, and draft preview routes', async () => {
    const http = client()
    const api = createCodingRuleApi(http)
    const signal = new AbortController().signal
    const version = {
      expectedOptimisticVersion: 7,
      expectedConcurrencyVersion: 'token-7',
    }

    await api.listCodingRules(
      { pageIndex: 2, pageSize: 20, keyword: 'lot', scopeType: 'Tenant', status: 'Draft' },
      { signal },
    )
    await api.getCodingRule('rule/id', { signal })
    await api.createCodingRule({
      scopeType: 'Tenant',
      nId: 'LOT_RULE',
      name: 'Lot rule',
      targetEntityNId: 'LOT',
      template: 'L-{YYYY}-{SEQ:4}',
      resetPolicy: 'Yearly',
    })
    await api.updateCodingRule('rule/id', {
      name: 'Lot rule 2',
      targetEntityNId: 'LOT',
      template: 'L2-{SEQ:4}',
      resetPolicy: 'Never',
      ...version,
    })
    await api.cloneCodingRule('rule/id', version)
    await api.publishCodingRule('rule/id', version)
    await api.disableCodingRule('rule/id', { ...version, changeReason: 'obsolete' })
    await api.previewCodingRule('rule/id', { factoryId: null }, { signal })

    expect(http.get).toHaveBeenNthCalledWith(
      1,
      '/referencedata/api/v1/reference-data/admin/coding-rules?pageIndex=2&pageSize=20&keyword=lot&scopeType=Tenant&status=Draft',
      { signal },
    )
    expect(http.get).toHaveBeenNthCalledWith(
      2,
      '/referencedata/api/v1/reference-data/admin/coding-rules/rule%2Fid',
      { signal },
    )
    expect(http.put).toHaveBeenCalledWith(
      '/referencedata/api/v1/reference-data/admin/coding-rules/rule%2Fid',
      expect.objectContaining(version),
    )
    expect(http.post).toHaveBeenNthCalledWith(
      1,
      '/referencedata/api/v1/reference-data/admin/coding-rules',
      expect.objectContaining({ nId: 'LOT_RULE', template: 'L-{YYYY}-{SEQ:4}' }),
    )
    expect(http.post).toHaveBeenNthCalledWith(
      2,
      '/referencedata/api/v1/reference-data/admin/coding-rules/rule%2Fid/clone',
      version,
    )
    expect(http.post).toHaveBeenNthCalledWith(
      3,
      '/referencedata/api/v1/reference-data/admin/coding-rules/rule%2Fid/publish',
      version,
    )
    expect(http.post).toHaveBeenNthCalledWith(
      4,
      '/referencedata/api/v1/reference-data/admin/coding-rules/rule%2Fid/disable',
      { ...version, changeReason: 'obsolete' },
    )
    expect(http.post).toHaveBeenLastCalledWith(
      '/referencedata/api/v1/reference-data/admin/coding-rules/rule%2Fid/preview',
      { factoryId: null },
      { signal },
    )
  })

  it('sends explicit source and revision for runtime preview and preserves the idempotency key', async () => {
    const http = client()
    const api = createCodingRuleApi(http)
    const signal = new AbortController().signal
    const request = {
      sourceScope: 'Tenant' as const,
      sourceTenantNId: 'TENANT-A',
      ruleRevision: 3,
      factoryId: null,
    }

    await api.previewCode('LOT/RULE', request, { signal })
    await api.generateCode('LOT/RULE', request, 'idem-123', {
      signal,
      headers: { 'X-Correlation-Id': 'corr-1' },
    })

    expect(http.post).toHaveBeenNthCalledWith(
      1,
      '/referencedata/api/v1/reference-data/coding-rules/LOT%2FRULE/preview',
      request,
      { signal },
    )
    expect(http.post).toHaveBeenNthCalledWith(
      2,
      '/referencedata/api/v1/reference-data/coding-rules/LOT%2FRULE/generate',
      request,
      {
        signal,
        headers: { 'X-Correlation-Id': 'corr-1', 'Idempotency-Key': 'idem-123' },
      },
    )
  })
})
