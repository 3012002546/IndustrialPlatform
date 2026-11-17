import type { Json } from '@/api/studio'
export const reportContexts = [
  'Instance',
  'PageParam',
  'Row',
  'ParentRow',
  'Root',
  'Context',
  'Constant',
]
export const binding = (from = 'Row'): Json => ({ from, path: [], nullPolicy: 'Error' })
export const paper = (): Json => ({
  size: 'A4',
  orientation: 'Portrait',
  margin: { top: 15, right: 15, bottom: 15, left: 15 },
  headerHeightMm: 15,
  footerHeightMm: 10,
})
export function initialReportDocument(kind = 'Report', mode = 'Simple'): Json {
  if (kind === 'Report' && mode === 'Query') {
    const doc = initialReportDocument()
    doc.mode = 'Query'
    doc.instances = [{ instanceId: 'rows', sourceType: 'Constant', value: [] }]
    doc.bands[0].data.instanceId = 'rows'
    doc.query = {
      instanceId: 'rows',
      resultBandId: doc.bands[0].id,
      columns: [],
      filters: [],
      defaultSort: [],
      defaultPageSize: 200,
    }
    appendQueryColumn(doc, 'Column 1')
    return doc
  }
  if (kind === 'ReportBundle')
    return {
      schemaVersion: 1,
      mode,
      parameters: [],
      instances: [],
      ...(mode === 'Simple'
        ? { items: [], pageNumberMode: 'Preserve' }
        : {
            paper: paper(),
            assembly: {
              root: { mode: 'RootPath', instanceId: '', path: [] },
              order: [],
              operation: { collection: { mode: 'ParentPath', path: [] }, order: [] },
              rules: [],
            },
          }),
    }
  return {
    schemaVersion: 1,
    mode: 'Fixed',
    parameters: [],
    instances: [],
    paper: paper(),
    bands: [
      {
        id: crypto.randomUUID(),
        kind: 'Detail',
        name: 'Detail',
        heightMm: 20,
        grow: true,
        controls: [],
        data: { mode: 'RootPath', instanceId: '', path: [] },
        emptyBehavior: 'Skip',
      },
    ],
    styles: [
      {
        id: 'body',
        fontFamily: 'sans-serif',
        fontSizePt: 10,
        fontWeight: 400,
        color: '#000000',
        textAlign: 'Left',
        lineHeight: 1.3,
      },
    ],
    assets: [],
  }
}
/** Query fields share the existing server contract; physical export widths remain automatic. */
export function appendQueryColumn(doc: Json, label: string) {
  const band = doc.bands.find((item: Json) => item.id === doc.query.resultBandId)
  if (!band) return
  let control = band.controls.find((item: Json) => item.type === 'Table')
  if (!control) {
    control = {
      id: crypto.randomUUID(),
      type: 'Table',
      xMm: 0,
      yMm: 0,
      widthMm: reportBodyWidth(doc),
      heightMm: 10,
      styleId: 'body',
      canGrow: true,
      table: { rowMode: 'BandRow', columns: [], headerMode: 'OncePerParent', repeatHeader: true },
    }
    band.controls.push(control)
  }
  const column = {
    id: crypto.randomUUID(),
    label,
    widthMm: 1,
    value: { from: 'Row', path: [], nullPolicy: 'Null' },
  }
  control.table.columns.push(column)
  for (const item of control.table.columns)
    item.widthMm = control.widthMm / control.table.columns.length
  for (const row of control.table.rows ?? [])
    row.cells.push({ columnId: column.id, value: { ...column.value } })
  doc.query.columns.push({
    controlId: control.id,
    columnId: column.id,
    visible: true,
    sortable: true,
  })
}
export function reportPaperSize(doc: Json): { width: number; height: number } {
  const p = doc.paper ?? paper()
  const sizes: Record<string, number[]> = {
    A4: [210, 297],
    A3: [297, 420],
    Letter: [215.9, 279.4],
    Custom: [Number(p.widthMm), Number(p.heightMm)],
  }
  const dimensions = sizes[p.size] ?? sizes.A4!
  return {
    width: dimensions[p.orientation === 'Landscape' ? 1 : 0]!,
    height: dimensions[p.orientation === 'Landscape' ? 0 : 1]!,
  }
}
export function reportBodyWidth(doc: Json): number {
  const p = doc.paper ?? paper()
  return Math.max(
    1,
    reportPaperSize(doc).width - Number(p.margin?.left ?? 15) - Number(p.margin?.right ?? 15),
  )
}
