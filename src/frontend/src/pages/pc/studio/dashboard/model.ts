import type { Json, RegistryEntry } from '@/api/studio'
import { clone } from '@/components/studio/shared/model'
import photoresistDesign from './samples/semiconductor-photoresist.dashboard.json'
import batchDetailDesign from './samples/semiconductor-photoresist-batch-detail.dashboard.json'
import cmpSlurryDesign from './samples/semiconductor-cmp-slurry.dashboard.json'
import cmpSlurryDetailDesign from './samples/semiconductor-cmp-slurry-batch-detail.dashboard.json'

export interface DashboardComponent {
  id: string
  type: string
  registryVersion: string
  name: string
  x: number
  y: number
  width: number
  height: number
  rotation: number
  zIndex: number
  locked: boolean
  visible: boolean
  props: Json
  bindings: Json[]
  events: string[]
  children: string[]
}

export interface DashboardDocument extends Json {
  schemaVersion: number
  canvas: { width: number; height: number; fit: 'Contain' | 'Cover' | 'Actual'; background: string }
  parameters: Json[]
  instances: Json[]
  components: DashboardComponent[]
  navigation: Json[]
}

export interface PlaylistDocument extends Json {
  schemaVersion: number
  parameters: Json[]
  items: Json[]
  loop: boolean
  onFailure: 'Skip' | 'Pause'
  preloadCount: number
}

const uid = () => crypto.randomUUID()

export function makeComponent(
  entry: Pick<RegistryEntry, 'key' | 'version'>,
  index = 0,
): DashboardComponent {
  const type = String(entry.key)
  const defaults: Record<string, Json> = {
    Text: { text: '工业看板', align: 'left', wrap: true },
    Button: { text: '查看详情', disabled: false },
    Image: { assetId: 'dashboard.factory-overview', fit: 'Contain', alt: '生产总览' },
    StateImage: {
      cases: [
        { value: 'Running', assetId: 'dashboard.machine-running', label: '运行' },
        { value: 'Stopped', assetId: 'dashboard.machine-stopped', label: '停止' },
        { value: 'Alarm', assetId: 'dashboard.machine-alarm', label: '告警' },
      ],
      defaultAssetId: 'dashboard.machine-stopped',
    },
    ImageCarousel: {
      assetIds: [
        'dashboard.factory-overview',
        'dashboard.machine-running',
        'dashboard.machine-alarm',
      ],
      intervalSeconds: 3,
      pauseOnHover: true,
    },
    ContentSwitch: {
      items: [
        { id: 'overview', label: '总览', children: [] },
        { id: 'detail', label: '详情', children: [] },
      ],
      autoSwitchSeconds: 5,
    },
    EmbeddedPage: {
      registeredPageKey: 'studio.dashboard-summary',
      parameterMap: [],
      sandboxPolicyKey: 'studio-readonly',
    },
    Number: { unit: '', digits: 0, prefix: '', suffix: '', display: '0', animate: false },
    Progress: { min: 0, max: 100, unit: '%', orientation: 'Horizontal' },
    Clock: { pattern: 'HH:mm:ss', timeZone: 'Asia/Taipei', showDate: true },
    'Chart.Bar': {
      xField: 'name',
      series: [{ name: '值', valueField: 'value' }],
      legend: true,
      unit: '',
    },
    'Chart.Line': {
      xField: 'name',
      series: [{ name: '值', valueField: 'value' }],
      legend: true,
      unit: '',
    },
    'Chart.Pie': {
      kind: 'Pie',
      series: [{ name: '值', valueField: 'value' }],
      legend: true,
      unit: '',
    },
    'Table.Scroll': {
      rowKeyPath: ['nId'],
      columns: [
        { field: 'name', label: '名称' },
        { field: 'value', label: '值' },
      ],
      scrollMode: 'Page',
      pageSeconds: 8,
    },
    'List.Scroll': {
      rowKeyPath: ['nId'],
      columns: [{ field: 'name', label: '名称' }],
      scrollMode: 'Page',
      pageSeconds: 8,
    },
    Tank: { shape: 'vertical', min: 0, max: 100, unit: '%', label: '储罐' },
    Pump: { label: '泵组', flowUnit: 'm³/h' },
    Valve: { label: '阀门' },
    Pipe: {
      points: [
        [0, 50],
        [100, 50],
      ],
      diameterPx: 8,
    },
    Level: { min: 0, max: 100, unit: '%', orientation: 'Vertical' },
    StatusLamp: {
      cases: [
        { value: 'Running', label: '运行', color: '#21c77a' },
        { value: 'Stopped', label: '停止', color: '#718096' },
      ],
      unknownColor: '#94a3b8',
    },
    Conveyor: { label: '输送线', orientation: 'Horizontal' },
    Thermometer: { min: 0, max: 100, unit: '°C', thresholds: [] },
    Motor: { variant: 'motor', label: '电机' },
    ProcessSteps: {
      keyField: 'key',
      labelField: 'label',
      stateField: 'state',
      direction: 'horizontal',
    },
    DataSource: { instanceId: '' },
    PageParameter: { parameterName: '' },
    DataQuality: { instanceId: '', showLastSuccess: true, showSourceTime: true },
  }
  const props = clone(defaults[type] ?? {})
  if (type.startsWith('Chart.')) props.kind = type.slice('Chart.'.length)
  if (type === 'Table.Scroll') props.presentation = 'Table'
  if (type === 'List.Scroll') props.presentation = 'List'
  return {
    id: uid(),
    type: type.startsWith('Chart.')
      ? 'Chart'
      : type === 'Table.Scroll' || type === 'List.Scroll'
        ? 'Table'
        : type,
    registryVersion: String(entry.version ?? '1'),
    name: `${type} ${index + 1}`,
    x: 48 + (index % 3) * 420,
    y: 48 + Math.floor(index / 3) * 230,
    width: type.startsWith('Chart.') || type.includes('Table') ? 560 : 320,
    height: type.startsWith('Chart.') || type.includes('Table') ? 260 : 150,
    rotation: 0,
    zIndex: index,
    locked: false,
    visible: true,
    props,
    bindings: [],
    events: [],
    children: [],
  }
}

