import type { Json } from '@/api/studio'
import { clone } from '../shared/model'
import { reportBodyWidth } from './model'
import { tableCell } from './tableEditing'

export interface QuerySourceField {
  path: string[]
  label: string
  dataType?: string
}

export function querySourceFields(schema: Json): QuerySourceField[] {
  const fields: QuerySourceField[] = []
  function visit(node: Json, path: string[]) {
    if (!node || path.length > 16) return
    if (node.type === 'Object') {
      for (const child of node.children ?? []) visit(child, [...path, String(child.name)])
    } else if (node.type !== 'Array' && path.length) {
      fields.push({
        path,
        label: node.label || path.join('.'),
        dataType: node.type === 'Guid' ? 'String' : node.type,
      })
    }
  }
  visit(schema.type === 'Array' ? schema.item : schema, [])
  return fields
}

/** Match stable bindings, retaining the user's column order, names, visibility and IDs. */
export function syncQueryFields(document: Json, fields: QuerySourceField[]): Json {
  if (!fields.length) return document
  const doc = clone(document)
  const band = doc.bands.find((item: Json) => item.id === doc.query.resultBandId)
  const tables: Json[] = band.controls.filter((item: Json) => item.type === 'Table')
  const table = tables[0]
  if (!table) return document
  const known = new Map<string, { source: Json; entry: Json }>()
  for (const entry of doc.query.columns) {
    const control = tables.find((item) => item.id === entry.controlId)
    const source = control?.table.columns.find((item: Json) => item.id === entry.columnId)
    const binding = control && tableCell(control.table, 0, entry.columnId)?.value
    if (source && binding?.from === 'Row')
      known.set(JSON.stringify(binding.path), { source, entry })
  }
  const available = new Map(fields.map((field) => [JSON.stringify(field.path), field]))
  const keys = [...known.keys()].filter((key) => available.has(key))
  keys.push(...[...available.keys()].filter((key) => !known.has(key)))
  const columns: Json[] = [],
    entries: Json[] = []
  for (const key of keys) {
    const field = available.get(key)!,
      prior = known.get(key)
    const source = prior?.source ?? { id: crypto.randomUUID(), label: field.label }
    source.value = { from: 'Row', path: field.path, nullPolicy: 'Null' }
    source.widthMm = Number(table.widthMm ?? reportBodyWidth(doc)) / keys.length
    columns.push(source)
    entries.push({
      ...(prior?.entry ?? { visible: true, sortable: true }),
      controlId: table.id,
      columnId: source.id,
      dataType: field.dataType ?? 'String',
    })
  }
  table.table = { ...table.table, rowMode: 'BandRow', columns }
  delete table.table.rows
  band.controls = band.controls.filter(
    (item: Json) => item.type !== 'Table' || item.id === table.id,
  )
  doc.query.columns = entries
  doc.query.filters = (doc.query.filters ?? []).flatMap((filter: Json) => {
    if (!filter.fieldId) return [filter]
    const column = entries.find((entry) => entry.columnId === filter.fieldId)
    return column ? [{ ...filter, dataType: column.dataType }] : []
  })
  doc.query.defaultSort = (doc.query.defaultSort ?? []).filter((sort: Json) =>
    entries.some(
      (entry) => entry.columnId === sort.fieldId && entry.visible !== false && entry.sortable,
    ),
  )
  return doc
}

export function queryFilterColumns(doc: Json, schemas: Json = {}): Json[] {
  const instance = doc.instances?.find((item: Json) => item.instanceId === doc.query?.instanceId)
  const fields = querySourceFields(schemas[instance?.revisionNId] ?? {})
  const band = doc.bands?.find((item: Json) => item.id === doc.query?.resultBandId)
  return (doc.query?.columns ?? []).map((entry: Json) => {
    const table = band?.controls.find((item: Json) => item.id === entry.controlId)?.table
    const column = table?.columns.find((item: Json) => item.id === entry.columnId)
    const binding = table && tableCell(table, 0, entry.columnId)?.value
    const field = fields.find((item) => JSON.stringify(item.path) === JSON.stringify(binding?.path))
    const sample = (binding?.path ?? []).reduce(
      (value: Json, key: string) => value?.[key],
      instance?.value?.[0],
    )
    const inferred =
      typeof sample === 'number'
        ? 'Decimal'
        : typeof sample === 'boolean'
          ? 'Boolean'
          : typeof sample === 'string' && /^\d{4}-\d{2}-\d{2}(T|$)/.test(sample)
            ? 'Date'
            : 'String'
    return {
      fieldId: entry.columnId,
      label: entry.label ?? column?.label ?? entry.columnId,
      dataType: field?.dataType ?? entry.dataType ?? inferred,
    }
  })
}
