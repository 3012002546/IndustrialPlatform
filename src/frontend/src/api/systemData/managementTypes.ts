import type { ThemePolicyDto } from './types'

export type { FeatureRuntimeDto, ThemePolicyDto } from './types'

export interface PageResultDto<T> {
  items: T[]
  total: number
  pageIndex: number
  pageSize: number
}

export interface OrganizationNodeDto {
  tenantNId: string
  nId: string
  name: string
  type: string
  status: string
  parentOrganizationNId: string | null
  displayOrder: number
  children: OrganizationNodeDto[]
}

export interface OrganizationDetailDto extends Omit<OrganizationNodeDto, 'children'> {
  organizationRevision: number
  optimisticVersion: number
  concurrencyVersion: string
}

export interface OrganizationMovePreviewDto {
  nId: string
  organizationRevision: number
  subtreeOrganizationCount: number
  subtreePositionCount: number
  subtreeAssignmentCount: number
  affectedCount: number
  previewedOn: string
  expectedOptimisticVersion: number
  expectedConcurrencyVersion: string
}

export interface MoveOrganizationRequest {
  targetParentOrganizationNId?: string
  previewOrganizationRevision?: number
  expectedOptimisticVersion?: number
  expectedConcurrencyVersion?: string
  reason?: string
}

export interface SetOrganizationStatusRequest {
  status: string
  reason?: string
}

export interface PositionDto {
  tenantNId: string
  nId: string
  organizationNId: string
  organizationName: string
  name: string
  description: string
  status: string
  displayOrder: number
  optimisticVersion: number
  concurrencyVersion: string
}

export interface CreatePositionRequest {
  nId?: string
  organizationNId?: string
  name?: string
  description?: string
  displayOrder?: number
}

export interface UpdatePositionRequest {
  name?: string
  description?: string
  displayOrder?: number
  expectedOptimisticVersion?: number
  expectedConcurrencyVersion?: string
}

export interface SetPositionStatusRequest {
  status: string
  reason?: string
}

export interface AssignmentDto {
  tenantNId: string
  nId: string
  userNId: string
  userDisplayNameSnapshot: string
  organizationNId: string
  positionNId: string
  positionName: string
  isPrimary: boolean
  effectiveFrom: string
  effectiveTo: string | null
  state: string
  cancelledOn: string | null
  cancelReason: string | null
  optimisticVersion: number
  concurrencyVersion: string
}

export interface CreateAssignmentRequest {
  nId?: string
  positionNId?: string
  isPrimary?: boolean
  effectiveFrom?: string
  effectiveTo?: string
}

export interface UpdateScheduledAssignmentRequest {
  effectiveFrom?: string
  effectiveTo?: string
  expectedOptimisticVersion?: number
  expectedConcurrencyVersion?: string
}

export interface CancelAssignmentRequest {
  reason?: string
}

export interface SetPrimaryAssignmentRequest {
  targetAssignmentNId?: string
  effectiveOn?: string
  reason?: string
  expectedUserAssignmentRevision?: number
}

export interface UiResourceDto {
  resourceNId: string
  ownerModuleNId: string
  manifestVersion: string
  type: string
  name: string
  routeName: string | null
  requiredPermissionNId: string | null
  supportedTerminals: string[]
  status: string
}

export interface NavigationNodeDto {
  nodeNId: string
  kind: string
  label: string
  parentNodeNId: string | null
  resourceNId: string | null
  featureNId: string | null
  iconKey: string | null
  displayOrder: number
  visibleTerminals: string[]
  status: string
  actionResourceNIds: string[]
  children: NavigationNodeDto[]
}

export interface NavigationDraftDto {
  draftRevision: number
  nodes: NavigationNodeDto[]
}

export interface NavigationDefaultImportItemDto {
  nodeNId: string
  label: string
  parentNodeNId: string | null
  kind: string
  level: number
  action: string
  reason: string
}

export interface NavigationDefaultImportPreviewDto {
  draftRevision: number
  items: NavigationDefaultImportItemDto[]
}

export interface ImportNavigationDefaultsRequest {
  expectedDraftRevision: number
}

export interface CreateNavigationNodeRequest {
  nodeNId: string
  kind: string
  label: string
  parentNodeNId: string | null
  navigationSetNId: string
  resourceNId: string | null
  featureNId: string | null
  iconKey: string | null
  visibleTerminals: string[]
  actionResourceNIds: string[]
  displayOrder: number
  expectedDraftRevision: number
}

