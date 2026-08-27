import type { HttpClient } from '@/api/httpClient'

import type {
  FeatureRuntimeDto,
  NavigationRuntimeDto,
  RuntimeSnapshotResult,
  ThemePolicyDto,
} from './types'

const SYSTEM_DATA_RUNTIME_PREFIX = '/systemdata/runtime'

export interface SystemDataRuntimeApi {
  getNavigation(
    terminal: 'Pc' | 'Pda' | 'Mobile',
    etag?: string,
  ): Promise<RuntimeSnapshotResult<NavigationRuntimeDto>>
  getFeatures(etag?: string): Promise<RuntimeSnapshotResult<FeatureRuntimeDto>>
  getThemePolicy(etag?: string): Promise<RuntimeSnapshotResult<ThemePolicyDto>>
}

function etagOf(headers: Record<string, string | undefined>): string | undefined {
  return headers['etag']
}

async function getSnapshot<T>(
  client: HttpClient,
  path: string,
  etag?: string,
): Promise<RuntimeSnapshotResult<T>> {
  const response = await client.getWithMeta<T>(path, {
    ...(etag === undefined ? {} : { headers: { 'If-None-Match': etag } }),
  })
  const responseEtag = etagOf(response.headers) ?? etag
  if (response.status === 304) return { kind: 'not-modified', etag: responseEtag }
  return { kind: 'updated', data: response.data, etag: responseEtag }
}

export function createSystemDataRuntimeApi(client: HttpClient): SystemDataRuntimeApi {
  return {
    getNavigation: (terminal, etag) =>
      getSnapshot<NavigationRuntimeDto>(
        client,
        `${SYSTEM_DATA_RUNTIME_PREFIX}/navigation?terminal=${terminal}`,
        etag,
      ),
    getFeatures: (etag) =>
      getSnapshot<FeatureRuntimeDto>(client, `${SYSTEM_DATA_RUNTIME_PREFIX}/features`, etag),
    getThemePolicy: (etag) =>
      getSnapshot<ThemePolicyDto>(client, `${SYSTEM_DATA_RUNTIME_PREFIX}/theme-policy`, etag),
  }
}
