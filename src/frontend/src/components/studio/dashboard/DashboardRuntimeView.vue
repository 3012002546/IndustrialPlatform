<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import type { DashboardRuntime, Json } from '@/api/studio'
import { useLocalizationStore } from '@/stores/localizationStore'
import type { DashboardComponent } from '@/pages/pc/studio/dashboard/model'
import { useDashboardText } from './dashboardCopy'
import { dashboardFontFamilies } from './appearance'
import { constantInstances } from './dataSources'
import DashboardMediaVisual from './DashboardMediaVisual.vue'
import DashboardEmbeddedSummary from './DashboardEmbeddedSummary.vue'
import DashboardTableViewport from './DashboardTableViewport.vue'
import type {
  DashboardMediaKind,
  DashboardMediaVisualProps,
  StateImageCase,
  ContentSwitchItem,
} from './mediaAssets'

const props = defineProps<{
  runtime: DashboardRuntime | Json
  parameters?: Json
  selectedComponentId?: string
  componentIds?: readonly string[]
  ancestorIds?: readonly string[]
  editing?: boolean
  editorScale?: number
  preloadNavigation?: boolean
}>()
const emit = defineEmits<{
  navigate: [component: DashboardComponent, row?: Json]
  preload: [candidates: { component: DashboardComponent; row?: Json }[]]
  select: [id: string]
  editPointerDown: [event: PointerEvent, component: DashboardComponent]
}>()
const localization = useLocalizationStore()
const t = useDashboardText()
const host = ref<HTMLElement>()
const viewportWidth = ref(0)
const viewportHeight = ref(0)
let observer: ResizeObserver | undefined
const now = ref(Date.now())
const startedOn = Date.now()
let animationTimer: ReturnType<typeof setInterval> | undefined
const documentValue = computed(() => props.runtime.document as Json)
const canvas = computed(
  () =>
    (documentValue.value.canvas as Json) ?? {
      width: 1920,
      height: 1080,
      fit: 'Contain',
      background: '#081525',
    },
)
const components = computed(() => {
  const candidates = ((documentValue.value.components as DashboardComponent[]) ?? []).filter(
    (component) =>
      component.visible !== false &&
      (!props.componentIds || props.componentIds.includes(component.id)) &&
      !props.ancestorIds?.includes(component.id),
  )
  const children = new Set(
    candidates.flatMap((component) =>
      component.type === 'ContentSwitch'
        ? ((component.props.items as ContentSwitchItem[]) ?? []).flatMap(
            (item) => item.children ?? [],
          )
        : component.type === 'Container'
          ? (component.children ?? [])
          : [],
    ),
  )
  return candidates
    .filter((component) => !children.has(component.id))
    .sort((a, b) => a.zIndex - b.zIndex)
})
function childRuntime(component: DashboardComponent): Json {
  return {
    ...props.runtime,
    document: {
      ...documentValue.value,
      canvas: {
        width: Math.max(1, component.width - 24),
        height: Math.max(1, component.height - 60),
        fit: 'Contain',
        background: 'transparent',
      },
    },
  }
}
const scale = computed(() => {
  if (props.editing && props.editorScale !== undefined) return props.editorScale
  const width = Number(canvas.value.width ?? 1920)
  const height = Number(canvas.value.height ?? 1080)
  if (!viewportWidth.value || !viewportHeight.value) return 1
  if (canvas.value.fit === 'Actual') return 1
  const x = viewportWidth.value / width
  const y = viewportHeight.value / height
  return canvas.value.fit === 'Cover' ? Math.max(x, y) : Math.min(x, y)
})
const canvasStyle = computed(() => ({
  width: `${Number(canvas.value.width ?? 1920)}px`,
  height: `${Number(canvas.value.height ?? 1080)}px`,
  background: String(canvas.value.background ?? '#081525'),
  transform: `scale(${scale.value})`,
}))
const frameStyle = computed(() => ({
  width: `${Number(canvas.value.width ?? 1920) * scale.value}px`,
  height: `${Number(canvas.value.height ?? 1080) * scale.value}px`,
}))
const instances = computed(() => ({
  ...((props.runtime.instances as Record<string, Json>) ?? {}),
  ...constantInstances(documentValue.value),
}))
const parameters = computed(() => props.parameters ?? {})
function instanceData(execution: Json | undefined): unknown {
  return execution && Object.prototype.hasOwnProperty.call(execution, 'data')
    ? execution.data
    : execution
}
function valueAt(root: unknown, path: unknown): unknown {
  const segments = Array.isArray(path) ? path : [path]
  let current = root
  for (const segment of segments) {
    if (segment == null || current == null || typeof current !== 'object') return undefined
    current = (current as Record<string, unknown>)[String(segment)]
  }
  return current
}
function sourceValue(source: Json | undefined): unknown {
  if (!source) return undefined
  if (source.from === 'Constant') return source.value
  const root =
    source.from === 'Instance'
      ? (() => {
          const execution = instances.value[String(source.instanceId ?? '')]
          return instanceData(execution)
        })()
      : source.from === 'PageParam'
        ? parameters.value
        : source.from === 'Context'
          ? { parameters: parameters.value }
          : undefined
  const current = valueAt(root, source.path)
  const resolved = current ?? (source.nullPolicy === 'Default' ? source.defaultValue : current)
  if (resolved == null || source.transform?.kind !== 'EnumMap') return resolved
  const args = source.transform.args as Json | undefined
  const entries = Array.isArray(args?.entries) ? (args.entries as Json[]) : []
  const entry = entries.find((item) => JSON.stringify(item.from) === JSON.stringify(resolved))
  if (entry) return entry.to
  if (args?.onMissing === 'Original') return resolved
  return args?.onMissing === 'Default' ? args.defaultValue : undefined
}
function value(component: DashboardComponent, property: string, fallback: unknown): unknown {
  const binding = component.bindings?.find((item) => item.property === property)
  return sourceValue(binding?.source as Json | undefined) ?? component.props?.[property] ?? fallback
}
function envelope(instanceId: string): Json {
  return instances.value[instanceId] ?? {}
}
function componentReadFailed(component: DashboardComponent): boolean {
  const ids = [
    component.props.instanceId,
    ...(component.bindings ?? [])
      .filter((binding) => binding.source?.from === 'Instance')
      .map((binding) => binding.source.instanceId),
  ]
  return ids.some((id) => id && instances.value[String(id)]?.error)
}
function rows(component: DashboardComponent): Json[] {
  const bound = value(component, 'rows', undefined)
  if (Array.isArray(bound)) return bound as Json[]
  const rowsBinding = component.bindings?.find((item) => item.property === 'rows')
  if (rowsBinding?.source?.from === 'Instance') {
    const execution = envelope(String(rowsBinding.source.instanceId ?? ''))
    const data = instanceData(execution)
    const collection = valueAt(data, rowsBinding.source.path ?? [])
    if (Array.isArray(collection)) return collection as Json[]
  }
  const instanceId = String(component.props?.instanceId ?? '')
  const execution = envelope(instanceId)
  const data = instanceData(execution)
  if (Array.isArray(data)) return data as Json[]
  if (data && typeof data === 'object') {
    const collectionPath = (component.props?.collectionPath as unknown[]) ?? []
    const collection = valueAt(data, collectionPath)
    return Array.isArray(collection) ? (collection as Json[]) : []
  }
  return []
}
function freshness(instanceId: string) {
  const execution = envelope(instanceId)
  const transport = String((props.runtime as Json).transport ?? 'Connected')
  if (transport !== 'Connected') return { label: t.value('transportDisconnected'), tone: 'bad' }
  const quality = execution.quality as Json | undefined
  if (execution.error || quality?.status === 'Error')
    return { label: t.value('sourceReadFailed'), tone: 'bad' }
  if (execution.constant) return { label: t.value('constant'), tone: 'good' }
  if (quality?.status === 'Stale') return { label: t.value('sourceDataStale'), tone: 'stale' }
  if (quality?.status === 'Unknown') return { label: t.value('sourceTimeUnknown'), tone: 'unknown' }
  const observed = execution.quality?.sourceObservedOn ?? execution.observedOn
  if (!observed) return { label: t.value('sourceTimeUnknown'), tone: 'unknown' }
  const age = now.value - Date.parse(String(observed))
  const instance = ((documentValue.value.instances as Json[]) ?? []).find(
    (item) => String(item.instanceId) === instanceId,
  )
  const staleAfterSeconds = Number(instance?.staleAfterSeconds ?? 300)
  return age > staleAfterSeconds * 1000
    ? { label: t.value('sourceDataStale'), tone: 'stale' }
    : { label: t.value('sourceDataNormal'), tone: 'good' }
}
function display(component: DashboardComponent): string {
  const value = valueFor(component)
  return value == null || value === ''
    ? '—'
    : typeof value === 'object'
      ? JSON.stringify(value)
      : String(value)
}
function valueFor(component: DashboardComponent): unknown {
  if (component.type === 'PageParameter')
    return parameters.value[String(component.props.parameterName ?? '')]
  const property =
    component.type === 'PageParameter'
      ? String(component.props?.parameterName ?? '')
      : component.type === 'DataSource' || component.type === 'DataQuality'
        ? undefined
        : component.type === 'Number' ||
            component.type === 'Progress' ||
            component.type === 'Level' ||
            component.type === 'Thermometer' ||
            variant(component) === 'Chart.WaterLevel' ||
            variant(component) === 'Chart.Gauge'
          ? 'value'
          : component.type === 'Tank'
            ? 'level'
            : component.type === 'StatusLamp' || component.type === 'Valve'
              ? 'state'
              : component.type === 'Pump' || component.type === 'Motor'
                ? 'running'
                : component.type === 'Conveyor'
                  ? 'speed'
                  : component.type === 'Clock'
                    ? 'shiftLabel'
                    : 'text'
  return property ? value(component, property, undefined) : undefined
}
function variant(component: DashboardComponent): string {
  if (component.type === 'Chart') return `Chart.${String(component.props?.kind ?? 'Bar')}`
  if (component.type === 'Table')
    return String(component.props?.presentation ?? 'Table') === 'List'
      ? 'List.Scroll'
      : 'Table.Scroll'
  return component.type
}
function componentStyle(component: DashboardComponent) {
  const appearance = (component.props?.appearance as Json | undefined) ?? {}
  return {
    left: `${component.x}px`,
    top: `${component.y}px`,
    width: `${component.width}px`,
    height: `${component.height}px`,
    zIndex: component.zIndex,
    transform: `rotate(${component.rotation ?? 0}deg)`,
    color: appearance.color ? String(appearance.color) : undefined,
    background: appearance.background ? String(appearance.background) : undefined,
    borderColor: appearance.borderColor ? String(appearance.borderColor) : undefined,
    borderWidth:
      appearance.borderWidth !== undefined ? `${Number(appearance.borderWidth)}px` : undefined,
    opacity: appearance.opacity !== undefined ? Number(appearance.opacity) : undefined,
    padding: appearance.paddingPx !== undefined ? `${Number(appearance.paddingPx)}px` : undefined,
    fontSize:
      appearance.fontSizePx !== undefined ? `${Number(appearance.fontSizePx)}px` : undefined,
    fontFamily: dashboardFontFamilies[String(appearance.fontKey ?? '')],
  }
}
function textStyle(component: DashboardComponent): Record<string, string> {
  return {
    textAlign: String(value(component, 'align', 'left')),
    fontSize: `${Number(component.props?.appearance?.fontSizePx ?? 24)}px`,
  }
}
function colorForState(component: DashboardComponent, state: unknown) {
  const cases = Array.isArray(component.props?.cases) ? (component.props.cases as Json[]) : []
  return String(
    cases.find((item) => JSON.stringify(item.value) === JSON.stringify(state))?.color ??
      component.props?.unknownColor ??
      '#94a3b8',
  )
}
function onComponentClick(component: DashboardComponent, row?: Json) {
  if (props.editing) {
    emit('select', component.id)
    return
  }
  if (component.events?.length) emit('navigate', component, row)
}
// Use the same bound, visible rows as the renderer; never enumerate an entire dataset.
const preloadCandidates = computed(() => {
  if (!props.preloadNavigation || props.editing) return []
  const rules = (documentValue.value.navigation as Json[]) ?? []
  return components.value
    .flatMap((component) => {
      const rule = rules.find((item) => item.componentId === component.id)
      if (!rule) return []
      return rule.event === 'RowClick'
        ? visibleRows(component)
            .slice(0, 8)
            .map((row) => ({ component, row }))
        : [{ component }]
    })
    .slice(0, 8)
})
watch(
  preloadCandidates,
  (candidates) => {
    if (candidates.length) emit('preload', candidates)
  },
  { immediate: true },
)

