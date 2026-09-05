import { nextTick, type Component } from 'vue'
import { createPinia, setActivePinia } from 'pinia'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import ElementPlus, { ElMessageBox } from 'element-plus'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import OrganizationsAdminPage from '@/components/systemData/OrganizationsAdminPage.vue'
import AssignmentsAdminPage from '@/components/systemData/AssignmentsAdminPage.vue'
import NavigationAdminPage from '@/components/systemData/NavigationAdminPage.vue'
import FeaturesAdminPage from '@/components/systemData/FeaturesAdminPage.vue'
import ServicesAdminPage from '@/components/systemData/ServicesAdminPage.vue'
import ThemesAdminPage from '@/components/systemData/ThemesAdminPage.vue'
import ServiceInitializationAdminPage from '@/components/systemData/ServiceInitializationAdminPage.vue'
import { useAuthStore } from '@/stores/authStore'
import { useSystemDataManagementStore } from '@/stores/systemData/managementStore'
import { PERMISSIONS } from '@/permissions'
import { persistAuthSession } from '../fixtures/session'

const api = vi.hoisted(() => ({
  listOrganizationsTree: vi.fn(),
  getOrganization: vi.fn(),
  createOrganization: vi.fn(),
  updateOrganization: vi.fn(),
  previewOrganizationMove: vi.fn(),
  moveOrganization: vi.fn(),
  setOrganizationStatus: vi.fn(),
  listPositions: vi.fn(),
  createPosition: vi.fn(),
  updatePosition: vi.fn(),
  setPositionStatus: vi.fn(),
  listAssignments: vi.fn(),
  createAssignment: vi.fn(),
  updateAssignment: vi.fn(),
  endAssignment: vi.fn(),
  cancelAssignment: vi.fn(),
  setPrimaryAssignment: vi.fn(),
  listResources: vi.fn(),
  getNavigationDraft: vi.fn(),
  previewNavigationDefaults: vi.fn(),
  importNavigationDefaults: vi.fn(),
  addNavigationNode: vi.fn(),
  updateNavigationNode: vi.fn(),
  deleteNavigationNode: vi.fn(),
  restoreNavigationNode: vi.fn(),
  validateNavigation: vi.fn(),
  publishNavigation: vi.fn(),
  rollbackNavigation: vi.fn(),
  listFeatures: vi.fn(),
  setFeatureOverride: vi.fn(),
  listServiceCatalog: vi.fn(),
  createServiceCatalog: vi.fn(),
  updateServiceCatalog: vi.fn(),
  setServiceCatalogStatus: vi.fn(),
  getThemePolicy: vi.fn(),
  updateThemePolicy: vi.fn(),
  listInitializationRegistrations: vi.fn(),
  listInitializationPlans: vi.fn(),
  listInitializationOperations: vi.fn(),
  getInitializationRegistration: vi.fn(),
  getInitializationPolicy: vi.fn(),
  registerInitialization: vi.fn(),
  createInitializationPlan: vi.fn(),
  getInitializationPlan: vi.fn(),
  listInitializationApprovals: vi.fn(),
  listInitializationBackupEvidence: vi.fn(),
  createApproval: vi.fn(),
  createBackupEvidence: vi.fn(),
  verifyBackupEvidence: vi.fn(),
  applyInitialization: vi.fn(),
  cancelInitialization: vi.fn(),
}))

const identityApi = vi.hoisted(() => ({
  listUsers: vi.fn(),
  getPermissionTree: vi.fn(),
}))

vi.mock('@/api/systemData/managementRegistry', () => ({
  getSystemDataManagementApi: () => api,
}))

vi.mock('@/api/identity/managementRegistry', () => ({
  getManagementApi: () => identityApi,
}))

interface Deferred<T> {
  promise: Promise<T>
  resolve(value: T): void
  reject(reason?: unknown): void
}

function deferred<T>(): Deferred<T> {
  let resolve!: (value: T) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise
    reject = rejectPromise
  })
  return { promise, resolve, reject }
}

const organization = () => ({
  tenantNId: 'tenant-1',
  nId: 'org-1',
  name: 'Platform',
  type: 'Company',
  status: 'Active',
  parentOrganizationNId: null,
  displayOrder: 0,
  children: [],
})

const organizationDetail = () => ({
  ...organization(),
  organizationRevision: 1,
  optimisticVersion: 1,
  concurrencyVersion: 'v1',
})

