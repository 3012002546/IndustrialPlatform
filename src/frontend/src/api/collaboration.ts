import type { HttpClient, RequestOptions } from '@/api/httpClient'

export interface CollaborationDirectoryUser {
  userNId: string
  displayName: string
  canStart: boolean
}

export interface CollaborationDirectoryPage {
  items: CollaborationDirectoryUser[]
  nextCursor: string | null
}

export interface CreateConversationRequest {
  peerUserNId: string
  requestNId?: string
}

export interface ConversationSummary {
  conversationNId: string
  peerUserNId: string
  peerDisplayName: string
  status: string
  lastMessageSequence: number
  lastMessageNId: string | null
  lastMessageOn: string | null
  lastMessagePreview?: ConversationLastMessagePreview | null
  unreadCount: number
  visibilityState?: 'Visible' | 'Hidden'
  presence?: Presence
  optimisticVersion: number
  concurrencyVersion: string
  projectionVersion?: number
}

export interface ConversationLastMessagePreview {
  messageNId: string
  sequence: number
  acceptedOn: string
  messageType: 'Text' | 'Image' | 'File' | string
  state: 'Accepted' | 'Retracted' | 'Restricted' | string
  text: string | null
}

export interface ConversationMember {
  userNId: string
  displayName: string
  visibilityState: string
  joinedOn: string
  lastReadSequence: number
  unreadCount: number
}

export interface ReadCursor {
  lastReadSequence: number
  unreadCount: number
  projectionVersion: number
  concurrencyVersion: string
}

export interface PersonalMessageVisibility {
  messageNId: string
  hidden: boolean
}

export interface ConversationDetail {
  conversationNId: string
  status: string
  currentMember: ConversationMember
  peerMember: ConversationMember
  lastMessageSequence: number
  retentionFloorSequence: number
  optimisticVersion: number
  concurrencyVersion: string
}

export interface ConversationPage {
  items: ConversationSummary[]
  nextCursor: string | null
  total: number
}

export interface SendMessageRequest {
  clientMessageNId: string
  messageType: 'Text' | 'Image' | 'File'
  textContent?: string
  replyToMessageNId?: string
  attachmentNId?: string
  idempotencyKey?: string
}

export interface MessageAttachment {
  attachmentNId: string
  fileNId: string | null
  fileName: string
  contentType: string
  size: number
  fileState: string
  referenceState: string
}

export interface Message {
  messageNId: string
  conversationNId: string
  sequence: number
  senderUserNId: string
  senderDisplayName: string
  clientMessageNId: string
  messageType: 'Text' | 'Image' | 'File' | string
  textContent: string | null
  replyToMessageNId: string | null
  attachment: MessageAttachment | null
  acceptedOn: string
  retractedOn: string | null
  retractionReason: string | null
  state: string
  messageStateVersion: number
}

export interface MessagePage {
  items: Message[]
  highWatermarkSequence: number
  retentionFloorSequence: number
  earliestAvailableSequence?: number
  nextCursor: string | null
  nextAfterSequence?: number | null
  hasMore: boolean
}

export interface AttachmentIntentRequest {
  conversationNId?: string
  attachmentNId?: string
  fileName: string
  mediaType: string
  sizeBytes: number
  contentType?: string
  size?: number
  sha256?: string
  requestNId: string
}

export interface AttachmentIntent {
  attachmentNId: string
  conversationNId: string
  uploadSessionNId: string | null
  transportId: string | null
  fileName: string
  contentType: string
  mediaType?: string
  size: number
  sizeBytes?: number
  state: string
  expiresOn: string
  fileUpload?: {
    session: {
      sessionNId: string
      transportId: string
      fileName: string
      mediaType: string
      sizeBytes: number
      offset: number
      writerEpoch: number
      state: string
      expiresOn: string
      resumeTicket?: string | null
    }
    uploadUrl: string
  }
}

export interface AttachmentUploadSession {
  sessionNId: string
  transportId: string
  fileName: string
  mediaType: string
  sizeBytes: number
  offset: number
  writerEpoch: number
  state: string
  fileNId?: string | null
  expiresOn: string
  resumeTicket?: string | null
}

export interface AttachmentAuthorization {
  attachmentNId: string
  fileNId: string
  referenceNId: string
  state: string
}

export interface Presence {
  userNId: string
  state: 'Online' | 'Offline' | 'Unknown' | string
  observedOn: string
  expiresOn: string
}

