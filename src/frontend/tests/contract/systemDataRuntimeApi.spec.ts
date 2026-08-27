import { http, HttpResponse } from 'msw'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'

import { createSystemDataRuntimeApi } from '@/api/systemData/runtimeApi'
import { createHttpClient } from '@/api/httpClient'

import { server } from '../fixtures/mswServer'

const BASE = 'http://localhost:5080'

describe('SystemData runtime HTTP contract', () => {
  beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
  afterEach(() => server.resetHandlers())
  afterAll(() => server.close())

  it('sends If-None-Match and exposes a 304 as not-modified', async () => {
    let requestEtag: string | null = null
    server.use(
      http.get(`${BASE}/systemdata/runtime/features`, ({ request }) => {
        requestEtag = request.headers.get('If-None-Match')
        return new HttpResponse(null, { status: 304, headers: { ETag: '"feature-v1"' } })
      }),
    )

    const api = createSystemDataRuntimeApi(createHttpClient({ baseUrl: BASE, timeoutMs: 1000 }))
    const result = await api.getFeatures('"feature-v1"')

    expect(requestEtag).toBe('"feature-v1"')
    expect(result).toEqual({ kind: 'not-modified', etag: '"feature-v1"' })
  })
})