const mediaKinds: readonly DashboardMediaKind[] = [
  'Image',
  'StateImage',
  'ImageCarousel',
  'ContentSwitch',
  'EmbeddedPage',
]
function isMediaComponent(component: DashboardComponent): boolean {
  return mediaKinds.includes(component.type as DashboardMediaKind)
}
function mediaString(component: DashboardComponent, property: string, fallback = ''): string {
  const resolved = value(component, property, component.props?.[property] ?? fallback)
  return resolved == null ? fallback : String(resolved)
}
function mediaNumber(component: DashboardComponent, property: string, fallback: number): number {
  const resolved = Number(value(component, property, component.props?.[property] ?? fallback))
  return Number.isFinite(resolved) ? resolved : fallback
}
function mediaBoolean(component: DashboardComponent, property: string, fallback: boolean): boolean {
  const resolved = value(component, property, component.props?.[property] ?? fallback)
  return typeof resolved === 'boolean' ? resolved : Boolean(resolved)
}
function mediaArray(component: DashboardComponent, property: string): unknown[] {
  const resolved = value(component, property, component.props?.[property])
  return Array.isArray(resolved) ? resolved : []
}
function mediaProps(component: DashboardComponent): DashboardMediaVisualProps {
  const cases = mediaArray(component, 'cases') as StateImageCase[]
  const items = mediaArray(component, 'items') as ContentSwitchItem[]
  return {
    kind: component.type as DashboardMediaKind,
    assetId: mediaString(component, 'assetId', 'dashboard.factory-overview'),
    fit: mediaString(component, 'fit', 'Contain') as 'Contain' | 'Cover',
    alt: mediaString(component, 'alt', component.name),
    state: value(component, 'state', ''),
    cases,
    defaultAssetId: mediaString(component, 'defaultAssetId', 'dashboard.machine-stopped'),
    assetIds: mediaArray(component, 'assetIds').map(String),
    intervalSeconds: mediaNumber(component, 'intervalSeconds', 3),
    pauseOnHover: mediaBoolean(component, 'pauseOnHover', true),
    items,
    activeItemId: mediaString(component, 'activeItemId'),
    autoSwitchSeconds: mediaNumber(component, 'autoSwitchSeconds', 5),
    registeredPageKey: mediaString(component, 'registeredPageKey'),
    parameterMap: Object.fromEntries(
      (mediaArray(component, 'parameterMap') as Json[]).map((mapping) => [
        String(mapping.target),
        sourceValue(mapping.source as Json),
      ]),
    ),
    embeddedPages: [
      {
        key: 'studio.dashboard-summary',
        title: { 'zh-CN': '参数概览', 'en-US': 'Parameter summary' },
        sandboxPolicyKey: 'studio-readonly',
        component: DashboardEmbeddedSummary,
      },
    ],
    sandboxPolicyKey: mediaString(component, 'sandboxPolicyKey', 'studio-readonly') as
      'studio-readonly' | 'studio-isolated',
    locale: localization.locale === 'en-US' ? 'en-US' : 'zh-CN',
    theme: 'dark',
  }
}

