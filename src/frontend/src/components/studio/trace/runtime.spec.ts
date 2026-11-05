import { afterEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h } from 'vue'
import { mount, type VueWrapper } from '@vue/test-utils'
import { useTraceRuntime } from './useTraceRuntime'
import type { TraceApi, TraceDetail, TraceResult, TraceSectionResult } from '@/api/studioTrace'
import { ApiError } from '@/api/errors'

function deferred<T>() {
  let resolve!: (value: T) => void, reject!: (reason: unknown) => void
  const promise = new Promise<T>((a, b) => {
    resolve = a
    reject = b
  })
  return { promise, resolve, reject }
}
function result(): TraceResult {
  return {
    requestId: 'q1',
    revisionNId: 'r1',
    nodes: ['A', 'B'].map((key) => ({
      key,
      objectTypeId: 'lot',
      nodeToken: `t-${key}`,
      grain: 'Batch',
      identity: { sourceSystem: 'MES', factory: 'F1', businessKey: key },
      summary: [],
    })),
    edges: [],
    status: 'Complete',
    partialReasons: [],
    observedOn: null,
    sourceObservations: [],
  }
}
const wrappers: VueWrapper[] = []
afterEach(() => {
  wrappers.splice(0).forEach((w) => w.unmount())
})
function setup() {
  const api = {
    linkSources: vi.fn<TraceApi['linkSources']>(),
    models: vi.fn<TraceApi['models']>(),
    starts: vi.fn<TraceApi['starts']>().mockResolvedValue({
      items: [
        {
          startKey: 'server-key',
          objectTypeId: 'lot',
          identity: { sourceSystem: 'MES', factory: 'F1', businessKey: 'A' },
          values: {},
        },
      ],
      totalCount: '1',
      page: 1,
      pageSize: 20,
    }),
    query: vi.fn<TraceApi['query']>().mockResolvedValue(result()),
    expand: vi.fn<TraceApi['expand']>().mockResolvedValue(result()),
    detail: vi
      .fn<TraceApi['detail']>()
      .mockResolvedValue({ fields: [], links: [], observedOn: null }),
    link: vi.fn<TraceApi['link']>(),
    section: vi.fn<TraceApi['section']>(),
    contract: vi.fn<TraceApi['contract']>(),
    options: vi.fn<TraceApi['options']>(),
    previewOptions: vi.fn<TraceApi['previewOptions']>(),
    previewStart: vi.fn<TraceApi['previewStart']>(),
    previewRelation: vi.fn<TraceApi['previewRelation']>(),
  } satisfies TraceApi
  let runtime!: ReturnType<typeof useTraceRuntime>
  const ctx = { nId: 'model', revisionNId: 'r1' }
  wrappers.push(
    mount(
      defineComponent({
        setup() {
          runtime = useTraceRuntime(api, () => ctx)
          return () => h('div')
        },
      }),
    ),
  )
  async function start() {
    await runtime.search({}, 1)
    runtime.selectedKeys.value = ['server-key']
    await runtime.query('Both')
  }
  return { api, runtime, ctx, start }
}
describe('trace request and selection generations', () => {
  it('loads only declared sections with the selected signed node and ignores stale replies', async () => {
    const { runtime, api, start } = setup()
    await start()
    api.detail.mockResolvedValue({
      fields: [],
      links: [],
      observedOn: null,
      sections: [{ id: 'checks', label: 'Checks', kind: 'Table', pageSize: 20 }],
    })
    await runtime.select([{ kind: 'node', id: 'A' }])
    await expect(runtime.loadSection('undeclared', 1, 20)).rejects.toThrow('GRAPH_CONTEXT_STALE')
    expect(api.section).not.toHaveBeenCalled()
    const delayed = deferred<TraceSectionResult>()
    api.section.mockReturnValueOnce(delayed.promise)
    const pending = runtime.loadSection('checks', 2, 20)
    const assertion = expect(pending).rejects.toMatchObject({ name: 'AbortError' })
    expect(api.section).toHaveBeenCalledWith(
      'model',
      {
        revisionNId: 'r1',
        nodeKey: 'A',
        nodeToken: 't-A',
        sectionId: 'checks',
        page: 2,
        pageSize: 20,
      },
      expect.any(AbortSignal),
    )
    await runtime.select([{ kind: 'node', id: 'B' }])
    expect(api.section.mock.calls[0]![2]?.aborted).toBe(true)
    delayed.resolve({
      sectionId: 'checks',
      kind: 'Table',
      fields: [],
      columns: [],
      items: [],
      totalCount: '0',
      page: 2,
      pageSize: 20,
      observedOn: null,
    })
    await assertion
    expect(runtime.selection.value[0]?.id).toBe('B')
    expect(runtime.detailError.value).toBe('')
  })
  it('cancels section work on tab disposal and clears a revoked graph', async () => {
    const { runtime, api, start } = setup()
    await start()
    api.detail.mockResolvedValue({
      fields: [],
      links: [],
      observedOn: null,
      sections: [{ id: 'checks', label: 'Checks', kind: 'Table', pageSize: 20 }],
    })
    await runtime.select([{ kind: 'node', id: 'A' }])
    const delayed = deferred<TraceSectionResult>()
    api.section.mockReturnValueOnce(delayed.promise)
    const controller = new AbortController()
    const pending = runtime.loadSection('checks', 1, 20, controller.signal)
    const assertion = expect(pending).rejects.toMatchObject({ name: 'AbortError' })
    controller.abort()
    delayed.reject(new Error('late failure'))
    await assertion
    expect(runtime.detailError.value).toBe('')
    api.section.mockRejectedValueOnce(
      new ApiError({ kind: 'forbidden', message: 'revoked', status: 403, correlationId: 'test' }),
    )
    await expect(runtime.loadSection('checks', 1, 20)).rejects.toThrow('revoked')
    expect(runtime.result.value).toBeNull()
    expect(runtime.selection.value).toEqual([])
  })
  it('sends only selected signed server keys, never business labels', async () => {
    const { runtime, api } = setup()
    await runtime.search({}, 1)
    runtime.selectedKeys.value = ['A', 'server-key']
    await runtime.query('Both')
    expect(api.query.mock.calls[0]![1]).toMatchObject({
      startKeys: ['server-key'],
      revisionNId: 'r1',
    })
  })
  it('drops a query success and failure after cancel, even if transport ignores AbortSignal', async () => {
    const { runtime, api } = setup()
    await runtime.search({}, 1)
    runtime.selectedKeys.value = ['server-key']
    const first = deferred<TraceResult>()
    api.query.mockReturnValueOnce(first.promise)
    const pending = runtime.query('Both')
    runtime.cancel()
    first.resolve(result())
    await pending
    expect(runtime.result.value).toBeNull()
    const second = deferred<TraceResult>()
    api.query.mockReturnValueOnce(second.promise)
    const rejected = runtime.query('Both')
    runtime.cancel()
    second.reject(new Error('late failure'))
    await rejected
    expect(runtime.error.value).toBe('')
  })
  it('drops old search failure after a newer search has succeeded', async () => {
    const { runtime, api } = setup()
    const old = deferred<never>()
    api.starts.mockReturnValueOnce(old.promise)
    const pending = runtime.search({ code: 'old' }, 1)
    await runtime.search({ code: 'new' }, 1)
    old.reject(new Error('old failed'))
    await pending
    expect(runtime.starts.value[0]?.startKey).toBe('server-key')
    expect(runtime.error.value).toBe('')
  })
  it('drops old node detail success after the selected node changed', async () => {
    const { runtime, api, start } = setup()
    await start()
    const old = deferred<TraceDetail>()
    api.detail.mockReturnValueOnce(old.promise)
    const pending = runtime.select([{ kind: 'node', id: 'A' }])
    await runtime.select([{ kind: 'node', id: 'B' }])
    old.resolve({ fields: [{ label: 'stale', value: 'A' }], links: [], observedOn: null })
    await pending
    expect(runtime.detail.value?.fields).toEqual([])
    expect(api.detail.mock.calls[1]![1]).toMatchObject({ nodeKey: 'B', nodeToken: 't-B' })
  })
  it('drops late detail failure after a new selection and after a new response version', async () => {
    const { runtime, api, start } = setup()
    await start()
    const old = deferred<TraceDetail>()
    api.detail.mockReturnValueOnce(old.promise)
    const pending = runtime.select([{ kind: 'node', id: 'A' }])
    await runtime.select([{ kind: 'node', id: 'B' }])
    await runtime.expand('Both')
    old.reject(new Error('late selection failure'))
    await pending
    expect(runtime.detailError.value).toBe('')
    expect(runtime.responseVersion.value).toBe('2')
  })
  it('serializes expansion and rejects wrong-revision snapshots', async () => {
    const { runtime, api, start } = setup()
    await start()
    await runtime.select([{ kind: 'node', id: 'A' }])
    const pending = deferred<TraceResult>()
    api.expand.mockReturnValueOnce(pending.promise)
    const first = runtime.expand('Both')
    await runtime.expand('Both')
    expect(api.expand).toHaveBeenCalledTimes(1)
    pending.resolve({ ...result(), revisionNId: 'r2' })
    await first
    expect(runtime.result.value?.revisionNId).toBe('r1')
    expect(runtime.error.value).toBe('TRACE_REVISION_MISMATCH')
  })
  it('clears inaccessible graph and selection on server revocation', async () => {
    const { runtime, api, start } = setup()
    await start()
    api.detail.mockRejectedValueOnce(
      new ApiError({ kind: 'forbidden', message: 'revoked', status: 403, correlationId: 'test' }),
    )
    await runtime.select([{ kind: 'node', id: 'A' }])
    expect(runtime.result.value).toBeNull()
    expect(runtime.starts.value).toEqual([])
    expect(runtime.detail.value).toBeNull()
  })
  it('unmount cancels pending work and ignores its late result', async () => {
    const { runtime, api } = setup()
    const old = deferred<never>()
    api.starts.mockReturnValueOnce(old.promise)
    const pending = runtime.search({}, 1)
    const signal = api.starts.mock.calls[0]![2]
    wrappers[0]!.unmount()
    expect(signal?.aborted).toBe(true)
    old.reject(new Error('unmounted'))
    await pending
    expect(runtime.error.value).toBe('')
  })
})
