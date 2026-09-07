import type { HttpClient } from '@/api/httpClient'

import type {
  AnnouncementDto,
  AnnouncementPageDto,
  AuditFactDto,
  AuditFactPageDto,
  AuditLifecycleRequest,
  FileObjectDto,
  FilePageDto,
  FileUploadDiscoveryDto,
  NotificationInboxItemDto,
  NotificationInboxPageDto,
  Pf04Api,
  UploadSessionDto,
} from './pf04Types'

const BASE = '/systemdata/api/v1'
const id = (value: string): string => encodeURIComponent(value)

function query(params: Record<string, string | number | boolean | undefined> = {}): string {
  const entries = Object.entries(params).filter(([, value]) => value !== undefined && value !== '')
  return entries.length === 0
    ? ''
    : `?${entries.map(([key, value]) => `${encodeURIComponent(key)}=${encodeURIComponent(String(value))}`).join('&')}`
}

export function createPf04Api(client: HttpClient): Pf04Api {
  return {
    listFiles: (search, page = 1, pageSize = 50, purpose, ownerUserNId, scanStatus, restricted) =>
      client.get<FilePageDto>(`${BASE}/files${query({ search, purpose, ownerUserNId, scanStatus, restricted, page, pageSize })}`),
    createUploadSession: (request) => client.post<UploadSessionDto>(`${BASE}/files/upload-sessions`, request),
    discoverUpload: (request) => client.post<FileUploadDiscoveryDto>(`${BASE}/files/upload-sessions/discover`, request),
    getUploadSession: (sessionNId) => client.get<UploadSessionDto>(`${BASE}/files/upload-sessions/${id(sessionNId)}`),
    setContentHash: (sessionNId, sha256) =>
      client.put<UploadSessionDto>(`${BASE}/files/upload-sessions/${id(sessionNId)}/content-hash`, { sha256 }),
    resumeProof: (sessionNId, writerEpoch, proof) =>
      client.post<UploadSessionDto>(`${BASE}/files/upload-sessions/${id(sessionNId)}/resume-proof`, {
        writerEpoch,
        proof,
      }),
    takeoverUpload: (sessionNId, expectedWriterEpoch, proof, idempotencyKey) =>
      client.post<UploadSessionDto>(`${BASE}/files/upload-sessions/${id(sessionNId)}/takeover`, { expectedWriterEpoch, proof, idempotencyKey }),
    pauseUpload: (sessionNId) => client.post<UploadSessionDto>(`${BASE}/files/upload-sessions/${id(sessionNId)}/pause`),
    resumeUpload: (sessionNId, writerEpoch, proof) => client.post<UploadSessionDto>(`${BASE}/files/upload-sessions/${id(sessionNId)}/resume`, { writerEpoch, proof }),
    cancelUpload: (sessionNId, reason) => client.post<UploadSessionDto>(`${BASE}/files/upload-sessions/${id(sessionNId)}/cancel`, { reason }),
    uploadChunk: async (transportId, body, offset, epoch, resumeTicket, signal) => {
      if (client.patch === undefined) throw new Error('当前 HTTP 客户端不支持断点上传')
      return client.patch<UploadSessionDto>(`${BASE}/files/uploads/${id(transportId)}`, body, {
        headers: {
          'Content-Type': body.type || 'application/octet-stream',
          'Upload-Offset': String(offset),
          'Upload-Epoch': String(epoch),
          'Tus-Resumable': '1.0.0',
          'X-Upload-Resume-Ticket': resumeTicket,
        },
        ...(signal === undefined ? {} : { signal }),
      })
    },
    completeUpload: (sessionNId) =>
      client.post<FileObjectDto>(`${BASE}/files/upload-sessions/${id(sessionNId)}/complete`),
    getFile: (fileNId) => client.get<FileObjectDto>(`${BASE}/files/${id(fileNId)}`),
    requestFileDeletion: (fileNId) => client.post<FileObjectDto>(`${BASE}/files/${id(fileNId)}/deletion-requests`),
    setFileRestriction: (fileNId, restricted) => client.put<FileObjectDto>(`${BASE}/files/${id(fileNId)}/restrictions`, { restricted }),
    downloadFile: async (fileNId) => {
      if (client.getBlob === undefined) throw new Error('当前 HTTP 客户端不支持文件下载')
      return client.getBlob(`${BASE}/files/${id(fileNId)}/content`)
    },
    listAnnouncements: (search, page = 1, pageSize = 50) =>
      client.get<AnnouncementPageDto>(`${BASE}/notifications/announcements${query({ search, page, pageSize })}`),
    createAnnouncement: (request) =>
      client.post<AnnouncementDto>(`${BASE}/notifications/announcements`, request),
    updateAnnouncement: (announcementNId, request) =>
      client.put<AnnouncementDto>(`${BASE}/notifications/announcements/${id(announcementNId)}`, request),
    publishAnnouncement: (announcementNId) =>
      client.post<AnnouncementDto>(`${BASE}/notifications/announcements/${id(announcementNId)}/publish`),
    revokeAnnouncement: (announcementNId) =>
      client.post<AnnouncementDto>(`${BASE}/notifications/announcements/${id(announcementNId)}/revoke`),
    getInbox: (page = 1, pageSize = 50) =>
      client.get<NotificationInboxPageDto>(`${BASE}/notifications/inbox${query({ page, pageSize })}`),
    markRead: async (notificationNId, read = true) => {
      await client.put<void>(`${BASE}/notifications/inbox/${id(notificationNId)}/read`, { read })
    },
    batchRead: async (notificationNIds) => {
      await client.post<void>(`${BASE}/notifications/inbox/read-batch`, { notificationNIds })
    },
    sendSystemMessage: (request) => client.post<NotificationInboxItemDto>(`${BASE}/notifications/system-messages`, request),
    listAudits: (params = {}) => client.get<AuditFactPageDto>(`${BASE}/audits/facts${query(params)}`),
    getAudit: (producerServiceKey, auditEventNId) => client.get<AuditFactDto>(`${BASE}/audits/facts/${id(producerServiceKey)}/${id(auditEventNId)}`),
    exportAudits: async (params = {}) => {
      if (client.getBlob === undefined) throw new Error('当前 HTTP 客户端不支持文件下载')
      return client.getBlob(`${BASE}/audits/exports${query(params)}`)
    },
    updateAuditLifecycle: (producerServiceKey, auditEventNId, request: AuditLifecycleRequest) =>
      client.post<void>(`${BASE}/audits/facts/${id(producerServiceKey)}/${id(auditEventNId)}/lifecycle`, request),
  }
}
