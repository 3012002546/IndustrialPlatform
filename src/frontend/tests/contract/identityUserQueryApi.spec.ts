import { describe, expect, it, vi } from 'vitest'

import type { HttpClient } from '@/api/httpClient'
import { createIdentityManagementApi } from '@/api/identity/management/managementApi'

describe('Identity Users OData API contract', () => {
  it('serializes the shared descriptor into the controlled users endpoint', async () => {
    const get = vi.fn(async (path: string) => ({ items: [], total: 0, pageIndex: 1, pageSize: 20, path }))
    const client = { get } as unknown as HttpClient
    const api = createIdentityManagementApi(client)

    await api.listUsersOData({
      filters: [{ field: 'loginName', operator: 'contains', value: 'alice' }],
      orderBy: [{ field: 'loginName', direction: 'asc' }],
      select: ['loginName', 'name'],
      pageIndex: 1,
      pageSize: 20,
    })

    const path = String(get.mock.calls[0]?.[0])
    expect(path).toContain('/identity/api/v1/odata/users?')
    expect(path).toContain('%24top=20')
    expect(path).toContain('%24filter=contains%28loginName%2C%27alice%27%29')
    expect(path).toContain('%24orderby=loginName+asc%2CuserNId+asc')
  })
})
