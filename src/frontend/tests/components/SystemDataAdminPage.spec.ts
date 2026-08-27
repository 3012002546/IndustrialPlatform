import { createPinia, setActivePinia } from 'pinia'
import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'

vi.mock('@/api/systemData/managementRegistry', () => ({
  getSystemDataManagementApi: () => ({
    listOrganizationsTree: vi.fn().mockResolvedValue([
      {
        tenantNId: 'tenant-1',
        nId: 'org-1',
        name: 'Platform',
        type: 'Company',
        status: 'Active',
        parentOrganizationNId: null,
        displayOrder: 0,
        children: [],
      },
    ]),
    getOrganization: vi.fn().mockResolvedValue({
      tenantNId: 'tenant-1',
      nId: 'org-1',
      name: 'Platform',
      type: 'Company',
      status: 'Active',
      parentOrganizationNId: null,
      displayOrder: 0,
      organizationRevision: 1,
      optimisticVersion: 1,
      concurrencyVersion: 'v1',
    }),
    listPositions: vi.fn().mockResolvedValue({ items: [], total: 0, pageIndex: 1, pageSize: 20 }),
    listAssignments: vi.fn().mockResolvedValue([]),
    getNavigationDraft: vi.fn().mockResolvedValue({ draftRevision: 1, nodes: [] }),
    listResources: vi.fn().mockResolvedValue([
      {
        resourceNId: 'resource-1',
        ownerModuleNId: 'module-1',
        manifestVersion: '1.0.0',
        type: 'Page',
        name: 'Home',
        routeName: 'pc-home',
        requiredPermissionNId: null,
        supportedTerminals: ['Pc', 'Pda', 'Mobile'],
        status: 'Active',
      },
    ]),
    listFeatures: vi.fn().mockResolvedValue([
      {
        featureNId: 'feature-1',
        ownerModuleNId: 'module-1',
        name: 'Feature One',
        description: null,
        defaultEnabled: true,
        status: 'Active',
        featureRevision: 1,
        effectiveEnabled: true,
      },
    ]),
    listServiceCatalog: vi.fn().mockResolvedValue([
      {
        serviceNId: 'platform-service',
        kind: 'Platform',
        name: 'Identity',
        description: null,
        entryPoint: '/identity',
        gatewayPathPrefix: '/identity',
        healthPath: '/health',
        ownerOrganizationNId: null,
        ownerOrganizationNameSnapshot: null,
        ownerDisplaySnapshot: null,
        supportedTerminals: ['Pc'],
        status: 'Active',
        source: 'Platform',
        degraded: false,
      },
      {
        serviceNId: 'external-service',
        kind: 'External',
        name: 'External',
        description: null,
        entryPoint: 'https://external.example.test',
        gatewayPathPrefix: null,
        healthPath: '/health',
        ownerOrganizationNId: null,
        ownerOrganizationNameSnapshot: null,
        ownerDisplaySnapshot: null,
        supportedTerminals: ['Pc'],
        status: 'Active',
        source: 'Tenant',
        degraded: true,
      },
    ]),
    getThemePolicy: vi.fn().mockResolvedValue({
      allowedPalettes: ['industrial-cyan'],
      allowedModes: ['light'],
      allowedPcDensities: ['comfortable'],
      defaultPalette: 'industrial-cyan',
      defaultMode: 'light',
      defaultPcDensity: 'comfortable',
    }),
    listInitializationRegistrations: vi.fn().mockResolvedValue({
      items: [
        {
          serviceKey: 'identity',
          moduleKey: 'identity-core',
          logicalDatabaseName: 'identity',
          provider: 'PostgreSQL',
          migrationVersion: '20260825.1',
          desiredState: 'SourceOfTruth',
          status: 'Registered',
          topologyRevision: 'topology-1',
          registeredOn: '2026-08-25T00:00:00Z',
          lastUpdatedOn: '2026-08-25T00:00:00Z',
        },
      ],
      total: 1,
      pageIndex: 1,
      pageSize: 20,
    }),
    listInitializationPlans: vi.fn().mockResolvedValue({
      items: [
        {
          tenantNId: 'tenant-1',
          planNId: 'plan-1',
          environmentNId: 'development',
          serviceKey: 'identity',
          moduleKey: 'identity-core',
          requestedMigrationVersion: '20260825.1',
          currentMigrationVersion: '20260824.1',
          targetStateFingerprint: 'fingerprint',
          planChecksum: 'plan-checksum',
          riskLevel: 'Low',
          destructiveChangeDetected: false,
          requiredPolicies: 'None',
          expiresOn: '2026-08-26T00:00:00Z',
          isExpired: false,
          createdByUserNId: 'user-1',
          createdOn: '2026-08-25T00:00:00Z',
          steps: [],
        },
      ],
      total: 1,
      pageIndex: 1,
      pageSize: 20,
    }),
    listInitializationOperations: vi.fn().mockResolvedValue({
      items: [
        {
          tenantNId: 'tenant-1',
          operationNId: 'operation-1',
          kind: 'Apply',
          environmentNId: 'development',
          serviceKey: 'identity',
          moduleKey: 'identity-core',
          planNId: 'plan-1',
          requestedVersion: '20260825.1',
          idempotencyKey: 'key-1',
          status: 'Running',
          phase: 'SchemaMigration',
          attempt: 1,
          leaseOwner: null,
          queuedOn: '2026-08-25T00:00:00Z',
          startedOn: '2026-08-25T00:00:01Z',
          completedOn: null,
          timeoutOn: '2026-08-25T00:05:00Z',
          sanitizedErrorCode: null,
          sanitizedErrorSummary: null,
          traceId: 'trace-1',
          createdByUserNId: 'user-1',
          steps: [{ sequence: 1, phase: 'Inspect', status: 'Succeeded', attempt: 1 }],
          seedObservations: null,
        },
      ],
      total: 1,
      pageIndex: 1,
      pageSize: 20,
    }),
  }),
}))

