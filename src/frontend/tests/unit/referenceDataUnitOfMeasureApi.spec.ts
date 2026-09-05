import { describe, expect, it, vi } from 'vitest'
import type { HttpClient } from '@/api/httpClient'
import { createUnitOfMeasureApi } from '@/api/referenceData/unitOfMeasure'
import { pcNavigationGroups } from '@/components/navigation/navigation'
import { PERMISSIONS } from '@/permissions'
import { ROUTE_NAMES, routes } from '@/router/routes'

function client() {
  return {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    patch: vi.fn(),
    delete: vi.fn(),
  } as unknown as HttpClient
}

describe('UnitOfMeasure API contract', () => {
  it('registers the guarded PC route and navigation entry', () => {
    const route = routes
      .flatMap((item) => item.children ?? [])
      .find((item) => item.name === ROUTE_NAMES.referenceDataUnitsOfMeasure)
    const navigation = pcNavigationGroups
      .flatMap((group) => group.items)
      .find((item) => item.id === 'reference-data-units-of-measure')

    expect(route).toMatchObject({
      path: 'system/reference-data/units-of-measure',
      meta: { permission: PERMISSIONS.referenceDataUnitOfMeasureView },
    })
    expect(navigation).toMatchObject({
      routeName: ROUTE_NAMES.referenceDataUnitsOfMeasure,
      permission: PERMISSIONS.referenceDataUnitOfMeasureView,
    })
  })

  it('uses management and explicit-source runtime routes', async () => {
    const http = client()
    const api = createUnitOfMeasureApi(http)
    const signal = new AbortController().signal

    await api.listUnitDimensions(
      { pageIndex: 2, pageSize: 20, keyword: 'mass', scopeType: 'Tenant' },
      { signal },
    )
    await api.getUnitDimension('dimension/id', { signal })
    await api.listAvailableUnitDimensions(
      { pageIndex: 1, pageSize: 100, keyword: 'mass' },
      { signal },
    )
    await api.getCurrentUnitDimension('MASS/NET', 'Tenant', 'TENANT-A', { signal })
    await api.getUnitDimensionRevision('MASS/NET', 7, 'Platform', null, { signal })

    expect(http.get).toHaveBeenNthCalledWith(
      1,
      '/referencedata/api/v1/reference-data/admin/units-of-measure/dimensions?pageIndex=2&pageSize=20&keyword=mass&scopeType=Tenant',
      { signal },
    )
    expect(http.get).toHaveBeenNthCalledWith(
      2,
      '/referencedata/api/v1/reference-data/admin/units-of-measure/dimensions/dimension%2Fid',
      { signal },
    )
    expect(http.get).toHaveBeenNthCalledWith(
      3,
      '/referencedata/api/v1/reference-data/units-of-measure/dimensions?pageIndex=1&pageSize=100&keyword=mass',
      { signal },
    )
    expect(http.get).toHaveBeenNthCalledWith(
      4,
      '/referencedata/api/v1/reference-data/units-of-measure/dimensions/MASS%2FNET?sourceScope=Tenant&sourceTenantNId=TENANT-A',
      { signal },
    )
    expect(http.get).toHaveBeenNthCalledWith(
      5,
      '/referencedata/api/v1/reference-data/units-of-measure/dimensions/MASS%2FNET/revisions/7?sourceScope=Platform',
      { signal },
    )
  })

  it('preserves decimal strings and both optimistic versions on writes and conversion', async () => {
    const http = client()
    const api = createUnitOfMeasureApi(http)
    const unit = {
      nId: 'mg',
      name: 'Milligram',
      symbol: 'mg',
      factorToBase: '0.000001000000',
      offsetToBase: '0',
      decimalPlaces: 12,
      roundingMode: 'ToEven' as const,
      enabled: true,
      sort: 1,
    }
    const create = {
      scopeType: 'Tenant' as const,
      scopeId: null,
      nId: 'MASS_PRECISE',
      name: 'Mass precise',
      description: null,
      conversionKind: 'Ratio' as const,
      baseUnitNId: 'kg',
      units: [unit],
    }
    const version = { expectedOptimisticVersion: 8, expectedConcurrencyVersion: 'token-8' }

    await api.createUnitDimension(create)
    await api.updateUnitDimension('dimension-1', { ...create, ...version })
    await api.cloneUnitDimension('dimension-1', version)
    await api.publishUnitDimension('dimension-1', version)
    await api.disableUnitDimension('dimension-1', { ...version, changeReason: 'obsolete' })
    await api.convertUnit({
      sourceScope: 'Tenant',
      sourceTenantNId: 'TENANT-A',
      unitDimensionNId: 'MASS_PRECISE',
      unitRevision: 7,
      fromUnitNId: 'mg',
      toUnitNId: 'kg',
      value: '9999999999999999.000000000001',
    })

    expect(http.post).toHaveBeenNthCalledWith(
      1,
      '/referencedata/api/v1/reference-data/admin/units-of-measure/dimensions',
      expect.objectContaining({
        units: [expect.objectContaining({ factorToBase: '0.000001000000' })],
      }),
    )
    expect(http.put).toHaveBeenCalledWith(
      '/referencedata/api/v1/reference-data/admin/units-of-measure/dimensions/dimension-1',
      expect.objectContaining(version),
    )
    expect(http.post).toHaveBeenNthCalledWith(
      5,
      '/referencedata/api/v1/reference-data/units-of-measure/convert',
      expect.objectContaining({ value: '9999999999999999.000000000001', unitRevision: 7 }),
      undefined,
    )
  })
})
