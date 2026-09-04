import type { HttpClient } from '@/api/httpClient'

import type {
  AssignmentDto,
  ApplyInitializationRequest,
  CancelAssignmentRequest,
  CreateApprovalRequest,
  CreateAssignmentRequest,
  CreateBackupEvidenceRequest,
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
  EnqueueInitializationOperationDto,
  ManagementQuery,
  MoveOrganizationRequest,
  NavigationDraftDto,
  NavigationDefaultImportPreviewDto,
  NavigationNodeDto,
  NavigationValidationDto,
  OrganizationDetailDto,
  OrganizationMovePreviewDto,
  OrganizationNodeDto,
  PageResultDto,
  PositionDto,
  RegisterServiceInitializationRequest,
  SetFeatureOverrideRequest,
  SetOrganizationStatusRequest,
  SetPositionStatusRequest,
  SetPrimaryAssignmentRequest,
  SetServiceCatalogStatusRequest,
  ServiceCatalogDto,
  SystemDataManagementApi,
  ThemePolicyDto,
  ThemePolicyUpdateRequest,
  UpdateNavigationNodeRequest,
  UpdatePositionRequest,
  UpdateScheduledAssignmentRequest,
  UpdateServiceCatalogRequest,
  UiResourceDto,
  UpdateOrganizationRequest,
} from './managementTypes'

const BASE = '/systemdata/api/v1'

function query(params: ManagementQuery = {}): string {
  const entries = Object.entries(params).filter(([, value]) => value !== undefined && value !== '')
  return entries.length === 0
    ? ''
    : `?${entries.map(([key, value]) => `${encodeURIComponent(key)}=${encodeURIComponent(String(value))}`).join('&')}`
}

const id = (value: string): string => encodeURIComponent(value)

