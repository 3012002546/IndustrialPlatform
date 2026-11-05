import { useLocalizationStore } from '@/stores/localizationStore'
import type { Diagnostic } from '../graph/types'
import type { TraceDocument } from './model'

const options: Record<string, string> = {
  Batch: '批次',
  Package: '包装',
  Serial: '序列号',
  WorkOrder: '工单',
  Operation: '工序',
  Custom: '自定义',
  Genealogy: '物料谱系',
  Context: '上下文关联',
  Consume: '消耗',
  Produce: '产出',
  Split: '拆分',
  Merge: '合并',
  Ship: '发运',
  Use: '使用',
  Inspect: '检验',
  String: '文本',
  Int32: '整数',
  Int64: '长整数',
  Decimal: '小数',
  Boolean: '是／否',
  Date: '日期',
  DateTime: '日期时间',
  Guid: '唯一标识',
  Array: '数组',
  Text: '文本',
  Select: '下拉选择',
  DateRange: '日期范围',
  BusinessSelector: '业务选择器',
  Caller: '调用方输入',
  TrustedContext: '可信系统上下文',
  tenant: '当前租户',
  user: '当前用户',
  authorizedFactoryIds: '已授权工厂',
  ImportAndScan: '导入并扫描',
  VerifySourceEvidence: '核验来源扫描凭据',
}
const messages: Record<string, string> = {
  'Stable ID is missing or duplicated': '标识缺失或重复，请检查对象和关系。',
  'Invalid node geometry or reference': '节点位置、尺寸或对象引用无效。',
  'Invalid edge geometry or reference': '连线布局或关系引用无效。',
  'Select a published start Dataset': '请选择已发布的起点数据集。',
  'Select valid start object types': '请选择有效的起点对象类型。',
  'Object name is required': '请输入对象名称。',
  'Select a published detail Dataset': '请选择已发布的明细数据集。',
  'Invalid graph layout direction': '请选择有效的图谱布局方向。',
  'Detail section ID is missing or duplicated': '明细分组标识缺失或重复。',
  'Detail section name is required': '请输入明细分组名称。',
  'Detail section requires a node context parameter':
    '明细分组至少需要一个当前节点或主明细字段的参数映射。',
  TRACE_SECTION_CONTEXT_REQUIRED: '当前节点缺少查询此分组所需的关联字段，请检查参数映射。',
  'Relation endpoints must exist': '请选择有效的来源对象和目标对象。',
  'Both traversal directions require a Dataset': '请分别配置正向和反向取数的数据集。',
  'Traversal output does not match the endpoint; repair the mapping':
    '取数结果的目标对象与连线端点不一致，请修正映射。',
  'Relation port is disconnected or unknown': '关系端口未连接或无效，请重新连接。',
  'Choose both endpoints': '请选择连线的两个端点。',
  'Connection must join an output to an input': '请从对象的输出端口连接到另一对象的输入端口。',
  GRAPH_STRUCTURE_INVALID: '模型结构无效，请检查对象、关系和布局。',
  GRAPH_VERSION_UNSUPPORTED: '当前模型版本不受支持。',
  GRAPH_READ_ONLY: '当前模型为只读，无法修改。',
  GRAPH_CONTEXT_STALE: '模型已变化，请重新选择后操作。',
  TRACE_MAPPING_INVALID: '追溯映射配置无效，请检查校验提示。',
}
const fields: Record<string, string> = {
  start: '起点配置',
  objects: '对象类型',
  relations: '关系',
  layout: '画布布局',
  nodes: '节点',
  edges: '连线',
  id: '标识',
  name: '名称',
  detail: '明细',
  sections: '明细分组',
  presentation: '展示配置',
  layoutDirection: '布局方向',
  datasetRevisionNId: '已发布数据集',
  objectTypeIds: '起点对象类型',
  forward: '正向取数',
  reverse: '反向取数',
  result: '输出映射',
  targetObjectTypeId: '目标对象类型',
  collectionPath: '集合路径',
  parameterMap: '参数映射',
  inputMap: '输入映射',
  identity: '身份映射',
  identityMap: '完整身份映射',
  targetIdentity: '目标身份',
  sourceSystem: '来源系统',
  factory: '工厂',
  businessKey: '业务标识',
  rowKey: '行标识',
  objectType: '对象类型映射',
  summary: '摘要',
  summaryMap: '摘要映射',
  fields: '显示字段',
  links: '文件与外链',
  value: '绑定值',
  source: '取值来源',
  path: '字段路径',
  nullPolicy: '空值处理',
  defaultValue: '默认值',
  grain: '粒度',
  kind: '类别',
  semantic: '关系语义',
  label: '显示标签',
  evidence: '事实证据',
  recordKey: '记录标识',
  occurredOn: '发生时间',
  quantity: '数量',
  unit: '单位',
  fromTypeId: '来源对象',
  toTypeId: '目标对象',
  sourcePort: '输出端口',
  targetPort: '输入端口',
  objectTypeId: '对象类型',
  relationId: '关系',
  vertices: '折点',
  parameters: '参数定义',
  type: '类型',
  itemType: '数组元素类型',
  contextKey: '上下文',
  searchFields: '搜索条件',
  control: '输入控件',
  columns: '结果列',
  scope: '适用范围',
  factoryIds: '工厂范围',
  industryTags: '行业标签',
  limits: '查询预算',
  maxDepth: '最大深度',
  maxNodes: '最大节点数',
  maxEdges: '最大边数',
  timeoutSeconds: '超时秒数',
  connection: '连线配置',
}
export function traceItemName(
  item: { name: string },
  index: number,
  kind = 'object',
  english = false,
) {
  return (
    item.name.trim() ||
    `${english ? (kind === 'object' ? 'Object type' : 'Relation') : kind === 'object' ? '对象类型' : '关系'} ${index + 1}`
  )
}
export function traceDiagnosticText(d: Diagnostic, doc: TraceDocument, english = false) {
  let path = d.path ?? ''
  const match = /^(objects|relations)(?:\[(\d+)\]|\.([^.]+))/.exec(path)
  let name = ''
  if (match) {
    const items = match[1] === 'objects' ? doc.objects : doc.relations
    const index =
      match[2] !== undefined ? Number(match[2]) : items.findIndex((item) => item.id === match[3])
    if (items[index])
      name = traceItemName(
        items[index],
        index,
        match[1] === 'objects' ? 'object' : 'relation',
        english,
      )
    path = path.slice(match[0].length)
  }
  const parts = path
    .split('.')
    .filter(Boolean)
    .map((part) => {
      const [key = '', index] = part.split(/[\[\]]/)
      return (english ? key : (fields[key] ?? '配置项')) + (index ? ` ${Number(index) + 1}` : '')
    })
  return {
    location: [name, ...parts].filter(Boolean).join(' / '),
    message: english
      ? d.message
      : (messages[d.message] ??
        d.message.replaceAll('Dataset', '数据集').replaceAll('Row绑定', '数据行绑定')),
  }
}
export function useTracePresentation() {
  const locale = useLocalizationStore()
  const english = () => locale.locale === 'en-US'
  return {
    option: (value: string) => (english() ? value : (options[value] ?? value)),
    itemName: (item: { name: string }, index: number, kind = 'object') =>
      traceItemName(item, index, kind, english()),
    diagnostic: (d: Diagnostic, doc: TraceDocument) => traceDiagnosticText(d, doc, english()),
    message: (value: string) => (english() ? value : (messages[value] ?? value)),
  }
}
