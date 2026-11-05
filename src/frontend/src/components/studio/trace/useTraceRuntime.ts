import { onBeforeUnmount, onDeactivated, ref } from 'vue'
import { ApiError } from '@/api/errors'
import type {
  TraceApi,
  TraceDetail,
  TraceModalLink,
  TraceResult,
  TraceSectionResult,
  TraceStartRow,
} from '@/api/studioTrace'
import type { Target } from '../graph/types'
import type { TraceDirection } from './model'
import { mergeTraceResults } from './adapter'

const message = (e: unknown) => (e instanceof Error ? e.message : String(e))
const denied = (e: unknown) =>
  e instanceof ApiError &&
  (['unauthorized', 'forbidden', 'notFound'].includes(e.kind) ||
    e.details.code === 'TRACE_CREDENTIAL_EXPIRED')
/** Owns request cancellation and stamps. Server selection tokens never enter the graph projection. */
export function useTraceRuntime(
  api: TraceApi,
  context: () => { nId: string; revisionNId: string },
) {
  const starts = ref<TraceStartRow[]>([]),
    totalCount = ref('0'),
    selectedKeys = ref<string[]>([])
  const result = ref<TraceResult | null>(null),
    detail = ref<TraceDetail | null>(null),
    modal = ref<TraceModalLink | null>(null),
    selection = ref<Target[]>([])
  const busy = ref(false),
    startsBusy = ref(false),
    detailBusy = ref(false),
    error = ref(''),
    detailError = ref('')
  const generation = ref('0'),
    responseVersion = ref('0'),
    selectionGeneration = ref('0')
  let graphRequest: AbortController | undefined,
    startsRequest: AbortController | undefined,
    detailRequest: AbortController | undefined,
    sectionRequest: AbortController | undefined,
    linkRequest: AbortController | undefined
  let startsGeneration = 0n
  const advance = () => {
    generation.value = (BigInt(generation.value) + 1n).toString()
  }
  const scope = () => JSON.stringify(context())
  function clearDetail() {
    selectionGeneration.value = (BigInt(selectionGeneration.value) + 1n).toString()
    detailRequest?.abort()
    sectionRequest?.abort()
    linkRequest?.abort()
    detail.value = null
    modal.value = null
    detailError.value = ''
    detailBusy.value = false
  }
  function invalidate(clear = true) {
    advance()
    graphRequest?.abort()
    startsRequest?.abort()
    startsGeneration++
    busy.value = false
    startsBusy.value = false
    clearDetail()
    if (clear) {
      result.value = null
      starts.value = []
      selectedKeys.value = []
      selection.value = []
      totalCount.value = '0'
      responseVersion.value = '0'
    }
  }
  function cancel() {
    invalidate(false)
  }
  function invalidateQuery() {
    advance()
    graphRequest?.abort()
    clearDetail()
    result.value = null
    selection.value = []
    responseVersion.value = '0'
    busy.value = false
  }
  async function search(parameters: Record<string, unknown>, page: number, pageSize = 20) {
    invalidate()
    const current = ++startsGeneration,
      owner = scope(),
      captured = context(),
      controller = new AbortController()
    startsRequest = controller
    startsBusy.value = true
    error.value = ''
    try {
      const value = await api.starts(
        captured.nId,
        { revisionNId: captured.revisionNId, parameters, page, pageSize },
        controller.signal,
      )
      if (current !== startsGeneration || owner !== scope()) return
      starts.value = value.items
      totalCount.value = value.totalCount
    } catch (e) {
      if (current === startsGeneration && owner === scope()) {
        error.value = message(e)
        if (denied(e)) {
          invalidate()
          error.value = message(e)
        }
      }
    } finally {
      if (current === startsGeneration && owner === scope()) startsBusy.value = false
    }
  }
  async function query(direction: TraceDirection, relationIds?: string[], maxDepth?: number) {
    if (busy.value || !selectedKeys.value.length) return
    // Keys must still belong to the latest server result, never to a label or a client-built identity.
    const keys = selectedKeys.value.filter((key) =>
      starts.value.some((row) => row.startKey === key),
    )
    if (!keys.length) return
    advance()
    clearDetail()
    result.value = null
    selection.value = []
    responseVersion.value = '0'
    await fetchGraph(false, direction, relationIds, maxDepth, keys)
  }
  async function expand(direction: TraceDirection, relationIds?: string[]) {
    if (!busy.value && result.value && selection.value[0]?.kind === 'node')
      await fetchGraph(true, direction, relationIds)
  }
  async function fetchGraph(
    expanding: boolean,
    direction: TraceDirection,
    relationIds?: string[],
    maxDepth?: number,
    startKeys: string[] = [],
  ) {
    const current = generation.value,
      baseVersion = responseVersion.value,
      owner = scope(),
      captured = context(),
      controller = new AbortController()
    graphRequest = controller
    busy.value = true
    error.value = ''
    const valid = () =>
      generation.value === current &&
      responseVersion.value === baseVersion &&
      owner === scope() &&
      !controller.signal.aborted
    try {
      const node = expanding
        ? result.value?.nodes.find((n) => n.key === selection.value[0]?.id)
        : undefined
      if (expanding && !node) return
      const value = node
        ? await api.expand(
            captured.nId,
            {
              revisionNId: captured.revisionNId,
              nodeKey: node.key,
              nodeToken: node.nodeToken,
              direction,
              ...(relationIds?.length ? { relationIds } : {}),
              ...(result.value?.cursor ? { cursor: result.value.cursor } : {}),
            },
            controller.signal,
          )
        : await api.query(
            captured.nId,
            {
              revisionNId: captured.revisionNId,
              startKeys,
              direction,
              ...(relationIds?.length ? { relationIds } : {}),
              ...(maxDepth ? { maxDepth } : {}),
            },
            controller.signal,
          )
      if (!valid()) return
      if (value.revisionNId !== captured.revisionNId) throw new Error('TRACE_REVISION_MISMATCH')
      result.value = mergeTraceResults(expanding ? result.value : null, value)
      responseVersion.value = (BigInt(baseVersion) + 1n).toString()
      const active = [...selection.value]
      clearDetail()
      if (active.length) void select(active)
    } catch (e) {
      if (valid()) {
        error.value = message(e)
        if (denied(e)) {
          invalidate()
          error.value = message(e)
        }
      }
    } finally {
      if (generation.value === current && owner === scope()) busy.value = false
    }
  }
  async function select(targets: Target[]) {
    clearDetail()
    selection.value = targets
    const target = targets[0]
    if (target?.kind !== 'node' || !result.value) return
    const node = result.value.nodes.find((n) => n.key === target.id)
    if (!node) return
    const current = generation.value,
      version = responseVersion.value,
      selected = selectionGeneration.value,
      owner = scope(),
      captured = context(),
      controller = new AbortController()
    detailRequest = controller
    detailBusy.value = true
    const valid = () =>
      current === generation.value &&
      version === responseVersion.value &&
      selected === selectionGeneration.value &&
      owner === scope() &&
      !controller.signal.aborted
    try {
      const value = await api.detail(
        captured.nId,
        { revisionNId: captured.revisionNId, nodeKey: node.key, nodeToken: node.nodeToken },
        controller.signal,
      )
      if (valid()) detail.value = value
    } catch (e) {
      if (valid()) {
        detailError.value = message(e)
        if (denied(e)) {
          invalidate()
          error.value = message(e)
        }
      }
    } finally {
      if (valid()) detailBusy.value = false
    }
  }
  async function openLink(linkId: string) {
    const target = selection.value[0]
    if (target?.kind !== 'node' || !detail.value?.links.some((l) => l.id === linkId)) return
    const node = result.value?.nodes.find((n) => n.key === target.id)
    if (!node) return
    linkRequest?.abort()
    const controller = new AbortController()
    linkRequest = controller
    const current = generation.value,
      version = responseVersion.value,
      selected = selectionGeneration.value,
      owner = scope(),
      captured = context()
    const valid = () =>
      current === generation.value &&
      version === responseVersion.value &&
      selected === selectionGeneration.value &&
      owner === scope() &&
      !controller.signal.aborted
    detailError.value = ''
    try {
      const value = await api.link(
        captured.nId,
        { revisionNId: captured.revisionNId, nodeKey: node.key, nodeToken: node.nodeToken, linkId },
        controller.signal,
      )
      if (valid()) modal.value = value
    } catch (e) {
      if (valid()) {
        detailError.value = message(e)
        if (denied(e)) {
          invalidate()
          error.value = message(e)
        }
      }
    }
  }
  /** Section loaders use the same verified node and revision as the visible details. */
  async function loadSection(
    sectionId: string,
    page: number,
    pageSize: number,
    signal?: AbortSignal,
  ): Promise<TraceSectionResult> {
    const target = selection.value[0]
    const node =
      target?.kind === 'node' ? result.value?.nodes.find((n) => n.key === target.id) : undefined
    if (!node || !detail.value?.sections?.some((section) => section.id === sectionId))
      throw new Error('GRAPH_CONTEXT_STALE')
    sectionRequest?.abort()
    const controller = new AbortController()
    sectionRequest = controller
    const abort = () => controller.abort()
    if (signal?.aborted) controller.abort()
    signal?.addEventListener('abort', abort, { once: true })
    const current = generation.value,
      version = responseVersion.value,
      selected = selectionGeneration.value,
      owner = scope(),
      captured = context()
    const valid = () =>
      current === generation.value &&
      version === responseVersion.value &&
      selected === selectionGeneration.value &&
      owner === scope() &&
      !controller.signal.aborted
    try {
      const value = await api.section(
        captured.nId,
        {
          revisionNId: captured.revisionNId,
          nodeKey: node.key,
          nodeToken: node.nodeToken,
          sectionId,
          page,
          pageSize,
        },
        controller.signal,
      )
      if (!valid()) throw new DOMException('Aborted', 'AbortError')
      if (value.sectionId !== sectionId) throw new Error('GRAPH_CONTEXT_STALE')
      return value
    } catch (e) {
      if (!valid()) throw new DOMException('Aborted', 'AbortError')
      if (denied(e)) {
        invalidate()
        error.value = message(e)
      }
      throw e
    } finally {
      signal?.removeEventListener('abort', abort)
    }
  }
  function closeLink() {
    linkRequest?.abort()
    modal.value = null
  }
  // Switching workspace tabs retains completed results; only floating link
  // previews close. Scope changes and unmount still invalidate every request.
  onDeactivated(closeLink)
  onBeforeUnmount(() => invalidate())
  return {
    starts,
    totalCount,
    selectedKeys,
    result,
    detail,
    modal,
    selection,
    busy,
    startsBusy,
    detailBusy,
    error,
    detailError,
    generation,
    responseVersion,
    selectionGeneration,
    search,
    query,
    expand,
    select,
    loadSection,
    openLink,
    closeLink,
    invalidate,
    invalidateQuery,
    cancel,
  }
}
