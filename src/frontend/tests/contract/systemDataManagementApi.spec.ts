import { http, HttpResponse } from 'msw'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'

import { createSystemDataManagementApi } from '@/api/systemData/managementApi'
import { createHttpClient } from '@/api/httpClient'

import { server } from '../fixtures/mswServer'

const BASE = 'http://localhost:5080'

describe('SystemData management HTTP contract', () => {
  beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
  afterEach(() => server.resetHandlers())
  afterAll(() => server.close())

  it('uses the backend organization tree and position query contracts', async () => {
    const paths: string[] = []
    server.use(
      http.get(`${BASE}/systemdata/api/v1/organizations/tree`, ({ request }) => {
        paths.push(new URL(request.url).pathname)
        return HttpResponse.json({ success: true, code: '200', message: 'success', data: [] })
      }),
      http.get(`${BASE}/systemdata/api/v1/positions`, ({ request }) => {
        paths.push(new URL(request.url).search)
        return HttpResponse.json({
          success: true,
          code: '200',
          message: 'success',
          data: { items: [], total: 0, pageIndex: 1, pageSize: 20 },
        })
      }),
    )

    const api = createSystemDataManagementApi(createHttpClient({ baseUrl: BASE, timeoutMs: 1000 }))
    await api.listOrganizationsTree('Active')
    await api.listPositions({ organizationNId: 'org-a', pageIndex: 1, pageSize: 20 })

    expect(paths).toEqual([
      '/systemdata/api/v1/organizations/tree',
      '?organizationNId=org-a&pageIndex=1&pageSize=20',
    ])
  })

  it('uses the backend backup evidence field name', async () => {
    let requestBody: unknown
    server.use(
      http.post(
        `${BASE}/systemdata/api/v1/service-initialization/plans/plan-1/backup-evidence`,
        async ({ request }) => {
          requestBody = await request.json()
          return HttpResponse.json({
            success: true,
            code: '200',
            message: 'success',
            data: { evidenceNId: 'evidence-1' },
          })
        },
      ),
    )

    const api = createSystemDataManagementApi(createHttpClient({ baseUrl: BASE, timeoutMs: 1000 }))
    await api.createBackupEvidence('plan-1', {
      backupProvider: '管理员登记',
      backupReference: 'snapshot-1',
    })

    expect(requestBody).toEqual({
      backupProvider: '管理员登记',
      backupReference: 'snapshot-1',
    })
  })

  it('sends organization and position create fields required by the backend', async () => {
    const requests: unknown[] = []
    server.use(
      http.post(`${BASE}/systemdata/api/v1/organizations`, async ({ request }) => {
        requests.push(await request.json())
        return HttpResponse.json({ success: true, code: '200', message: 'success', data: {} })
      }),
      http.post(`${BASE}/systemdata/api/v1/positions`, async ({ request }) => {
        requests.push(await request.json())
        return HttpResponse.json({ success: true, code: '200', message: 'success', data: {} })
      }),
    )

    const api = createSystemDataManagementApi(createHttpClient({ baseUrl: BASE, timeoutMs: 1000 }))
    await api.createOrganization({
      nId: 'pf02-company',
      name: 'PF02 验收公司',
      type: 'Company',
      displayOrder: 0,
    })
    await api.createPosition({
      nId: 'pf02-position',
      organizationNId: 'pf02-company',
      name: 'PF02 管理岗位',
      description: 'contract test',
      displayOrder: 1,
    })

    expect(requests).toEqual([
      { nId: 'pf02-company', name: 'PF02 验收公司', type: 'Company', displayOrder: 0 },
      {
        nId: 'pf02-position',
        organizationNId: 'pf02-company',
        name: 'PF02 管理岗位',
        description: 'contract test',
        displayOrder: 1,
      },
    ])
  })
})
