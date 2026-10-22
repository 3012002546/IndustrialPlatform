import { describe, expect, it, vi } from 'vitest'
import type { UploadSessionDto } from '@/api/systemData/systemDataOperationsTypes'
import { uploadFileChunks } from '@/utils/uploadFileChunks'

const chunkSize = 1024 * 1024
function session(offset = 0): UploadSessionDto {
  return {
    tenantNId: 'tenant',
    sessionNId: 'session',
    transportId: 'transport',
    fileName: 'payload.bin',
    contentType: 'application/octet-stream',
    length: 2 * chunkSize + 3,
    offset,
    sha256: 'hash',
    purpose: 'LedgerField',
    writerEpoch: 1,
    status: 'Ready',
    expiresOn: '2099-01-01T00:00:00Z',
    fileNId: null,
    errorCode: null,
    resumeTicket: 'ticket-1',
    resumeTicketExpiresOn: null,
  }
}

describe('shared authorized upload transfer', () => {
  it('resumes from the server offset, slices chunks and carries forward rotated writer evidence', async () => {
    const signal = new AbortController().signal
    const progress = vi.fn()
    const api = {
      uploadChunk: vi.fn(async (_id: string, body: Blob, offset: number) => ({
        ...session(offset + body.size),
        writerEpoch: 2,
        resumeTicket: 'ticket-2',
      })),
    }
    const file = new Blob([new Uint8Array(2 * chunkSize + 3)])
    const result = await uploadFileChunks(api, file, session(3), { signal, onProgress: progress })
    expect(result?.offset).toBe(file.size)
    expect(api.uploadChunk.mock.calls.map((call) => call[2])).toEqual([3, chunkSize + 3])
    expect(api.uploadChunk).toHaveBeenNthCalledWith(
      1,
      'transport',
      expect.any(Blob),
      3,
      1,
      'ticket-1',
      signal,
    )
    expect(api.uploadChunk).toHaveBeenNthCalledWith(
      2,
      'transport',
      expect.any(Blob),
      chunkSize + 3,
      2,
      'ticket-2',
      signal,
    )
    expect(progress).toHaveBeenCalledTimes(2)
  })

  it('does not publish a late response or finish after cancellation', async () => {
    const controller = new AbortController()
    const progress = vi.fn()
    const api = {
      uploadChunk: vi.fn(async () => {
        controller.abort()
        return session(8)
      }),
    }
    expect(
      await uploadFileChunks(api, new Blob(['12345678']), session(), {
        signal: controller.signal,
        onProgress: progress,
      }),
    ).toBeNull()
    expect(progress).not.toHaveBeenCalled()
    expect(api.uploadChunk).toHaveBeenCalledTimes(1)
  })

  it('stops between chunks when paused or superseded by a new upload', async () => {
    let stopped = false
    const api = { uploadChunk: vi.fn(async () => session(chunkSize)) }
    expect(
      await uploadFileChunks(api, new Blob([new Uint8Array(2 * chunkSize)]), session(), {
        signal: new AbortController().signal,
        shouldStop: () => stopped,
        onProgress: () => {
          stopped = true
        },
      }),
    ).toBeNull()
    expect(api.uploadChunk).toHaveBeenCalledTimes(1)
  })

  it.each([0, -1, 9, Number.NaN])(
    'rejects nonadvancing or invalid server offset %s',
    async (offset) => {
      const api = { uploadChunk: vi.fn(async () => session(offset)) }
      await expect(
        uploadFileChunks(api, new Blob(['12345678']), session(), {
          signal: new AbortController().signal,
          onProgress: vi.fn(),
        }),
      ).rejects.toThrow('UPLOAD_OFFSET_MISMATCH')
      expect(api.uploadChunk).toHaveBeenCalledTimes(1)
    },
  )
})