const position = () => ({
  tenantNId: 'tenant-1',
  nId: 'position-1',
  organizationNId: 'org-1',
  organizationName: 'Platform',
  name: 'Operator',
  description: '',
  status: 'Active',
  displayOrder: 0,
  optimisticVersion: 1,
  concurrencyVersion: 'v1',
})

const assignment = () => ({
  tenantNId: 'tenant-1',
  nId: 'assignment-1',
  userNId: 'user-1',
  userDisplayNameSnapshot: 'Mock User',
  organizationNId: 'org-1',
  positionNId: 'position-1',
  positionName: 'Operator',
  isPrimary: true,
  effectiveFrom: '2026-01-01T00:00:00Z',
  effectiveTo: null,
  state: 'Active',
  cancelledOn: null,
  cancelReason: null,
  optimisticVersion: 1,
  concurrencyVersion: 'v1',
})

const feature = () => ({
  featureNId: 'feature-1',
  ownerModuleNId: 'module-1',
  name: 'Feature One',
  description: null,
  defaultEnabled: true,
  status: 'Active',
  featureRevision: 1,
  effectiveEnabled: true,
})

const service = () => ({
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
  degraded: false,
})

const themePolicy = () => ({
  policyRevision: 1,
  configured: true,
  allowedPalettes: ['industrial-cyan'],
  allowedModes: ['light'],
  allowedPcDensities: ['comfortable'],
  defaultPalette: 'industrial-cyan',
  defaultMode: 'light',
  defaultPcDensity: 'comfortable',
})

const plan = () => ({
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
})

const allPermissions = Object.values(PERMISSIONS)
const wrappers: VueWrapper[] = []