function columnField(column: Json): string {
  return String(column.field ?? column.key ?? '')
}
function columnLabel(column: Json): string {
  const label = column.label
  return label && typeof label === 'object'
    ? String(label[localization.locale] ?? label['zh-CN'] ?? columnField(column))
    : String(label ?? columnField(column))
}
function columnStyle(column: Json) {
  const width = Number(column.widthPercent)
  return {
    width: Number.isFinite(width) && width > 0 && width <= 100 ? `${width}%` : undefined,
    textAlign: ['left', 'center', 'right'].includes(String(column.align))
      ? (String(column.align) as 'left' | 'center' | 'right')
      : undefined,
  }
}
function tableCellStyle(component: DashboardComponent) {
  const height = Number(component.props.rowHeight)
  return Number.isFinite(height) && height >= 16 && height <= 200
    ? {
        height: `${height - 1}px`,
        lineHeight: `${height - 1}px`,
        overflow: 'hidden',
        whiteSpace: 'nowrap' as const,
        textOverflow: 'ellipsis',
        display: 'block',
      }
    : {}
}
function seriesColor(series: Json, index: number): string {
  return String(series.color || ['#28b7bd', '#60a5fa', '#fbbf24', '#f97316'][index % 4])
}
function chartRows(component: DashboardComponent): Json[] {
  const values = rows(component)
  if (variant(component) === 'Chart.Ranking')
    return [...values]
      .sort(
        (a, b) =>
          (chartNumber(a, component.props.valueField) -
            chartNumber(b, component.props.valueField)) *
          (component.props.order === 'Asc' ? 1 : -1),
      )
      .slice(0, Number(component.props.maxItems ?? 8))
  return values.slice(0, 100)
}
function chartSeries(component: DashboardComponent): Json[] {
  const series = component.props?.series
  return Array.isArray(series) && series.length > 0 ? (series as Json[]) : [{ valueField: 'value' }]
}
function chartNumber(row: Json, field: unknown): number {
  const value = valueAt(row, String(field ?? 'value').split('.'))
  const number = Number(value)
  return Number.isFinite(number) ? number : 0
}
function chartMax(component: DashboardComponent): number {
  if (variant(component) === 'Chart.StackedBar')
    return Math.max(
      1,
      ...chartRows(component).map((row) =>
        chartSeries(component).reduce(
          (sum, series) => sum + Math.max(0, chartNumber(row, series.valueField)),
          0,
        ),
      ),
    )
  const values = chartRows(component).flatMap((row) =>
    chartSeries(component).map((series) => chartNumber(row, series.valueField)),
  )
  return Math.max(1, ...values.map((value) => Math.abs(value)))
}
function barHeight(component: DashboardComponent, row: Json, series: Json): number {
  return (Math.max(0, chartNumber(row, series.valueField)) / chartMax(component)) * 160
}
function barY(component: DashboardComponent, row: Json, series: Json, index: number): number {
  const prior =
    variant(component) === 'Chart.StackedBar'
      ? chartSeries(component)
          .slice(0, index)
          .reduce((sum, value) => sum + barHeight(component, row, value), 0)
      : 0
  return 200 - prior - barHeight(component, row, series)
}
function scatterCoordinate(component: DashboardComponent, row: Json, axis: 'x' | 'y'): number {
  const field = component.props[axis === 'x' ? 'xField' : 'yField'] ?? (axis === 'x' ? 'x' : 'y')
  const values = chartRows(component).map((item) => chartNumber(item, field))
  const low = Math.min(0, ...values),
    high = Math.max(1, ...values)
  const ratio = (chartNumber(row, field) - low) / (high - low)
  return axis === 'x' ? 44 + ratio * 520 : 200 - ratio * 160
}
function visibleRows(component: DashboardComponent): Json[] {
  const all = rows(component)
  const rowHeight = Number(component.props.rowHeight)
  const size =
    Number.isFinite(rowHeight) && rowHeight >= 16
      ? Math.max(
          1,
          Math.floor(
            (component.height -
              Number(component.props.appearance?.paddingPx ?? 12) * 2 -
              (variant(component).includes('Table') ? rowHeight : 0)) /
              rowHeight,
          ),
        )
      : 8
  if (['None', 'Continuous'].includes(component.props.scrollMode)) return all
  const period = Math.max(3, Number(component.props.pageSeconds ?? 8)) * 1000
  const page =
    Math.floor((now.value - startedOn) / period) % Math.max(1, Math.ceil(all.length / size))
  return all.slice(page * size, (page + 1) * size)
}
function clockText(component: DashboardComponent): string {
  const options: Intl.DateTimeFormatOptions = {
    timeZone: String(component.props.timeZone ?? 'UTC'),
    hourCycle: 'h23',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  }
  try {
    const parts = Object.fromEntries(
      new Intl.DateTimeFormat('en-GB', options)
        .formatToParts(now.value)
        .map((part) => [part.type, part.value]),
    )
    const fields: Record<string, string> = {
      yyyy: parts.year!,
      MM: parts.month!,
      dd: parts.day!,
      HH: parts.hour!,
      mm: parts.minute!,
      ss: parts.second!,
    }
    const pattern = String(component.props.pattern ?? 'HH:mm:ss')
    const formatted = pattern.replace(/yyyy|MM|dd|HH|mm|ss/g, (key) => fields[key] ?? key)
    return component.props.showDate === false
      ? formatted
      : `${parts.year}-${parts.month}-${parts.day} ${formatted}`
  } catch {
    return String(component.props.timeZone ?? 'UTC') + ' ?'
  }
}
function chartPoints(component: DashboardComponent, series: Json): string {
  const values = chartRows(component)
  const maximum = chartMax(component)
  return values
    .map((row, index) => {
      const x = 44 + index * (520 / Math.max(1, values.length - 1))
      const y = 200 - (Math.max(0, chartNumber(row, series.valueField)) / maximum) * 160
      return `${x},${y}`
    })
    .join(' ')
}
function chartAreaPath(component: DashboardComponent, series: Json): string {
  const points = chartPoints(component, series)
  const last = Math.max(0, chartRows(component).length - 1)
  const lastX = 44 + last * (520 / Math.max(1, last))
  return `M ${points} L ${lastX} 200 L 44 200 Z`
}
function pieStyle(component: DashboardComponent): Record<string, string> {
  const series = chartSeries(component)[0] ?? { valueField: 'value' }
  const values = chartRows(component).map((row) => Math.max(0, chartNumber(row, series.valueField)))
  const total = values.reduce((sum, value) => sum + value, 0) || 1
  const colors = ['#2dd4bf', '#60a5fa', '#fbbf24', '#f97316', '#c084fc', '#f43f5e']
  let offset = 0
  const stops = values.map((value, index) => {
    const start = offset
    offset += (value / total) * 360
    return `${colors[index % colors.length]} ${start}deg ${offset}deg`
  })
  return { background: `conic-gradient(${stops.join(', ')})` }
}
function heatColor(value: number, maximum: number, component: DashboardComponent): string {
  const stops = (
    Array.isArray(component.props.colorStops) ? (component.props.colorStops as Json[]) : []
  )
    .filter((stop) => typeof stop.value === 'number' && Number.isFinite(stop.value))
    .slice()
    .sort((a, b) => a.value - b.value)
  const stop = stops.filter((stop) => value >= stop.value).at(-1) ?? stops[0]
  if (stop?.color) return String(stop.color)
  const intensity = Math.max(0, Math.min(1, value / Math.max(1, maximum)))
  return `rgba(45, 212, 191, ${0.15 + intensity * 0.8})`
}
function thresholdColor(component: DashboardComponent): string | undefined {
  const current = Number(value(component, 'value', 0))
  const thresholds = Array.isArray(component.props.thresholds)
    ? (component.props.thresholds as Json[])
    : []
  return thresholds.find((range) => current >= range.min && current <= range.max)?.color
}
function meterPercent(component: DashboardComponent): number {
  const minimum = Number(component.props?.min ?? 0)
  const maximum = Number(component.props?.max ?? 100)
  const current = Number(value(component, component.type === 'Tank' ? 'level' : 'value', 0))
  if (!Number.isFinite(current) || maximum <= minimum) return 0
  return Math.max(0, Math.min(100, ((current - minimum) / (maximum - minimum)) * 100))
}
function tankLevelPercent(component: DashboardComponent): number {
  return meterPercent(component)
}
function tankReading(component: DashboardComponent, property: string): string {
  const current = value(component, property, undefined)
  return current == null || current === '' ? '—' : String(current)
}
function industrialReadings(component: DashboardComponent): string {
  const fields: Record<string, string[]> = {
    Pump: ['running', 'flow', 'state'],
    Valve: ['open', 'opening', 'state'],
    Pipe: ['flow', 'direction', 'state'],
    Conveyor: ['speed', 'direction', 'state'],
    Motor: ['running', 'speed', 'state'],
  }
  return (fields[component.type] ?? [])
    .map((property) => tankReading(component, property))
    .join(' · ')
}
function industrialActive(component: DashboardComponent): boolean {
  return (
    value(component, 'running', false) === true ||
    value(component, 'open', false) === true ||
    value(component, 'state', '') === 'Running' ||
    Number(value(component, 'flow', 0)) !== 0 ||
    Number(value(component, 'speed', 0)) !== 0
  )
}
function formatTimestamp(value: unknown): string {
  if (!value) return '—'
  const date = new Date(String(value))
  return Number.isNaN(date.getTime()) ? String(value) : date.toLocaleString()
}
function qualityTime(
  component: DashboardComponent,
  key: 'observedOn' | 'sourceObservedOn',
): string {
  const instance = envelope(String(component.props?.instanceId ?? ''))
  const quality = instance.quality as Json | undefined
  const timestamp = key === 'sourceObservedOn' ? quality?.sourceObservedOn : instance.observedOn
  return formatTimestamp(timestamp)
}
function qualityReason(component: DashboardComponent): string {
  const instance = envelope(String(component.props?.instanceId ?? ''))
  const quality = instance.quality as Json | undefined
  const reason = String(quality?.reasonCode ?? '')
  return reason === 'SOURCE_TIME_NOT_PROVIDED' ? t.value('sourceTimeUnknown') : reason
}
function updateSize() {
  viewportWidth.value = host.value?.clientWidth ?? 0
  viewportHeight.value = host.value?.clientHeight ?? 0
}
onMounted(() => {
  updateSize()
  if (typeof ResizeObserver !== 'undefined') {
    observer = new ResizeObserver(updateSize)
    if (host.value) observer.observe(host.value)
  }
  animationTimer = setInterval(() => {
    now.value = Date.now()
  }, 1000)
})
onBeforeUnmount(() => {
  observer?.disconnect()
  if (animationTimer) clearInterval(animationTimer)
})
</script>

