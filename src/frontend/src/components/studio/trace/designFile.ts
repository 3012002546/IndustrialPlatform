import { readTraceDocument, type TraceDocument } from './model'
import { traceStructureErrors } from './adapter'

export const TRACE_DESIGN_FORMAT = 'IndustrialPlatform.TraceDesign'
export const MAX_TRACE_FILE_BYTES = 2 * 1024 * 1024
export class TraceDesignFileError extends Error {
  constructor(readonly reason: 'designFileSize' | 'designFileInvalid' | 'designFileVersion') {
    super(reason)
  }
}
const invalid = (): never => {
  throw new TraceDesignFileError('designFileInvalid')
}
function object(value: unknown): Record<string, unknown> {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return invalid()
  return value as Record<string, unknown>
}
function array(value: unknown, check: (value: unknown) => void) {
  if (!Array.isArray(value)) return invalid()
  value.forEach(check)
}
const string = (value: unknown) => {
  if (typeof value !== 'string') invalid()
}
const number = (value: unknown) => {
  if (typeof value !== 'number' || !Number.isFinite(value)) invalid()
}
const boolean = (value: unknown) => {
  if (typeof value !== 'boolean') invalid()
}
function fields(value: unknown, names: string[], check = string) {
  const item = object(value)
  names.forEach((key) => check(item[key]))
  return item
}
function binding(value: unknown) {
  const item = fields(value, ['from'])
  if (item.path !== undefined)
    array(item.path, (part) => {
      if (typeof part !== 'string' && typeof part !== 'number') invalid()
    })
  if (item.nullPolicy !== undefined) string(item.nullPolicy)
}
const identity = (value: unknown) => {
  const item = object(value)
  ;['sourceSystem', 'factory', 'businessKey'].forEach((key) => binding(item[key]))
}
const mappings = (value: unknown) =>
  array(value, (entry) => {
    const item = fields(entry, ['target'])
    binding(item.source)
  })
const displayFields = (value: unknown) =>
  array(value, (entry) => {
    const item = fields(entry, ['label'])
    binding(item.value)
  })