export interface UpdateNavigationNodeRequest {
  label: string
  parentNodeNId: string | null
  resourceNId: string | null
  featureNId: string | null
  iconKey: string | null
  visibleTerminals: string[]
  actionResourceNIds: string[]
  displayOrder: number
  expectedDraftRevision: number
}

export interface NavigationValidationDto {
  draftRevision: number
  isValid: boolean
  errors: Array<{
    code: string
    message: string
    nodeNId: string | null
    resourceNId?: string | null
    moduleNId?: string | null
    manifestVersion?: string | null
    manifestChecksum?: string | null
    trustedReceiptVersion?: string | null
    trustedReceiptChecksum?: string | null
    trustedReceiptVerified?: boolean | null
    receiptDetails?: Array<{
      resourceNId: string
      moduleNId: string
      manifestVersion?: string | null
      manifestChecksum?: string | null
      trustedReceiptVersion?: string | null
      trustedReceiptChecksum?: string | null
      trustedReceiptVerified: boolean
    }>
  }>
}

export interface ThemePolicyUpdateRequest {
  expectedPolicyRevision: number
  allowedPalettes: string[]
  allowedModes: string[]
  allowedPcDensities: string[]
  defaultPalette: string
  defaultMode: string
  defaultPcDensity: string
}

export interface FeatureDefinitionDto {
  featureNId: string
  ownerModuleNId: string
  name: string
  description: string | null
  defaultEnabled: boolean
  status: string
  featureRevision: number
  effectiveEnabled: boolean
}

export interface SetFeatureOverrideRequest {
  mode?: string
  reason?: string
}

export interface ServiceCatalogDto {
  serviceNId: string
  kind: string
  name: string
  description: string | null
  entryPoint: string
  gatewayPathPrefix: string | null
  healthPath: string | null
  ownerOrganizationNId: string | null
  ownerOrganizationNameSnapshot: string | null
  ownerDisplaySnapshot: string | null
  supportedTerminals: string[]
  status: string
  source: string
  degraded: boolean
}

export interface CreateServiceCatalogRequest {
  serviceNId?: string
  name?: string
  description?: string
  entryPoint?: string
  supportedTerminals?: string[]
  ownerOrganizationNId?: string
  technicalLeadUserNId?: string
  technicalOwnerUserNId?: string
}

export interface UpdateServiceCatalogRequest {
  name?: string
  description?: string
  entryPoint?: string
  ownerOrganizationNId?: string
  technicalLeadUserNId?: string
  technicalOwnerUserNId?: string
  status?: string
}

export interface SetServiceCatalogStatusRequest {
  status: string
}

export interface InitializationRegistrationSummaryDto {
  serviceKey: string
  moduleKey: string
  usesServiceInitializer?: boolean
  logicalDatabaseName: string
  provider: string
  migrationVersion: string
  desiredState: string
  status: string
  topologyRevision: string
  registeredOn: string
  lastUpdatedOn: string
}

export interface InitializationSeedSetDto {
  seedKey: string
  seedVersion: string
  seedClass: string
  scope: string
  seedArtifactId: string
  seedChecksum: string
  seedSignature: string | null
  requiredForReadiness: boolean
  allowedEnvironments: string[] | string
  dependsOnMigrationVersion: string | null
  dependsOnSeedKeys: string[] | string
  bootstrapPolicy: string | null
}

export interface InitializationRegistrationDto extends InitializationRegistrationSummaryDto {
  tenantNId: string
  environmentNId: string
  physicalDatabaseName: string
  isSharedPhysicalDatabase: boolean
  topologyMode: string
  migrationArtifactId: string
  artifactChecksum: string
  artifactSignature: string | null
  ownerNId: string
  autoProvision: boolean
  autoMigrate: boolean
  manifestVersion: string
  seedSets: InitializationSeedSetDto[] | null
  usesServiceInitializer?: boolean
}

export interface InitializationEnvironmentPolicyDto {
  tenantNId: string
  environmentNId: string
  environmentKind: string
  approvalRequired: boolean
  backupRequired: boolean
  planTtlSeconds: number
  planTimeoutSeconds: number
  applyTimeoutSeconds: number
  maxPreMigrationRetries: number
  policyRevision: number
  initializationPolicy: string
  isExplicit: boolean
}

export interface InitializationBackupEvidenceDto {
  tenantNId: string
  evidenceNId: string
  planNId: string
  planChecksum: string
  targetStateFingerprint: string
  backupProvider: string
  backupReference: string
  capturedOn: string
  verifiedOn: string | null
  retentionUntil: string
  verifiedByUserNId: string | null
  status: string
}

