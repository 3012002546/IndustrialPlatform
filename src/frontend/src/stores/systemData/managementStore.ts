import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'

import { getSystemDataManagementApi } from '@/api/systemData/managementRegistry'
import { localeMessages } from '@/localization/i18n'
import { systemDataPageCopy } from '@/localization/systemData'
import { useLocalizationStore } from '@/stores/localizationStore'
import { useSystemDataRuntimeStore } from './runtimeStore'
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
  InitializationApprovalDto,
  InitializationBackupEvidenceDto,
  InitializationEnvironmentPolicyDto,
  InitializationOperationDto,
  InitializationPlanDto,
  InitializationRegistrationDto,
  InitializationRegistrationSummaryDto,
  MoveOrganizationRequest,
  NavigationDraftDto,
  NavigationDefaultImportPreviewDto,
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
  ThemePolicyUpdateRequest,
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
  organizationTreeLoading: Ref<boolean>
  organizationDetailLoading: Ref<boolean>
  organizationDetailError: Ref<string | null>
  selectedOrganizationNId: Ref<string | null>
  selectedOrganization: Ref<OrganizationDetailDto | null>
  movePreview: Ref<OrganizationMovePreviewDto | null>
  positions: Ref<PageResultDto<PositionDto> | null>
  positionPage: Ref<number>
  selectOrganization(nId: string): Promise<void>
  clearOrganizationSelection(): void
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
  navigationDefaultPreview: Ref<NavigationDefaultImportPreviewDto | null>
  resources: Ref<UiResourceDto[]>
  addNavigationNode(request: CreateNavigationNodeRequest): Promise<boolean>
  previewNavigationDefaults(): Promise<void>
  importNavigationDefaults(): Promise<void>
  updateNavigationNode(nId: string, request: UpdateNavigationNodeRequest): Promise<void>
  deleteNavigationNode(nId: string): Promise<void>
  restoreNavigationNode(nId: string): Promise<void>
  navigationValidation: Ref<NavigationValidationDto | null>
  navigationPublishedRevision: Ref<number | null>
  features: Ref<FeatureDefinitionDto[]>
  services: Ref<ServiceCatalogDto[]>
  themePolicy: Ref<ThemePolicyDto | null>
  initializationRegistrations: Ref<PageResultDto<InitializationRegistrationSummaryDto> | null>
  initializationPlans: Ref<PageResultDto<InitializationPlanDto> | null>
  initializationOperations: Ref<PageResultDto<InitializationOperationDto> | null>
  initializationRegistration: Ref<InitializationRegistrationDto | null>
  initializationPolicy: Ref<InitializationEnvironmentPolicyDto | null>
  initializationApprovals: Ref<InitializationApprovalDto[]>
  initializationBackupEvidence: Ref<InitializationBackupEvidenceDto | null>
  initializationSelectedPlanNId: Ref<string>
  load(kind: SystemDataAdminKind): Promise<void>
  loadAssignments(userNId: string): Promise<void>
  createOrganization(request: CreateOrganizationRequest): Promise<void>
  setFeatureOverride(featureNId: string, mode: string, reason: string): Promise<void>
  createService(name: string, entryPoint: string, ownerOrganizationNId?: string): Promise<void>
  updateService(nId: string, request: UpdateServiceCatalogRequest): Promise<void>
  setServiceStatus(nId: string, request: SetServiceCatalogStatusRequest): Promise<void>
  updateThemeDefaults(request?: ThemePolicyUpdateRequest): Promise<void>
  validateNavigation(): Promise<void>
  publishNavigation(): Promise<void>
  rollbackNavigation(): Promise<void>
  registerInitialization(request: RegisterServiceInitializationRequest): Promise<void>
  loadInitializationRegistration(serviceKey: string, moduleKey: string): Promise<void>
  clearInitializationRegistrationSelection(): void
  loadInitializationPolicy(): Promise<void>
  selectInitializationPlan(planNId: string): Promise<void>
  clearInitializationPlanSelection(): void
  loadInitializationGates(planNId: string): Promise<void>
  createInitializationPlan(request: CreateInitializationPlanRequest): Promise<void>
  createApproval(planNId: string, reason: string): Promise<void>
  createBackupEvidence(planNId: string, reference: string): Promise<void>
  verifyBackupEvidence(evidenceNId: string): Promise<void>
  applyInitialization(request: ApplyInitializationRequest): Promise<void>
  cancelInitialization(operationNId: string): Promise<void>
  retry(kind: SystemDataAdminKind): Promise<void>
}