function traversal(value: unknown) {
  const item = fields(value, ['datasetRevisionNId'])
  mappings(item.inputMap)
  const result = fields(item.result, ['targetObjectTypeId'])
  array(result.collectionPath, string)
  identity(result.targetIdentity)
  displayFields(result.summaryMap)
}
/** Validate nested editor inputs before any imported JSON becomes reactive editable state. */
function checkDocument(value: unknown): TraceDocument {
  const doc = object(value)
  if (doc.schemaVersion !== 1) throw new TraceDesignFileError('designFileVersion')
  if (doc.presentation !== undefined) {
    const presentation = fields(doc.presentation, ['layoutDirection'])
    if (!['LeftToRight', 'TopToBottom'].includes(String(presentation.layoutDirection))) invalid()
  }
  array(doc.parameters, (entry) => {
    const item = fields(entry, ['name', 'type'])
    boolean(item.required)
  })
  const scope = object(doc.scope)
  array(scope.factoryIds, string)
  array(scope.industryTags, string)
  const start = fields(doc.start, ['datasetRevisionNId'])
  array(start.objectTypeIds, string)
  array(start.collectionPath, string)
  mappings(start.parameterMap)
  binding(start.rowKey)
  binding(start.objectType)
  identity(start.identityMap)
  array(start.searchFields, (entry) => {
    const item = fields(entry, ['parameterName', 'label', 'control'])
    boolean(item.required)
  })
  array(start.columns, (entry) => {
    const item = fields(entry, ['fieldId', 'label'])
    number(item.width)
    boolean(item.visible)
  })
  array(doc.objects, (entry) => {
    const item = fields(entry, ['id', 'name'])
    identity(item.identity)
    fields(item.identity, ['grain'])
    displayFields(item.summary)
    const detail = fields(item.detail, ['datasetRevisionNId'])
    mappings(detail.parameterMap)
    displayFields(detail.fields)
    if (detail.sections !== undefined) {
      const ids = new Set<string>()
      array(detail.sections, (entry) => {
        const section = fields(entry, ['id', 'label', 'kind', 'datasetRevisionNId'])
        const id = String(section.id)
        if (!id || ids.has(id) || !['Fields', 'Table'].includes(String(section.kind))) invalid()
        ids.add(id)
        mappings(section.parameterMap)
        array(section.collectionPath, string)
        displayFields(section.fields)
        array(section.columns, (entry) => {
          const column = fields(entry, ['fieldId', 'label'])
          number(column.width)
          boolean(column.visible)
        })
        if (section.rowKey !== undefined) binding(section.rowKey)
        else if (section.kind === 'Table') invalid()
        number(section.pageSize)
        if (
          !Number.isInteger(section.pageSize) ||
          Number(section.pageSize) < 1 ||
          Number(section.pageSize) > 200
        )
          invalid()
      })
    }
    array(detail.links, (entry) => {
      const link = fields(entry, ['id', 'label', 'kind', 'openMode'])
      binding(link.value)
      if (link.parameterMap !== undefined) mappings(link.parameterMap)
      if (link.fileSource !== undefined) {
        const source = fields(link.fileSource, ['mode'])
        if (source.version !== undefined) binding(source.version)
      }
    })
    fields(item.style, ['color', 'iconKey'])
    fields(item.style, ['width', 'height'], number)
  })
  array(doc.relations, (entry) => {
    const item = fields(entry, ['id', 'name', 'fromTypeId', 'toTypeId', 'kind', 'semantic'])
    traversal(item.forward)
    traversal(item.reverse)
    binding(item.identity)
    binding(item.label)
    const evidence = object(item.evidence)
    binding(evidence.recordKey)
    binding(evidence.occurredOn)
    if (evidence.quantity !== undefined) binding(evidence.quantity)
    if (evidence.unit !== undefined) binding(evidence.unit)
  })
  fields(doc.limits, ['maxDepth', 'maxNodes', 'maxEdges', 'timeoutSeconds'], number)
  const layout = object(doc.layout)
  array(layout.nodes, (entry) => {
    fields(entry, ['objectTypeId'])
    fields(entry, ['x', 'y', 'width', 'height'], number)
  })
  array(layout.edges, (entry) => {
    const edge = fields(entry, ['relationId'])
    for (const port of ['sourcePort', 'targetPort']) if (edge[port] !== null) string(edge[port])
    array(edge.vertices, (point) => {
      fields(point, ['x', 'y'], number)
    })
  })
  const result = readTraceDocument(doc)
  const ids = new Set(result.objects.map((item) => item.id))
  if (
    traceStructureErrors(result).length ||
    result.relations.some((item) => !ids.has(item.fromTypeId) || !ids.has(item.toTypeId))
  )
    invalid()
  return result
}
export function parseTraceDesign(text: string): { name: string; document: TraceDocument } {
  if (new TextEncoder().encode(text).length > MAX_TRACE_FILE_BYTES)
    throw new TraceDesignFileError('designFileSize')
  let parsed: unknown
  try {
    parsed = JSON.parse(text.replace(/^\uFEFF/, ''))
  } catch {
    return invalid()
  }
  const file = object(parsed)
  const wrapped = 'format' in file || 'document' in file
  if (wrapped && (file.format !== TRACE_DESIGN_FORMAT || file.version !== 1))
    throw new TraceDesignFileError('designFileVersion')
  return {
    name: wrapped && typeof file.name === 'string' ? file.name.slice(0, 128) : '',
    document: checkDocument(wrapped ? file.document : file),
  }
}
export function serializeTraceDesign(name: string, doc: TraceDocument): string {
  const {
    schemaVersion,
    parameters,
    scope,
    start,
    objects,
    relations,
    layout,
    limits,
    presentation,
  } = doc
  return JSON.stringify(
    {
      format: TRACE_DESIGN_FORMAT,
      version: 1,
      name,
      exportedOn: new Date().toISOString(),
      document: {
        schemaVersion,
        parameters,
        scope,
        start,
        objects,
        relations,
        layout,
        limits,
        ...(presentation ? { presentation } : {}),
      },
    },
    null,
    2,
  )
}
export function downloadTraceDesign(name: string, doc: TraceDocument) {
  const content = serializeTraceDesign(name, doc)
  if (new Blob([content]).size > MAX_TRACE_FILE_BYTES)
    throw new TraceDesignFileError('designFileSize')
  const url = URL.createObjectURL(new Blob([content], { type: 'application/json;charset=utf-8' }))
  const link = document.createElement('a')
  link.href = url
  link.download = `${name.replace(/[<>:"/\\|?*\u0000-\u001f]/g, '_').slice(0, 80) || 'trace'}.trace.json`
  link.click()
  setTimeout(() => URL.revokeObjectURL(url), 1000)
}
