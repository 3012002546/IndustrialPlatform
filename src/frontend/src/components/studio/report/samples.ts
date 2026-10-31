import type { Json } from '@/api/studio'
import { clone } from '@/components/studio/shared/model'
import { initialReportDocument, paper } from './model'
import rows from './samples/cmp-batches.json'

export const cmpSampleName = '样例-CMP批次生产汇总报表'
const rowBinding = (name: string): Json => ({ from: 'Row', path: [name], nullPolicy: 'Error' })
const text = (
  id: string,
  value: string,
  xMm: number,
  yMm: number,
  widthMm: number,
  styleId = 'body',
): Json => ({
  id,
  type: 'Text',
  xMm,
  yMm,
  widthMm,
  heightMm: 7,
  canGrow: true,
  styleId,
  value: { from: 'Constant', value, nullPolicy: 'Error' },
})

export function cmpSampleReport(): Json {
  const doc = initialReportDocument()
  doc.parameters = [
    {
      name: 'reportTitle',
      label: '报表标题',
      type: 'String',
      required: false,
      nullable: false,
      defaultValue: 'CMP批次生产汇总报表',
    },
  ]
  doc.instances = [
    {
      instanceId: 'cmpBatches',
      sourceType: 'Constant',
      value: clone(rows),
      parameters: [],
      dependsOn: [],
    },
  ]
  doc.styles.push(
    { ...clone(doc.styles[0]), id: 'title', fontSizePt: 18, fontWeight: 700, color: '#153E59' },
    { ...clone(doc.styles[0]), id: 'number', textAlign: 'Right', numberFormat: '0.0' },
  )
  const title = text('title', '', 0, 0, 180, 'title')
  title.heightMm = 12
  title.value = { from: 'PageParam', path: ['reportTitle'], nullPolicy: 'Error' }
  const columns: [string, string, number][] = [
    ['workOrderNo', '工单', 28],
    ['operationName', '工序', 18],
    ['batchNo', '批次', 28],
    ['product', '产品', 24],
    ['plannedVolume', '计划量 L', 23],
    ['actualVolume', '实际量 L', 23],
    ['quality', '质量状态', 20],
    ['reviewer', '复核人', 16],
  ]
  const total = (id: string, field: string, xMm: number): Json => ({
    id,
    type: 'Field',
    xMm,
    yMm: 9,
    widthMm: 50,
    heightMm: 7,
    styleId: 'number',
    aggregate: { function: 'Sum', scopeBandId: 'batches', field: rowBinding(field) },
  })
  doc.bands = [
    {
      id: 'report-header',
      kind: 'ReportHeader',
      name: '报表说明',
      heightMm: 30,
      keepWithNext: true,
      controls: [
        title,
        text('subtitle', '模拟样例 · 2026-10-28 · 2个工单 / 6个批次 · 数量单位 L', 0, 16, 180),
      ],
    },
    {
      id: 'batches',
      kind: 'Detail',
      name: 'CMP批次明细',
      heightMm: 12,
      grow: true,
      data: { mode: 'RootPath', instanceId: 'cmpBatches', path: [] },
      controls: [
        {
          id: 'batch-table',
          type: 'Table',
          xMm: 0,
          yMm: 0,
          widthMm: 180,
          heightMm: 10,
          styleId: 'body',
          canGrow: true,
          table: {
            rowMode: 'BandRow',
            headerMode: 'OncePerParent',
            repeatHeader: true,
            columns: columns.map(([id, label, widthMm]) => ({
              id,
              label,
              widthMm,
              value: rowBinding(id),
              styleId: ['plannedVolume', 'actualVolume'].includes(id) ? 'number' : 'body',
            })),
          },
        },
      ],
    },
    {
      id: 'report-footer',
      kind: 'ReportFooter',
      name: '产量汇总',
      heightMm: 33,
      controls: [
        text('planned-label', '计划合计 L', 0, 0, 50),
        total('planned-total', 'plannedVolume', 0),
        text('actual-label', '实际合计 L', 60, 0, 50),
        total('actual-total', 'actualVolume', 60),
        text('note', '模拟数据，仅用于C3功能验证；待检与复核批次不计为放行。', 0, 23, 180),
      ],
    },
    {
      id: 'page-footer',
      kind: 'PageFooter',
      name: '页码',
      heightMm: 7,
      controls: [
        {
          id: 'page-number',
          type: 'PageNumber',
          xMm: 160,
          yMm: 0,
          widthMm: 20,
          heightMm: 5,
          styleId: 'body',
        },
      ],
    },
  ]
  return doc
}

export function cmpSampleBundle(mode: string, reportNId: string, revisionNId: string): Json {
  const doc = initialReportDocument('ReportBundle', mode)
  if (mode === 'Simple') {
    doc.items = ['生产汇总联', '质量复核联'].map((name, i) => ({
      id: `copy-${i + 1}`,
      name,
      reportNId,
      revisionNId,
      enabled: true,
      required: true,
      startOnNewPage: true,
      parameterMap: [
        {
          target: 'reportTitle',
          source: { from: 'Constant', value: `CMP批次${name}`, nullPolicy: 'Error' },
        },
      ],
    }))
    return doc
  }
  const workOrders = ['WO-CMP-1028-A', 'WO-CMP-1028-B'].map((workOrderNo) => ({
    workOrderNo,
    operations: [
      { operationName: '配制', batches: rows.filter((row) => row.workOrderNo === workOrderNo) },
      ...(workOrderNo.endsWith('A') ? [{ operationName: '包装（暂无记录）', batches: [] }] : []),
    ],
  }))
  doc.paper = paper()
  doc.instances = [
    {
      instanceId: 'workOrders',
      sourceType: 'Constant',
      value: clone(workOrders),
      parameters: [],
      dependsOn: [],
    },
  ]
  doc.assembly.root = { mode: 'RootPath', instanceId: 'workOrders', path: [] }
  doc.assembly.operation.collection = { mode: 'ParentPath', path: ['operations'] }
  doc.assembly.rules = [
    {
      id: 'cmp-records',
      name: '逐批次汇集配制记录',
      priority: 1,
      reportNId,
      revisionNId,
      parameterMap: [],
      repeat: { mode: 'ParentPath', path: ['batches'] },
      required: false,
      emptyBehavior: 'Skip',
      headerMode: 'Inherit',
      breakBefore: 'Auto',
      content: {
        mode: 'BandSubtree',
        entryBandId: 'batches',
        rowMap: Object.keys(rows[0]!).map((name) => ({
          targetPath: [name],
          source: {
            from: name === 'workOrderNo' ? 'Root' : name === 'operationName' ? 'ParentRow' : 'Row',
            path: [name],
            nullPolicy: 'Error',
          },
        })),
        ancestorMap: [],
      },
    },
  ]
  return doc
}
