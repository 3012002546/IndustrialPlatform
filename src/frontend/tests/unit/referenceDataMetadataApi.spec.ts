import { describe, expect, it, vi } from 'vitest'
import type { HttpClient } from '@/api/httpClient'
import { createMetadataApi } from '@/api/referenceData/metadata'

function client() {
  return {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    patch: vi.fn(),
    delete: vi.fn(),
  } as unknown as HttpClient
}

describe('Metadata API contract', () => {
  it('uses admin, effective and explicit fixed revision routes with cancellation', async () => {
    const http = client()
    const api = createMetadataApi(http)
    const signal = new AbortController().signal

    await api.listMetadataSchemas(
      { pageIndex: 2, pageSize: 20, keyword: 'asset', scopeType: 'Tenant' },
      { signal },
    )
    await api.getMetadataSchema('schema/id', { signal })
    await api.getEffectiveMetadataSchema('Equipment/Main', { signal })
    await api.getMetadataSchemaRevision('Equipment/Main', 7, 'Tenant', 'TENANT-A', { signal })
    await api.getMetadataSchemaRevision('Equipment/Main', 3, 'Platform', null, { signal })

    expect(http.get).toHaveBeenNthCalledWith(
      1,
      '/referencedata/api/v1/reference-data/admin/metadata-schemas?pageIndex=2&pageSize=20&keyword=asset&scopeType=Tenant',
      { signal, preserveJsonNumberKeys: ['minValue', 'maxValue'] },
    )
    expect(http.get).toHaveBeenNthCalledWith(
      2,
      '/referencedata/api/v1/reference-data/admin/metadata-schemas/schema%2Fid',
      { signal, preserveJsonNumberKeys: ['minValue', 'maxValue'] },
    )
    expect(http.get).toHaveBeenNthCalledWith(
      3,
      '/referencedata/api/v1/reference-data/metadata-schemas/Equipment%2FMain',
      { signal, preserveJsonNumberKeys: ['minValue', 'maxValue'] },
    )
    expect(http.get).toHaveBeenNthCalledWith(
      4,
      '/referencedata/api/v1/reference-data/metadata-schemas/Equipment%2FMain/revisions/7?sourceScope=Tenant&sourceTenantNId=TENANT-A',
      { signal, preserveJsonNumberKeys: ['minValue', 'maxValue'] },
    )
    expect(http.get).toHaveBeenNthCalledWith(
      5,
      '/referencedata/api/v1/reference-data/metadata-schemas/Equipment%2FMain/revisions/3?sourceScope=Platform',
      { signal, preserveJsonNumberKeys: ['minValue', 'maxValue'] },
    )
  })

  it('preserves aggregate versions and fixed unit coordinates on writes', async () => {
    const http = client()
    const api = createMetadataApi(http)
    const attribute = {
      nId: 'WEIGHT',
      name: 'Weight',
      dataType: 'Decimal' as const,
      required: false,
      isArray: false,
      enabled: true,
      sort: 0,
      defaultValue: '0.000000000001',
      minLength: null,
      maxLength: null,
      minValue: '-0.000000000001',
      maxValue: '9999999999999999.123456789012',
      pattern: null,
      dictionaryNId: null,
      referenceTarget: null,
      precision: 28,
      scale: 12,
      unitDimensionNId: 'MASS',
      defaultUnitNId: 'KG',
      unitRevision: 4,
      unitSourceScope: 'Tenant' as const,
      unitSourceTenantNId: 'TENANT-A',
      description: null,
    }
    const create = {
      scopeType: 'Tenant' as const,
      nId: 'EQUIPMENT',
      name: 'Equipment',
      description: null,
      attributes: [attribute],
    }
    const version = { expectedOptimisticVersion: 8, expectedConcurrencyVersion: 'token-8' }

    await api.createMetadataSchema(create)
    await api.updateMetadataSchema('schema-1', { ...create, ...version })
    await api.cloneMetadataSchema('schema-1', version)
    await api.checkMetadataPublication('schema-1')
    await api.publishMetadataSchema('schema-1', version)
    await api.disableMetadataSchema('schema-1', { ...version, changeReason: 'obsolete' })

    expect(http.post).toHaveBeenNthCalledWith(
      1,
      '/referencedata/api/v1/reference-data/admin/metadata-schemas',
      expect.stringContaining('"minValue":-0.000000000001'),
      expect.objectContaining({
        headers: { 'Content-Type': 'application/json' },
        preserveJsonNumberKeys: ['minValue', 'maxValue'],
      }),
    )
    const createBody = vi.mocked(http.post).mock.calls[0]![1] as string
    expect(createBody).toContain('"maxValue":9999999999999999.123456789012')
    expect(createBody).not.toContain('"minValue":"-0.000000000001"')
    expect(createBody).toContain('"unitSourceScope":"Tenant"')
    expect(createBody).toContain('"unitSourceTenantNId":"TENANT-A"')
    expect(createBody).toContain('"unitRevision":4')
    expect(http.put).toHaveBeenCalledWith(
      '/referencedata/api/v1/reference-data/admin/metadata-schemas/schema-1',
      expect.stringContaining('"expectedConcurrencyVersion":"token-8"'),
      expect.objectContaining({
        preserveJsonNumberKeys: ['minValue', 'maxValue'],
      }),
    )
    expect(http.get).toHaveBeenCalledWith(
      '/referencedata/api/v1/reference-data/admin/metadata-schemas/schema-1/publication-check',
      undefined,
    )
  })
})