export interface InitializationApprovalDto {
  tenantNId: string
  approvalNId: string
  planNId: string
  planChecksum: string
  targetStateFingerprint: string
  approvedByUserNId: string
  reason: string | null
  approvedOn: string
  expiresOn: string
  status: string
}

export interface InitializationPlanDto {
  tenantNId: string
  planNId: string
  environmentNId: string
  serviceKey: string
  moduleKey: string
  requestedMigrationVersion: string
  currentMigrationVersion: string
  targetStateFingerprint: string
  planChecksum: string
  riskLevel: string
  destructiveChangeDetected: boolean
  requiredPolicies: string
  expiresOn: string
  isExpired: boolean
  createdByUserNId: string
  createdOn: string
  steps: Array<{
    sequence: number
    stepKind: string
    riskLevel: string
    inputSummary: string | null
    preconditionSummary: string | null
    postconditionSummary: string | null
  }>
}

export interface InitializationOperationDto {
  tenantNId: string
  operationNId: string
  kind: string
  environmentNId: string
  serviceKey: string
  moduleKey: string
  planNId: string | null
  requestedVersion: string
  idempotencyKey: string
  status: string
  phase: string
  attempt: number
  leaseOwner: string | null
  queuedOn: string
  startedOn: string | null
  completedOn: string | null
  timeoutOn: string
  sanitizedErrorCode: string | null
  sanitizedErrorSummary: string | null
  traceId: string
  createdByUserNId: string
  steps: Array<{ sequence: number; phase: string; status: string; attempt: number }>
  seedObservations: Array<Record<string, unknown>> | null
}

export interface EnqueueInitializationOperationDto {
  operationNId: string
  kind: string
  status: string
  phase: string
  acceptedOn: string
}

export interface RegisterServiceInitializationRequest {
  serviceKey?: string
  moduleKey?: string
  logicalDatabaseName?: string
  provider?: string
  topologyMode?: string
  migrationArtifactId?: string
  requestedVersion?: string
  artifactChecksum?: string
  artifactSignature?: string
  desiredState?: string
  autoProvision?: boolean
  autoMigrate?: boolean
  ownerNId?: string
  manifestVersion?: string
  seedSets?: Array<Record<string, string | boolean | undefined>>
}

export interface CreateInitializationPlanRequest {
  serviceKey?: string
  moduleKey?: string
  requestedVersion?: string
  desiredState?: string
}

export interface CreateApprovalRequest {
  reason?: string
}

export interface CreateBackupEvidenceRequest {
  backupProvider?: string
  backupReference?: string
  retentionUntil?: string
}

export interface ApplyInitializationRequest {
  planNId?: string
  moduleKey?: string
  requestedVersion?: string
}

export interface CreateOrganizationRequest {
  nId?: string
  name?: string
  type?: string
  parentOrganizationNId?: string
  displayOrder?: number
}

export interface UpdateOrganizationRequest {
  name?: string
  displayOrder?: number
  expectedOptimisticVersion?: number
  expectedConcurrencyVersion?: string
}

export interface ManagementQuery {
  [key: string]: string | number | boolean | string[] | undefined
}

export interface SystemDataExportParams extends ManagementQuery {
  search?: string | undefined
  kind?: string | undefined
  status?: string | undefined
  organizationNId?: string | undefined
  userNId?: string | undefined
  quantity?: number | 'all' | undefined
  sortField?: string | undefined
  sortOrder?: 'asc' | 'desc' | undefined
  columns?: string[] | undefined
}

