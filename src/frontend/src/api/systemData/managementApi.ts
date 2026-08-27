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
  InitializationOperationDto,
  InitializationPlanDto,
  InitializationRegistrationDto,
  InitializationRegistrationSummaryDto,
  EnqueueInitializationOperationDto,
  ManagementQuery,
  MoveOrganizationRequest,
  NavigationDraftDto,
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
    addNavigationNode: (request: CreateNavigationNodeRequest) =>
      client.post<NavigationNodeDto>(`${BASE}/navigation/draft/nodes`, request),
    updateNavigationNode: (nId, request: UpdateNavigationNodeRequest) =>
      client.put<NavigationNodeDto>(`${BASE}/navigation/draft/nodes/${id(nId)}`, request),
    deleteNavigationNode: (nId) => client.delete<void>(`${BASE}/navigation/draft/nodes/${id(nId)}`),
    validateNavigation: () => client.post<NavigationValidationDto>(`${BASE}/navigation/validate`),
    publishNavigation: () => client.post<{ revision: number }>(`${BASE}/navigation/publish`),
    rollbackNavigation: () => client.post<{ revision: number }>(`${BASE}/navigation/rollback`),
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
    updateThemePolicy: (request) => client.put<ThemePolicyDto>(`${BASE}/theme-policy`, request),
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
    createApproval: (planNId, request: CreateApprovalRequest) =>
      client.post(`${BASE}/service-initialization/plans/${id(planNId)}/approvals`, request),
    createBackupEvidence: (planNId, request: CreateBackupEvidenceRequest) =>
      client.post(`${BASE}/service-initialization/plans/${id(planNId)}/backup-evidence`, request),
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
