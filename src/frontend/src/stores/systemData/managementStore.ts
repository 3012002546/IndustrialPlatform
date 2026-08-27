import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'

import { getSystemDataManagementApi } from '@/api/systemData/managementRegistry'
import type {
  AssignmentDto,
  ApplyInitializationRequest,
  CancelAssignmentRequest,
  CreateAssignmentRequest,
  CreateInitializationPlanRequest,
  CreateNavigationNodeRequest,
  CreateOrganizationRequest,
  CreatePositionRequest,
  CreateServiceCatalogRequest,
  FeatureDefinitionDto,
  InitializationOperationDto,
  InitializationPlanDto,
  InitializationRegistrationSummaryDto,
  MoveOrganizationRequest,
  NavigationDraftDto,
  NavigationValidationDto,
  OrganizationDetailDto,
  OrganizationNodeDto,
  OrganizationMovePreviewDto,
  PageResultDto,
  PositionDto,
  RegisterServiceInitializationRequest,
  SetOrganizationStatusRequest,
  SetPositionStatusRequest,
  SetPrimaryAssignmentRequest,
  SetServiceCatalogStatusRequest,
  ServiceCatalogDto,
  ThemePolicyDto,
  UpdateNavigationNodeRequest,
  UpdateOrganizationRequest,
  UpdatePositionRequest,
  UpdateScheduledAssignmentRequest,
  UpdateServiceCatalogRequest,
  UiResourceDto,
} from '@/api/systemData/managementTypes'

export type SystemDataAdminKind =
  | 'organizations'
  | 'assignments'
  | 'navigation'
  | 'features'
  | 'services'
  | 'themes'
  | 'service-initialization'

export interface SystemDataManagementStoreState {
  loading: Ref<boolean>
  error: Ref<string | null>
  traceId: Ref<string | null>
  organizationTree: Ref<OrganizationNodeDto[]>
  selectedOrganizationNId: Ref<string | null>
  selectedOrganization: Ref<OrganizationDetailDto | null>
  movePreview: Ref<OrganizationMovePreviewDto | null>
  positions: Ref<PageResultDto<PositionDto> | null>
  positionPage: Ref<number>
  selectOrganization(nId: string): Promise<void>
  loadPositions(page?: number): Promise<void>
  previewOrganizationMove(nId: string, targetParentOrganizationNId?: string): Promise<void>
  moveOrganization(nId: string, request: MoveOrganizationRequest): Promise<void>
  setOrganizationStatus(nId: string, request: SetOrganizationStatusRequest): Promise<void>
  updateOrganization(nId: string, request: UpdateOrganizationRequest): Promise<void>
  createPosition(request: CreatePositionRequest): Promise<void>
  updatePosition(nId: string, request: UpdatePositionRequest): Promise<void>
  setPositionStatus(nId: string, request: SetPositionStatusRequest): Promise<void>
  assignments: Ref<AssignmentDto[]>
  assignmentUserNId: Ref<string>
  createAssignment(userNId: string, request: CreateAssignmentRequest): Promise<void>
  updateAssignment(nId: string, request: UpdateScheduledAssignmentRequest): Promise<void>
  endAssignment(nId: string): Promise<void>
  cancelAssignment(nId: string, request: CancelAssignmentRequest): Promise<void>
  setPrimaryAssignment(userNId: string, request: SetPrimaryAssignmentRequest): Promise<void>
  navigationDraft: Ref<NavigationDraftDto | null>
  resources: Ref<UiResourceDto[]>
  addNavigationNode(request: CreateNavigationNodeRequest): Promise<void>
  updateNavigationNode(nId: string, request: UpdateNavigationNodeRequest): Promise<void>
  deleteNavigationNode(nId: string): Promise<void>
  navigationValidation: Ref<NavigationValidationDto | null>
  features: Ref<FeatureDefinitionDto[]>
  services: Ref<ServiceCatalogDto[]>
  themePolicy: Ref<ThemePolicyDto | null>
  initializationRegistrations: Ref<PageResultDto<InitializationRegistrationSummaryDto> | null>
  initializationPlans: Ref<PageResultDto<InitializationPlanDto> | null>
  initializationOperations: Ref<PageResultDto<InitializationOperationDto> | null>
  load(kind: SystemDataAdminKind): Promise<void>
  loadAssignments(userNId: string): Promise<void>
  createOrganization(request: CreateOrganizationRequest): Promise<void>
  setFeatureOverride(featureNId: string, mode: string, reason: string): Promise<void>
  createService(name: string, entryPoint: string, ownerOrganizationNId?: string): Promise<void>
  updateService(nId: string, request: UpdateServiceCatalogRequest): Promise<void>
  setServiceStatus(nId: string, request: SetServiceCatalogStatusRequest): Promise<void>
  updateThemeDefaults(): Promise<void>
  validateNavigation(): Promise<void>
  publishNavigation(): Promise<void>
  rollbackNavigation(): Promise<void>
  registerInitialization(request: RegisterServiceInitializationRequest): Promise<void>
  createInitializationPlan(request: CreateInitializationPlanRequest): Promise<void>
  createApproval(planNId: string, reason: string): Promise<void>
  createBackupEvidence(planNId: string, reference: string): Promise<void>
  applyInitialization(request: ApplyInitializationRequest): Promise<void>
  cancelInitialization(operationNId: string): Promise<void>
  retry(kind: SystemDataAdminKind): Promise<void>
}