function resetApi(): void {
  vi.clearAllMocks()
  api.listOrganizationsTree.mockResolvedValue([organization()])
  api.getOrganization.mockResolvedValue(organizationDetail())
  api.createOrganization.mockResolvedValue(organizationDetail())
  api.updateOrganization.mockResolvedValue(organizationDetail())
  api.previewOrganizationMove.mockResolvedValue({
    nId: 'org-1',
    organizationRevision: 1,
    subtreeOrganizationCount: 1,
    subtreePositionCount: 1,
    subtreeAssignmentCount: 0,
    affectedCount: 1,
    previewedOn: '2026-08-25T00:00:00Z',
    expectedOptimisticVersion: 1,
    expectedConcurrencyVersion: 'v1',
  })
  api.moveOrganization.mockResolvedValue(organizationDetail())
  api.setOrganizationStatus.mockResolvedValue(organizationDetail())
  api.listPositions.mockResolvedValue({
    items: [position()],
    total: 1,
    pageIndex: 1,
    pageSize: 100,
  })
  api.createPosition.mockResolvedValue(position())
  api.updatePosition.mockResolvedValue(position())
  api.setPositionStatus.mockResolvedValue(position())
  api.listAssignments.mockResolvedValue([])
  api.createAssignment.mockResolvedValue(assignment())
  api.updateAssignment.mockResolvedValue(assignment())
  api.endAssignment.mockResolvedValue(assignment())
  api.cancelAssignment.mockResolvedValue(assignment())
  api.setPrimaryAssignment.mockResolvedValue([assignment()])
  api.listResources.mockResolvedValue([])
  api.getNavigationDraft.mockResolvedValue({ draftRevision: 1, nodes: [] })
  api.previewNavigationDefaults.mockResolvedValue({
    draftRevision: 1,
    items: [
      {
        nodeNId: 'default-home',
        label: 'Home',
        parentNodeNId: null,
        kind: 'Link',
        level: 1,
        action: 'Add',
        reason: '',
      },
    ],
  })
  api.importNavigationDefaults.mockResolvedValue({ draftRevision: 1, items: [] })
  api.addNavigationNode.mockResolvedValue({
    nodeNId: 'navigation.group.new',
    kind: 'Group',
    label: 'Home',
    parentNodeNId: null,
    resourceNId: null,
    featureNId: null,
    iconKey: null,
    displayOrder: 0,
    visibleTerminals: ['Pc', 'Pda', 'Mobile'],
    status: 'Active',
    actionResourceNIds: [],
    children: [],
  })
  api.updateNavigationNode.mockResolvedValue(undefined)
  api.deleteNavigationNode.mockResolvedValue(undefined)
  api.restoreNavigationNode.mockResolvedValue(undefined)
  api.validateNavigation.mockResolvedValue({ draftRevision: 1, isValid: true, errors: [] })
  api.publishNavigation.mockResolvedValue({ revision: 2 })
  api.rollbackNavigation.mockResolvedValue({ revision: 2 })
  api.listFeatures.mockResolvedValue([feature()])
  api.setFeatureOverride.mockResolvedValue(feature())
  api.listServiceCatalog.mockResolvedValue([service()])
  api.createServiceCatalog.mockResolvedValue(service())
  api.updateServiceCatalog.mockResolvedValue(service())
  api.setServiceCatalogStatus.mockResolvedValue(service())
  api.getThemePolicy.mockResolvedValue(themePolicy())
  api.updateThemePolicy.mockResolvedValue({ ...themePolicy(), policyRevision: 2 })
  api.listInitializationRegistrations.mockResolvedValue({
    items: [],
    total: 0,
    pageIndex: 1,
    pageSize: 20,
  })
  api.listInitializationPlans.mockResolvedValue({
    items: [plan()],
    total: 1,
    pageIndex: 1,
    pageSize: 20,
  })
  api.listInitializationOperations.mockResolvedValue({
    items: [],
    total: 0,
    pageIndex: 1,
    pageSize: 20,
  })
  api.getInitializationRegistration.mockResolvedValue({})
  api.getInitializationPolicy.mockResolvedValue({
    tenantNId: 'tenant-1',
    environmentNId: 'development',
    environmentKind: 'Development',
    approvalRequired: false,
    backupRequired: false,
    planTtlSeconds: 3600,
    planTimeoutSeconds: 3600,
    applyTimeoutSeconds: 3600,
    maxPreMigrationRetries: 1,
    policyRevision: 1,
    initializationPolicy: 'Development',
    isExplicit: true,
  })
  api.registerInitialization.mockResolvedValue({})
  api.createInitializationPlan.mockResolvedValue({
    operationNId: 'operation-plan',
    kind: 'Plan',
    status: 'Queued',
    phase: 'Planning',
    acceptedOn: '2026-08-25T00:00:00Z',
  })
  api.getInitializationPlan.mockResolvedValue(plan())
  api.listInitializationApprovals.mockResolvedValue([])
  api.listInitializationBackupEvidence.mockResolvedValue(null)
  api.createApproval.mockResolvedValue({})
  api.createBackupEvidence.mockResolvedValue({})
  api.verifyBackupEvidence.mockResolvedValue({})
  api.applyInitialization.mockResolvedValue({
    operationNId: 'operation-apply',
    kind: 'Apply',
    status: 'Queued',
    phase: 'Queued',
    acceptedOn: '2026-08-25T00:00:00Z',
  })
  api.cancelInitialization.mockResolvedValue({})
  identityApi.listUsers.mockResolvedValue({
    items: [{ userNId: 'user-1', name: 'Mock User', loginName: 'mock' }],
  })
  identityApi.getPermissionTree.mockResolvedValue([])
}

async function mountPage(
  component: Component,
): Promise<{ wrapper: VueWrapper; store: ReturnType<typeof useSystemDataManagementStore> }> {
  const pinia = createPinia()
  setActivePinia(pinia)
  useAuthStore().adoptSession(persistAuthSession(allPermissions))
  const wrapper = mount(component, {
    global: { plugins: [pinia, ElementPlus] },
  })
  wrappers.push(wrapper)
  await flushPromises()
  return { wrapper, store: useSystemDataManagementStore() }
}

function click(selector: string): void {
  const element = document.body.querySelector(selector)
  expect(element, `missing ${selector}`).not.toBeNull()
  ;(element as HTMLElement).click()
}

async function setBodyInput(index: number, value: string): Promise<void> {
  const inputs = document.body.querySelectorAll('.app-form-drawer input')
  const input = inputs[index] as HTMLInputElement | undefined
  expect(input, `missing drawer input ${index}`).toBeDefined()
  input!.value = value
  input!.dispatchEvent(new Event('input', { bubbles: true }))
  await nextTick()
}

afterEach(() => {
  wrappers.splice(0).forEach((wrapper) => wrapper.unmount())
  document.body.innerHTML = ''
  sessionStorage.clear()
})

