// Reproducible, synthetic MES report example. No connection to production MES.
import { mkdir, writeFile } from 'node:fs/promises'
import { resolve } from 'node:path'

const out = resolve('src/frontend/src/components/studio/report/samples/photoresist-example.json')
const folder = '示例/光刻胶批生产记录'
const description = '合成示例数据；用于演示完整工单快照驱动报表，不含真实配方或生产判定标准。'
const prefix = 'demo-photoresist-20261114-'
const orderNo = 'DEMO-PR-20261114-001'
const batch = 'DEMO-PR-B001'
const product = '光刻胶演示产品 PR-DEMO'
const definitions = [
  ['orderInfo', '工单基本信息', [['label', '项目', 43], ['value', '内容', 137]]],
  ['bom', '执行BOM信息', [['code', '物料编码', 30], ['name', '物料名称', 48], ['lot', '批次', 38], ['quantity', '执行量', 24], ['unit', '单位', 16], ['role', '用途', 24]]],
  ['basic', '工序基本信息', [['label', '项目', 43], ['value', '内容', 137]]],
  ['tips', '操作提示', [['sequence', '序号', 15], ['instruction', '提示内容', 125], ['confirmation', '确认记录', 40]]],
  ['materials', '物料需求', [['code', '物料编码', 32], ['name', '物料名称', 50], ['required', '需求量', 25], ['issued', '领用量', 25], ['unit', '单位', 16], ['status', '状态', 32]]],
  ['weighings', '称量记录', [['record', '记录号', 32], ['material', '物料', 38], ['lot', '批次', 32], ['tare', '皮重/kg', 22], ['net', '净重/kg', 22], ['operator', '称量/复核', 34]]],
  ['inspections', '过程报检', [['request', '报检单号', 34], ['item', '检验项目', 40], ['result', '记录值', 26], ['unit', '单位', 18], ['status', '状态', 25], ['reviewer', '检验/复核', 37]]],
  ['parameters', '工艺参数', [['name', '参数名称', 48], ['recorded', '记录值', 38], ['unit', '单位', 24], ['equipment', '设备', 34], ['operator', '记录人', 36]]],
]
const names = Object.fromEntries(definitions.map(([key, name]) => [key, name]))
const order = {
  id: orderNo, display: `工单：${orderNo}　批次：${batch}`,
  orderInfo: [
    { label: '工单编号', value: orderNo }, { label: '产品名称', value: product },
    { label: '产品批号', value: batch }, { label: '计划数量', value: '100.00 kg（示例）' },
    { label: '生产线 / 车间', value: '光刻胶示例线 / 洁净配制车间' },
    { label: '工艺路线 / 执行BOM版本', value: 'PR-DEMO-R01 / BOM-DEMO-V1' },
    { label: '计划日期 / 工单状态', value: '2026-11-14 / 示例完工待复核' },
    { label: '记录说明', value: '全份数据为合成示例，所有工序和明细一次采集后组装。' },
  ],
  bom: [
    { code: 'DEMO-R01', name: '树脂溶液（示例）', lot: 'DEMO-R-001', quantity: '30.00', unit: 'kg', role: '主料' },
    { code: 'DEMO-S01', name: '配制溶剂（示例）', lot: 'DEMO-S-001', quantity: '65.00', unit: 'kg', role: '溶剂' },
    { code: 'DEMO-A01', name: '功能组分（示例）', lot: 'DEMO-A-001', quantity: '5.00', unit: 'kg', role: '辅料' },
    { code: 'DEMO-F01', name: '过滤组件（示例）', lot: 'DEMO-F-001', quantity: '2', unit: '套', role: '耗材' },
    { code: 'DEMO-P01', name: '洁净包装瓶（示例）', lot: 'DEMO-P-001', quantity: '20', unit: '个', role: '包装' },
  ],
  operations: [],
}
const stages = [
  ['开工确认', ['tips', 'parameters']],
  ['原料核对', ['materials']],
  ['称量备料', ['tips', 'materials', 'weighings']],
  ['投料配制', ['tips', 'materials', 'weighings', 'inspections', 'parameters']],
  ['混合熟化', ['tips', 'parameters']],
  ['预过滤', ['parameters']],
  ['精密过滤', ['tips', 'inspections', 'parameters']],
  ['过程取样', ['tips', 'inspections']],
  ['灌装包装', ['tips', 'materials', 'weighings']],
  ['完工复核', []],
]
for (const [index, [name, available]] of stages.entries()) {
  const no = String((index + 1) * 10).padStart(3, '0')
  const equipment = ['CHK-DEMO', 'MAT-DEMO', 'BAL-DEMO', 'MIX-DEMO', 'MIX-DEMO', 'FLT-DEMO-1', 'FLT-DEMO-2', 'LAB-DEMO', 'FILL-DEMO', 'REV-DEMO'][index]
  const op = {
    id: `OP-${no}`, sequence: index + 1, name, display: `${orderNo}　${no} ${name}`,
    basic: [
      { label: '工序编号 / 名称', value: `${no} / ${name}` },
      { label: '执行状态 / 设备', value: `已完成（示例） / ${equipment}` },
      { label: '执行人员 / 复核人员', value: '示例操作员 / 示例复核员' },
      { label: '开始 / 完成时间', value: `2026-11-14 ${String(8 + index).padStart(2, '0')}:00 / ${String(8 + index).padStart(2, '0')}:30` },
    ],
    tips: [], materials: [], weighings: [], inspections: [], parameters: [],
  }
  for (const key of available) {
    if (key === 'tips') op.tips = [
      { sequence: '1', instruction: `执行${name}前核对工单、设备及上一工序交接状态。`, confirmation: '示例：已确认' },
      { sequence: '2', instruction: '本页仅展示操作提示与确认记录的报表结构。', confirmation: '示例：已复核' },
    ]
    if (key === 'materials') op.materials = [
      { code: index === 8 ? 'DEMO-P01' : 'DEMO-R01', name: index === 8 ? '洁净包装瓶（示例）' : '树脂溶液（示例）', required: index === 8 ? '20' : '30.00', issued: index === 8 ? '20' : '30.00', unit: index === 8 ? '个' : 'kg', status: '示例：已领用' },
      { code: index === 8 ? 'DEMO-L01' : 'DEMO-S01', name: index === 8 ? '批号标签（示例）' : '配制溶剂（示例）', required: index === 8 ? '20' : '65.00', issued: index === 8 ? '20' : '65.00', unit: index === 8 ? '张' : 'kg', status: '示例：已领用' },
    ]
    if (key === 'weighings') op.weighings = Array.from({ length: index === 2 ? 6 : 2 }, (_, i) => ({
      record: `W-${no}-${String(i + 1).padStart(2, '0')}`, material: index === 8 ? '灌装成品（示例）' : i % 2 ? '配制溶剂（示例）' : '树脂溶液（示例）',
      lot: batch, tare: (1.2 + i / 100).toFixed(2), net: (index === 8 ? 5 : 10 + i).toFixed(2), operator: '示例甲 / 示例乙',
    }))
    if (key === 'inspections') op.inspections = [
      { request: `QC-${no}-01`, item: '外观记录', result: '示例：均匀', unit: '—', status: '已记录', reviewer: '示例检验员' },
      { request: `QC-${no}-02`, item: '黏度记录', result: '4.85（示例）', unit: 'mPa·s', status: '待复核', reviewer: '示例检验员' },
      { request: `QC-${no}-03`, item: '颗粒记录', result: '12（示例）', unit: '个/mL', status: '待复核', reviewer: '示例检验员' },
    ]
    if (key === 'parameters') op.parameters = [
      { name: '环境温度（示例）', recorded: '22.0', unit: '℃', equipment, operator: '示例操作员' },
      { name: '执行时长（示例）', recorded: '30', unit: 'min', equipment, operator: '示例操作员' },
    ]
  }
  order.operations.push(op)
}