export function initialDashboardDocument(): DashboardDocument {
  return {
    schemaVersion: 1,
    canvas: { width: 1920, height: 1080, fit: 'Contain', background: '#081525' },
    parameters: [],
    instances: [],
    components: [
      {
        ...makeComponent({ key: 'Text', version: '1' }),
        name: '标题',
        x: 56,
        y: 44,
        width: 680,
        height: 70,
        props: { text: '生产总览 · 运行看板', align: 'left', wrap: true },
      },
      {
        ...makeComponent({ key: 'DataQuality', version: '1' }),
        name: '数据质量',
        x: 56,
        y: 132,
        width: 360,
        height: 110,
      },
    ],
    navigation: [],
  }
}

export function initialPlaylistDocument(): PlaylistDocument {
  return {
    schemaVersion: 1,
    parameters: [],
    items: [],
    loop: true,
    onFailure: 'Pause',
    preloadCount: 1,
  }
}

function sample(title: string, components: DashboardComponent[]): DashboardDocument {
  const base = initialDashboardDocument()
  return {
    ...base,
    canvas: { width: 1920, height: 1080, fit: 'Contain', background: '#081525' },
    components: [
      { ...base.components[0]!, name: '标题', props: { text: title, align: 'left', wrap: true } },
      ...components.map((component, index) => ({ ...component, zIndex: index + 1 })),
    ],
  }
}