beforeEach(() => {
  resetApi()
  vi.spyOn(ElMessageBox, 'confirm').mockResolvedValue('confirm' as never)
})

describe('SystemData page-local submit locks', () => {
  it('locks organization create, preserves the form after failure, and permits one retry', async () => {
    const pending = deferred<unknown>()
    api.createOrganization.mockImplementationOnce(() => pending.promise)
    const { wrapper } = await mountPage(OrganizationsAdminPage)

    await wrapper.get('[data-testid="systemdata-organizations-new"]').trigger('click')
    await nextTick()
    await setBodyInput(0, 'org-new')
    await setBodyInput(1, 'New Organization')
    click('[data-testid="form-drawer-submit"]')
    click('[data-testid="form-drawer-submit"]')
    click('[data-testid="form-drawer-submit"]')

    expect(api.createOrganization).toHaveBeenCalledTimes(1)
    await nextTick()
    expect(document.querySelector('[data-testid="form-drawer-submit"]')).toHaveProperty(
      'disabled',
      true,
    )
    pending.reject(new Error('temporary failure'))
    await flushPromises()
    expect((document.querySelectorAll('.app-form-drawer input')[1] as HTMLInputElement).value).toBe(
      'New Organization',
    )

    click('[data-testid="form-drawer-submit"]')
    expect(api.createOrganization).toHaveBeenCalledTimes(2)
    await flushPromises()
    expect(document.querySelector('[data-testid="form-drawer-submit"]')).toBeNull()
    expect(api.listOrganizationsTree).toHaveBeenCalledTimes(2)
  })

  it('locks assignment creation after selecting a user and position', async () => {
    const pending = deferred<unknown>()
    api.createAssignment.mockImplementationOnce(() => pending.promise)
    const { wrapper } = await mountPage(AssignmentsAdminPage)

    await wrapper.get('.systemdata-assignment-query input').setValue('mock')
    await wrapper.get('[data-testid="query-panel-submit"]').trigger('click')
    await flushPromises()
    await wrapper.get('.systemdata-assignment-query__results button').trigger('click')
    await flushPromises()
    wrapper
      .findAll('button')
      .find((button) => button.text().includes('新建任职'))
      ?.trigger('click')
    await nextTick()
    const positionSelect = document.querySelector('.app-form-drawer .el-select') as HTMLElement
    positionSelect.click()
    await nextTick()
    ;(document.querySelector('.el-select-dropdown__item:not(.is-disabled)') as HTMLElement).click()
    await nextTick()
    click('[data-testid="form-drawer-submit"]')
    click('[data-testid="form-drawer-submit"]')
    click('[data-testid="form-drawer-submit"]')

    expect(api.createAssignment).toHaveBeenCalledTimes(1)
    pending.resolve(assignment())
    await flushPromises()
  })

  it('locks navigation node save, default import, and publish independently', async () => {
    const { wrapper } = await mountPage(NavigationAdminPage)
    const addPending = deferred<unknown>()
    api.addNavigationNode.mockImplementationOnce(() => addPending.promise)
    await wrapper.get('[data-testid="systemdata-navigation-new-first-level"]').trigger('click')
    await setBodyInput(1, 'Home')
    click('[data-testid="systemdata-navigation-save"]')
    click('[data-testid="systemdata-navigation-save"]')
    click('[data-testid="systemdata-navigation-save"]')
    expect(api.addNavigationNode).toHaveBeenCalledTimes(1)
    await nextTick()
    expect(document.querySelector('[data-testid="systemdata-navigation-save"]')).toHaveProperty(
      'disabled',
      true,
    )
    addPending.resolve(undefined)
    await flushPromises()

    await wrapper.get('[data-testid="systemdata-navigation-defaults"]').trigger('click')
    await flushPromises()
    const importPending = deferred<unknown>()
    api.importNavigationDefaults.mockImplementationOnce(() => importPending.promise)
    click('[data-testid="systemdata-navigation-defaults-confirm"]')
    click('[data-testid="systemdata-navigation-defaults-confirm"]')
    click('[data-testid="systemdata-navigation-defaults-confirm"]')
    expect(api.importNavigationDefaults).toHaveBeenCalledTimes(1)
    await nextTick()
    expect(
      document.querySelector('[data-testid="systemdata-navigation-defaults-confirm"]'),
    ).toHaveProperty('disabled', true)
    importPending.resolve({ draftRevision: 1, items: [] })
    await flushPromises()

    const publishPending = deferred<{ revision: number }>()
    api.publishNavigation.mockImplementationOnce(() => publishPending.promise)
    await wrapper.get('[data-testid="systemdata-navigation-publish"]').trigger('click')
    await wrapper.get('[data-testid="systemdata-navigation-publish"]').trigger('click')
    await wrapper.get('[data-testid="systemdata-navigation-publish"]').trigger('click')
    expect(api.publishNavigation).toHaveBeenCalledTimes(1)
    publishPending.resolve({ revision: 2 })
    await flushPromises()
  })

  it('locks feature override, service create, and theme save', async () => {
    const featurePending = deferred<unknown>()
    api.setFeatureOverride.mockImplementationOnce(() => featurePending.promise)
    const featurePage = await mountPage(FeaturesAdminPage)
    await featurePage.wrapper
      .findAll('button')
      .find((button) => button.text().includes('覆盖'))
      ?.trigger('click')
    await nextTick()
    await nextTick()
    ;(document.querySelector('.app-form-drawer .el-checkbox__original') as HTMLElement).click()
    await nextTick()
    click('[data-testid="form-drawer-submit"]')
    click('[data-testid="form-drawer-submit"]')
    click('[data-testid="form-drawer-submit"]')
    await flushPromises()
    expect(api.setFeatureOverride).toHaveBeenCalledTimes(1)
    featurePending.resolve(feature())
    await flushPromises()

    const servicePending = deferred<unknown>()
    api.createServiceCatalog.mockImplementationOnce(() => servicePending.promise)
    const servicePage = await mountPage(ServicesAdminPage)
    await servicePage.wrapper
      .findAll('button')
      .find((button) => button.text().includes('外部服务'))
      ?.trigger('click')
    await nextTick()
    await setBodyInput(0, 'New Service')
    click('[data-testid="form-drawer-submit"]')
    click('[data-testid="form-drawer-submit"]')
    click('[data-testid="form-drawer-submit"]')
    expect(api.createServiceCatalog).toHaveBeenCalledTimes(1)
    servicePending.resolve(service())
    await flushPromises()

    const themePending = deferred<unknown>()
    api.updateThemePolicy.mockImplementationOnce(() => themePending.promise)
    const themePage = await mountPage(ThemesAdminPage)
    const themeSave = themePage.wrapper
      .findAll('button')
      .find((button) => button.text().includes('保存'))
    expect(themeSave).toBeDefined()
    themeSave!.element.click()
    themeSave!.element.click()
    themeSave!.element.click()
    expect(api.updateThemePolicy).toHaveBeenCalledTimes(1)
    themePending.resolve(themePolicy())
    await flushPromises()
  })

  it('locks initialization plan creation and apply confirmation', async () => {
    const planPending = deferred<unknown>()
    api.createInitializationPlan.mockImplementationOnce(() => planPending.promise)
    const { wrapper } = await mountPage(ServiceInitializationAdminPage)

    await wrapper
      .findAll('.systemdata-init-tabs button')
      .find((button) => button.text().includes('计划'))
      ?.trigger('click')
    await nextTick()
    await wrapper
      .findAll('button')
      .find((button) => button.text().includes('生成计划'))
      ?.trigger('click')
    await nextTick()
    await setBodyInput(0, 'identity')
    await setBodyInput(1, 'identity-core')
    await setBodyInput(2, '20260825.1')
    click('[data-testid="form-drawer-submit"]')
    click('[data-testid="form-drawer-submit"]')
    click('[data-testid="form-drawer-submit"]')
    expect(api.createInitializationPlan).toHaveBeenCalledTimes(1)
    planPending.resolve({})
    await flushPromises()

    await wrapper.get('.systemdata-init-plan-index button').trigger('click')
    await flushPromises()
    const applyPending = deferred<unknown>()
    api.applyInitialization.mockImplementationOnce(() => applyPending.promise)
    const applyButton = wrapper.findAll('button').find((button) => button.text().includes('Apply'))
    expect(applyButton).toBeDefined()
    applyButton!.element.click()
    applyButton!.element.click()
    applyButton!.element.click()
    await flushPromises()
    expect(api.applyInitialization).toHaveBeenCalledTimes(1)
    applyPending.resolve({})
    await flushPromises()
  })
})