export interface ComplianceScope {
  conversationNId?: string
  conversationNIds?: string[]
  userNIds?: string[]
  from?: string
  until?: string
  schemaVersion?: number
  scopeType?: 'Conversation' | 'TimeRange' | 'MessageSet'
  fromOn?: string
  toOn?: string
  messageNIds?: string[]
}

export interface ComplianceSearchRequest {
  scope: ComplianceScope
  keyword?: string
  cursor?: string
  pageSize?: number
  requestNId?: string
  reason?: string
  caseReference?: string
  readOriginal?: boolean
}

export interface ComplianceMessage {
  conversationNId: string
  messageNId: string
  sequence: number
  senderUserNId: string
  messageType: string
  textContent: string | null
  acceptedOn: string
  state: string
}

export interface ComplianceSearchPage {
  items: ComplianceMessage[]
  nextCursor: string | null
  scopeChecksum: string
  remainingViewBudget: number
  expiresOn?: string
}

export interface ComplianceDownloadAuthorization {
  authorization: { method: string; url: string; requiresAuthentication: boolean }
  expiresOn: string
}

export interface ComplianceDisposition {
  dispositionNId: string
  subjectType: string
  subjectNId: string
  state: string
  reason: string
  createdOn: string
  expiresOn: string | null
  createdByUserNId: string
  messageNId?: string
  dispositionVersion?: number
  messageStateVersion?: number
  optimisticVersion?: number
  concurrencyVersion?: string
}

export interface LegalHoldCase {
  holdCaseNId: string
  state: string
  reason: string
  scope: ComplianceScope
  createdByUserNId: string
  createdOn: string
  releasedOn: string | null
  operationId?: string
  operationNId?: string
  scopeChecksum?: string
  optimisticVersion?: number
  concurrencyVersion?: string
  externalCaseReference?: string
  fileSyncState?: string
  reviewedByUserNId?: string
  reviewedOn?: string | null
  releaseRequestedByUserNId?: string
  releaseApprovalExpiresOn?: string | null
  releaseApprovedByUserNId?: string
}

export interface ComplianceExport {
  exportNId: string
  state: string
  scopeChecksum: string
  downloadToken: string | null
  createdOn: string
  expiresOn: string | null
  operationId?: string
  operationNId?: string
  optimisticVersion?: number
  concurrencyVersion?: string
  scope?: ComplianceScope
  fields?: string[]
  caseReference?: string
  requestedByUserNId?: string
  approvedByUserNId?: string
  approvedOn?: string | null
  approvalExpiresOn?: string | null
  completedOn?: string | null
  errorCode?: string | null
}

export interface RetentionPolicy {
  policyNId: string
  messageRetentionDays: number
  attachmentRetentionDays: number
  auditRetentionDays: number
  enabled: boolean
  optimisticVersion: number
  concurrencyVersion: string
  auditRetentionYears?: number
  exportRetentionHours?: number
  reviewDueHours?: number
  status?: string
}

export interface StepUpResponse {
  proof: string
  expiresAt?: string
  expiresOn?: string
  action?: string
  scopeChecksum?: string
  requestNId?: string
}

export interface StepUpContext {
  requestNId: string
  action: string
  scopeChecksum: string
  requestHash: string
  binding: string
  expiresOn: string
}