<template>
  <div
    ref="host"
    class="dashboard-runtime-host"
    :class="{ 'is-embedded': componentIds, 'is-editing': editing }"
    role="application"
    aria-label="Dashboard runtime"
  >
    <div
      v-if="String((runtime as Json).transport ?? 'Connected') !== 'Connected'"
      class="dashboard-runtime-transport"
      role="status"
    >
      {{ t('transportDisconnectedLastValue') }}
    </div>
    <div class="dashboard-runtime-frame" :style="frameStyle">
      <div class="dashboard-runtime-canvas" :style="canvasStyle">
        <article
          v-for="component in components"
          :key="component.id"
          :data-component-id="component.id"
          class="dashboard-runtime-component"
          :class="[
            'dashboard-component-' + component.type.replaceAll('.', '-'),
            { 'is-selected': component.id === selectedComponentId },
          ]"
          :style="componentStyle(component)"
          @click.stop="onComponentClick(component)"
        >
          <span
            v-if="componentReadFailed(component)"
            class="dashboard-source-error"
            role="status"
            >{{ t('sourceReadFailed') }}</span
          >
          <DashboardMediaVisual
            v-if="component.type === 'ContentSwitch'"
            v-bind="mediaProps(component)"
          >
            <template #default="{ item }">
              <DashboardRuntimeView
                :runtime="childRuntime(component)"
                :parameters="parameters"
                :component-ids="item.children ?? []"
                :ancestor-ids="[...(ancestorIds ?? []), component.id]"
                :selected-component-id="selectedComponentId ?? ''"
                :editing="editing ?? false"
                :preload-navigation="preloadNavigation ?? false"
                @preload="emit('preload', $event)"
                @select="emit('select', $event)"
                @edit-pointer-down="(event, child) => emit('editPointerDown', event, child)"
                @navigate="(child, row) => emit('navigate', child, row)"
              />
            </template>
          </DashboardMediaVisual>
          <DashboardRuntimeView
            v-else-if="component.type === 'Container'"
            :runtime="childRuntime(component)"
            :parameters="parameters"
            :component-ids="component.children ?? []"
            :ancestor-ids="[...(ancestorIds ?? []), component.id]"
            :selected-component-id="selectedComponentId ?? ''"
            :editing="editing ?? false"
            :preload-navigation="preloadNavigation ?? false"
            @preload="emit('preload', $event)"
            @select="emit('select', $event)"
            @edit-pointer-down="(event, child) => emit('editPointerDown', event, child)"
            @navigate="(child, row) => emit('navigate', child, row)"
          />
          <DashboardMediaVisual
            v-else-if="isMediaComponent(component)"
            v-bind="mediaProps(component)"
          />
          <template v-else-if="component.type === 'Text' || component.type === 'ScrollingText'">
            <div
              class="dashboard-text"
              :class="{
                'is-scrolling': component.type === 'ScrollingText',
                'pause-on-hover': component.props.pauseOnHover !== false,
              }"
              :style="{
                ...textStyle(component),
                '--scroll-duration': `${Math.max(3, component.width / Math.max(10, Number(component.props.speedPxPerSecond ?? 30)))}s`,
              }"
            >
              {{ display(component) }}
            </div>
          </template>
          <template v-else-if="component.type === 'Button'"
            ><button
              class="dashboard-button"
              :disabled="Boolean(value(component, 'disabled', false))"
            >
              {{ String(value(component, 'text', '查看详情')) }}
            </button></template
          >
          <template v-else-if="component.type === 'Number' || component.type === 'PageParameter'"
            ><div
              class="dashboard-number"
              :style="
                component.props?.appearance?.fontSizePx
                  ? { fontSize: `${component.props.appearance.fontSizePx}px` }
                  : undefined
              "
            >
              <span
                >{{ String(component.props?.prefix ?? '')
                }}{{
                  component.type === 'Number' &&
                  Number.isFinite(Number(valueFor(component))) &&
                  valueFor(component) != null
                    ? Number(valueFor(component)).toFixed(Number(component.props.digits ?? 0))
                    : display(component)
                }}{{ String(component.props?.suffix ?? '') }}</span
              ><small>{{ String(component.props?.unit ?? '') }}</small>
            </div></template
          >
          <template v-else-if="component.type === 'Clock'"
            ><div
              class="dashboard-number"
              :style="{ fontSize: `${Number(component.props?.appearance?.fontSizePx ?? 42)}px` }"
            >
              <span>{{ clockText(component) }}</span
              ><small>{{ String(value(component, 'shiftLabel', '')) }}</small>
            </div></template
          >
          <template v-else-if="variant(component) === 'Chart.Gauge'">
            <svg
              class="dashboard-gauge"
              viewBox="0 0 220 140"
              role="img"
              :aria-label="component.name"
            >
              <path d="M25 110a85 85 0 0 1 170 0" fill="none" stroke="#24445d" stroke-width="16" />
              <path
                d="M25 110a85 85 0 0 1 170 0"
                fill="none"
                stroke="#2dd4bf"
                stroke-width="16"
                pathLength="100"
                :stroke-dasharray="`${meterPercent(component)} 100`"
              />
              <line
                x1="110"
                y1="110"
                x2="40"
                y2="110"
                stroke="#e7fbff"
                stroke-width="4"
                :transform="`rotate(${meterPercent(component) * 1.8} 110 110)`"
              />
              <text x="110" y="136" text-anchor="middle" fill="currentColor">
                {{ display(component) }} {{ component.props.unit }}
              </text>
            </svg>
          </template>
          <template
            v-else-if="
              component.type === 'Progress' ||
              component.type === 'Level' ||
              component.type === 'Thermometer' ||
              variant(component) === 'Chart.WaterLevel'
            "
          >
            <div
              class="dashboard-meter"
              :class="{ 'is-horizontal': component.props?.orientation === 'Horizontal' }"
            >
              <div class="dashboard-meter-track">
                <div
                  class="dashboard-meter-fill"
                  :style="{
                    height: `${meterPercent(component)}%`,
                    '--dashboard-meter-value': `${meterPercent(component)}%`,
                    background: thresholdColor(component),
                  }"
                />
              </div>
              <strong>{{ display(component) }} {{ String(component.props?.unit ?? '') }}</strong
              ><small>{{ String(component.props?.label ?? '') }}</small>
            </div>
          </template>
          <template v-else-if="component.type === 'StatusLamp'"
            ><div class="dashboard-status">
              <i
                :style="{
                  background: colorForState(component, value(component, 'state', 'Unknown')),
                }"
              /><span>{{ display(component) }}</span>
            </div></template
          >
          <template
            v-else-if="
              ['Tank', 'Pipe', 'Pump', 'Valve', 'Conveyor', 'Motor'].includes(component.type)
            "
            ><svg
              class="dashboard-industrial-svg"
              viewBox="0 0 220 120"
              role="img"
              :aria-label="component.name"
              :class="{ 'is-active': industrialActive(component) }"
              :data-state="String(value(component, 'state', 'Unknown'))"
            >
              <rect
                v-if="component.type === 'Tank'"
                x="65"
                y="12"
                width="90"
                height="88"
                rx="26"
                fill="#173c5c"
                stroke="#66c5d8"
                stroke-width="4"
              />
              <rect
                v-if="component.type === 'Tank'"
                x="70"
                :y="100 - tankLevelPercent(component) * 0.8"
                width="80"
                :height="tankLevelPercent(component) * 0.8"
                rx="20"
                fill="#2dd4bf"
                opacity="0.78"
              />
              <polyline
                v-if="component.type === 'Pipe'"
                :points="
                  (
                    (component.props.points as number[][]) ?? [
                      [8, 60],
                      [212, 60],
                    ]
                  )
                    .map((point) => point.join(','))
                    .join(' ')
                "
                stroke="#62a4c5"
                :stroke-width="Number(component.props.diameterPx ?? 8)"
                fill="none"
              />
              <circle
                v-else-if="component.type === 'Pump'"
                cx="110"
                cy="60"
                r="35"
                fill="#173c5c"
                stroke="#66c5d8"
                stroke-width="4"
              />
              <path
                v-else-if="component.type === 'Valve'"
                d="M75 35l35 25-35 25zm70 0l-35 25 35 25z"
                fill="#173c5c"
                stroke="#66c5d8"
                stroke-width="4"
              />
              <rect
                v-else-if="component.type === 'Conveyor'"
                x="20"
                y="40"
                width="180"
                height="35"
                rx="12"
                fill="#173c5c"
                stroke="#66c5d8"
                stroke-width="4"
              />
              <circle
                v-else-if="component.type === 'Motor'"
                cx="110"
                cy="60"
                r="35"
                fill="#173c5c"
                stroke="#66c5d8"
                stroke-width="4"
              />
              <text x="110" y="118" text-anchor="middle" fill="currentColor" font-size="12">
                {{ String(component.props?.label ?? component.name) }}
              </text>
              <path
                v-if="['Pump', 'Motor'].includes(component.type)"
                class="dashboard-rotor"
                d="M110 35v50M85 60h50"
                stroke="#85e5eb"
                stroke-width="5"
              />
              <path
                v-if="component.type === 'Conveyor'"
                class="dashboard-flow"
                d="M30 57h160"
                fill="none"
                stroke="#85e5eb"
                stroke-width="5"
                stroke-dasharray="10 8"
              />
              <template v-if="component.type === 'Tank'">
                <text x="110" y="34" text-anchor="middle" fill="#e7fbff" font-size="13">
                  {{ t('level') }} {{ tankReading(component, 'level')
                  }}{{ String(component.props?.unit ?? '%') }}
                </text>
                <text x="110" y="52" text-anchor="middle" fill="#c4e6ef" font-size="11">
                  {{ t('temperature') }} {{ tankReading(component, 'temperature') }}
                </text>
                <text x="110" y="70" text-anchor="middle" fill="#c4e6ef" font-size="11">
                  {{ t('state') }} {{ tankReading(component, 'state') }}
                </text>
              </template>
              <text v-else x="110" y="22" text-anchor="middle" fill="currentColor" font-size="11">
                {{ industrialReadings(component) }}
              </text>
            </svg></template
          >
          <template
            v-else-if="
              variant(component).startsWith('Chart.') &&
              !['Chart.WaterLevel', 'Chart.Gauge'].includes(variant(component))
            "
          >
            <div
              v-if="['Chart.Pie', 'Chart.Donut'].includes(variant(component))"
              class="dashboard-pie-wrap"
            >
              <div
                class="dashboard-pie"
                :class="{ 'is-donut': variant(component) === 'Chart.Donut' }"
                :style="pieStyle(component)"
                role="img"
                :aria-label="component.name"
              />
              <div class="dashboard-pie-legend">
                <span v-for="(row, index) in chartRows(component).slice(0, 6)" :key="index">
                  <i
                    :style="{
                      background: [
                        '#2dd4bf',
                        '#60a5fa',
                        '#fbbf24',
                        '#f97316',
                        '#c084fc',
                        '#f43f5e',
                      ][index % 6],
                    }"
                  />
                  {{
                    String(
                      row[
                        String(component.props?.categoryField ?? component.props?.xField ?? 'name')
                      ] ?? index + 1,
                    )
                  }}
                </span>
              </div>
            </div>
            <div v-else-if="variant(component) === 'Chart.Ranking'" class="dashboard-ranking">
              <div
                v-for="(row, index) in chartRows(component).slice(
                  0,
                  Number(component.props?.maxItems ?? 8),
                )"
                :key="index"
              >
                <span
                  >{{ index + 1 }}.
                  {{ String(row[String(component.props?.categoryField ?? 'name')] ?? '') }}</span
                >
                <b
                  :style="{
                    width: `${(chartNumber(row, component.props?.valueField ?? 'value') / chartMax(component)) * 100}%`,
                  }"
                />
                <em>{{ chartNumber(row, component.props?.valueField ?? 'value') }}</em>
              </div>
            </div>
            <div v-else-if="variant(component) === 'Chart.Heatmap'" class="dashboard-heatmap">
              <span
                v-for="(row, index) in chartRows(component).slice(0, 36)"
                :key="index"
                :style="{
                  background: heatColor(
                    chartNumber(row, component.props?.valueField ?? 'value'),
                    chartMax(component),
                    component,
                  ),
                }"
                :title="String(row[String(component.props?.xField ?? 'name')] ?? '')"
              />
            </div>
            <svg
              v-else
              class="dashboard-chart-svg"
              viewBox="0 0 600 260"
              role="img"
              :aria-label="component.name"
            >
              <path d="M30 20V220H580" stroke="#49677d" fill="none" />
              <g v-if="component.props.legend" font-size="12" fill="currentColor">
                <g
                  v-for="(series, index) in chartSeries(component)"
                  :key="index"
                  :transform="`translate(${40 + (index % 4) * 140}, ${3 + Math.floor(index / 4) * 14})`"
                >
                  <rect width="8" height="8" :fill="seriesColor(series, index)" />
                  <text x="14" y="9">{{ series.name ?? series.valueField }}</text>
                </g>
              </g>
              <template v-if="['Chart.Line', 'Chart.Area'].includes(variant(component))">
                <path
                  v-if="variant(component) === 'Chart.Area'"
                  :d="
                    chartAreaPath(component, chartSeries(component)[0] ?? { valueField: 'value' })
                  "
                  :fill="seriesColor(chartSeries(component)[0] ?? {}, 0)"
                  opacity="0.22"
                />
                <polyline
                  v-for="(series, seriesIndex) in chartSeries(component)"
                  :key="seriesIndex"
                  :points="chartPoints(component, series)"
                  fill="none"
                  :stroke="seriesColor(series, seriesIndex)"
                  stroke-width="4"
                  stroke-linejoin="round"
                />
              </template>
              <template v-else-if="variant(component) === 'Chart.Scatter'">
                <circle
                  v-for="(row, index) in chartRows(component)"
                  :key="index"
                  :cx="scatterCoordinate(component, row, 'x')"
                  :cy="scatterCoordinate(component, row, 'y')"
                  r="5"
                  fill="#28b7bd"
                />
              </template>
              <template v-else>
                <template v-for="(row, index) in chartRows(component).slice(0, 12)" :key="index">
                  <rect
                    v-for="(series, seriesIndex) in chartSeries(component)"
                    :key="seriesIndex"
                    :x="
                      45 +
                      index * 42 +
                      (variant(component) === 'Chart.StackedBar'
                        ? 0
                        : seriesIndex * Math.min(9, 36 / chartSeries(component).length))
                    "
                    :y="barY(component, row, series, seriesIndex)"
                    :width="Math.max(1, Math.min(7, 36 / chartSeries(component).length - 2))"
                    :height="barHeight(component, row, series)"
                    :fill="seriesColor(series, seriesIndex)"
                  />
                  <text
                    :x="55 + index * 42"
                    y="242"
                    text-anchor="middle"
                    font-size="10"
                    fill="currentColor"
                  >
                    {{
                      String(
                        row[
                          String(
                            component.props?.categoryField ?? component.props?.xField ?? 'name',
                          )
                        ] ?? '',
                      )
                    }}
                  </text>
                </template>
              </template>
            </svg>
          </template>
          <ol
            v-else-if="component.type === 'ProcessSteps'"
            class="dashboard-process-steps"
            :class="{ vertical: String(component.props.direction).toLowerCase() === 'vertical' }"
          >
            <li
              v-for="(row, index) in rows(component)"
              :key="String(row[String(component.props.keyField ?? 'key')] ?? index)"
              :class="{
                current:
                  row[String(component.props.keyField ?? 'key')] ===
                  value(component, 'currentKey', undefined),
              }"
              :data-state="String(row[String(component.props.stateField ?? 'state')] ?? 'Unknown')"
              @click.stop="onComponentClick(component, row)"
            >
              <b>{{ index + 1 }}</b
              ><span>{{ row[String(component.props.labelField ?? 'label')] ?? '—' }}</span>
              <small>{{ row[String(component.props.stateField ?? 'state')] ?? 'Unknown' }}</small>
            </li>
          </ol>
          <template
            v-else-if="
              variant(component) === 'Table.Scroll' ||
              variant(component) === 'List.Scroll' ||
              variant(component) === 'Chart.Ranking' ||
              variant(component) === 'Chart.Heatmap'
            "
            ><DashboardTableViewport
              v-slot="{ repeat }"
              :continuous="component.props.scrollMode === 'Continuous'"
              :page-seconds="component.props.pageSeconds"
              :style="{
                '--dashboard-table-header':
                  component.props.appearance?.background &&
                  component.props.appearance.background !== 'transparent'
                    ? component.props.appearance.background
                    : canvas.background || '#142738',
              }"
            >
              <table
                :class="{ 'has-row-height': component.props.rowHeight !== undefined }"
                :style="{
                  tableLayout: ((component.props.columns as Json[]) ?? []).some(
                    (column) => column.widthPercent !== undefined,
                  )
                    ? 'fixed'
                    : undefined,
                }"
              >
                <colgroup>
                  <col
                    v-for="(column, index) in (component.props.columns as Json[]) ?? []"
                    :key="index"
                    :style="columnStyle(column)"
                  />
                </colgroup>
                <thead v-if="variant(component).includes('Table')">
                  <tr>
                    <th
                      v-for="column in (component.props?.columns as Json[]) ?? []"
                      :key="columnField(column)"
                      :style="columnStyle(column)"
                    >
                      <span :style="tableCellStyle(component)">{{ columnLabel(column) }}</span>
                    </th>
                  </tr>
                </thead>
                <tbody
                  v-for="copy in repeat ? 2 : 1"
                  :key="copy"
                  :aria-hidden="copy === 2 ? true : undefined"
                >
                  <tr
                    v-for="(row, index) in visibleRows(component)"
                    :key="index"
                    @click.stop="onComponentClick(component, row)"
                  >
                    <td
                      v-for="column in (component.props?.columns as Json[]) ?? [
                        { field: 'label', label: t('item') },
                        { field: 'state', label: t('status') },
                      ]"
                      :key="columnField(column)"
                      :style="columnStyle(column)"
                    >
                      <span :style="tableCellStyle(component)">{{
                        String(valueAt(row, columnField(column).split('.')) ?? '—')
                      }}</span>
                    </td>
                  </tr>
                </tbody>
              </table>
              <el-empty
                v-if="!rows(component).length"
                :description="
                  component.type === 'ProcessSteps' ? t('waitingProcessData') : t('noData')
                "
                :image-size="38" /></DashboardTableViewport
          ></template>
          <template v-else-if="component.type === 'DataQuality' || component.type === 'DataSource'"
            ><div class="dashboard-quality">
              <strong
                >{{
                  component.type === 'DataSource' ? t('dataInstanceTitle') : t('dataQualityTitle')
                }} </strong
              ><span v-if="component.type === 'DataSource'">{{
                String(component.props?.instanceId ?? '未绑定')
              }}</span
              ><span
                v-else
                :class="'tone-' + freshness(String(component.props?.instanceId ?? '')).tone"
                >{{ freshness(String(component.props?.instanceId ?? '')).label }}</span
              >
              <template v-if="component.type === 'DataQuality'">
                <small v-if="component.props?.showLastSuccess !== false">
                  {{ t('lastSuccessfulFetch', { value: qualityTime(component, 'observedOn') }) }}
                </small>
                <small v-if="component.props?.showSourceTime !== false">
                  {{
                    t('sourceSampleTime', {
                      value: qualityTime(component, 'sourceObservedOn'),
                    })
                  }}
                  <template v-if="qualityReason(component)">
                    （{{ qualityReason(component) }}）
                  </template>
                </small>
              </template>
            </div></template
          >
          <template v-else
            ><div class="dashboard-generic">
              <span>{{ component.name || component.type }}</span
              ><small>{{ component.type }}</small>
            </div></template
          >
          <div
            v-if="editing"
            class="dashboard-editor-component"
            :class="{ locked: component.locked }"
            role="button"
            tabindex="0"
            :aria-label="component.name || component.type"
            @pointerdown.stop="emit('editPointerDown', $event, component)"
            @click.stop="emit('select', component.id)"
            @keydown.enter.prevent="emit('select', component.id)"
            @keydown.space.prevent="emit('select', component.id)"
          />
        </article>
      </div>
    </div>
  </div>
