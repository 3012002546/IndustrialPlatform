import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { registerSystemDataManagementApi } from '@/api/systemData/managementRegistry'
import type { SystemDataManagementApi } from '@/api/systemData/managementTypes'
import { registerSystemDataRuntimeApi } from '@/api/systemData/runtimeRegistry'
import type { SystemDataRuntimeApi } from '@/api/systemData/runtimeApi'
import { useSystemDataManagementStore } from '@/stores/systemData/managementStore'
import { useSystemDataRuntimeStore } from '@/stores/systemData/runtimeStore'

const page = {
  items: [] as Array<{ nId: string; name: string }>,
  total: 0,
  pageIndex: 1,
  pageSize: 20,
}

function deferred<T>(): {
  promise: Promise<T>
  resolve(value: T): void
} {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((nextResolve) => {
    resolve = nextResolve
  })
  return { promise, resolve }
}

describe('SystemData management store', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('releases plan/apply idempotency keys only after a successful response', async () => {
    const createPlan = vi
      .fn()
      .mockRejectedValueOnce(new Error('network timeout'))
      .mockResolvedValue({ operationNId: 'op-1', kind: 'Plan', status: 'Queued', acceptedOn: '' })
    const apply = vi
      .fn()
      .mockResolvedValue({ operationNId: 'op-2', kind: 'Apply', status: 'Queued', acceptedOn: '' })
    registerSystemDataManagementApi({
      createInitializationPlan: createPlan,
      applyInitialization: apply,
      listInitializationRegistrations: vi.fn().mockResolvedValue(page),
      listInitializationPlans: vi.fn().mockResolvedValue(page),
      listInitializationOperations: vi.fn().mockResolvedValue(page),
    } as unknown as SystemDataManagementApi)
    const store = useSystemDataManagementStore()
    const request = { serviceKey: 'identity', moduleKey: 'identity', requestedVersion: 'v1' }

    await store.createInitializationPlan(request)
    await store.createInitializationPlan(request)
    expect(createPlan).toHaveBeenCalledTimes(2)
    expect(createPlan.mock.calls[0]?.[1]).toBe(createPlan.mock.calls[1]?.[1])

    await store.applyInitialization({
      planNId: 'plan-1',
      moduleKey: 'identity',
      requestedVersion: 'v1',
    })
    await store.applyInitialization({
      planNId: 'plan-1',
      moduleKey: 'identity',
      requestedVersion: 'v1',
    })
    expect(apply).toHaveBeenCalledTimes(2)
    expect(apply.mock.calls[0]?.[1]).not.toBe(apply.mock.calls[1]?.[1])
  })

  it('ignores a late child-table response from a previous organization selection', async () => {
    const lateA = deferred<typeof page>()
    let positionCall = 0
    const api = {
      getOrganization: vi.fn(async (nId: string) => ({
        tenantNId: 'tenant',
        nId,
        name: nId,
        type: 'Company',
        status: 'Active',
        parentOrganizationNId: null,
        displayOrder: 0,
        children: [],
        organizationRevision: 1,
        optimisticVersion: 1,
        concurrencyVersion: nId,
      })),
      listPositions: vi.fn((request: { organizationNId?: string }) => {
        positionCall += 1
        if (positionCall === 2) return lateA.promise
        return Promise.resolve({
          ...page,
          items: [{ nId: request.organizationNId ?? '', name: 'B' }],
        })
      }),
    }
    registerSystemDataManagementApi(api as unknown as SystemDataManagementApi)
    const store = useSystemDataManagementStore()

    await store.selectOrganization('org-a')
    const lateRequest = store.loadPositions()
    await store.selectOrganization('org-b')
    expect(store.selectedOrganizationNId).toBe('org-b')
    expect(store.positions?.items[0]?.nId).toBe('org-b')

    lateA.resolve({ ...page, items: [{ nId: 'org-a', name: 'late A' }] })
    await lateRequest
    expect(store.positions?.items[0]?.nId).toBe('org-b')
  })

  it('does not restore child rows after the parent selection is cleared', async () => {
    const late = deferred<typeof page>()
    let positionCall = 0
    const api = {
      getOrganization: vi.fn(async (nId: string) => ({ nId })),
      listPositions: vi.fn(() => {
        positionCall += 1
        return positionCall === 2 ? late.promise : Promise.resolve(page)
      }),
    }
    registerSystemDataManagementApi(api as unknown as SystemDataManagementApi)
    const store = useSystemDataManagementStore()

    await store.selectOrganization('org-a')
    const request = store.loadPositions()
    store.clearOrganizationSelection()
    late.resolve({ ...page, items: [{ nId: 'org-a', name: 'late A' }] })
    await request

    expect(store.selectedOrganizationNId).toBeNull()
    expect(store.positions).toBeNull()
  })

  it('keeps initialization approvals and backup evidence scoped to the selected plan', async () => {
    const approvalsA = deferred<unknown[]>()
    const evidenceA = deferred<unknown>()
    const approvalsB = deferred<unknown[]>()
    const evidenceB = deferred<unknown>()
    registerSystemDataManagementApi({
      listInitializationApprovals: vi.fn(
        (planNId: string) =>
          (planNId === 'plan-a' ? approvalsA.promise : approvalsB.promise) as Promise<never[]>,
      ),
      listInitializationBackupEvidence: vi.fn(
        (planNId: string) =>
          (planNId === 'plan-a' ? evidenceA.promise : evidenceB.promise) as Promise<null>,
      ),
    } as unknown as SystemDataManagementApi)
    const store = useSystemDataManagementStore()

    const first = store.selectInitializationPlan('plan-a')
    const second = store.selectInitializationPlan('plan-b')
    approvalsB.resolve([{ approvalNId: 'approval-b', planNId: 'plan-b' }])
    evidenceB.resolve({ evidenceNId: 'evidence-b', planNId: 'plan-b' })
    await second
    approvalsA.resolve([{ approvalNId: 'approval-a', planNId: 'plan-a' }])
    evidenceA.resolve({ evidenceNId: 'evidence-a', planNId: 'plan-a' })
    await first

    expect(store.initializationSelectedPlanNId).toBe('plan-b')
    expect(store.initializationApprovals[0]?.planNId).toBe('plan-b')
    expect(store.initializationBackupEvidence?.planNId).toBe('plan-b')

    store.clearInitializationPlanSelection()
    expect(store.initializationApprovals).toEqual([])
    expect(store.initializationBackupEvidence).toBeNull()
  })

  it('does not apply a validation response after the draft revision has advanced', async () => {
    const lateValidation = deferred<{
      draftRevision: number
      isValid: boolean
      errors: []
    }>()
    let draftRevision = 1
    const api = {
      getNavigationDraft: vi.fn(async () => ({ draftRevision, nodes: [] })),
      listResources: vi.fn().mockResolvedValue([]),
      listFeatures: vi.fn().mockResolvedValue([]),
      validateNavigation: vi.fn(() => lateValidation.promise),
      updateNavigationNode: vi.fn(async () => {
        draftRevision = 2
      }),
    }
    registerSystemDataManagementApi(api as unknown as SystemDataManagementApi)
    const store = useSystemDataManagementStore()
    await store.load('navigation')

    const validationRequest = store.validateNavigation()
    await store.updateNavigationNode('node-1', {
      label: 'Node',
      parentNodeNId: null,
      resourceNId: null,
      featureNId: null,
      iconKey: null,
      visibleTerminals: ['Pc'],
      actionResourceNIds: [],
      displayOrder: 0,
      expectedDraftRevision: 1,
    })
    lateValidation.resolve({
      draftRevision: 1,
      isValid: false,
      errors: [],
    })
    await validationRequest

    expect(store.navigationDraft?.draftRevision).toBe(2)
    expect(store.navigationValidation).toBeNull()
  })

  it('keeps the publish response revision and refreshes runtime at that revision', async () => {
    let published = false
    const managementApi = {
      getNavigationDraft: vi.fn(async () => ({ draftRevision: published ? 7 : 4, nodes: [] })),
      listResources: vi.fn().mockResolvedValue([]),
      listFeatures: vi.fn().mockResolvedValue([]),
      publishNavigation: vi.fn(async () => {
        published = true
        return { revision: 7 }
      }),
    }
    registerSystemDataManagementApi(managementApi as unknown as SystemDataManagementApi)
    registerSystemDataRuntimeApi({
      getNavigation: vi.fn().mockResolvedValue({
        kind: 'updated',
        etag: '"nav-7"',
        data: { revision: 7, degraded: false, nodes: [] },
      }),
      getFeatures: vi.fn().mockResolvedValue({
        kind: 'updated',
        etag: '"features-7"',
        data: { revision: 7, degraded: false, items: [] },
      }),
      getThemePolicy: vi.fn().mockResolvedValue({
        kind: 'updated',
        etag: '"theme-7"',
        data: {
          policyRevision: 7,
          degraded: false,
          allowedPalettes: ['industrial-cyan'],
          allowedModes: ['system'],
          allowedPcDensities: ['comfortable'],
          defaultPalette: 'industrial-cyan',
          defaultMode: 'system',
          defaultPcDensity: 'comfortable',
        },
      }),
    } as unknown as SystemDataRuntimeApi)
    const store = useSystemDataManagementStore()
    await store.load('navigation')
    await store.publishNavigation()

    expect(managementApi.publishNavigation).toHaveBeenCalledWith(4)
    expect(store.navigationPublishedRevision).toBe(7)
    expect(store.navigationDraft?.draftRevision).toBe(7)
    expect(useSystemDataRuntimeStore().navigationRevision).toBe(7)
  })
})