export interface CollaborationApi {
  searchUsers(
    keyword: string,
    cursor?: string,
    limit?: number,
    options?: RequestOptions,
  ): Promise<CollaborationDirectoryPage>
  createConversation(request: CreateConversationRequest): Promise<ConversationSummary>
  listConversations(params?: {
    cursor?: string
    pageSize?: number
    visibility?: string
    unreadOnly?: boolean
  }): Promise<ConversationPage>
  getConversation(conversationNId: string): Promise<ConversationDetail>
  sendMessage(conversationNId: string, request: SendMessageRequest): Promise<Message>
  getMessages(
    conversationNId: string,
    params?: {
      mode?: 'history' | 'after' | 'window'
      cursor?: string
      sequence?: number
      afterSequence?: number
      fromSequence?: number
      toSequence?: number
      pageSize?: number
    },
  ): Promise<MessagePage>
  getMessageByClient(clientMessageNId: string): Promise<Message>
  markRead(conversationNId: string, sequence: number): Promise<ReadCursor>
  hideConversation(conversationNId: string, throughSequence?: number): Promise<unknown>
  restoreConversation(conversationNId: string): Promise<unknown>
  retractMessage(conversationNId: string, messageNId: string, reason?: string): Promise<Message>
  setPersonalMessageVisibility(
    conversationNId: string,
    messageNId: string,
    hidden: boolean,
  ): Promise<PersonalMessageVisibility>
  createAttachmentIntent(
    conversationNId: string,
    request: AttachmentIntentRequest,
  ): Promise<AttachmentIntent>
  getAttachmentUploadSession(attachmentNId: string): Promise<AttachmentUploadSession>
  setAttachmentContentHash(attachmentNId: string, sha256: string): Promise<AttachmentUploadSession>
  resumeAttachmentProof(
    attachmentNId: string,
    writerEpoch: number,
    proof: string,
  ): Promise<AttachmentUploadSession>
  resumeAttachmentUpload(
    attachmentNId: string,
    writerEpoch: number,
    proof: string,
  ): Promise<AttachmentUploadSession>
  uploadAttachmentChunk(
    attachmentNId: string,
    transportId: string,
    content: Blob,
    expectedOffset: number,
    writerEpoch: number,
    resumeTicket?: string | null,
  ): Promise<AttachmentUploadSession>
  completeAttachmentUpload(attachmentNId: string): Promise<AttachmentUploadSession>
  authorizeAttachment(
    conversationNId: string,
    attachmentNId: string,
    fileNId: string,
    requestNId: string,
  ): Promise<AttachmentAuthorization>
  setPresence(state: string): Promise<Presence>
  getPresence(userNId: string): Promise<Presence>
  searchCompliance(request: ComplianceSearchRequest, proof?: string): Promise<ComplianceSearchPage>
  listDispositions(): Promise<ComplianceDisposition[]>
  createDisposition(
    request: Record<string, unknown>,
    proof?: string,
  ): Promise<ComplianceDisposition>
  listLegalHolds(params?: {
    cursor?: string
    limit?: number
    status?: string
  }): Promise<{ items: LegalHoldCase[]; nextCursor: string | null }>
  createLegalHold(request: Record<string, unknown>, proof?: string): Promise<LegalHoldCase>
  updateLegalHold(
    holdCaseNId: string,
    request: Record<string, unknown>,
    proof: string,
    requestNId: string,
  ): Promise<LegalHoldCase>
  prepareExport(request: Record<string, unknown>, proof?: string): Promise<ComplianceExport>
  approveExport(
    exportNId: string,
    request: Record<string, unknown>,
    requestNId: string,
    proof: string,
  ): Promise<ComplianceExport>
  authorizeExportDownload(
    exportNId: string,
    request: { requestNId: string },
    proof: string,
  ): Promise<ComplianceDownloadAuthorization>
  downloadExport(exportNId: string, requestNId: string, proof: string): Promise<Blob>
  getExport(exportNId: string): Promise<ComplianceExport>
  listExports(params?: {
    cursor?: string
    limit?: number
    status?: string
  }): Promise<{ items: ComplianceExport[]; nextCursor: string | null }>
  getRetention(): Promise<RetentionPolicy>
  updateRetention(request: Record<string, unknown>, proof?: string): Promise<RetentionPolicy>
  createStepUp(request: {
    binding?: string
    currentPassword: string
    password?: string
  }): Promise<StepUpResponse>
  createStepUpContext(request: Record<string, unknown>): Promise<StepUpContext>
}

const COLLABORATION_BASE = '/collaboration/api/v1'
const IDENTITY_BASE = '/identity/api/v1'

function id(value: string): string {
  return encodeURIComponent(value)
}

function query(params: Record<string, unknown>): string {
  const values = Object.entries(params).filter(
    ([, value]) => value !== undefined && value !== null && value !== '',
  )
  if (values.length === 0) return ''
  const search = new URLSearchParams()
  for (const [key, value] of values) search.set(key, String(value))
  return `?${search.toString()}`
}

function requestId(): string {
  return crypto.randomUUID().replaceAll('-', '')
}

function requestHeaders(requestNId: string): RequestOptions {
  return { headers: { 'X-Request-NId': requestNId, 'Idempotency-Key': requestNId } }
}

function requestHeadersWithProof(requestNId: string, proof?: string): RequestOptions {
  return {
    headers: {
      'X-Request-NId': requestNId,
      'Idempotency-Key': requestNId,
      ...(proof ? { 'X-Collaboration-Step-Up': proof } : {}),
    },
  }
}

function numberValue(value: unknown, fallback = 0): number {
  const parsed = typeof value === 'number' ? value : Number(value)
  return Number.isFinite(parsed) ? parsed : fallback
}

