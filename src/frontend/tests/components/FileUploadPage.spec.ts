import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, describe, expect, it, vi } from 'vitest'

import FileUploadPage from '@/pages/mobile/FileUploadPage.vue'
import { registerPf04Api } from '@/api/systemData/pf04Registry'
import type { FileObjectDto, Pf04Api, UploadSessionDto } from '@/api/systemData/pf04Types'

vi.mock('@/utils/sha256File', () => ({
  sampleFingerprint: vi.fn(async () => 'sample-fingerprint'),
  sha256File: vi.fn(async () => 'sha256-hash'),
}))

const CHUNK_SIZE = 1024 * 1024

function makeSession(overrides: Partial<UploadSessionDto> = {}): UploadSessionDto {
  return {
    tenantNId: 'tenant-1',
    sessionNId: 'session-1',
    transportId: 'transport-1',
    fileName: 'payload.bin',
    contentType: 'application/octet-stream',
    length: 2 * CHUNK_SIZE,
    offset: 0,
    sha256: 'sha256-hash',
    purpose: 'systemdata',
    writerEpoch: 1,
    status: 'Ready',
    expiresOn: '2099-01-01T00:00:00Z',
    fileNId: null,
    errorCode: null,
    resumeTicket: 'resume-ticket',
    resumeTicketExpiresOn: '2099-01-01T00:00:00Z',
    ...overrides,
  }
}

function makeApi(overrides: Partial<Pf04Api> = {}): Pf04Api {
  return {
    listFiles: vi.fn(),
    createUploadSession: vi.fn(async () => makeSession()),
    discoverUpload: vi.fn(async () => ({ candidates: [] })),
    getUploadSession: vi.fn(async () => makeSession()),
    setContentHash: vi.fn(async () => makeSession()),
    resumeProof: vi.fn(async () => makeSession()),
    takeoverUpload: vi.fn(async () => makeSession()),
    pauseUpload: vi.fn(async () => makeSession()),
    resumeUpload: vi.fn(async () => makeSession()),
    cancelUpload: vi.fn(async () => makeSession()),
    uploadChunk: vi.fn(async () => makeSession({ offset: 2 * CHUNK_SIZE })),
    completeUpload: vi.fn(async () => ({})),
    getFile: vi.fn(),
    requestFileDeletion: vi.fn(),
    setFileRestriction: vi.fn(),
    downloadFile: vi.fn(),
    listAnnouncements: vi.fn(),
    createAnnouncement: vi.fn(),
    updateAnnouncement: vi.fn(),
    publishAnnouncement: vi.fn(),
    revokeAnnouncement: vi.fn(),
    getInbox: vi.fn(),
    markRead: vi.fn(),
    batchRead: vi.fn(),
    sendSystemMessage: vi.fn(),
    listAudits: vi.fn(),
    getAudit: vi.fn(),
    exportAudits: vi.fn(),
    ...overrides,
  } as unknown as Pf04Api
}

function makeFile(): File {
  return new File([new Uint8Array(2 * CHUNK_SIZE)], 'payload.bin', {
    type: 'application/octet-stream',
  })
}

async function chooseFile(wrapper: ReturnType<typeof mount>, file: File): Promise<void> {
  const input = wrapper.get('input[type="file"]')
  Object.defineProperty(input.element, 'files', { configurable: true, value: [file] })
  await input.trigger('change')
}

function buttonWithText(wrapper: ReturnType<typeof mount>, text: string) {
  return wrapper.findAll('button').find((button) => button.text().includes(text))
}

afterEach(() => {
  registerPf04Api(makeApi())
})

describe('mobile FileUploadPage', () => {
  it('stops the active chunk request without starting a later chunk or cancelling the server session', async () => {
    const file = makeFile()
    const signals: AbortSignal[] = []
    const uploadChunk = vi.fn(
      (
        _transportId: string,
        _body: Blob,
        _offset: number,
        _epoch: number,
        _resumeTicket: string,
        signal?: AbortSignal,
      ) =>
        new Promise<UploadSessionDto>((_resolve, reject) => {
          if (signal !== undefined) {
            signals.push(signal)
            signal.addEventListener('abort', () => reject(new Error('aborted')), { once: true })
          }
        }),
    )
    const completeUpload = vi.fn()
    const cancelUpload = vi.fn()
    registerPf04Api(
      makeApi({
        createUploadSession: vi.fn(async () => makeSession({ status: 'WaitingForProof' })),
        resumeProof: vi.fn(async () => makeSession({ status: 'Ready' })),
        uploadChunk,
        completeUpload,
        cancelUpload,
      }),
    )

    const wrapper = mount(FileUploadPage)
    await chooseFile(wrapper, file)
    await buttonWithText(wrapper, '开始上传')!.trigger('click')
    await vi.waitFor(() => expect(uploadChunk).toHaveBeenCalledTimes(1))

    await buttonWithText(wrapper, '终止')!.trigger('click')
    await flushPromises()

    expect(signals[0]?.aborted).toBe(true)
    expect(uploadChunk).toHaveBeenCalledTimes(1)
    expect(completeUpload).not.toHaveBeenCalled()
    expect(cancelUpload).not.toHaveBeenCalled()
    expect(wrapper.get('[role="status"]').text()).toContain('已停止')
  })

  it('rediscovers and resumes the same file from the server offset after it is selected again', async () => {
    const file = makeFile()
    const candidate = makeSession({ offset: CHUNK_SIZE, status: 'WaitingForProof' })
    const offsets: number[] = []
    const discoverUpload = vi
      .fn()
      .mockResolvedValueOnce({ candidates: [] })
      .mockResolvedValueOnce({ candidates: [candidate] })
    const getUploadSession = vi.fn(async () => candidate)
    const resumeProof = vi
      .fn()
      .mockResolvedValueOnce(makeSession({ offset: 0, status: 'Ready' }))
      .mockResolvedValueOnce(makeSession({ offset: CHUNK_SIZE, status: 'Ready' }))
    const uploadChunk = vi.fn(
      async (
        _transportId: string,
        _body: Blob,
        offset: number,
        _epoch: number,
        _resumeTicket: string,
        signal?: AbortSignal,
      ) => {
        offsets.push(offset)
        if (offset === 0) return makeSession({ offset: CHUNK_SIZE })
        if (offsets.length === 2) {
          return new Promise<UploadSessionDto>((_resolve, reject) => {
            signal?.addEventListener('abort', () => reject(new Error('aborted')), { once: true })
          })
        }
        return makeSession({ offset: file.size })
      },
    )
    const completeUpload = vi.fn(async () => ({} as FileObjectDto))
    registerPf04Api(
      makeApi({ discoverUpload, getUploadSession, resumeProof, uploadChunk, completeUpload }),
    )

    const wrapper = mount(FileUploadPage)
    await chooseFile(wrapper, file)
    await buttonWithText(wrapper, '开始上传')!.trigger('click')
    await vi.waitFor(() => expect(offsets).toEqual([0, CHUNK_SIZE]))

    await buttonWithText(wrapper, '终止')!.trigger('click')
    await flushPromises()
    await chooseFile(wrapper, file)
    await buttonWithText(wrapper, '开始上传')!.trigger('click')
    await vi.waitFor(() => expect(completeUpload).toHaveBeenCalledTimes(1))

    expect(discoverUpload).toHaveBeenCalledTimes(2)
    expect(getUploadSession).toHaveBeenCalledWith('session-1')
    expect(resumeProof).toHaveBeenCalledWith('session-1', 1, 'sha256-hash')
    expect(offsets).toEqual([0, CHUNK_SIZE, CHUNK_SIZE])
  })
})
