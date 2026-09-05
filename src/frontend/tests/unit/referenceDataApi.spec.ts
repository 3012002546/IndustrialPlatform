import { describe, expect, it, vi } from 'vitest'
import {
  createReferenceDataApi,
  getReferenceDataApi,
  registerReferenceDataApi,
} from '@/api/referenceData'
import type { HttpClient } from '@/api/httpClient'
import { pcNavigationGroups } from '@/components/navigation/navigation'
import { enUS } from '@/locales/en-US'
import { zhCN } from '@/locales/zh-CN'
import { PERMISSIONS } from '@/permissions'
import { ROUTE_NAMES, routes } from '@/router/routes'

describe('ReferenceData API and route contracts', () => {
  it('uses the shared client and preserves query, cancellation and concurrency fields', async () => {
    const client = {
      get: vi.fn().mockResolvedValue({}),
      post: vi.fn().mockResolvedValue({}),
      put: vi.fn().mockResolvedValue({}),
    } as unknown as HttpClient
    const api = createReferenceDataApi(client)
    expect(api).toMatchObject({
      listMetadataSchemas: expect.any(Function),
      listCodingRules: expect.any(Function),
      listStateMachines: expect.any(Function),
    })
    registerReferenceDataApi(api)
    expect(getReferenceDataApi()).toBe(api)
    const signal = new AbortController().signal
    await api.listDictionaries(
      { pageIndex: 2, pageSize: 25, keyword: 'A & B', status: '', scopeType: 'Tenant' },
      { signal },
    )
    expect(client.get).toHaveBeenCalledWith(
      '/referencedata/api/v1/reference-data/admin/dictionaries?pageIndex=2&pageSize=25&keyword=A+%26+B&scopeType=Tenant',
      { signal },
    )
    const version = {
      expectedOptimisticVersion: 4,
      expectedConcurrencyVersion: 'revision-token',
      changeReason: 'Retire',
    }
    await api.disableDictionary('id/one', version)
    expect(client.post).toHaveBeenCalledWith(
      '/referencedata/api/v1/reference-data/admin/dictionaries/id%2Fone/disable',
      version,
    )
    await api.checkDictionaryPublication('id', { signal })
    expect(client.get).toHaveBeenLastCalledWith(
      '/referencedata/api/v1/reference-data/admin/dictionaries/id/publication-check',
      { signal },
    )
  })

  it('registers the real dictionary page with its view permission and business workspace', () => {
    const route = routes
      .find((route) => route.path === '/pc')
      ?.children?.find((route) => route.name === ROUTE_NAMES.referenceDataDictionaries)
    expect(route).toMatchObject({
      path: 'system/reference-data/dictionaries',
      meta: {
        permission: PERMISSIONS.referenceDataDictionaryView,
        workspace: 'business',
        requiresAuth: true,
      },
    })
    expect(route?.component).toBeTruthy()
  })

  it('registers Metadata, CodingRule, and StateMachine routes, navigation, permissions, and shell copy', () => {
    const pcRoutes = routes.find((route) => route.path === '/pc')?.children ?? []
    const navigation = pcNavigationGroups.flatMap((group) => group.items)
    const cases = [
      {
        name: ROUTE_NAMES.referenceDataMetadata,
        path: 'system/reference-data/metadata',
        id: 'reference-data-metadata',
        permission: PERMISSIONS.referenceDataMetadataView,
        zh: '元数据 Schema',
        en: 'Metadata schemas',
      },
      {
        name: ROUTE_NAMES.referenceDataCodingRules,
        path: 'system/reference-data/coding-rules',
        id: 'reference-data-coding-rules',
        permission: PERMISSIONS.referenceDataCodingRuleView,
        zh: '编码规则',
        en: 'Coding rules',
      },
      {
        name: ROUTE_NAMES.referenceDataStateMachines,
        path: 'system/reference-data/state-machines',
        id: 'reference-data-state-machines',
        permission: PERMISSIONS.referenceDataStateMachineView,
        zh: '状态机定义',
        en: 'State machines',
      },
    ] as const

    for (const item of cases) {
      expect(pcRoutes.find((route) => route.name === item.name)).toMatchObject({
        path: item.path,
        meta: {
          permission: item.permission,
          workspace: 'business',
          requiresAuth: true,
        },
      })
      expect(navigation.find((entry) => entry.id === item.id)).toMatchObject({
        routeName: item.name,
        permission: item.permission,
      })
      expect(zhCN.shell.navigation.item[item.id]).toBe(item.zh)
      expect(enUS.shell.navigation.item[item.id]).toBe(item.en)
    }
  })
})