function normalizeConversationPage(value: ConversationPage): ConversationPage {
  return {
    ...value,
    total: numberValue(value.total),
    items: value.items.map((item) => ({
      ...item,
      lastMessageSequence: numberValue(item.lastMessageSequence),
      ...(item.lastMessagePreview === undefined
        ? {}
        : {
            lastMessagePreview:
              item.lastMessagePreview === null
                ? null
                : {
                    ...item.lastMessagePreview,
                    sequence: numberValue(item.lastMessagePreview.sequence),
                  },
          }),
      unreadCount: numberValue(item.unreadCount),
      optimisticVersion: numberValue(item.optimisticVersion),
      projectionVersion: numberValue(item.projectionVersion),
    })),
  }
}

function normalizeMessagePage(value: MessagePage): MessagePage {
  return {
    ...value,
    highWatermarkSequence: numberValue(value.highWatermarkSequence),
    retentionFloorSequence: numberValue(value.retentionFloorSequence),
    earliestAvailableSequence: numberValue(value.earliestAvailableSequence),
    nextAfterSequence:
      value.nextAfterSequence === undefined
        ? null
        : value.nextAfterSequence === null
          ? null
          : numberValue(value.nextAfterSequence),
    items: value.items.map(normalizeMessage),
  }
}

function normalizeMessage(value: Message): Message {
  return {
    ...value,
    sequence: numberValue(value.sequence),
    messageStateVersion: numberValue(value.messageStateVersion),
    attachment: value.attachment
      ? { ...value.attachment, size: numberValue(value.attachment.size) }
      : null,
  }
}

function normalizeReadCursor(value: ReadCursor): ReadCursor {
  return {
    ...value,
    lastReadSequence: numberValue(value.lastReadSequence),
    unreadCount: numberValue(value.unreadCount),
    projectionVersion: numberValue(value.projectionVersion),
  }
}

function normalizeUploadSession(value: AttachmentUploadSession): AttachmentUploadSession {
  return {
    ...value,
    sizeBytes: numberValue(value.sizeBytes),
    offset: numberValue(value.offset),
    writerEpoch: numberValue(value.writerEpoch),
  }
}

function normalizeAttachmentIntent(value: AttachmentIntent): AttachmentIntent {
  return {
    ...value,
    size: numberValue(value.size),
    ...(value.sizeBytes === undefined ? {} : { sizeBytes: numberValue(value.sizeBytes) }),
    ...(value.fileUpload
      ? {
          fileUpload: {
            ...value.fileUpload,
            session: normalizeUploadSession(value.fileUpload.session),
          },
        }
      : {}),
  }
}

function normalizeComplianceSearchPage(value: ComplianceSearchPage): ComplianceSearchPage {
  return {
    ...value,
    items: value.items.map((item) => ({ ...item, sequence: numberValue(item.sequence) })),
  }
}