export interface SystemDataManagementApi {
  exportOrganizations(params: SystemDataExportParams): Promise<Blob>
  exportPositions(params: SystemDataExportParams): Promise<Blob>
  exportAssignments(userNId: string, params: SystemDataExportParams): Promise<Blob>
  exportFeatures(params: SystemDataExportParams): Promise<Blob>
  exportServices(params: SystemDataExportParams): Promise<Blob>
  exportInitializationRegistrations(params: SystemDataExportParams): Promise<Blob>
  exportInitializationOperations(params: SystemDataExportParams): Promise<Blob>
  listOrganizationsTree(status?: string): Promise<OrganizationNodeDto[]>
  getOrganization(nId: string): Promise<OrganizationDetailDto>
  createOrganization(request: CreateOrganizationRequest): Promise<OrganizationDetailDto>
  updateOrganization(
    nId: string,
    request: UpdateOrganizationRequest,
  ): Promise<OrganizationDetailDto>
  previewOrganizationMove(
    nId: string,
    targetParentOrganizationNId?: string,
  ): Promise<OrganizationMovePreviewDto>
  moveOrganization(nId: string, request: MoveOrganizationRequest): Promise<OrganizationDetailDto>
  setOrganizationStatus(
    nId: string,
    request: SetOrganizationStatusRequest,
  ): Promise<OrganizationDetailDto>
  listPositions(params?: ManagementQuery): Promise<PageResultDto<PositionDto>>
  createPosition(request: CreatePositionRequest): Promise<PositionDto>
  updatePosition(nId: string, request: UpdatePositionRequest): Promise<PositionDto>
  setPositionStatus(nId: string, request: SetPositionStatusRequest): Promise<PositionDto>
  listAssignments(userNId: string): Promise<AssignmentDto[]>
  createAssignment(userNId: string, request: CreateAssignmentRequest): Promise<AssignmentDto>
  updateAssignment(nId: string, request: UpdateScheduledAssignmentRequest): Promise<AssignmentDto>
  endAssignment(nId: string): Promise<AssignmentDto>
  cancelAssignment(nId: string, request: CancelAssignmentRequest): Promise<AssignmentDto>
  setPrimaryAssignment(
    userNId: string,
    request: SetPrimaryAssignmentRequest,
  ): Promise<AssignmentDto[]>
  listResources(): Promise<UiResourceDto[]>
  getNavigationDraft(): Promise<NavigationDraftDto>
  previewNavigationDefaults(): Promise<NavigationDefaultImportPreviewDto>
  importNavigationDefaults(
    request: ImportNavigationDefaultsRequest,
  ): Promise<NavigationDefaultImportPreviewDto>
  addNavigationNode(request: CreateNavigationNodeRequest): Promise<NavigationNodeDto>
  updateNavigationNode(
    nId: string,
    request: UpdateNavigationNodeRequest,
  ): Promise<NavigationNodeDto>
  deleteNavigationNode(nId: string, expectedDraftRevision: number): Promise<void>
  restoreNavigationNode(nId: string, expectedDraftRevision: number): Promise<NavigationNodeDto>
  validateNavigation(): Promise<NavigationValidationDto>
  publishNavigation(expectedDraftRevision: number): Promise<{ revision: number }>
  rollbackNavigation(expectedDraftRevision: number): Promise<{ revision: number }>
  listFeatures(): Promise<FeatureDefinitionDto[]>
  setFeatureOverride(
    featureNId: string,
    request: SetFeatureOverrideRequest,
  ): Promise<FeatureDefinitionDto>
  listServiceCatalog(): Promise<ServiceCatalogDto[]>
  createServiceCatalog(request: CreateServiceCatalogRequest): Promise<ServiceCatalogDto>
  updateServiceCatalog(
    nId: string,
    request: UpdateServiceCatalogRequest,
  ): Promise<ServiceCatalogDto>
  setServiceCatalogStatus(
    nId: string,
    request: SetServiceCatalogStatusRequest,
  ): Promise<ServiceCatalogDto>
  getThemePolicy(): Promise<ThemePolicyDto>
  updateThemePolicy(request: ThemePolicyUpdateRequest): Promise<ThemePolicyDto>
  listInitializationRegistrations(): Promise<PageResultDto<InitializationRegistrationSummaryDto>>
  listInitializationPlans(): Promise<PageResultDto<InitializationPlanDto>>
  listInitializationOperations(): Promise<PageResultDto<InitializationOperationDto>>
  getInitializationRegistration(
    serviceKey: string,
    moduleKey: string,
  ): Promise<InitializationRegistrationDto>
  getInitializationPolicy(): Promise<InitializationEnvironmentPolicyDto>
  registerInitialization(
    request: RegisterServiceInitializationRequest,
  ): Promise<InitializationRegistrationDto>
  createInitializationPlan(
    request: CreateInitializationPlanRequest,
    idempotencyKey: string,
  ): Promise<EnqueueInitializationOperationDto>
  getInitializationPlan(planNId: string): Promise<InitializationPlanDto>
  listInitializationApprovals(planNId: string): Promise<InitializationApprovalDto[]>
  listInitializationBackupEvidence(planNId: string): Promise<InitializationBackupEvidenceDto | null>
  createApproval(
    planNId: string,
    request: CreateApprovalRequest,
  ): Promise<InitializationApprovalDto>
  createBackupEvidence(
    planNId: string,
    request: CreateBackupEvidenceRequest,
  ): Promise<InitializationBackupEvidenceDto>
  verifyBackupEvidence(evidenceNId: string): Promise<InitializationBackupEvidenceDto>
  applyInitialization(
    request: ApplyInitializationRequest,
    idempotencyKey: string,
  ): Promise<EnqueueInitializationOperationDto>
  cancelInitialization(operationNId: string): Promise<InitializationOperationDto>
}