</template>

<style scoped>
.dashboard-source-error {
  position: absolute;
  inset: auto 4px 2px;
  z-index: 5;
  color: #ffd166;
  background: #081525e6;
  font-size: 12px;
  line-height: 20px;
  padding: 0 4px;
}
.dashboard-runtime-host {
  position: relative;
  width: 100%;
  min-height: 520px;
  overflow: auto;
  background: #07111c;
  border: 1px solid rgba(116, 169, 194, 0.22);
  border-radius: 8px;
}
.dashboard-runtime-transport {
  position: sticky;
  z-index: 2;
  top: 0;
  padding: 6px 10px;
  color: #ffd3a1;
  background: rgba(117, 50, 39, 0.92);
  font-size: 12px;
}
.dashboard-runtime-host.is-embedded,
.dashboard-runtime-host.is-editing {
  min-height: 0;
  height: 100%;
  border: 0;
  border-radius: 0;
  background: transparent;
}
.dashboard-text.is-scrolling {
  white-space: nowrap;
  animation: dashboard-scroll var(--scroll-duration) linear infinite;
}
.dashboard-text.is-scrolling.pause-on-hover:hover {
  animation-play-state: paused;
}
@keyframes dashboard-scroll {
  from {
    transform: translateX(100%);
  }
  to {
    transform: translateX(-100%);
  }
}
.dashboard-runtime-frame {
  position: relative;
  margin: 0 auto;
}
.dashboard-runtime-canvas {
  position: absolute;
  left: 0;
  top: 0;
  transform-origin: top left;
  overflow: hidden;
  color: #d7edf6;
}
.dashboard-runtime-component {
  position: absolute;
  box-sizing: border-box;
  overflow: hidden;
  border: 1px solid rgba(110, 190, 211, 0.25);
  border-radius: 8px;
  padding: 12px;
  background: rgba(9, 34, 55, 0.84);
  box-shadow: 0 8px 24px rgba(0, 0, 0, 0.16);
}
.dashboard-runtime-component.is-selected {
  outline: 2px solid #ffc857;
}
.dashboard-editor-component {
  position: absolute;
  inset: 0;
  z-index: 20;
  cursor: move;
  user-select: none;
  touch-action: none;
}
.dashboard-editor-component:hover,
.dashboard-editor-component:focus-visible {
  outline: 1px dashed #52c2ce;
  outline-offset: -1px;
}
.dashboard-editor-component.locked {
  cursor: default;
}
.dashboard-text {
  font-size: 24px;
  font-weight: 650;
  white-space: pre-wrap;
}
.dashboard-button {
  font-family: inherit;
  width: 100%;
  height: 100%;
  border: 1px solid #36b8c5;
  border-radius: 6px;
  color: #e7fbff;
  background: #12465b;
  cursor: pointer;
}
.dashboard-number {
  display: flex;
  height: 100%;
  flex-direction: column;
  justify-content: center;
  gap: 8px;
  font-size: 42px;
  font-weight: 700;
  color: #85e5eb;
}
.dashboard-number small,
.dashboard-meter small,
.dashboard-quality span {
  color: #9ab6c2;
  font-size: 13px;
}
.dashboard-meter {
  height: 100%;
  display: flex;
  align-items: center;
  gap: 12px;
  flex-direction: column;
  justify-content: center;
}
.dashboard-meter-track {
  position: relative;
  width: 38px;
  height: 78%;
  border-radius: 20px;
  background: #123448;
  overflow: hidden;
}
.dashboard-meter-fill {
  position: absolute;
  bottom: 0;
  width: 100%;
  background: linear-gradient(#35d2d2, #2483bb);
}
.dashboard-meter.is-horizontal {
  flex-direction: row;
}
.dashboard-meter.is-horizontal .dashboard-meter-track {
  width: 78%;
  height: 22px;
}
.dashboard-meter.is-horizontal .dashboard-meter-fill {
  right: auto;
  height: 100%;
  width: var(--dashboard-meter-value, 62%);
  background: linear-gradient(90deg, #2483bb, #35d2d2);
}
.dashboard-status {
  display: flex;
  align-items: center;
  gap: 12px;
  height: 100%;
  font-size: 20px;
}
.dashboard-status i {
  width: 22px;
  height: 22px;
  display: block;
  border-radius: 50%;
  box-shadow: 0 0 16px currentColor;
}
.dashboard-industrial-svg,
.dashboard-chart-svg,
.dashboard-gauge {
  width: 100%;
  height: 100%;
  color: #b8dce8;
}
.dashboard-industrial-svg.is-active {
  color: #40d28b;
}
.dashboard-industrial-svg.is-active .dashboard-rotor {
  transform-origin: 110px 60px;
  animation: dashboard-rotate 2s linear infinite;
}
.dashboard-industrial-svg.is-active .dashboard-flow,
.dashboard-industrial-svg.is-active polyline {
  stroke-dasharray: 10 8;
  animation: dashboard-flow 1s linear infinite;
}
@keyframes dashboard-rotate {
  to {
    transform: rotate(360deg);
  }
}
@keyframes dashboard-flow {
  to {
    stroke-dashoffset: -36;
  }
}
.dashboard-process-steps {
  display: flex;
  margin: 0;
  padding: 0;
  gap: 14px;
  list-style: none;
  height: 100%;
  align-items: center;
}
.dashboard-process-steps.vertical {
  flex-direction: column;
  align-items: stretch;
}
.dashboard-process-steps li {
  display: grid;
  grid-template-columns: auto minmax(0, 1fr);
  flex: 1;
  min-width: 0;
  gap: 6px;
  padding: 8px;
  border: 1px solid #49677d;
  border-radius: 8px;
}
.dashboard-process-steps li b {
  grid-row: 1 / 3;
}
.dashboard-process-steps li span,
.dashboard-process-steps li small {
  grid-column: 2;
}
.dashboard-process-steps li.current {
  border-color: #2dd4bf;
  background: #12465b;
}
.dashboard-pie-wrap {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 18px;
  height: 100%;
}
.dashboard-pie {
  width: min(48%, 180px);
  aspect-ratio: 1;
  border: 14px solid rgba(7, 17, 28, 0.4);
  border-radius: 50%;
  box-shadow: 0 0 0 1px rgba(116, 169, 194, 0.35);
}
.dashboard-pie-legend {
  display: grid;
  gap: 5px;
  color: #b8dce8;
  font-size: 12px;
}
.dashboard-pie.is-donut {
  mask-image: radial-gradient(circle, transparent 34%, black 35%);
}
.dashboard-pie-legend span {
  display: flex;
  align-items: center;
  gap: 5px;
}
.dashboard-pie-legend i {
  width: 9px;
  height: 9px;
  display: inline-block;
  border-radius: 50%;
}
.dashboard-ranking {
  display: grid;
  gap: 7px;
  height: 100%;
  align-content: center;
  font-size: 12px;
}
.dashboard-ranking > div {
  display: grid;
  grid-template-columns: minmax(90px, 1fr) 2fr 40px;
  align-items: center;
  gap: 8px;
}
.dashboard-ranking b {
  height: 10px;
  border-radius: 5px;
  background: linear-gradient(90deg, #2dd4bf, #60a5fa);
}
.dashboard-ranking em {
  color: #85e5eb;
  font-style: normal;
  text-align: right;
}
.dashboard-heatmap {
  display: grid;
  grid-template-columns: repeat(6, minmax(14px, 1fr));
  gap: 4px;
  align-content: center;
  height: 100%;
}
.dashboard-heatmap span {
  display: block;
  min-height: 22px;
  border-radius: 3px;
}
.dashboard-table-wrap {
  height: 100%;
  overflow: auto;
  scrollbar-width: none;
  -ms-overflow-style: none;
}
.dashboard-table-wrap::-webkit-scrollbar {
  display: none;
}
thead th {
  position: sticky;
  top: 0;
  z-index: 1;
  background: var(--dashboard-table-header, #102b33);
}
table {
  width: 100%;
  border-collapse: collapse;
  font-size: 14px;
}
th,
td {
  padding: 8px;
  border-bottom: 1px solid rgba(116, 169, 194, 0.2);
  text-align: left;
}
.has-row-height th,
.has-row-height td {
  padding: 0 8px;
}
tr {
  cursor: pointer;
}
tr:hover {
  background: rgba(70, 170, 195, 0.16);
}
.dashboard-quality,
.dashboard-generic {
  display: flex;
  flex-direction: column;
  gap: 10px;
  justify-content: center;
  height: 100%;
}
.dashboard-quality strong {
  color: #e6fbff;
}
.tone-good {
  color: #40d28b;
}
.tone-stale {
  color: #ffc857;
}
.tone-unknown,
.tone-bad {
  color: #ff8c78;
}
</style>