function messageOf(error: unknown): string {
  if (error instanceof Error) return error.message
  return 'SystemData 管理接口暂不可用。'
}

export const useSystemDataManagementStore = defineStore(
  'systemDataManagement',
  (): SystemDataManagementStoreState => {
    const loading = ref(false)
    const error = ref<string | null>(null)
    const traceId = ref<string | null>(null)
    const organizationTree = ref<OrganizationNodeDto[]>([])
    const selectedOrganizationNId = ref<string | null>(null)
    const selectedOrganization = ref<OrganizationDetailDto | null>(null)
    const movePreview = ref<OrganizationMovePreviewDto | null>(null)
    const positions = ref<PageResultDto<PositionDto> | null>(null)
    const positionPage = ref(1)
    const assignments = ref<AssignmentDto[]>([])
    const assignmentUserNId = ref('')
    const navigationDraft = ref<NavigationDraftDto | null>(null)
    const resources = ref<UiResourceDto[]>([])
    const navigationValidation = ref<NavigationValidationDto | null>(null)
    const features = ref<FeatureDefinitionDto[]>([])
    const services = ref<ServiceCatalogDto[]>([])
    const themePolicy = ref<ThemePolicyDto | null>(null)
    const initializationRegistrations =
      ref<PageResultDto<InitializationRegistrationSummaryDto> | null>(null)
    const initializationPlans = ref<PageResultDto<InitializationPlanDto> | null>(null)
    const initializationOperations = ref<PageResultDto<InitializationOperationDto> | null>(null)

    async function run(action: () => Promise<void>): Promise<void> {
      const api = getSystemDataManagementApi()
      if (api === null) {
        error.value = 'SystemData 管理接口暂未装配。'
        return
      }
      loading.value = true
      error.value = null
      traceId.value = null
      try {
        await action()
      } catch (reason) {
        const details = (reason as { details?: { code?: string; traceId?: string } }).details
        const message = messageOf(reason)
        error.value = details?.code ? `[${details.code}] ${message}` : message
        traceId.value = details?.traceId ?? null
      } finally {
        loading.value = false
      }
    }

    async function load(kind: SystemDataAdminKind): Promise<void> {
      await run(async () => {
        const api = getSystemDataManagementApi()
        if (api === null) return
        if (kind === 'organizations') {
          organizationTree.value = await api.listOrganizationsTree()
          if (selectedOrganizationNId.value !== null)
            await selectOrganization(selectedOrganizationNId.value)
          return
        }
        if (kind === 'assignments') {
          ;[organizationTree.value, positions.value] = await Promise.all([
            api.listOrganizationsTree(),
            api.listPositions({ pageIndex: 1, pageSize: 100 }),
          ])
          return
        }
        if (kind === 'navigation') {
          ;[navigationDraft.value, resources.value, features.value] = await Promise.all([
            api.getNavigationDraft(),
            api.listResources(),
            api.listFeatures(),
          ])
          return
        }
        if (kind === 'features') {
          features.value = await api.listFeatures()
          return
        }
        if (kind === 'services') {
          ;[services.value, organizationTree.value] = await Promise.all([
            api.listServiceCatalog(),
            api.listOrganizationsTree(),
          ])
          return
        }
        if (kind === 'themes') {
          themePolicy.value = await api.getThemePolicy()
          return
        }
        const [registrations, plans, operations] = await Promise.all([
          api.listInitializationRegistrations(),
          api.listInitializationPlans(),
          api.listInitializationOperations(),
        ])
        initializationRegistrations.value = registrations
        initializationPlans.value = plans
        initializationOperations.value = operations
      })
    }

    async function loadAssignments(userNId: string): Promise<void> {
      assignmentUserNId.value = userNId
      await run(async () => {
        const api = getSystemDataManagementApi()
        if (api !== null) assignments.value = await api.listAssignments(userNId)
      })
    }

    async function selectOrganization(nId: string): Promise<void> {
      selectedOrganizationNId.value = nId
      await run(async () => {
        const api = getSystemDataManagementApi()
        if (api === null) return
        selectedOrganization.value = await api.getOrganization(nId)
        positionPage.value = 1
        positions.value = await api.listPositions({
          organizationNId: nId,
          pageIndex: 1,
          pageSize: 20,
        })
      })
    }

    async function loadPositions(page = positionPage.value): Promise<void> {
      if (selectedOrganizationNId.value === null) return
      positionPage.value = page
      await run(async () => {
        const api = getSystemDataManagementApi()
        if (api !== null)
          positions.value = await api.listPositions({
            organizationNId: selectedOrganizationNId.value ?? undefined,
            pageIndex: page,
            pageSize: 20,
          })
      })
    }

    async function previewOrganizationMove(
      nId: string,
      targetParentOrganizationNId?: string,
    ): Promise<void> {
      await run(async () => {
        const api = getSystemDataManagementApi()
        if (api !== null)
          movePreview.value = await api.previewOrganizationMove(nId, targetParentOrganizationNId)
      })
    }

    async function moveOrganization(nId: string, request: MoveOrganizationRequest): Promise<void> {
      await run(async () => {
        const api = getSystemDataManagementApi()
        if (api !== null) {
          await api.moveOrganization(nId, request)
          movePreview.value = null
          await load('organizations')
        }
      })
    }

    async function setOrganizationStatus(
      nId: string,
      request: SetOrganizationStatusRequest,
    ): Promise<void> {
      await run(async () => {
        const api = getSystemDataManagementApi()
        if (api !== null) {
          await api.setOrganizationStatus(nId, request)
          await load('organizations')
        }
      })
    }

    async function updateOrganization(
      nId: string,
      request: UpdateOrganizationRequest,
    ): Promise<void> {
      await run(async () => {
        const api = getSystemDataManagementApi()
        if (api !== null) {
          selectedOrganization.value = await api.updateOrganization(nId, request)
          organizationTree.value = await api.listOrganizationsTree()
        }
      })
    }

    async function createPosition(request: CreatePositionRequest): Promise<void> {
      await run(async () => {
        const api = getSystemDataManagementApi()
        if (api !== null) {
          await api.createPosition(request)
          await loadPositions(1)
        }
      })
    }

    async function updatePosition(nId: string, request: UpdatePositionRequest): Promise<void> {
      await run(async () => {
        const api = getSystemDataManagementApi()
        if (api !== null) {
          await api.updatePosition(nId, request)
          await loadPositions()
        }
      })
    }

    async function setPositionStatus(
      nId: string,
      request: SetPositionStatusRequest,
    ): Promise<void> {
      await run(async () => {
        const api = getSystemDataManagementApi()
        if (api !== null) {
          await api.setPositionStatus(nId, request)
          await loadPositions()
        }
      })
    }

    async function createOrganization(request: CreateOrganizationRequest): Promise<void> {
      await run(async () => {
        const api = getSystemDataManagementApi()
        if (api !== null) {
          await api.createOrganization(request)
          organizationTree.value = await api.listOrganizationsTree()
        }
      })
    }

    async function setFeatureOverride(
      featureNId: string,
      mode: string,
      reason: string,
    ): Promise<void> {
      await run(async () => {
        const api = getSystemDataManagementApi()
        if (api !== null) {
          await api.setFeatureOverride(featureNId, { mode, reason })
          features.value = await api.listFeatures()
        }
      })
    }

    async function createAssignment(
      userNId: string,
      request: CreateAssignmentRequest,
    ): Promise<void> {
      await run(async () => {
        const api = getSystemDataManagementApi()
        if (api !== null)
          assignments.value = [await api.createAssignment(userNId, request), ...assignments.value]
      })
    }

    async function updateAssignment(
      nId: string,
      request: UpdateScheduledAssignmentRequest,
    ): Promise<void> {
      await run(async () => {
        const api = getSystemDataManagementApi()
        if (api !== null && assignmentUserNId.value) {
          const updated = await api.updateAssignment(nId, request)
          assignments.value = assignments.value.map((item) => (item.nId === nId ? updated : item))
        }
      })
    }

    async function endAssignment(nId: string): Promise<void> {
      await run(async () => {
        const api = getSystemDataManagementApi()
        if (api !== null) {
          const updated = await api.endAssignment(nId)
          assignments.value = assignments.value.map((item) => (item.nId === nId ? updated : item))
        }
      })
    }

    async function cancelAssignment(nId: string, request: CancelAssignmentRequest): Promise<void> {
      await run(async () => {
        const api = getSystemDataManagementApi()
        if (api !== null) {
          const updated = await api.cancelAssignment(nId, request)
          assignments.value = assignments.value.map((item) => (item.nId === nId ? updated : item))
        }
      })
    }

    async function setPrimaryAssignment(
      userNId: string,
      request: SetPrimaryAssignmentRequest,
    ): Promise<void> {
      await run(async () => {
        const api = getSystemDataManagementApi()
        if (api !== null) assignments.value = await api.setPrimaryAssignment(userNId, request)
      })
    }

    async function addNavigationNode(request: CreateNavigationNodeRequest): Promise<void> {
      await run(async () => {
        const api = getSystemDataManagementApi()
        if (api !== null) {
          await api.addNavigationNode(request)
          navigationDraft.value = await api.getNavigationDraft()
        }
      })
    }

    async function updateNavigationNode(
      nId: string,
      request: UpdateNavigationNodeRequest,
    ): Promise<void> {
      await run(async () => {
        const api = getSystemDataManagementApi()
        if (api !== null) {
          await api.updateNavigationNode(nId, request)
          navigationDraft.value = await api.getNavigationDraft()
        }
      })
    }

    async function deleteNavigationNode(nId: string): Promise<void> {
      await run(async () => {
        const api = getSystemDataManagementApi()
        if (api !== null) {
          await api.deleteNavigationNode(nId)
          navigationDraft.value = await api.getNavigationDraft()
        }
      })
    }

    async function createService(
      name: string,
      entryPoint: string,
      ownerOrganizationNId?: string,
    ): Promise<void> {
      await run(async () => {
        const api = getSystemDataManagementApi()
        if (api !== null) {
          const request: CreateServiceCatalogRequest = {
            serviceNId: name,
            name,
            entryPoint,
            supportedTerminals: ['Pc'],
          }
          if (ownerOrganizationNId) request.ownerOrganizationNId = ownerOrganizationNId
          await api.createServiceCatalog(request)
          services.value = await api.listServiceCatalog()
        }
      })
    }

    async function updateService(nId: string, request: UpdateServiceCatalogRequest): Promise<void> {
      await run(async () => {
        const api = getSystemDataManagementApi()
        if (api !== null) {
          await api.updateServiceCatalog(nId, request)
          services.value = await api.listServiceCatalog()
        }
      })
    }

    async function setServiceStatus(
      nId: string,
      request: SetServiceCatalogStatusRequest,
    ): Promise<void> {
      await run(async () => {
        const api = getSystemDataManagementApi()
        if (api !== null) {
          await api.setServiceCatalogStatus(nId, request)
          services.value = await api.listServiceCatalog()
        }
      })
    }

    async function updateThemeDefaults(): Promise<void> {
      await run(async () => {
        const api = getSystemDataManagementApi()
        if (api !== null && themePolicy.value !== null) {
          themePolicy.value = await api.updateThemePolicy({
            allowedPalettes: themePolicy.value.allowedPalettes,
            allowedModes: themePolicy.value.allowedModes,
            allowedPcDensities: themePolicy.value.allowedPcDensities,
            defaultPalette: themePolicy.value.defaultPalette,
            defaultMode: themePolicy.value.defaultMode,
            defaultPcDensity: themePolicy.value.defaultPcDensity,
          })
        }
      })
    }

    async function validateNavigation(): Promise<void> {
      await run(async () => {
        const api = getSystemDataManagementApi()
        if (api !== null) navigationValidation.value = await api.validateNavigation()
      })
    }

    async function publishNavigation(): Promise<void> {
      await run(async () => {
        const api = getSystemDataManagementApi()
        if (api !== null) {
          await api.publishNavigation()
          navigationDraft.value = await api.getNavigationDraft()
        }
      })
    }

    async function rollbackNavigation(): Promise<void> {
      await run(async () => {
        const api = getSystemDataManagementApi()
        if (api !== null) {
          await api.rollbackNavigation()
          navigationDraft.value = await api.getNavigationDraft()
        }
      })
    }

    async function registerInitialization(
      request: RegisterServiceInitializationRequest,
    ): Promise<void> {
      await run(async () => {
        const api = getSystemDataManagementApi()
        if (api !== null) {
          await api.registerInitialization(request)
          await load('service-initialization')
        }
      })
    }

    async function createInitializationPlan(
      request: CreateInitializationPlanRequest,
    ): Promise<void> {
      await run(async () => {
        const api = getSystemDataManagementApi()
        if (api !== null) {
          await api.createInitializationPlan(request, `systemdata-plan-${Date.now()}`)
          await load('service-initialization')
        }
      })
    }

    async function createApproval(planNId: string, reason: string): Promise<void> {
      await run(async () => {
        const api = getSystemDataManagementApi()
        if (api !== null) await api.createApproval(planNId, { reason })
      })
    }

    async function createBackupEvidence(planNId: string, reference: string): Promise<void> {
      await run(async () => {
        const api = getSystemDataManagementApi()
        if (api !== null)
          await api.createBackupEvidence(planNId, {
            backupProvider: '管理员登记',
            backupReference: reference,
          })
      })
    }

    async function applyInitialization(request: ApplyInitializationRequest): Promise<void> {
      await run(async () => {
        const api = getSystemDataManagementApi()
        if (api !== null) {
          await api.applyInitialization(request, `systemdata-apply-${Date.now()}`)
          await load('service-initialization')
        }
      })
    }

    async function cancelInitialization(operationNId: string): Promise<void> {
      await run(async () => {
        const api = getSystemDataManagementApi()
        if (api !== null) {
          await api.cancelInitialization(operationNId)
          await load('service-initialization')
        }
      })
    }

    return {
      loading,
      error,
      traceId,
      organizationTree,
      selectedOrganizationNId,
      selectedOrganization,
      movePreview,
      positions,
      positionPage,
      selectOrganization,
      loadPositions,
      previewOrganizationMove,
      moveOrganization,
      setOrganizationStatus,
      updateOrganization,
      createPosition,
      updatePosition,
      setPositionStatus,
      assignments,
      assignmentUserNId,
      createAssignment,
      updateAssignment,
      endAssignment,
      cancelAssignment,
      setPrimaryAssignment,
      navigationDraft,
      resources,
      addNavigationNode,
      updateNavigationNode,
      deleteNavigationNode,
      navigationValidation,
      features,
      services,
      themePolicy,
      initializationRegistrations,
      initializationPlans,
      initializationOperations,
      load,
      loadAssignments,
      createOrganization,
      setFeatureOverride,
      createService,
      updateService,
      setServiceStatus,
      updateThemeDefaults,
      validateNavigation,
      publishNavigation,
      rollbackNavigation,
      registerInitialization,
      createInitializationPlan,
      createApproval,
      createBackupEvidence,
      applyInitialization,
      cancelInitialization,
      retry: load,
    }
  },
)