const bind = (from, path) => ({ from, path, nullPolicy: 'Error' })
const literal = value => ({ from: 'Constant', value, nullPolicy: 'Error' })
const paper = { size: 'A4', orientation: 'Portrait', margin: { top: 15, right: 15, bottom: 15, left: 15 }, headerHeightMm: 8, footerHeightMm: 8 }
const body = { id: 'body', fontFamily: 'MicrosoftYaHei', fontSizePt: 9, fontWeight: 400, color: '#202830', textAlign: 'Left', lineHeight: 1.35, paddingMm: 1.5 }
const label = (id, value, x, y, width, styleId = 'body', type = 'Text') => ({ id, type, xMm: x, yMm: y, widthMm: width, heightMm: 7, styleId, canGrow: true, value })
const resources = definitions.map(([key, name, columns], index) => {
  const header = index < 2
  const owner = header ? order : order.operations.find(op => op[key].length)
  const rows = owner[key]
  return {
    key, nId: prefix + key.toLowerCase(), kind: 'Report', name: `示例·光刻胶·${String(index + 1).padStart(2, '0')} ${name}`, folder, description,
    document: {
      schemaVersion: 1, mode: 'Fixed', paper,
      parameters: [{ name: 'context', label: '工单与工序', type: 'String', required: false, nullable: false, defaultValue: owner.display }],
      instances: [{ instanceId: '模板明细', sourceType: 'Constant', value: rows, parameters: [], dependsOn: [] }],
      styles: [body, { ...body, id: 'title', fontSizePt: 16, fontWeight: 700, color: '#163E50', paddingMm: 0 }, { ...body, id: 'caption', fontSizePt: 8, color: '#56616A', paddingMm: 0 }], assets: [],
      bands: [
        { id: 'section-title', kind: 'ReportHeader', name: name + '标题', heightMm: 12, keepWithNext: true, controls: [
          label('title', literal(name), 0, 2, 60, 'title'),
          label('context', bind('PageParam', ['context']), 62, 2, 118, 'caption', 'Field'),
        ] },
        { id: 'records', kind: 'Detail', name: name + '明细', heightMm: 7, grow: true, emptyBehavior: 'Skip', data: { mode: 'RootPath', instanceId: '模板明细', path: [] }, controls: [
          { id: 'record-table', type: 'Table', xMm: 0, yMm: 0, widthMm: 180, heightMm: 7, styleId: 'body', canGrow: true, table: {
            rowMode: 'BandRow', headerMode: 'OncePerParent', repeatHeader: true,
            columns: columns.map(([id, label, widthMm]) => ({ id, label, widthMm, styleId: 'body', value: bind('Row', [id]) })),
          } },
        ] },
        { id: 'page-footer', kind: 'PageFooter', name: '示例页脚', heightMm: 7, controls: [
          label('footer', literal('合成示例数据 · 光刻胶批生产记录'), 0, 0, 150, 'caption'),
          { id: 'page-number', type: 'PageNumber', xMm: 160, yMm: 0, widthMm: 20, heightMm: 6, styleId: 'caption' },
        ] },
      ],
    },
  }
})
const bundle = {
  nId: prefix + 'bundle', kind: 'ReportBundle', name: '示例·光刻胶·完整工单批生产记录（数据驱动）', folder, description,
  document: {
    schemaVersion: 1, mode: 'ElectronicRecord', parameters: [], paper,
    instances: [{ instanceId: '工单快照', sourceType: 'Constant', value: [order], parameters: [], dependsOn: [] }],
    assembly: {
      stitchMode: 'DataDriven', root: { mode: 'RootPath', instanceId: '工单快照', path: [] }, order: [],
      operation: { collection: { mode: 'ParentPath', path: ['operations'] }, order: [{ path: ['sequence'], direction: 'Asc' }] },
      rules: [], templates: resources.map((r, index) => ({
        id: r.key, reportNId: r.nId, revisionNId: '', priority: index + 1, scope: index < 2 ? 'WorkOrder' : 'Operation', required: r.key === 'basic',
        breakBefore: 'Auto',
        parameterMap: [{ target: 'context', source: bind(index < 2 ? 'Root' : 'Row', ['display']) }],
        instanceMap: [{ target: '模板明细', source: bind(index < 2 ? 'Root' : 'Row', [r.key]) }],
      })),
    },
  },
}
const expectedSections = [
  ...resources.slice(0, 2).filter(r => order[r.key].length).map(r => ({ key: r.key, operation: null, title: names[r.key], rows: order[r.key].length })),
  ...order.operations.flatMap(op => resources.slice(2).filter(r => op[r.key].length).map(r => ({ key: r.key, operation: op.id, title: names[r.key], rows: op[r.key].length }))),
]
await mkdir(resolve('src/frontend/src/components/studio/report/samples'), { recursive: true })
await writeFile(out, JSON.stringify({ format: 'IndustrialPlatform.PhotoresistExample', version: 1, description, resources, bundle, expectedSections }, null, 2) + '\n')
console.log(JSON.stringify({ path: out, reports: resources.length, operations: order.operations.length, included: expectedSections.length, rows: expectedSections.reduce((sum, s) => sum + s.rows, 0), optionalSections: expectedSections.length - 12 }))