export function createCollaborationApi(client: HttpClient): CollaborationApi {
  return {
    searchUsers: (keyword, cursor, limit, options) =>
      client.get<CollaborationDirectoryPage>(
        `${COLLABORATION_BASE}/users${query({ keyword, cursor, limit })}`,
        options,
      ),
    createConversation: (body) => {
      const requestNId = body.requestNId ?? requestId()
      return client.post<ConversationSummary>(
        `${COLLABORATION_BASE}/conversations`,
        { requestNId, peerUserNId: body.peerUserNId },
        requestHeaders(requestNId),
      )
    },
    listConversations: (params = {}) =>
      client
        .get<ConversationPage>(`${COLLABORATION_BASE}/conversations${query(params)}`)
        .then(normalizeConversationPage),
    getConversation: (conversationNId) =>
      client.get<ConversationDetail>(`${COLLABORATION_BASE}/conversations/${id(conversationNId)}`),
    sendMessage: (conversationNId, body) =>
      client
        .post<Message>(
          `${COLLABORATION_BASE}/conversations/${id(conversationNId)}/messages`,
          body,
          requestHeaders(body.idempotencyKey ?? body.clientMessageNId),
        )
        .then(normalizeMessage),
    getMessages: (conversationNId, params = {}) =>
      client
        .get<MessagePage>(
          `${COLLABORATION_BASE}/conversations/${id(conversationNId)}/messages${query(params)}`,
        )
        .then(normalizeMessagePage),
    getMessageByClient: (clientMessageNId) =>
      client
        .get<Message>(`${COLLABORATION_BASE}/messages/by-client/${id(clientMessageNId)}`)
        .then(normalizeMessage),
    markRead: (conversationNId, sequence) =>
      client
        .put<ReadCursor>(
          `${COLLABORATION_BASE}/conversations/${id(conversationNId)}/read-cursor`,
          { sequence: String(sequence) },
          { headers: { 'Content-Type': 'application/json' } },
        )
        .then(normalizeReadCursor),
    hideConversation: (conversationNId, throughSequence) =>
      client.post(`${COLLABORATION_BASE}/conversations/${id(conversationNId)}/hide`, {
        throughSequence,
      }),
    restoreConversation: (conversationNId) =>
      client.post(`${COLLABORATION_BASE}/conversations/${id(conversationNId)}/restore`, {}),
    retractMessage: (conversationNId, messageNId, reason) =>
      client
        .post<Message>(
          `${COLLABORATION_BASE}/conversations/${id(conversationNId)}/messages/${id(messageNId)}/retract`,
          { reason },
        )
        .then(normalizeMessage),
    setPersonalMessageVisibility: (conversationNId, messageNId, hidden) =>
      client.put<PersonalMessageVisibility>(
        `${COLLABORATION_BASE}/conversations/${id(conversationNId)}/messages/${id(messageNId)}/personal-visibility`,
        { hidden },
      ),
    createAttachmentIntent: (conversationNId, body) =>
      client
        .post<AttachmentIntent>(
          `${COLLABORATION_BASE}/attachments/intents`,
          { ...body, conversationNId },
          requestHeaders(body.requestNId),
        )
        .then(normalizeAttachmentIntent),
    getAttachmentUploadSession: (attachmentNId) =>
      client
        .get<AttachmentUploadSession>(
          `${COLLABORATION_BASE}/attachments/${id(attachmentNId)}/upload-session`,
        )
        .then(normalizeUploadSession),
    setAttachmentContentHash: (attachmentNId, sha256) =>
      client
        .put<AttachmentUploadSession>(
          `${COLLABORATION_BASE}/attachments/${id(attachmentNId)}/upload-session/content-hash`,
          { sha256 },
        )
        .then(normalizeUploadSession),
    resumeAttachmentProof: (attachmentNId, writerEpoch, proof) =>
      client
        .post<AttachmentUploadSession>(
          `${COLLABORATION_BASE}/attachments/${id(attachmentNId)}/upload-session/resume-proof`,
          { writerEpoch, proof },
        )
        .then(normalizeUploadSession),
    resumeAttachmentUpload: (attachmentNId, writerEpoch, proof) =>
      client
        .post<AttachmentUploadSession>(
          `${COLLABORATION_BASE}/attachments/${id(attachmentNId)}/upload-session/resume`,
          { writerEpoch, proof },
        )
        .then(normalizeUploadSession),
    uploadAttachmentChunk: (
      attachmentNId,
      transportId,
      content,
      expectedOffset,
      writerEpoch,
      resumeTicket,
    ) => {
      if (client.patch === undefined) throw new Error('当前 HTTP 客户端不支持分片上传。')
      return client
        .patch<AttachmentUploadSession>(
          `${COLLABORATION_BASE}/attachments/${id(attachmentNId)}/upload`,
          content,
          {
            headers: {
              'Content-Type': 'application/octet-stream',
              'Upload-Transport-Id': transportId,
              'Upload-Offset': String(expectedOffset),
              'Upload-Writer-Epoch': String(writerEpoch),
              ...(resumeTicket ? { 'Upload-Resume-Ticket': resumeTicket } : {}),
            },
          },
        )
        .then(normalizeUploadSession)
    },
    completeAttachmentUpload: (attachmentNId) =>
      client
        .post<AttachmentUploadSession>(
          `${COLLABORATION_BASE}/attachments/${id(attachmentNId)}/upload-session/complete`,
          {},
        )
        .then(normalizeUploadSession),
    authorizeAttachment: (conversationNId, attachmentNId, fileNId, requestNId) =>
      client.post<AttachmentAuthorization>(
        `${COLLABORATION_BASE}/conversations/${id(conversationNId)}/attachments/${id(attachmentNId)}/authorization`,
        { fileNId, requestNId },
        requestHeaders(requestNId),
      ),
    setPresence: (state) => client.put<Presence>(`${COLLABORATION_BASE}/presence`, { state }),
    getPresence: (userNId) => client.get<Presence>(`${COLLABORATION_BASE}/presence/${id(userNId)}`),
    searchCompliance: (body, proof) => {
      const requestNId = body.requestNId ?? requestId()
      return client
        .post<ComplianceSearchPage>(
          `${COLLABORATION_BASE}/compliance/views`,
          { ...body, requestNId, limit: body.pageSize ?? 20 },
          requestHeadersWithProof(requestNId, proof),
        )
        .then(normalizeComplianceSearchPage)
    },
    listDispositions: () =>
      client.get<ComplianceDisposition[]>(`${COLLABORATION_BASE}/compliance/dispositions`),
    createDisposition: (body, proof) => {
      const requestNId = String(body.requestNId ?? requestId())
      return client.post<ComplianceDisposition>(
        `${COLLABORATION_BASE}/compliance/dispositions`,
        body,
        requestHeadersWithProof(requestNId, proof),
      )
    },
    listLegalHolds: (params = {}) =>
      client.get<{ items: LegalHoldCase[]; nextCursor: string | null }>(
        `${COLLABORATION_BASE}/compliance/legal-holds${query(params)}`,
      ),
    createLegalHold: (body, proof) => {
      const requestNId = String(body.requestNId ?? requestId())
      return client.post<LegalHoldCase>(
        `${COLLABORATION_BASE}/compliance/legal-holds`,
        body,
        requestHeadersWithProof(requestNId, proof),
      )
    },
    updateLegalHold: (holdCaseNId, body, proof, requestNId) => {
      const action = String(body.action ?? '')
      const suffix =
        action === 'review'
          ? 'review'
          : action === 'release-request'
            ? 'release-requests'
            : action === 'release-approve'
              ? 'release-approvals'
              : 'actions'
      return client.post<LegalHoldCase>(
        `${COLLABORATION_BASE}/compliance/legal-holds/${id(holdCaseNId)}/${suffix}`,
        { ...body, requestNId },
        { headers: { 'X-Collaboration-Step-Up': proof, 'X-Request-NId': requestNId } },
      )
    },
    prepareExport: (body, proof) => {
      const requestNId = String(body.requestNId ?? requestId())
      return client.post<ComplianceExport>(
        `${COLLABORATION_BASE}/compliance/exports`,
        body,
        requestHeadersWithProof(requestNId, proof),
      )
    },
    approveExport: (exportNId, body, requestNId, proof) =>
      client.post<ComplianceExport>(
        `${COLLABORATION_BASE}/compliance/exports/${id(exportNId)}/approve`,
        body,
        requestHeadersWithProof(requestNId, proof),
      ),
    authorizeExportDownload: (exportNId, body, proof) =>
      client.post<ComplianceDownloadAuthorization>(
        `${COLLABORATION_BASE}/compliance/exports/${id(exportNId)}/authorizations`,
        body,
        requestHeadersWithProof(body.requestNId, proof),
      ),
    downloadExport: (exportNId, requestNId, proof) => {
      if (client.getBlob === undefined) throw new Error('当前 HTTP 客户端不支持导出下载。')
      return client.getBlob(`${COLLABORATION_BASE}/compliance/exports/${id(exportNId)}/content`, {
        headers: {
          'X-Collaboration-Step-Up': proof,
          'X-Collaboration-Request-Id': requestNId,
        },
      })
    },
    getExport: (exportNId) =>
      client.get<ComplianceExport>(`${COLLABORATION_BASE}/compliance/exports/${id(exportNId)}`),
    listExports: (params = {}) =>
      client.get<{ items: ComplianceExport[]; nextCursor: string | null }>(
        `${COLLABORATION_BASE}/compliance/exports${query(params)}`,
      ),
    getRetention: () => client.get<RetentionPolicy>(`${COLLABORATION_BASE}/compliance/retention`),
    updateRetention: (body, proof) => {
      const requestNId = String(body.requestNId ?? requestId())
      return client.put<RetentionPolicy>(
        `${COLLABORATION_BASE}/compliance/retention`,
        body,
        requestHeadersWithProof(requestNId, proof),
      )
    },
    createStepUp: (body) =>
      client.post<StepUpResponse>(`${IDENTITY_BASE}/auth/step-up`, {
        binding: body.binding,
        password: body.password ?? body.currentPassword,
        currentPassword: body.currentPassword,
      }),
    createStepUpContext: (body) =>
      client.post<StepUpContext>(
        `${COLLABORATION_BASE}/compliance/step-up-context`,
        body,
        requestHeaders(String(body.requestNId ?? requestId())),
      ),
  }
}

export type { RequestOptions }
