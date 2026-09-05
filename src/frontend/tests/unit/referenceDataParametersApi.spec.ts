import { describe, expect, it, vi } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'
import type { HttpClient } from '@/api/httpClient'
import { createReferenceDataApi } from '@/api/referenceData'
import { configurationKeyBody, configurationValueBody } from '@/api/referenceData/parameters'
import type { WriteConfigurationKey } from '@/api/referenceData/parameterTypes'
import { PERMISSIONS } from '@/permissions'
import { ROUTE_NAMES, routes } from '@/router/routes'

const request: WriteConfigurationKey = {
  nId: 'Amount',
  name: 'Amount',
  description: null,
  dataType: 'Decimal',
  valueMode: 'Single',
  valueJson: '999999999999999999.1234567890',
  defaultValueJson: null,
  isMandatory: false,
  isReadOnly: false,
  dictionaryNId: null,
  referenceTarget: null,
  status: 'Active',
  sort: 0,
  changeReason: 'Adjust amount',
  expectedAppDomainOptimisticVersion: 3,
  expectedAppDomainConcurrencyVersion: 'root-token',
}
describe('Parameter API contracts', () => {
  it('preserves exact Decimal and Int64 JSON tokens and explicit unconfigured values', () => {
    const body = configurationKeyBody(request)
    expect(body).toContain('"value":999999999999999999.1234567890')
    expect(body).toContain('"defaultValue":null')
    expect(body).not.toContain('valueJson')
    expect(
      configurationValueBody({
        ...request,
        valueJson: '9223372036854775807',
        isDefault: false,
        enabled: true,
      }),
    ).toContain('"value":9223372036854775807')
    expect(configurationKeyBody({ ...request, dataType: 'Boolean', valueJson: 'false' })).toContain(
      '"value":false',
    )
    expect(() => configurationKeyBody({ ...request, valueJson: '1,"isReadOnly":false' })).toThrow()
    expect(() => configurationKeyBody({ ...request, valueJson: 'null' })).toThrow()
  })

  it('sends raw JSON through the shared client with root versions and encoded paths', async () => {
    const client = {
      get: vi.fn().mockResolvedValue({}),
      post: vi.fn().mockResolvedValue({}),
      put: vi.fn().mockResolvedValue({}),
    } as unknown as HttpClient
    const api = createReferenceDataApi(client)
    await api.updateConfigurationKey('domain/1', 'key/2', request)
    expect(client.put).toHaveBeenCalledWith(
      '/referencedata/api/v1/reference-data/admin/configuration-domains/domain%2F1/keys/key%2F2',
      configurationKeyBody(request),
      { headers: { 'Content-Type': 'application/json' } },
    )
    const signal = new AbortController().signal
    await api.configurationHistory('domain/1', null, 2, 20, { signal })
    expect(client.get).toHaveBeenLastCalledWith(
      '/referencedata/api/v1/reference-data/admin/configuration-domains/domain%2F1/history?pageIndex=2&pageSize=20',
      { signal },
    )
    const version = {
      changeReason: 'Disable',
      expectedAppDomainOptimisticVersion: 3,
      expectedAppDomainConcurrencyVersion: 'root-token',
    }
    await api.setConfigurationKeyStatus('one', 'two', false, version)
    expect(client.post).toHaveBeenLastCalledWith(
      '/referencedata/api/v1/reference-data/admin/configuration-domains/one/keys/two/disable',
      version,
    )
  })

  it('registers the real parameter page behind its view permission', () => {
    expect(
      routes
        .find((route) => route.path === '/pc')
        ?.children?.find((route) => route.name === ROUTE_NAMES.referenceDataParameters),
    ).toMatchObject({
      path: 'system/reference-data/configurations',
      meta: {
        permission: PERMISSIONS.referenceDataParameterView,
        requiresAuth: true,
        workspace: 'business',
      },
    })
  })

  it('redirects the legacy parameter URL to the permission-protected canonical route', async () => {
    const router = createRouter({ history: createMemoryHistory(), routes })

    await router.push('/pc/system/reference-data/parameters')

    expect(router.currentRoute.value).toMatchObject({
      name: ROUTE_NAMES.referenceDataParameters,
      path: '/pc/system/reference-data/configurations',
      meta: {
        permission: PERMISSIONS.referenceDataParameterView,
        requiresAuth: true,
        workspace: 'business',
      },
    })
  })
})
