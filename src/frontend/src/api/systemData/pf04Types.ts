export interface UploadSessionDto {
  tenantNId: string
  sessionNId: string
  transportId: string
  fileName: string
  contentType: string
  length: number
  offset: number
  sha256: string
  purpose: string
  writerEpoch: number
  status: string
  expiresOn: string
  fileNId: string | null
  errorCode: string | null
  resumeTicket: string | null
  resumeTicketExpiresOn: string | null
}

export interface FileObjectDto {
  tenantNId: string
  fileNId: string
  fileName: string
  contentType: string
  length: number
  sha256: string
  scanStatus: string
  restricted: boolean
  deletionStatus: string
  createdOn: string
  retentionUntil: string | null
}

export interface FilePageDto {
  items: FileObjectDto[]
  page: number
  pageSize: number
  total: number
}

export interface FileUploadDiscoveryDto {
  candidates: UploadSessionDto[]
}

export interface FileReferenceDto {
  tenantNId: string
  referenceNId: string
  fileNId: string
  ownerUserNId: string
  purpose: string
  createdOn: string
  deletedOn: string | null
}

export interface AnnouncementDto {
  tenantNId: string
  announcementNId: string
  title: string
  body: string
  priority: number
  status: string
  recipientCount: number
  publishedOn: string | null
  expiresOn: string | null
  resourceNId?: string | null
  targetRoute?: string | null
}

export interface NotificationInboxItemDto {
  notificationNId: string
  kind: string
  title: string
  body: string
  senderUserNId: string
  isRead: boolean
  deliveredOn: string
  readOn: string | null
  expiresOn: string | null
  resourceNId?: string | null
  targetRoute?: string | null
}

export interface NotificationInboxPageDto {
  items: NotificationInboxItemDto[]
  page: number
  pageSize: number
  total: number
  unreadCount: number
}

export interface AuditFactDto {
  tenantNId: string
  producerServiceKey: string
  auditEventNId: string
  occurredOn: string
  receivedOn: string
  actorUserNId: string | null
  action: string
  objectType: string
  objectNId: string | null
  payloadJson: string
  traceId: string
  severity: string
}

export interface AuditFactPageDto {
  items: AuditFactDto[]
  page: number
  pageSize: number
  total: number
}

export interface Pf04Api {
  listFiles(search?: string, page?: number, pageSize?: number): Promise<FilePageDto>
  createUploadSession(request: Record<string, unknown>): Promise<UploadSessionDto>
  discoverUpload(request: Record<string, unknown>): Promise<FileUploadDiscoveryDto>
  getUploadSession(sessionNId: string): Promise<UploadSessionDto>
  setContentHash(sessionNId: string, sha256: string): Promise<UploadSessionDto>
  resumeProof(sessionNId: string, writerEpoch: number, proof: string): Promise<UploadSessionDto>
  takeoverUpload(sessionNId: string, expectedWriterEpoch: number, proof: string, idempotencyKey?: string): Promise<UploadSessionDto>
  pauseUpload(sessionNId: string): Promise<UploadSessionDto>
  resumeUpload(sessionNId: string, writerEpoch: number, proof: string): Promise<UploadSessionDto>
  cancelUpload(sessionNId: string, reason?: string): Promise<UploadSessionDto>
  uploadChunk(transportId: string, body: Blob, offset: number, epoch: number, resumeTicket: string, signal?: AbortSignal): Promise<UploadSessionDto>
  completeUpload(sessionNId: string): Promise<FileObjectDto>
  getFile(fileNId: string): Promise<FileObjectDto>
  requestFileDeletion(fileNId: string): Promise<FileObjectDto>
  setFileRestriction(fileNId: string, restricted: boolean): Promise<FileObjectDto>
  downloadFile(fileNId: string): Promise<Blob>
  listAnnouncements(search?: string): Promise<AnnouncementDto[]>
  createAnnouncement(request: Record<string, unknown>): Promise<AnnouncementDto>
  updateAnnouncement(announcementNId: string, request: Record<string, unknown>): Promise<AnnouncementDto>
  publishAnnouncement(announcementNId: string): Promise<AnnouncementDto>
  revokeAnnouncement(announcementNId: string): Promise<AnnouncementDto>
  getInbox(page?: number, pageSize?: number): Promise<NotificationInboxPageDto>
  markRead(notificationNId: string, read?: boolean): Promise<void>
  batchRead(notificationNIds: string[]): Promise<void>
  sendSystemMessage(request: Record<string, unknown>): Promise<NotificationInboxItemDto>
  listAudits(params?: Record<string, string | number | undefined>): Promise<AuditFactPageDto>
  getAudit(producerServiceKey: string, auditEventNId: string): Promise<AuditFactDto>
  exportAudits(params?: Record<string, string | number | undefined>): Promise<Blob>
}