import SystemDataAdminPage from '@/components/systemData/SystemDataAdminPage.vue'

describe('SystemDataAdminPage', () => {
  it.each([
    ['organizations', '组织详情与岗位'],
    ['assignments', 'Identity 用户搜索'],
    ['navigation', '草稿树'],
    ['features', '功能开关'],
    ['services', 'Platform'],
    ['themes', '允许配色'],
    ['service-initialization', '服务/模块注册'],
  ] as const)('renders the authorized %s page key path', async (kind, marker) => {
    const pinia = createPinia()
    setActivePinia(pinia)
    const wrapper = mount(SystemDataAdminPage, {
      props: { kind, title: `SystemData ${kind}` },
      global: { plugins: [pinia] },
    })

    await flushPromises()

    expect(wrapper.get('[data-testid="systemdata-admin-page"]')).toBeTruthy()
    expect(wrapper.text()).toContain(marker)
    if (kind === 'service-initialization') {
      expect(wrapper.text()).toContain('SeedSets')
      expect(wrapper.text()).toContain('Operation')
      expect(wrapper.text()).toContain('环境策略')
      expect(wrapper.text()).toContain('20260825.1')

      await wrapper.get('button[aria-pressed="false"]:nth-of-type(3)').trigger('click')
      expect(wrapper.text()).toContain('plan-checksum')

      const operationTab = wrapper
        .findAll('.systemdata-init-tabs button')
        .find((button) => button.text() === 'Operation')
      await operationTab?.trigger('click')
      expect(wrapper.text()).toContain('Inspect:Succeeded')
    }
    wrapper.unmount()
  })

  it('组织创建表单在缺少 NId/名称/顺序时阻止提交', async () => {
    const pinia = createPinia()
    setActivePinia(pinia)
    const wrapper = mount(SystemDataAdminPage, {
      props: { kind: 'organizations', title: '行政组织与岗位' },
      global: { plugins: [pinia] },
    })
    await flushPromises()

    await wrapper.get('.systemdata-admin-toolbar button').trigger('click')
    const submit = document.body.querySelector('[data-testid="form-drawer-submit"]')
    expect(submit).not.toBeNull()
    submit?.dispatchEvent(new MouseEvent('click', { bubbles: true }))
    await flushPromises()
    expect(document.body.querySelector('[role="alert"]')?.textContent).toContain('组织名称')
    expect(document.body.querySelector('[data-testid="form-drawer-submit"]')).not.toBeNull()
    wrapper.unmount()
  })
})
