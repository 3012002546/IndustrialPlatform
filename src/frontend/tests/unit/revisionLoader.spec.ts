import { afterEach, expect, it, vi } from 'vitest'
import { createRevisionLoader } from '@/components/studio/report/revisionLoader'

afterEach(() => vi.useRealTimers())

it('shares concurrent requests and reuses history when returning to a template', async () => {
  const fetch = vi.fn().mockResolvedValue([]),
    loader = createRevisionLoader(fetch)
  await Promise.all([loader.load('a', 'v1'), loader.load('a', 'v1')])
  await loader.load('b', 'v1')
  await loader.load('a', 'v1')
  expect(fetch.mock.calls).toEqual([['a'], ['b']])
})

it('refreshes after expiry or a new published revision and isolates designer instances', async () => {
  vi.useFakeTimers()
  const fetch = vi.fn().mockResolvedValue([]),
    loader = createRevisionLoader(fetch)
  await loader.load('a', 'v1')
  await loader.load('a', 'v2')
  vi.advanceTimersByTime(60_001)
  await loader.load('a', 'v2')
  await createRevisionLoader(fetch).load('a', 'v2')
  expect(fetch).toHaveBeenCalledTimes(4)
})

it('retries errors and never restores a cleared entry when an old request finishes', async () => {
  const fetch = vi.fn().mockRejectedValueOnce(new Error('offline')).mockResolvedValue([]),
    loader = createRevisionLoader(fetch)
  await expect(loader.load('a')).rejects.toThrow('offline')
  await loader.load('a')
  let finish!: (value: []) => void
  fetch.mockImplementationOnce(
    () =>
      new Promise((resolve) => {
        finish = resolve
      }),
  )
  const pending = loader.load('b')
  loader.clear()
  await loader.load('b')
  finish([])
  await pending
  await loader.load('b')
  expect(fetch).toHaveBeenCalledTimes(4)
})