function messageOf(error: unknown, fallback: string): string {
  if (error instanceof Error) return error.message
  return fallback
}

export const useSystemDataManagementStore = defineStore(
  'systemDataManagement',
  (): SystemDataManagementStoreState => {
    const loading = ref(false)
    const error = ref<string | null>(null)
    const traceId = ref<string | null>(null)
    const localization = useLocalizationStore()
    const organizationTree = ref<OrganizationNodeDto[]>([])
    const organizationTreeLoading = ref(false)
    const organizationDetailLoading = ref(false)
    const organizationDetailError = ref<string | null>(null)
    const selectedOrganizationNId = ref<string | null>(null)
    const selectedOrganization = ref<OrganizationDetailDto | null>(null)
    const movePreview = ref<OrganizationMovePreviewDto | null>(null)
    const positions = ref<PageResultDto<PositionDto> | null>(null)
    const positionPage = ref(1)
    const assignments = ref<AssignmentDto[]>([])
    const assignmentUserNId = ref('')
    const navigationDraft = ref<NavigationDraftDto | null>(null)
    const navigationDefaultPreview = ref<NavigationDefaultImportPreviewDto | null>(null)
    const resources = ref<UiResourceDto[]>([])
    const navigationValidation = ref<NavigationValidationDto | null>(null)
    const navigationPublishedRevision = ref<number | null>(null)
    const features = ref<FeatureDefinitionDto[]>([])
    const services = ref<ServiceCatalogDto[]>([])
    const themePolicy = ref<ThemePolicyDto | null>(null)
    const initializationRegistrations =
      ref<PageResultDto<InitializationRegistrationSummaryDto> | null>(null)
    const initializationPlans = ref<PageResultDto<InitializationPlanDto> | null>(null)
    const initializationOperations = ref<PageResultDto<InitializationOperationDto> | null>(null)
    const initializationRegistration = ref<InitializationRegistrationDto | null>(null)
    const initializationPolicy = ref<InitializationEnvironmentPolicyDto | null>(null)
    const initializationApprovals = ref<InitializationApprovalDto[]>([])
    const initializationBackupEvidence = ref<InitializationBackupEvidenceDto | null>(null)
    const initializationSelectedPlanNId = ref('')
    const planIdempotencyKeys = new Map<string, string>()
    const applyIdempotencyKeys = new Map<string, string>()
    let organizationSelectionRequest = 0
    let assignmentSelectionRequest = 0
    let initializationRegistrationRequest = 0
    let initializationGateRequest = 0
    let navigationValidationRequest = 0
    let navigationWriteRequest = 0

    function invalidateNavigationValidation(): void {
      navigationValidationRequest += 1
      navigationValidation.value = null
    }

    function idempotencyKey(cache: Map<string, string>, prefix: string, request: unknown): string {
      const fingerprint = JSON.stringify(request)
      const existing = cache.get(fingerprint)
      if (existing !== undefined) return existing
      const random =
        typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function'
          ? crypto.randomUUID()
          : `${Date.now()}-${Math.random().toString(36).slice(2)}`
      const key = `systemdata-${prefix}-${random}`
      cache.set(fingerprint, key)
      return key
    }

    function forgetIdempotencyKey(cache: Map<string, string>, request: unknown): void {
      cache.delete(JSON.stringify(request))
    }

    async function run(action: () => Promise<void>, skipWhenBusy = false): Promise<boolean> {
      if (skipWhenBusy && loading.value) return false
      const api = getSystemDataManagementApi()
      if (api === null) {
        error.value = localeMessages[localization.locale].systemData.copy.interfaceUnavailable
        return false
      }
      loading.value = true
      error.value = null
      traceId.value = null
      try {
        await action()
        return true
      } catch (reason) {
        const details = (reason as { details?: { code?: string; traceId?: string } }).details
        const message = messageOf(
          reason,
          localeMessages[localization.locale].systemData.copy.interfaceUnavailable,
        )
        error.value = details?.code ? `[${details.code}] ${message}` : message
        traceId.value = details?.traceId ?? null
        return false
      } finally {
        loading.value = false
      }
    }

    async function load(kind: SystemDataAdminKind): Promise<void> {
      if (kind === 'navigation' || kind === 'features') invalidateNavigationValidation()
      await run(async () => {
        const api = getSystemDataManagementApi()
        if (api === null) return
        if (kind === 'organizations') {
          organizationTreeLoading.value = true
          try {
            organizationTree.value = await api.listOrganizationsTree()
            if (selectedOrganizationNId.value !== null)
              await selectOrganization(selectedOrganizationNId.value)
          } finally {
            organizationTreeLoading.value = false
          }
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
          ;[features.value, navigationDraft.value] = await Promise.all([
            api.listFeatures(),
            api.getNavigationDraft(),
          ])
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
        if (typeof api.getInitializationPolicy === 'function') {
          initializationPolicy.value = await api.getInitializationPolicy()
        }
        const selectedPlanNId = initializationSelectedPlanNId.value
        if (
          selectedPlanNId &&
          typeof api.listInitializationApprovals === 'function' &&
          typeof api.listInitializationBackupEvidence === 'function'
        ) {
          initializationApprovals.value = []
          initializationBackupEvidence.value = null
          await refreshInitializationGates(api, selectedPlanNId, ++initializationGateRequest)
        }
      })
    }

    async function refreshInitializationGates(
      api: NonNullable<ReturnType<typeof getSystemDataManagementApi>>,
      planNId: string,
      requestId: number,
    ): Promise<void> {
      const [approvals, evidence] = await Promise.all([
        api.listInitializationApprovals(planNId),
        api.listInitializationBackupEvidence(planNId),
      ])
      if (
        requestId === initializationGateRequest &&
        initializationSelectedPlanNId.value === planNId
      ) {
        initializationApprovals.value = approvals
        initializationBackupEvidence.value = evidence
      }
    }

    async function loadAssignments(userNId: string): Promise<void> {
      const requestId = ++assignmentSelectionRequest
      assignmentUserNId.value = userNId
      assignments.value = []
      await run(async () => {
        const api = getSystemDataManagementApi()
        if (api !== null) {
          const nextAssignments = await api.listAssignments(userNId)
          if (requestId === assignmentSelectionRequest) assignments.value = nextAssignments
        }
      })
    }

    function clearOrganizationSelection(): void {
      organizationSelectionRequest++
      selectedOrganizationNId.value = null
      selectedOrganization.value = null
      positions.value = null
      organizationDetailLoading.value = false
      organizationDetailError.value = null
      movePreview.value = null
    }

    function clearInitializationRegistrationSelection(): void {
      initializationRegistrationRequest++
      initializationRegistration.value = null
    }

    async function selectOrganization(nId: string): Promise<void> {
      const requestId = ++organizationSelectionRequest
      selectedOrganizationNId.value = nId
      error.value = null
      selectedOrganization.value = null
      positions.value = null
      movePreview.value = null
      organizationDetailLoading.value = true
      organizationDetailError.value = null
      const api = getSystemDataManagementApi()
      if (api === null) {
        organizationDetailError.value =
          localeMessages[localization.locale].systemData.copy.interfaceUnavailable
        organizationDetailLoading.value = false
        return
      }
      try {
        const [detail, nextPositions] = await Promise.all([
          api.getOrganization(nId),
          api.listPositions({ organizationNId: nId, pageIndex: 1, pageSize: 20 }),
        ])
        if (requestId !== organizationSelectionRequest) return
        selectedOrganization.value = detail
        positionPage.value = 1
        positions.value = nextPositions
      } catch (reason) {
        if (requestId === organizationSelectionRequest) {
          organizationDetailError.value = messageOf(
            reason,
            localeMessages[localization.locale].systemData.copy.interfaceUnavailable,
          )
        }
      } finally {
        if (requestId === organizationSelectionRequest) organizationDetailLoading.value = false
      }
    }

    async function loadPositions(page = positionPage.value): Promise<void> {
      const organizationNId = selectedOrganizationNId.value
      if (organizationNId === null) return
      const requestId = organizationSelectionRequest
      error.value = null
      positionPage.value = page
      organizationDetailLoading.value = true
      organizationDetailError.value = null
      const api = getSystemDataManagementApi()
      if (api === null) {
        organizationDetailError.value =
          localeMessages[localization.locale].systemData.copy.interfaceUnavailable
        organizationDetailLoading.value = false
        return
      }
      try {
        const nextPositions = await api.listPositions({
          organizationNId,
          pageIndex: page,
          pageSize: 20,
        })
        if (
          requestId === organizationSelectionRequest &&
          selectedOrganizationNId.value === organizationNId
        )
          positions.value = nextPositions
      } catch (reason) {
        if (requestId === organizationSelectionRequest) {
          organizationDetailError.value = messageOf(
            reason,
            localeMessages[localization.locale].systemData.copy.interfaceUnavailable,
          )
        }
      } finally {
        if (requestId === organizationSelectionRequest) organizationDetailLoading.value = false
      }
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

    async function addNavigationNode(request: CreateNavigationNodeRequest): Promise<boolean> {
      invalidateNavigationValidation()
      return run(async () => {
        const api = getSystemDataManagementApi()
        if (api !== null) {
          await api.addNavigationNode(request)
          navigationDraft.value = await api.getNavigationDraft()
        }
      })
    }

    async function previewNavigationDefaults(): Promise<void> {
      await run(async () => {
        const api = getSystemDataManagementApi()
        if (api !== null) navigationDefaultPreview.value = await api.previewNavigationDefaults()
      })
    }

    async function importNavigationDefaults(): Promise<void> {
      invalidateNavigationValidation()
      await run(async () => {
        const api = getSystemDataManagementApi()
        if (api !== null) {
          navigationDefaultPreview.value = await api.importNavigationDefaults({
            expectedDraftRevision: navigationDraft.value?.draftRevision ?? 0,
          })
          navigationDraft.value = await api.getNavigationDraft()
        }
      })
    }

    async function updateNavigationNode(
      nId: string,
      request: UpdateNavigationNodeRequest,
    ): Promise<void> {
      invalidateNavigationValidation()
      await run(async () => {
        const api = getSystemDataManagementApi()
        if (api !== null) {
          await api.updateNavigationNode(nId, request)
          navigationDraft.value = await api.getNavigationDraft()
        }
      })
    }

    async function deleteNavigationNode(nId: string): Promise<void> {
      invalidateNavigationValidation()
      await run(async () => {
        const api = getSystemDataManagementApi()
        if (api !== null) {
          await api.deleteNavigationNode(nId, navigationDraft.value?.draftRevision ?? 0)
          navigationDraft.value = await api.getNavigationDraft()
        }
      })
    }

    async function restoreNavigationNode(nId: string): Promise<void> {
      invalidateNavigationValidation()
      await run(async () => {
        const api = getSystemDataManagementApi()
        if (api !== null) {
          await api.restoreNavigationNode(nId, navigationDraft.value?.draftRevision ?? 0)
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

    async function updateThemeDefaults(request?: ThemePolicyUpdateRequest): Promise<void> {
      await run(async () => {
        const api = getSystemDataManagementApi()
        if (api !== null && themePolicy.value !== null) {
          const response = await api.updateThemePolicy(
            request ?? {
              expectedPolicyRevision: themePolicy.value.policyRevision,
              allowedPalettes: themePolicy.value.allowedPalettes,
              allowedModes: themePolicy.value.allowedModes,
              allowedPcDensities: themePolicy.value.allowedPcDensities,
              defaultPalette: themePolicy.value.defaultPalette,
              defaultMode: themePolicy.value.defaultMode,
              defaultPcDensity: themePolicy.value.defaultPcDensity,
            },
          )
          themePolicy.value = response
        }
      })
    }

    async function validateNavigation(): Promise<void> {
      const requestId = ++navigationValidationRequest
      const expectedRevision = navigationDraft.value?.draftRevision ?? 0
      await run(async () => {
        const api = getSystemDataManagementApi()
        if (api !== null) {
          const validation = await api.validateNavigation()
          if (
            requestId === navigationValidationRequest &&
            validation.draftRevision === expectedRevision &&
            navigationDraft.value?.draftRevision === expectedRevision
          ) {
            navigationValidation.value = validation
          }
        }
      })
    }

    async function publishNavigation(): Promise<void> {
      const requestId = ++navigationWriteRequest
      const expectedRevision = navigationDraft.value?.draftRevision ?? 0
      let publishedRevision: number | null = null
      const written = await run(async () => {
        const api = getSystemDataManagementApi()
        if (api !== null) {
          const response = await api.publishNavigation(expectedRevision)
          publishedRevision = response.revision
          navigationPublishedRevision.value = response.revision
        }
      })
      if (written && publishedRevision !== null && requestId === navigationWriteRequest)
        await refreshNavigationAfterWrite(publishedRevision, requestId)
    }

    async function rollbackNavigation(): Promise<void> {
      const requestId = ++navigationWriteRequest
      const expectedRevision = navigationDraft.value?.draftRevision ?? 0
      let publishedRevision: number | null = null
      const written = await run(async () => {
        const api = getSystemDataManagementApi()
        if (api !== null) {
          const response = await api.rollbackNavigation(expectedRevision)
          publishedRevision = response.revision
          navigationPublishedRevision.value = response.revision
        }
      })
      if (written && publishedRevision !== null && requestId === navigationWriteRequest)
        await refreshNavigationAfterWrite(publishedRevision, requestId)
    }

    async function refreshNavigationAfterWrite(
      publishedRevision: number,
      requestId: number,
    ): Promise<void> {
      invalidateNavigationValidation()
      const api = getSystemDataManagementApi()
      const navigationCopy = systemDataPageCopy(localization.locale, 'navigation')
      if (api === null) {
        error.value = navigationCopy.runtimeRefreshFailed
        return
      }
      try {
        const nextDraft = await api.getNavigationDraft()
        if (requestId !== navigationWriteRequest) return
        navigationDraft.value = nextDraft
        const refreshed = await useSystemDataRuntimeStore().refresh('Pc', publishedRevision)
        if (requestId !== navigationWriteRequest) return
        if (!refreshed) throw new Error('runtime refresh failed')
      } catch {
        error.value = navigationCopy.runtimeRefreshFailed
      }
    }

    async function registerInitialization(
      request: RegisterServiceInitializationRequest,
    ): Promise<void> {
      await run(async () => {
        const api = getSystemDataManagementApi()
        if (api !== null) {
          initializationRegistration.value = await api.registerInitialization(request)
          await load('service-initialization')
        }
      }, true)
    }

    async function loadInitializationRegistration(
      serviceKey: string,
      moduleKey: string,
    ): Promise<void> {
      const requestId = ++initializationRegistrationRequest
      initializationRegistration.value = null
      await run(async () => {
        const api = getSystemDataManagementApi()
        if (api !== null && typeof api.getInitializationRegistration === 'function') {
          const registration = await api.getInitializationRegistration(serviceKey, moduleKey)
          if (requestId === initializationRegistrationRequest)
            initializationRegistration.value = registration
        }
      })
    }

    async function loadInitializationGates(planNId: string): Promise<void> {
      const requestId = ++initializationGateRequest
      initializationSelectedPlanNId.value = planNId
      initializationApprovals.value = []
      initializationBackupEvidence.value = null
      await run(async () => {
        const api = getSystemDataManagementApi()
        if (
          api !== null &&
          typeof api.listInitializationApprovals === 'function' &&
          typeof api.listInitializationBackupEvidence === 'function'
        ) {
          await refreshInitializationGates(api, planNId, requestId)
        }
      })
    }

    async function selectInitializationPlan(planNId: string): Promise<void> {
      await loadInitializationGates(planNId)
    }

    function clearInitializationPlanSelection(): void {
      initializationGateRequest++
      initializationSelectedPlanNId.value = ''
      initializationApprovals.value = []
      initializationBackupEvidence.value = null
    }

    async function loadInitializationPolicy(): Promise<void> {
      await run(async () => {
        const api = getSystemDataManagementApi()
        if (api !== null && typeof api.getInitializationPolicy === 'function') {
          initializationPolicy.value = await api.getInitializationPolicy()
        }
      })
    }

    async function createInitializationPlan(
      request: CreateInitializationPlanRequest,
    ): Promise<void> {
      await run(async () => {
        const api = getSystemDataManagementApi()
        if (api !== null) {
          const key = idempotencyKey(planIdempotencyKeys, 'plan', request)
          await api.createInitializationPlan(request, key)
          forgetIdempotencyKey(planIdempotencyKeys, request)
          await load('service-initialization')
        }
      }, true)
    }

    async function createApproval(planNId: string, reason: string): Promise<void> {
      const requestId = initializationGateRequest
      await run(async () => {
        const api = getSystemDataManagementApi()
        if (api !== null) {
          const approval = await api.createApproval(planNId, { reason })
          if (
            requestId === initializationGateRequest &&
            initializationSelectedPlanNId.value === planNId
          ) {
            initializationApprovals.value = [approval, ...initializationApprovals.value]
          }
        }
      }, true)
    }

    async function createBackupEvidence(planNId: string, reference: string): Promise<void> {
      const requestId = initializationGateRequest
      await run(async () => {
        const api = getSystemDataManagementApi()
        if (api !== null) {
          const evidence = await api.createBackupEvidence(planNId, {
            backupProvider: '管理员登记',
            backupReference: reference,
          })
          if (
            requestId === initializationGateRequest &&
            initializationSelectedPlanNId.value === planNId &&
            evidence.planNId === planNId
          )
            initializationBackupEvidence.value = evidence
        }
      }, true)
    }

    async function verifyBackupEvidence(evidenceNId: string): Promise<void> {
      const requestId = initializationGateRequest
      const planNId = initializationSelectedPlanNId.value
      await run(async () => {
        const api = getSystemDataManagementApi()
        if (api !== null) {
          const evidence = await api.verifyBackupEvidence(evidenceNId)
          if (
            requestId === initializationGateRequest &&
            initializationSelectedPlanNId.value === planNId &&
            evidence.planNId === planNId
          )
            initializationBackupEvidence.value = evidence
        }
      }, true)
    }

    async function applyInitialization(request: ApplyInitializationRequest): Promise<void> {
      await run(async () => {
        const api = getSystemDataManagementApi()
        if (api !== null) {
          const key = idempotencyKey(applyIdempotencyKeys, 'apply', request)
          await api.applyInitialization(request, key)
          forgetIdempotencyKey(applyIdempotencyKeys, request)
          await load('service-initialization')
        }
      }, true)
    }

    async function cancelInitialization(operationNId: string): Promise<void> {
      await run(async () => {
        const api = getSystemDataManagementApi()
        if (api !== null) {
          await api.cancelInitialization(operationNId)
          await load('service-initialization')
        }
      }, true)
    }

    return {
      loading,
      error,
      traceId,
      organizationTree,
      organizationTreeLoading,
      organizationDetailLoading,
      organizationDetailError,
      selectedOrganizationNId,
      selectedOrganization,
      movePreview,
      positions,
      positionPage,
      selectOrganization,
      clearOrganizationSelection,
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
      navigationDefaultPreview,
      resources,
      navigationPublishedRevision,
      addNavigationNode,
      previewNavigationDefaults,
      importNavigationDefaults,
      updateNavigationNode,
      deleteNavigationNode,
      restoreNavigationNode,
      navigationValidation,
      features,
      services,
      themePolicy,
      initializationRegistrations,
      initializationPlans,
      initializationOperations,
      initializationRegistration,
      initializationPolicy,
      initializationApprovals,
      initializationBackupEvidence,
      initializationSelectedPlanNId,
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
      loadInitializationRegistration,
      clearInitializationRegistrationSelection,
      loadInitializationPolicy,
      selectInitializationPlan,
      clearInitializationPlanSelection,
      loadInitializationGates,
      createInitializationPlan,
      createApproval,
      createBackupEvidence,
      verifyBackupEvidence,
      applyInitialization,
      cancelInitialization,
      retry: load,
    }
  },
)
