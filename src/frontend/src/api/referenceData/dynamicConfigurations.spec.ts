import { describe, expect, it, vi } from 'vitest'
import {
  createDynamicConfigurationApi,
  dynamicConfigurationBody,
  dynamicRecordBody,
} from './dynamicConfigurations'
import type { HttpClient } from '@/api/httpClient'

const version = { expectedOptimisticVersion: 2, expectedConcurrencyVersion: 'token' }
describe('dynamic configuration API', () => {
  it('preserves decimal constraints, defaults, values and explicit null as raw JSON tokens', () => {
    expect(
      dynamicConfigurationBody({
        ...version,
        name: 'Schema',
        description: null,
        fields: [
          {
            nId: 'Amount',
            name: 'Amount',
            dataType: 'Decimal',
            required: false,
            enabled: true,
            sort: 0,
            defaultValueJson: '999999999999999999.1234567890',
            minLength: null,
            maxLength: null,
            minValueJson: '-999999999999999999.1234567890',
            maxValueJson: null,
            scale: 10,
            pattern: null,
            dictionaryNId: null,
            referenceTarget: null,
            description: null,
          },
        ],
      }),
    ).toContain('"defaultValue":999999999999999999.1234567890')
    expect(
      dynamicRecordBody({
        ...version,
        nId: 'One',
        name: null,
        category: null,
        sort: 0,
        enabled: true,
        valuesJson: { Amount: '999999999999999999.1234567890', Empty: '""', False: 'false' },
      }),
    ).toContain('"Amount":999999999999999999.1234567890,"Empty":"","False":false')
    expect(() =>
      dynamicRecordBody({
        ...version,
        nId: 'One',
        name: null,
        category: null,
        sort: 0,
        enabled: true,
        valuesJson: { Amount: 'null' },
      }),
    ).toThrow('REF-DYNAMIC-CONFIG-FIELD-INVALID')
  })

  it('uses the canonical management and fixed snapshot routes', async () => {
    const get = vi.fn().mockResolvedValue({ items: [], total: 0, pageIndex: 1, pageSize: 20 })
    const post = vi.fn().mockResolvedValue({})
    const api = createDynamicConfigurationApi({
      get,
      post,
      put: vi.fn(),
      delete: vi.fn(),
    } as unknown as HttpClient)
    await api.listDynamicRecords('root/id', { pageIndex: 1, pageSize: 20, nId: 'ONE' })
    await api.getDynamicSnapshotRecords(
      {
        definitionId: 'id',
        nId: 'ROOT/A',
        name: 'A',
        fields: [],
        sourceScope: 'Tenant',
        sourceTenantNId: 'TENANT A',
        revision: 7,
        publishedOn: '2026-09-05T00:00:00Z',
      },
      { pageIndex: 2, pageSize: 10 },
    )
    await api.disableDynamicRecord('root/id', 'record/id', version)
    expect(get.mock.calls[0]?.[0]).toBe(
      '/referencedata/api/v1/reference-data/admin/dynamic-properties/configurations/root%2Fid/records?pageIndex=1&pageSize=20&nId=ONE',
    )
    expect(get.mock.calls[1]?.[0]).toContain(
      'revision=7&sourceScope=Tenant&sourceTenantNId=TENANT+A',
    )
    expect(post.mock.calls[0]?.[0]).toContain('/root%2Fid/records/record%2Fid/disable')
  })
})