export function dashboardSamples(): { key: string; label: string; document: DashboardDocument }[] {
  const make = (
    type: string,
    props: Json,
    geometry: Partial<DashboardComponent>,
  ): DashboardComponent => ({ ...makeComponent({ key: type, version: '1' }), props, ...geometry })
  return [
    {
      key: 'photoresist',
      label: '半导体光刻胶 · 完整运营样例（演示）',
      document: clone(photoresistDesign.document) as DashboardDocument,
    },
    {
      key: 'photoresist-detail',
      label: '半导体光刻胶 · 批次执行明细（演示）',
      document: clone(batchDetailDesign.document) as DashboardDocument,
    },
    {
      key: 'cmp-slurry',
      label: '半导体研磨液 · 制造运营总览（演示）',
      document: clone(cmpSlurryDesign.document) as DashboardDocument,
    },
    {
      key: 'cmp-slurry-detail',
      label: '半导体研磨液 · 批次制备与检验明细（演示）',
      document: clone(cmpSlurryDetailDesign.document) as DashboardDocument,
    },
    {
      key: 'production',
      label: '生产总览',
      document: sample('生产总览 · 产能与设备状态', [
        make(
          'Chart.Bar',
          {
            xField: 'name',
            series: [{ name: '产量', valueField: 'value' }],
            legend: true,
            unit: '件',
          },
          { x: 56, y: 150, width: 760, height: 360 },
        ),
        make(
          'Tank',
          { shape: 'vertical', min: 0, max: 100, unit: '%', label: '原料罐 A' },
          { x: 850, y: 170, width: 250, height: 300 },
        ),
        make(
          'StatusLamp',
          {
            cases: [
              { value: 'Running', label: '运行', color: '#21c77a' },
              { value: 'Stopped', label: '停止', color: '#718096' },
            ],
            unknownColor: '#94a3b8',
          },
          { x: 1160, y: 175, width: 300, height: 150 },
        ),
        make(
          'Table.Scroll',
          {
            rowKeyPath: ['nId'],
            columns: [
              { field: 'name', label: '设备' },
              { field: 'value', label: '状态' },
            ],
            scrollMode: 'Page',
            pageSeconds: 8,
          },
          { x: 56, y: 560, width: 960, height: 350 },
        ),
      ]),
    },
    {
      key: 'execution',
      label: '在制执行',
      document: sample('在制执行 · 工单节拍', [
        make(
          'Number',
          { unit: '件', digits: 0, prefix: '', suffix: '', display: '128', animate: true },
          { x: 56, y: 160, width: 280, height: 160 },
        ),
        make(
          'Progress',
          { min: 0, max: 100, unit: '%', orientation: 'Horizontal' },
          { x: 380, y: 190, width: 600, height: 120 },
        ),
        make(
          'ProcessSteps',
          { keyField: 'key', labelField: 'label', stateField: 'state', direction: 'horizontal' },
          { x: 56, y: 390, width: 1200, height: 220 },
        ),
        make(
          'Conveyor',
          { label: '装配线 A', orientation: 'horizontal' },
          { x: 56, y: 700, width: 1000, height: 170 },
        ),
      ]),
    },
    {
      key: 'process',
      label: '工艺组态',
      document: sample('工艺组态 · 关键回路', [
        make(
          'Pipe',
          {
            points: [
              [0, 50],
              [100, 50],
            ],
            diameterPx: 10,
          },
          { x: 80, y: 230, width: 900, height: 120 },
        ),
        make(
          'Pump',
          { label: 'P-101', flowUnit: 'm³/h' },
          { x: 180, y: 430, width: 260, height: 180 },
        ),
        make('Valve', { label: 'V-201' }, { x: 530, y: 430, width: 220, height: 180 }),
        make(
          'Thermometer',
          { min: 0, max: 100, unit: '°C', thresholds: [] },
          { x: 850, y: 420, width: 250, height: 220 },
        ),
        make(
          'DataQuality',
          { instanceId: '', showLastSuccess: true, showSourceTime: true },
          { x: 80, y: 760, width: 380, height: 130 },
        ),
      ]),
    },
  ]
}