export function createSystemDataManagementApi(client: HttpClient): SystemDataManagementApi {
  return {
    exportOrganizations: (params) => {
      if (client.getBlob === undefined)
        return Promise.reject(new Error('当前 HTTP 客户端不支持文件下载'))
      return client.getBlob(`${BASE}/organizations/tree/export${query(params)}`)
    },
    exportPositions: (params) => {
      if (client.getBlob === undefined)
        return Promise.reject(new Error('当前 HTTP 客户端不支持文件下载'))
      return client.getBlob(`${BASE}/positions/export${query(params)}`)
    },
    exportAssignments: (userNId, params) => {
      if (client.getBlob === undefined)
        return Promise.reject(new Error('当前 HTTP 客户端不支持文件下载'))
      return client.getBlob(`${BASE}/users/${id(userNId)}/assignments/export${query(params)}`)
    },
    exportFeatures: (params) => {
      if (client.getBlob === undefined)
        return Promise.reject(new Error('当前 HTTP 客户端不支持文件下载'))
      return client.getBlob(`${BASE}/features/export${query(params)}`)
    },
    exportServices: (params) => {
      if (client.getBlob === undefined)
        return Promise.reject(new Error('当前 HTTP 客户端不支持文件下载'))
      return client.getBlob(`${BASE}/service-catalog/export${query(params)}`)
    },
    exportInitializationRegistrations: (params) => {
      if (client.getBlob === undefined)
        return Promise.reject(new Error('当前 HTTP 客户端不支持文件下载'))
      return client.getBlob(`${BASE}/service-initialization/registrations/export${query(params)}`)
    },
    exportInitializationOperations: (params) => {
      if (client.getBlob === undefined)
        return Promise.reject(new Error('当前 HTTP 客户端不支持文件下载'))
      return client.getBlob(`${BASE}/service-initialization/operations/export${query(params)}`)
    },
    listOrganizationsTree: (status) =>
      client.get<OrganizationNodeDto[]>(`${BASE}/organizations/tree${query({ status })}`),
    getOrganization: (nId) => client.get<OrganizationDetailDto>(`${BASE}/organizations/${id(nId)}`),
    createOrganization: (request: CreateOrganizationRequest) =>
      client.post<OrganizationDetailDto>(`${BASE}/organizations`, request),
    updateOrganization: (nId, request: UpdateOrganizationRequest) =>
      client.put<OrganizationDetailDto>(`${BASE}/organizations/${id(nId)}`, request),
    previewOrganizationMove: (nId, targetParentOrganizationNId) =>
      client.post<OrganizationMovePreviewDto>(`${BASE}/organizations/${id(nId)}/move-preview`, {
        targetParentOrganizationNId,
      }),
    moveOrganization: (nId, request: MoveOrganizationRequest) =>
      client.post<OrganizationDetailDto>(`${BASE}/organizations/${id(nId)}/move`, request),
    setOrganizationStatus: (nId, request: SetOrganizationStatusRequest) =>
      client.put<OrganizationDetailDto>(`${BASE}/organizations/${id(nId)}/status`, request),
    listPositions: (params) =>
      client.get<PageResultDto<PositionDto>>(`${BASE}/positions${query(params)}`),
    createPosition: (request: CreatePositionRequest) =>
      client.post<PositionDto>(`${BASE}/positions`, request),
    updatePosition: (nId, request: UpdatePositionRequest) =>
      client.put<PositionDto>(`${BASE}/positions/${id(nId)}`, request),
    setPositionStatus: (nId, request: SetPositionStatusRequest) =>
      client.put<PositionDto>(`${BASE}/positions/${id(nId)}/status`, request),
    listAssignments: (userNId) =>
      client.get<AssignmentDto[]>(`${BASE}/users/${id(userNId)}/assignments`),
    createAssignment: (userNId, request: CreateAssignmentRequest) =>
      client.post<AssignmentDto>(`${BASE}/users/${id(userNId)}/assignments`, request),
    updateAssignment: (nId, request: UpdateScheduledAssignmentRequest) =>
      client.put<AssignmentDto>(`${BASE}/assignments/${id(nId)}`, request),
    endAssignment: (nId) => client.post<AssignmentDto>(`${BASE}/assignments/${id(nId)}/end`),
    cancelAssignment: (nId, request: CancelAssignmentRequest) =>
      client.post<AssignmentDto>(`${BASE}/assignments/${id(nId)}/cancel`, request),
    setPrimaryAssignment: (userNId, request: SetPrimaryAssignmentRequest) =>
      client.post<AssignmentDto[]>(`${BASE}/users/${id(userNId)}/primary-assignment`, request),
    listResources: () => client.get<UiResourceDto[]>(`${BASE}/resources`),
    getNavigationDraft: () => client.get<NavigationDraftDto>(`${BASE}/navigation/draft`),
    previewNavigationDefaults: () =>
      client.get<NavigationDefaultImportPreviewDto>(`${BASE}/navigation/defaults/preview`),
    importNavigationDefaults: (request) =>
      client.post<NavigationDefaultImportPreviewDto>(`${BASE}/navigation/defaults/import`, request),
    addNavigationNode: (request: CreateNavigationNodeRequest) =>
      client.post<NavigationNodeDto>(`${BASE}/navigation/draft/nodes`, request),
    updateNavigationNode: (nId, request: UpdateNavigationNodeRequest) =>
      client.put<NavigationNodeDto>(`${BASE}/navigation/draft/nodes/${id(nId)}`, request),
    deleteNavigationNode: (nId, expectedDraftRevision) =>
      client.delete<void>(
        `${BASE}/navigation/draft/nodes/${id(nId)}${query({ expectedDraftRevision })}`,
      ),
    restoreNavigationNode: (nId, expectedDraftRevision) =>
      client.post<NavigationNodeDto>(
        `${BASE}/navigation/draft/nodes/${id(nId)}/restore${query({ expectedDraftRevision })}`,
      ),
    validateNavigation: () => client.post<NavigationValidationDto>(`${BASE}/navigation/validate`),
    publishNavigation: (expectedDraftRevision) =>
      client.post<{ revision: number }>(
        `${BASE}/navigation/publish${query({ expectedDraftRevision })}`,
      ),
    rollbackNavigation: (expectedDraftRevision) =>
      client.post<{ revision: number }>(
        `${BASE}/navigation/rollback${query({ expectedDraftRevision })}`,
      ),
    listFeatures: () => client.get<FeatureDefinitionDto[]>(`${BASE}/features`),
    setFeatureOverride: (featureNId, request: SetFeatureOverrideRequest) =>
      client.put(`${BASE}/features/${id(featureNId)}/override`, request),
    listServiceCatalog: () => client.get<ServiceCatalogDto[]>(`${BASE}/service-catalog`),
    createServiceCatalog: (request: CreateServiceCatalogRequest) =>
      client.post<ServiceCatalogDto>(`${BASE}/service-catalog`, request),
    updateServiceCatalog: (nId, request: UpdateServiceCatalogRequest) =>
      client.put<ServiceCatalogDto>(`${BASE}/service-catalog/${id(nId)}`, request),
    setServiceCatalogStatus: (nId, request: SetServiceCatalogStatusRequest) =>
      client.put<ServiceCatalogDto>(`${BASE}/service-catalog/${id(nId)}/status`, request),
    getThemePolicy: () => client.get<ThemePolicyDto>(`${BASE}/theme-policy`),
    updateThemePolicy: (request: ThemePolicyUpdateRequest) =>
      client.put<ThemePolicyDto>(`${BASE}/theme-policy`, request),
    listInitializationRegistrations: () =>
      client.get<PageResultDto<InitializationRegistrationSummaryDto>>(
        `${BASE}/service-initialization/registrations`,
      ),
    listInitializationPlans: () =>
      client.get<PageResultDto<InitializationPlanDto>>(`${BASE}/service-initialization/plans`),
    listInitializationOperations: () =>
      client.get<PageResultDto<InitializationOperationDto>>(
        `${BASE}/service-initialization/operations`,
      ),
    getInitializationRegistration: (serviceKey, moduleKey) =>
      client.get<InitializationRegistrationDto>(
        `${BASE}/service-initialization/registrations/${id(serviceKey)}/${id(moduleKey)}`,
      ),
    getInitializationPolicy: () =>
      client.get<InitializationEnvironmentPolicyDto>(
        `${BASE}/service-initialization/environment-policy`,
      ),
    registerInitialization: (request: RegisterServiceInitializationRequest) =>
      client.put<InitializationRegistrationDto>(
        `${BASE}/service-initialization/registrations/${id(request.serviceKey ?? '')}/${id(request.moduleKey ?? '')}`,
        request,
      ),
    createInitializationPlan: (request: CreateInitializationPlanRequest, idempotencyKey: string) =>
      client.post<EnqueueInitializationOperationDto>(
        `${BASE}/service-initialization/plans`,
        request,
        { headers: { 'Idempotency-Key': idempotencyKey } },
      ),
    getInitializationPlan: (planNId) =>
      client.get<InitializationPlanDto>(`${BASE}/service-initialization/plans/${id(planNId)}`),
    listInitializationApprovals: (planNId) =>
      client.get<InitializationApprovalDto[]>(
        `${BASE}/service-initialization/plans/${id(planNId)}/approvals`,
      ),
    listInitializationBackupEvidence: (planNId) =>
      client.get<InitializationBackupEvidenceDto | null>(
        `${BASE}/service-initialization/plans/${id(planNId)}/backup-evidence`,
      ),
    createApproval: (planNId, request: CreateApprovalRequest) =>
      client.post<InitializationApprovalDto>(
        `${BASE}/service-initialization/plans/${id(planNId)}/approvals`,
        request,
      ),
    createBackupEvidence: (planNId, request: CreateBackupEvidenceRequest) =>
      client.post<InitializationBackupEvidenceDto>(
        `${BASE}/service-initialization/plans/${id(planNId)}/backup-evidence`,
        request,
      ),
    verifyBackupEvidence: (evidenceNId) =>
      client.post<InitializationBackupEvidenceDto>(
        `${BASE}/service-initialization/backup-evidence/${id(evidenceNId)}/verify`,
      ),
    applyInitialization: (request: ApplyInitializationRequest, idempotencyKey: string) =>
      client.post<EnqueueInitializationOperationDto>(
        `${BASE}/service-initialization/operations/apply`,
        request,
        {
          headers: { 'Idempotency-Key': idempotencyKey },
        },
      ),
    cancelInitialization: (operationNId) =>
      client.post<InitializationOperationDto>(
        `${BASE}/service-initialization/operations/${id(operationNId)}/cancel`,
      ),
  }
}
