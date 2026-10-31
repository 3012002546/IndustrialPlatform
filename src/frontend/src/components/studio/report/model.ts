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
