import { describe, expect, it, vi } from 'vitest'
import type { HttpClient } from '@/api/httpClient'
import { createStateMachineApi } from '@/api/referenceData/stateMachines'

function client() {
  return {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    delete: vi.fn(),
    getWithMeta: vi.fn(),
  } as unknown as HttpClient
}

describe('StateMachine API contract', () => {
  it('uses management routes and preserves the aggregate graph and both versions', async () => {
    const http = client()
    const api = createStateMachineApi(http)
    const signal = new AbortController().signal
    const nodes = [
      {
        nId: 'OPEN',
        name: 'Open',
        description: null,
        isInitial: true,
        isTerminal: false,
        outcome: 'None' as const,
        color: '#1677FF',
        sort: 0,
      },
      {
        nId: 'DONE',
        name: 'Done',
        description: null,
        isInitial: false,
        isTerminal: true,
        outcome: 'Success' as const,
        color: '#52C41A',
        sort: 1,
      },
    ]
    const transitions = [
      {
        fromStatusNId: 'OPEN',
        actionNId: 'APPROVE',
        actionName: 'Approve',
        toStatusNId: 'DONE',
        description: null,
      },
    ]
    const version = {
      expectedOptimisticVersion: 9,
      expectedConcurrencyVersion: 'token-9',
    }

    await api.listStateMachines(
      { pageIndex: 2, pageSize: 20, keyword: 'order', scopeType: 'Tenant', status: 'Draft' },
      { signal },
    )
    await api.getStateMachine('machine/id', { signal })
    await api.createStateMachine({
      scopeType: 'Tenant',
      nId: 'ORDER_FLOW',
      name: 'Order flow',
      description: null,
      nodes,
      transitions,
    })
    await api.updateStateMachine('machine/id', {
      name: 'Order flow 2',
      description: null,
      nodes,
      transitions,
      ...version,
    })
    await api.cloneStateMachine('machine/id', version)
    await api.checkStateMachinePublication('machine/id', { signal })
    await api.publishStateMachine('machine/id', version)
    await api.disableStateMachine('machine/id', { ...version, changeReason: 'obsolete' })

    expect(http.get).toHaveBeenNthCalledWith(
      1,
      '/referencedata/api/v1/reference-data/admin/state-machines?pageIndex=2&pageSize=20&keyword=order&scopeType=Tenant&status=Draft',
      { signal },
    )
    expect(http.get).toHaveBeenNthCalledWith(
      2,
      '/referencedata/api/v1/reference-data/admin/state-machines/machine%2Fid',
      { signal },
    )
    expect(http.get).toHaveBeenNthCalledWith(
      3,
      '/referencedata/api/v1/reference-data/admin/state-machines/machine%2Fid/publication-check',
      { signal },
    )
    expect(http.post).toHaveBeenNthCalledWith(
      1,
      '/referencedata/api/v1/reference-data/admin/state-machines',
      expect.objectContaining({ nId: 'ORDER_FLOW', nodes, transitions }),
    )
    expect(http.put).toHaveBeenCalledWith(
      '/referencedata/api/v1/reference-data/admin/state-machines/machine%2Fid',
      expect.objectContaining({ ...version, nodes, transitions }),
    )
    expect(http.post).toHaveBeenNthCalledWith(
      2,
      '/referencedata/api/v1/reference-data/admin/state-machines/machine%2Fid/clone',
      version,
    )
    expect(http.post).toHaveBeenNthCalledWith(
      3,
      '/referencedata/api/v1/reference-data/admin/state-machines/machine%2Fid/publish',
      version,
    )
    expect(http.post).toHaveBeenNthCalledWith(
      4,
      '/referencedata/api/v1/reference-data/admin/state-machines/machine%2Fid/disable',
      { ...version, changeReason: 'obsolete' },
    )
  })

  it('uses available, explicit current and fixed source routes and evaluates without side effects', async () => {
    const http = client()
    const api = createStateMachineApi(http)
    const signal = new AbortController().signal
    const evaluation = {
      sourceScope: 'Tenant' as const,
      sourceTenantNId: 'TENANT-A',
      revision: 7,
      fromStatusNId: 'OPEN',
      actionNId: 'REJECT',
    }

    await api.listAvailableStateMachines(
      { pageIndex: 1, pageSize: 100, keyword: 'order' },
      { signal },
    )
    await api.getCurrentStateMachine('ORDER/FLOW', 'Tenant', 'TENANT-A', { signal })
    await api.getStateMachineRevision('ORDER/FLOW', 7, 'Platform', null, { signal })
    await api.evaluateStateTransition('ORDER/FLOW', evaluation, { signal })

    expect(http.get).toHaveBeenNthCalledWith(
      1,
      '/referencedata/api/v1/reference-data/state-machines?pageIndex=1&pageSize=100&keyword=order',
      { signal },
    )
    expect(http.get).toHaveBeenNthCalledWith(
      2,
      '/referencedata/api/v1/reference-data/state-machines/ORDER%2FFLOW?sourceScope=Tenant&sourceTenantNId=TENANT-A',
      { signal },
    )
    expect(http.get).toHaveBeenNthCalledWith(
      3,
      '/referencedata/api/v1/reference-data/state-machines/ORDER%2FFLOW/revisions/7?sourceScope=Platform',
      { signal },
    )
    expect(http.post).toHaveBeenCalledWith(
      '/referencedata/api/v1/reference-data/state-machines/ORDER%2FFLOW/evaluate',
      evaluation,
      { signal },
    )
  })
})
