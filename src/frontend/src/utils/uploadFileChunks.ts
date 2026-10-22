import type {
  SystemDataOperationsApi,
  UploadSessionDto,
} from '@/api/systemData/systemDataOperationsTypes'

/** Transfer an already authorized session; proof, takeover and file ownership stay with the caller. */
export async function uploadFileChunks(
  api: Pick<SystemDataOperationsApi, 'uploadChunk'>,
  file: Blob,
  initial: UploadSessionDto,
  options: {
    signal: AbortSignal
    shouldStop?: () => boolean
    onChunk?: (end: number) => void
    onProgress: (session: UploadSessionDto) => void
  },
): Promise<UploadSessionDto | null> {
  const stopped = () => options.signal.aborted || options.shouldStop?.()
  let ready = initial
  if (
    !Number.isSafeInteger(Number(ready.offset)) ||
    Number(ready.offset) < 0 ||
    Number(ready.offset) > file.size
  )
    throw new Error('UPLOAD_OFFSET_MISMATCH')
  while (Number(ready.offset) < file.size) {
    if (stopped()) return null
    const start = Number(ready.offset)
    const end = Math.min(file.size, start + 1024 * 1024)
    options.onChunk?.(end)
    ready = await api.uploadChunk(
      ready.transportId,
      file.slice(start, end),
      start,
      ready.writerEpoch,
      ready.resumeTicket ?? '',
      options.signal,
    )
    if (stopped()) return null
    const offset = Number(ready.offset)
    if (!Number.isSafeInteger(offset) || offset <= start || offset > file.size)
      throw new Error('UPLOAD_OFFSET_MISMATCH')
    options.onProgress(ready)
  }
  return stopped() ? null : ready
}
