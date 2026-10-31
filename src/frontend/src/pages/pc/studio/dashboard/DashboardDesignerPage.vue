<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch, type Component } from 'vue'
import {
  Box,
  EditPen,
  Pointer,
  Picture,
  CopyDocument,
  Files,
  Odometer,
  Operation,
  Clock,
  Tickets,
  Switch,
  Monitor,
  Histogram,
  TrendCharts,
  PieChart,
  DataLine,
  Grid,
  Coin,
  Connection,
  ScaleToOriginal,
  Sunny,
  DArrowRight,
  SetUp,
  DataBoard,
  Warning,
} from '@element-plus/icons-vue'
import { onBeforeRouteLeave, useRoute, useRouter } from 'vue-router'
import { ElMessageBox } from 'element-plus'
import AppPage from '@/components/base/AppPage.vue'
import AppFormDrawer from '@/components/management/AppFormDrawer.vue'
import DashboardRuntimeView from '@/components/studio/dashboard/DashboardRuntimeView.vue'
import DashboardAppearanceEditor from '@/components/studio/dashboard/DashboardAppearanceEditor.vue'
import DashboardStructuredEditor from '@/components/studio/dashboard/DashboardStructuredEditor.vue'
import DashboardDataSourceEditor from '@/components/studio/dashboard/DashboardDataSourceEditor.vue'
import DashboardConstantEditor from '@/components/studio/dashboard/DashboardConstantEditor.vue'
import {
  constantInstances,
  createDataSchedule,
  dataPaths,
  mergeRuntime,
  shareDashboardConstants,
} from '@/components/studio/dashboard/dataSources'
import { structuredKinds } from '@/components/studio/dashboard/structuredFields'
import {
  getStudioApi,
  type DashboardRuntime,
  type Json,
  type RegistryEntry,
  type StudioResource,
} from '@/api/studio'
import { clone, createHistory } from '@/components/studio/shared/model'
import {
  allowPaletteDrop,
  canvasDropPoint,
  paletteKey,
  startPaletteDrag,
} from '@/components/studio/shared/paletteDrag'
import { useStudioCopy } from '@/components/studio/shared/copy'
import {
  dashboardComponentLabels,
  useDashboardText,
} from '@/components/studio/dashboard/dashboardCopy'
import { dashboardSchemaPaths } from '@/components/studio/dashboard/schemaPaths'
import { listDashboardResources } from '@/components/studio/dashboard/designerResources'
import {
  DashboardDesignFileError,
  downloadDashboardDesign,
  MAX_DESIGN_FILE_BYTES,
  parseDashboardDesign,
  type DashboardDesignFile,
} from '@/components/studio/dashboard/designFile'
import { useAuthStore } from '@/stores/authStore'
import {
  dashboardSamples,
  initialDashboardDocument,
  makeComponent,
  type DashboardComponent,
  type DashboardDocument,
} from './model'
import '@/components/studio/shared/studio.css'

const route = useRoute(),
  router = useRouter(),
  t = useStudioCopy(),
  dashboardT = useDashboardText()
const auth = useAuthStore()
const resource = ref<StudioResource | null>(null)
const documentValue = ref<DashboardDocument>(initialDashboardDocument())
const registry = ref<RegistryEntry[]>([])
const componentSearch = ref('')
const componentCategory = ref('basic')
const componentCategories = ['basic', 'chart', 'industrial']
const componentIcons: Record<string, Component> = {
  Container: Box,
  Text: EditPen,
  Button: Pointer,
  Image: Picture,
  StateImage: CopyDocument,
  ImageCarousel: Files,
  Number: Odometer,
  Progress: Operation,
  Clock,
  ScrollingText: Tickets,
  ContentSwitch: Switch,
  EmbeddedPage: Monitor,
  'Chart.Bar': Histogram,
  'Chart.Line': TrendCharts,
  'Chart.Area': DataLine,
  'Chart.Pie': PieChart,
  'Chart.WaterLevel': DataLine,
  'Chart.Gauge': Odometer,
  'Table.Scroll': Grid,
  'List.Scroll': Tickets,
  'Chart.Ranking': Histogram,
  'Chart.Heatmap': Grid,
  Tank: Coin,
  Pump: Odometer,
  Valve: SetUp,
  Pipe: Connection,
  Level: ScaleToOriginal,
  StatusLamp: Sunny,
  Conveyor: DArrowRight,
  Thermometer: DataLine,
  Motor: SetUp,
  ProcessSteps: Operation,
  DataSource: Coin,
  PageParameter: DataBoard,
  DataQuality: Warning,
}
const resources = ref<StudioResource[]>([])
const contracts = ref<Record<string, Json>>({})
const saved = ref('')
const selectedId = ref('')
const drawer = ref<'preview' | 'publish' | 'import' | ''>('')
const sourcesVisible = ref(false)
const parametersVisible = ref(false)
const activeSourceId = ref('')
const activeSource = computed(() =>
  documentValue.value.instances.find((item) => item.instanceId === activeSourceId.value),
)
const canvasSchedule = createDataSchedule()
let canvasRefreshTimer: ReturnType<typeof setTimeout> | undefined
const importedDesign = ref<DashboardDesignFile | null>(null)
const importError = ref('')
let importReadVersion = 0
const preview = ref<DashboardRuntime | null>(null)
const previewParameters = ref<Json>({})
const previewNavigation = ref<Json | null>(null)
const releaseNote = ref('')
const error = ref('')
const notice = ref('')
const busy = ref(false)
const catalogLoading = ref(false)
const dataLoading = ref(false)
const dataError = ref('')
const canvasData = ref<DashboardRuntime | null>(null)
const canvasHost = ref<HTMLElement>()
const autoFit = ref(true)
let canvasObserver: ResizeObserver | undefined
let loadVersion = 0
let dataVersion = 0
let dataTimer: ReturnType<typeof setTimeout> | undefined
const leftCollapsed = ref(false)
const rightCollapsed = ref(false)
const propertySection = ref('basic')
const dataBindingSection = ref('fields')
const dataSourceProperties = new Set([
  'instanceId',
  'collectionPath',
  'parameterName',
  'parameterMap',
])
const propertySections = ['basic', 'content', 'appearance', 'data', 'interaction']
const propertyContent = ref<HTMLElement>()
const expandedBindings = ref<number[]>([0])
const expandedNavigation = ref<string[]>([])
const zoom = ref(0.52)
const boundsWarning = ref('')
const history = createHistory<DashboardDocument>(documentValue.value)
const drag = ref<{ id: string; x: number; y: number; pointerX: number; pointerY: number } | null>(
  null,
)
const samples = dashboardSamples()
const selected = computed(
  () =>
    documentValue.value.components.find((component) => component.id === selectedId.value) ?? null,
)
const registeredComponents = computed(() =>
  registry.value.filter(
    (entry) =>
      entry.category &&
      entry.propsSchema &&
      !['DataSource', 'PageParameter', 'DataQuality'].includes(entry.key),
  ),
)
const dashboardEntries = computed(() =>
  registeredComponents.value.filter(
    (entry) =>
      (componentSearch.value.trim() ||
        dashboardComponentLabels[entry.key as keyof typeof dashboardComponentLabels]?.category ===
          componentCategory.value) &&
      `${entry.key} ${dashboardT.value.componentLabel(entry.key)} ${dashboardT.value.categoryLabel(String(entry.category))}`
        .toLowerCase()
        .includes(componentSearch.value.trim().toLowerCase()),
  ),
)
function selectComponentCategory(category: string) {
  componentCategory.value = category
  componentSearch.value = ''
}
function registryKey(component: DashboardComponent): string {
  if (component.type === 'Chart') {
    const kind = String(component.props?.kind ?? 'Bar')
    return `Chart.${({ StackedBar: 'Bar', Donut: 'Pie', Scatter: 'Line' } as Record<string, string>)[kind] ?? kind}`
  }
  if (component.type === 'Table')
    return String(component.props?.presentation ?? 'Table') === 'List'
      ? 'List.Scroll'
      : 'Table.Scroll'
  return component.type
}
const selectedEntry = computed(() =>
  registry.value.find(
    (entry) =>
      entry.propsSchema && entry.key === (selected.value ? registryKey(selected.value) : ''),
  ),
)
function propsSchemaProperties(entry: RegistryEntry | undefined): Record<string, Json> | undefined {
  const schema = entry?.propsSchema
  if (!schema || typeof schema !== 'object' || Array.isArray(schema)) return undefined
  const properties = (schema as Record<string, unknown>).properties
  return properties && typeof properties === 'object' && !Array.isArray(properties)
    ? (properties as Record<string, Json>)
    : undefined
}
const propFields = computed(() => {
  const properties = propsSchemaProperties(selectedEntry.value)
  return properties && typeof properties === 'object'
    ? Object.entries(properties)
        .filter(
          ([key]) =>
            !(key === 'pageSeconds' && selected.value?.props.scrollMode === 'None') &&
            propertyGroup(key) === propertySection.value &&
            (propertySection.value !== 'data' ||
              dataSourceProperties.has(key) === (dataBindingSection.value === 'source')),
        )
        .map(([key, schema]) => ({ key, schema }))
        .sort((a, b) => Number(b.key === 'rowHeight') - Number(a.key === 'rowHeight'))
    : []
})
function propertyGroup(key: string): string {
  if (
    [
      'appearance',
      'align',
      'wrap',
      'fit',
      'display',
      'orientation',
      'variant',
      'shape',
      'kind',
      'presentation',
      'legend',
      'colorStops',
      'unknownColor',
      'points',
      'diameterPx',
    ].includes(key)
  )
    return 'appearance'
  if (
    [
      'instanceId',
      'collectionPath',
      'rowKeyPath',
      'categoryField',
      'xField',
      'yField',
      'valueField',
      'keyField',
      'labelField',
      'stateField',
      'series',
      'columns',
      'parameterName',
      'parameterMap',
      'rowHeight',
    ].includes(key)
  )
    return 'data'
  return 'content'
}
watch(
  [propertySection, selectedId, dataBindingSection],
  () => {
    if (propertyContent.value) propertyContent.value.scrollTop = 0
  },
  { flush: 'post' },
)
watch(selectedId, () => {
  expandedBindings.value = [0]
})
function propValue(key: string): unknown {
  return selected.value?.props?.[key]
}
function propertyOptions(key: string, schema: Json): unknown[] {
  if (Array.isArray(schema.enum)) return schema.enum
  const choices: Record<string, string[]> = {
    align: ['left', 'center', 'right', 'justify'],
    fit: ['Contain', 'Cover'],
    scrollMode: ['None', 'Page', 'Continuous'],
    direction: ['horizontal', 'vertical'],
    shape: ['vertical', 'horizontal'],
    variant: ['motor', 'fan'],
    order: ['Asc', 'Desc'],
  }
  const options = choices[key] ?? []
  const current = propValue(key)
  return current != null && options.length && !options.includes(String(current))
    ? [...options, current]
    : options
}
function propText(key: string): string {
  const value = propValue(key)
  return value == null ? '' : typeof value === 'string' ? value : JSON.stringify(value)
}
function propNumber(key: string): number {
  const value = Number(propValue(key))
  return Number.isFinite(value) ? value : 0
}
function propJson(key: string): string {
  const value = propValue(key)
  const schema = propsSchemaProperties(selectedEntry.value)?.[key]
  return JSON.stringify(
    value ?? (schema && typeof schema === 'object' && schema.type === 'array' ? [] : {}),
    null,
    2,
  )
}
function schemaNumber(schema: Json, key: 'minimum' | 'maximum'): number | undefined {
  if (!schema || typeof schema !== 'object' || Array.isArray(schema)) return undefined
  const value = (schema as Record<string, unknown>)[key]
  return typeof value === 'number' ? value : undefined
}
function propPathText(key: string): string {
  const value = propValue(key)
  return Array.isArray(value) ? value.map(String).join('.') : ''
}
function updatePropPath(key: string, value: string) {
  selectedMutate((component) => {
    component.props[key] = value
      .split('.')
      .map((segment) => segment.trim())
      .filter(Boolean)
  })
}
function updateProp(key: string, value: unknown, schema: Json) {
  selectedMutate((component) => {
    const type = String(schema.type ?? 'string')
    if (type === 'number') {
      if (value == null) delete component.props[key]
      else component.props[key] = Number(value)
    } else if (type === 'boolean') component.props[key] = Boolean(value)
    else if (type === 'array' || type === 'object') {
      try {
        component.props[key] = typeof value === 'string' ? JSON.parse(value) : value
      } catch {
        return
      }
    } else component.props[key] = String(value ?? '')
  })
}
function contract(nId: string, revisionNId?: string): Json {
  const revision =
    revisionNId || resources.value.find((item) => item.nId === nId)?.publishedRevisionNId || ''
  return contracts.value[`${nId}/${revision}`] ?? {}
}
function parametersOf(nId: string, revisionNId?: string): Json[] {
  return contract(nId, revisionNId).parameters ?? []
}
function availableResources(kind: string): StudioResource[] {
  return resources.value.filter(
    (item) => item.kind === kind && item.isEnabled && item.publishedRevisionNId,
  )
}
async function loadContract(nId: string, revisionNId?: string) {
  const revision =
    revisionNId || resources.value.find((item) => item.nId === nId)?.publishedRevisionNId
  if (!nId || !revision) return
  const key = `${nId}/${revision}`
  if (contracts.value[key]) return
  try {
    contracts.value[key] = (await getStudioApi().revision(nId, revision)).document
  } catch (e) {
    error.value = e instanceof Error ? e.message : t.value('error')
  }
}
async function targetChanged(rule: Json, kindChanged = false) {
  if (kindChanged) rule.target.resourceNId = ''
  rule.target.revisionNId = ''
  mutate(() => undefined)
  await loadContract(rule.target.resourceNId)
}
function fieldOptions(source: Json, rule?: Json): string[] {
  if (source.from === 'PageParam')
    return documentValue.value.parameters.map((item) => String(item.name)).filter(Boolean)
  if (source.from === 'Context') return ['tenant', 'user', 'authorizedFactoryIds']
  let instanceId = source.instanceId
  let collectionPath: string[] = []
  if (source.from === 'Row' && rule) {
    const component = documentValue.value.components.find((item) => item.id === rule.componentId)
    const rowsSource = component?.bindings.find((binding) => binding.property === 'rows')?.source
    instanceId = rowsSource?.instanceId ?? component?.props.instanceId
    collectionPath = rowsSource?.path ?? component?.props.collectionPath ?? []
  }
  const instance = documentValue.value.instances.find((item) => item.instanceId === instanceId)
  if (instance?.sourceType === 'Constant') {
    let value = instance.value
    if (source.from === 'Row') {
      for (const segment of collectionPath) value = value?.[segment]
      value = Array.isArray(value) ? value.find((row) => row && typeof row === 'object') : undefined
    }
    return dataPaths(value)
  }
  return instance
    ? dashboardSchemaPaths(
        contract(instance.datasetNId, instance.revisionNId).result,
        source.from === 'Row',
        collectionPath,
      )
    : []
}
function pathOptions(source: Json): string[] {
  return fieldOptions(source)
}
async function loadInstancePaths() {
  await Promise.all([
    ...documentValue.value.instances.map((instance) =>
      loadContract(instance.datasetNId, instance.revisionNId),
    ),
    ...documentValue.value.navigation.map((rule) =>
      loadContract(rule.target.resourceNId, rule.target.revisionNId),
    ),
  ])
}
const canvasStyle = computed(() => ({
  width: `${documentValue.value.canvas.width * zoom.value}px`,
  height: `${documentValue.value.canvas.height * zoom.value}px`,
  background: documentValue.value.canvas.background,
}))
const canvasParameters = computed<Json>(() =>
  Object.fromEntries(
    documentValue.value.parameters.map((parameter) => [
      String(parameter.name),
      previewParameters.value[String(parameter.name)] ?? parameter.defaultValue ?? '',
    ]),
  ),
)
const canvasRuntime = computed<Json>(() => ({
  ...(canvasData.value ?? {}),
  document: documentValue.value,
  instances: { ...constantInstances(documentValue.value), ...(canvasData.value?.instances ?? {}) },
  transport: 'Connected',
}))
function fitCanvas() {
  const host = canvasHost.value
  if (!host || host.clientWidth <= 40 || host.clientHeight <= 40) return
  zoom.value = Math.max(
    0.2,
    Math.min(
      2,
      (host.clientWidth - 40) / documentValue.value.canvas.width,
      (host.clientHeight - 40) / documentValue.value.canvas.height,
    ),
  )
}
function enableAutoFit() {
  autoFit.value = true
  fitCanvas()
}
function showActualSize() {
  autoFit.value = false
  zoom.value = 1
}
watch(
  canvasHost,
  (host) => {
    canvasObserver?.disconnect()
    if (host) canvasObserver?.observe(host)
    if (autoFit.value) fitCanvas()
  },
  { flush: 'post' },
)
watch(
  () => [documentValue.value.canvas.width, documentValue.value.canvas.height],
  () => {
    if (autoFit.value) void nextTick(fitCanvas)
  },
)
async function refreshCanvasData(instanceIds?: unknown) {
  const ids = Array.isArray(instanceIds) ? (instanceIds as string[]) : undefined
  clearTimeout(canvasRefreshTimer)
  const current = ++dataVersion
  clearTimeout(dataTimer)
  dataError.value = ''
  if (
    !resource.value ||
    !documentValue.value.instances.some((source) => source.sourceType !== 'Constant')
  ) {
    canvasData.value = null
    dataLoading.value = false
    return
  }
  if (
    documentValue.value.parameters.some(
      (parameter) =>
        parameter.required &&
        !['TrustedContext'].includes(String(parameter.source)) &&
        (canvasParameters.value[String(parameter.name)] == null ||
          canvasParameters.value[String(parameter.name)] === ''),
    )
  ) {
    canvasData.value = null
    dataLoading.value = false
    dataError.value = dashboardT.value('requiredParameterBeforeRun')
    return
  }
  dataLoading.value = true
  try {
    const result = await getStudioApi().dashboardPreview(
      clone(documentValue.value),
      clone(canvasParameters.value),
      undefined,
      undefined,
      resource.value.nId,
      ids,
    )
    if (current === dataVersion) {
      canvasData.value = ids ? mergeRuntime(canvasData.value, result) : result
      canvasSchedule.mark(Object.keys(result.instances))
    }
  } catch (e) {
    if (current === dataVersion) {
      canvasData.value = null
      dataError.value = e instanceof Error ? e.message : t.value('error')
    }
  } finally {
    if (current === dataVersion) {
      dataLoading.value = false
      if (ids) canvasSchedule.mark(ids)
      scheduleCanvasRefresh()
    }
  }
}
function scheduleCanvasRefresh() {
  clearTimeout(canvasRefreshTimer)
  if (
    !documentValue.value.instances.some(
      (item) => item.sourceType !== 'Constant' && Number(item.refreshSeconds) > 0,
    )
  )
    return
  canvasRefreshTimer = setTimeout(() => {
    const ids = canvasSchedule.due(documentValue.value)
    if (ids.length) void refreshCanvasData(ids)
    else scheduleCanvasRefresh()
  }, canvasSchedule.delay(documentValue.value))
}
watch(
  () =>
    JSON.stringify([
      documentValue.value.instances,
      documentValue.value.parameters,
      canvasParameters.value,
    ]),
  () => {
    dataVersion++
    canvasData.value = null
    dataLoading.value = false
    clearTimeout(dataTimer)
    clearTimeout(canvasRefreshTimer)
    if (resource.value) dataTimer = setTimeout(() => void refreshCanvasData(), 400)
  },
)
const propKeys = computed(() => (selected.value ? Object.keys(selected.value.props ?? {}) : []))
const bindableProperties = computed(() => {
  const values = selectedEntry.value?.bindableProperties
  return Array.isArray(values) && values.length > 0 ? values.map(String) : propKeys.value
})
function setDocument(next: DashboardDocument) {
  documentValue.value = next
  history.commit(next)
}
function mutate(mutator: (next: DashboardDocument) => void) {
  const next = clone(documentValue.value)
  mutator(next)
  next.instances = next.instances.filter(
    (source) =>
      !source.scopeComponentId ||
      next.components.some((component) =>
        component.bindings.some(
          (binding) =>
            binding.source?.from === 'Instance' && binding.source.instanceId === source.instanceId,
        ),
      ),
  )
  setDocument(next)
}
function selectedMutate(mutator: (component: DashboardComponent) => void) {
  if (!selectedId.value) return
  mutate((next) => {
    const component = next.components.find((item) => item.id === selectedId.value)
    if (component) mutator(component)
  })
}
function undo() {
  documentValue.value = history.undo()
  selectedId.value = documentValue.value.components[0]?.id ?? ''
}
function redo() {
  documentValue.value = history.redo()
  selectedId.value = documentValue.value.components[0]?.id ?? ''
}
function ensureDocument(value: Json): DashboardDocument {
  const base = initialDashboardDocument()
  return {
    ...base,
    ...clone(value),
    canvas: { ...base.canvas, ...((value.canvas as Json) ?? {}) },
    parameters: Array.isArray(value.parameters) ? value.parameters : [],
    instances: Array.isArray(value.instances) ? value.instances : [],
    components: Array.isArray(value.components) ? value.components : base.components,
    navigation: Array.isArray(value.navigation) ? value.navigation : [],
  } as DashboardDocument
}
async function load() {
  const current = ++loadVersion
  busy.value = true
  error.value = ''
  boundsWarning.value = ''
  try {
    const draft = await getStudioApi().draft(String(route.params.nId))
    if (current !== loadVersion) return
    resource.value = draft.resource
    documentValue.value = ensureDocument(draft.draftDocument)
    saved.value = JSON.stringify(documentValue.value)
    history.reset(documentValue.value)
    selectedId.value = documentValue.value.components[0]?.id ?? ''
    await nextTick()
    if (current !== loadVersion) return
    if (autoFit.value) fitCanvas()
    // Prioritize the canvas before directory-backed option requests compete for the connection.
    void loadCatalog(current)
    void loadResourceOptions(current)
    void loadInstancePaths()
  } catch (e) {
    if (current === loadVersion) error.value = e instanceof Error ? e.message : t.value('error')
  } finally {
    if (current === loadVersion) busy.value = false
  }
}
async function loadCatalog(current: number) {
  catalogLoading.value = true
  try {
    const catalog = await getStudioApi().registry()
    if (current === loadVersion) registry.value = catalog.components
  } catch (e) {
    if (current === loadVersion) error.value = e instanceof Error ? e.message : t.value('error')
  } finally {
    if (current === loadVersion) catalogLoading.value = false
  }
}
async function loadResourceOptions(current: number) {
  try {
    const capabilities = await getStudioApi().capabilities()
    if (current !== loadVersion) return
    const enabled = (module: string) =>
      module === 'Core' ||
      capabilities.modules.some((item) => item.key === module && item.installed && item.enabled)
    const list = await listDashboardResources(
      [
        ['Dataset', 'studio.dataset.view', 'Core'],
        ['Dashboard', 'studio.dashboard.view', 'Dashboard'],
        ['LowCodePage', 'studio.lowcode.view', 'LowCode'],
        ['Report', 'studio.report.view', 'Report'],
      ]
        .filter(([, permission, module]) => enabled(module!) && auth.hasPermission(permission!))
        .map(([kind]) => kind!),
    )
    if (current !== loadVersion) return
    resources.value = list
    void loadInstancePaths()
  } catch (e) {
    if (current === loadVersion) error.value = e instanceof Error ? e.message : t.value('error')
  }
}
function addComponent(entry: RegistryEntry, point?: { x: number; y: number }) {
  mutate((next) => {
    const component = makeComponent(entry, next.components.length)
    if (point) {
      component.width = Math.min(component.width, next.canvas.width)
      component.height = Math.min(component.height, next.canvas.height)
      component.x = Math.round(Math.max(0, Math.min(next.canvas.width - component.width, point.x)))
      component.y = Math.round(
        Math.max(0, Math.min(next.canvas.height - component.height, point.y)),
      )
    }
    next.components.push(component)
    selectedId.value = component.id
  })
}
function dropComponent(event: DragEvent) {
  const key = paletteKey(event, 'dashboard')
  const entry = dashboardEntries.value.find((item) => item.key === key)
  if (!entry) return
  addComponent(entry, canvasDropPoint(event, event.currentTarget as HTMLElement, zoom.value))
}
function removeSelected() {
  if (!selectedId.value) return
  mutate((next) => {
    next.components = next.components.filter((component) => component.id !== selectedId.value)
    next.navigation = next.navigation.filter((rule) => rule.componentId !== selectedId.value)
    selectedId.value = next.components[0]?.id ?? ''
  })
}
function setCanvasNumber(key: 'width' | 'height', value: number | undefined) {
  mutate((next) => {
    next.canvas[key] = Math.max(320, Math.min(16384, Number(value ?? 320)))
    checkBounds(next)
  })
}
function checkBounds(value = documentValue.value) {
  const outside = value.components.filter(
    (component) =>
      component.x < 0 ||
      component.y < 0 ||
      component.x + component.width > value.canvas.width ||
      component.y + component.height > value.canvas.height,
  )
  boundsWarning.value = outside.length
    ? dashboardT.value('boundsWarning', { count: outside.length })
    : ''
}
function fixBounds() {
  mutate((next) => {
    for (const component of next.components) {
      component.x = Math.max(0, Math.min(component.x, next.canvas.width - component.width))
      component.y = Math.max(0, Math.min(component.y, next.canvas.height - component.height))
      component.width = Math.min(component.width, next.canvas.width)
      component.height = Math.min(component.height, next.canvas.height)
    }
  })
  boundsWarning.value = ''
}
function pointerDown(event: PointerEvent, component: DashboardComponent) {
  if (component.locked) return
  selectedId.value = component.id
  drag.value = {
    id: component.id,
    x: component.x,
    y: component.y,
    pointerX: event.clientX,
    pointerY: event.clientY,
  }
  ;(event.currentTarget as HTMLElement).setPointerCapture?.(event.pointerId)
}
function pointerMove(event: PointerEvent) {
  if (!drag.value) return
  const active = drag.value
  const next = clone(documentValue.value)
  const component = next.components.find((item) => item.id === active.id)
  if (!component) return
  component.x = active.x + (event.clientX - active.pointerX) / zoom.value
  component.y = active.y + (event.clientY - active.pointerY) / zoom.value
  checkBounds(next)
  documentValue.value = next
}
function pointerUp() {
  if (drag.value) history.commit(documentValue.value)
  drag.value = null
}
function align(mode: 'left' | 'center' | 'right' | 'top' | 'middle' | 'bottom') {
  if (!selected.value) return
  const selectedComponent = selected.value
  mutate((next) => {
    for (const component of next.components) {
      if (component.id !== selectedComponent.id) continue
      if (mode === 'left') component.x = 0
      if (mode === 'center') component.x = (next.canvas.width - component.width) / 2
      if (mode === 'right') component.x = next.canvas.width - component.width
      if (mode === 'top') component.y = 0
      if (mode === 'middle') component.y = (next.canvas.height - component.height) / 2
      if (mode === 'bottom') component.y = next.canvas.height - component.height
    }
  })
}
function addParameter() {
  mutate((next) =>
    next.parameters.push({
      name: `parameter${next.parameters.length + 1}`,
      label: dashboardT.value('pageParameters'),
      type: 'String',
      required: false,
      defaultValue: '',
    }),
  )
}
function addInstance() {
  mutate((next) =>
    next.instances.push({
      instanceId: `instance${next.instances.length + 1}`,
      datasetNId: '',
      revisionNId: '',
      sourceType: 'Dataset',
      refreshSeconds: 30,
      staleAfterSeconds: 300,
      parameters: [],
      label: dashboardT.value('dataInstance'),
    }),
  )
}
async function openDataSources() {
  sourcesVisible.value = true
  activeSourceId.value ||= documentValue.value.instances[0]?.instanceId ?? ''
  await Promise.all(availableResources('Dataset').map((item) => loadContract(item.nId)))
}
function applyDataExample() {
  setDocument(shareDashboardConstants(documentValue.value, dashboardT.value('sharedDataExample')))
  activeSourceId.value = documentValue.value.instances.at(-1)?.instanceId ?? ''
}
function updateSource(value: Json) {
  mutate((next) => {
    const index = next.instances.findIndex((item) => item.instanceId === value.instanceId)
    if (index >= 0) next.instances[index] = value
  })
}
function bindingInstance(source: Json): Json | undefined {
  return documentValue.value.instances.find((item) => item.instanceId === source.instanceId)
}
function bindingType(source: Json): string {
  return source.from === 'Instance' &&
    bindingInstance(source)?.scopeComponentId === selectedId.value &&
    bindingInstance(source)?.sourceType === 'Api'
    ? 'Api'
    : source.from
}
function setBindingType(source: Json, value: string) {
  if (value === 'Api') {
    const id = 'api-' + crypto.randomUUID()
    source.from = 'Instance'
    source.instanceId = id
    source.path = []
    documentValue.value.instances.push({
      instanceId: id,
      sourceType: 'Api',
      scopeComponentId: selectedId.value,
      label: selected.value?.name || dashboardT.value('apiSource'),
      datasetNId: '',
      revisionNId: '',
      parameters: [],
      refreshSeconds: 30,
      staleAfterSeconds: 300,
    })
    void Promise.all(availableResources('Dataset').map((item) => loadContract(item.nId)))
  } else {
    source.from = value
    if (value === 'Constant' && source.value === undefined) source.value = ''
    source.path ??= []
  }
  mutate(() => undefined)
}
function sourceUsage(id: string): string {
  return (
    documentValue.value.components
      .filter(
        (component) =>
          component.props.instanceId === id ||
          component.bindings.some(
            (binding) => binding.source?.from === 'Instance' && binding.source.instanceId === id,
          ),
      )
      .map((component) => component.name)
      .join('、') || dashboardT.value('sourceUnused')
  )
}
function addNavigationParameter(rule: Json) {
  mutate((next) => {
    const target = next.navigation.find((item) => item.id === rule.id)
    if (!target) return
    const parameterMap = Array.isArray(target.parameterMap)
      ? target.parameterMap
      : (target.parameterMap = [])
    parameterMap.push({
      target: `parameter${parameterMap.length + 1}`,
      source: { from: 'PageParam', path: [], nullPolicy: 'Error' },
    })
  })
}
function addBinding() {
  selectedMutate((component) =>
    component.bindings.push({
      property: propKeys.value[0] ?? 'text',
      source: { from: 'Constant', value: '', nullPolicy: 'Error' },
    }),
  )
  expandedBindings.value = [Math.max(0, (selected.value?.bindings.length ?? 1) - 1)]
  dataBindingSection.value = 'fields'
}
function configureBindingSource(index: number) {
  expandedBindings.value = [index]
  dataBindingSection.value = 'source'
}
function bindingSourceLabel(source: Json): string {
  return dashboardT.value(
    (
      {
        Constant: 'constant',
        Instance: 'sharedPageSource',
        PageParam: 'pageParameter',
        Row: 'row',
      } as Record<string, string>
    )[bindingType(source)] ?? dashboardT.value('apiSource'),
  )
}
function pathText(source: Json): string {
  return Array.isArray(source.path) ? source.path.map(String).join('.') : String(source.path ?? '')
}
function constantText(source: Json): string {
  return typeof source.value === 'string'
    ? source.value
    : JSON.stringify(source.value ?? null, null, 2)
}
function setConstant(source: Json, text: string) {
  try {
    const value =
      typeof source.value === 'string' || source.value === undefined ? text : JSON.parse(text)
    source.value = value
    source.nullPolicy ??= 'Error'
    mutate(() => undefined)
    error.value = ''
  } catch {
    error.value = dashboardT.value('constantJsonInvalid')
  }
}
function setBindingPath(source: Json, text: string) {
  source.path = text
    .split('.')
    .map((segment) => segment.trim())
    .filter(Boolean)
  mutate(() => undefined)
}
function addNavigation() {
  const id = crypto.randomUUID()
  mutate((next) => {
    const component = next.components.find((item) => item.id === selectedId.value)
    const event = component?.type === 'Table' ? 'RowClick' : 'Click'
    next.navigation.push({
      id,
      componentId: selectedId.value,
      event,
      target: { kind: 'Dashboard', resourceNId: '' },
      parameterMap: [],
      openMode: 'Current',
    })
    if (component && !component.events.includes(id)) component.events.push(id)
  })
  expandedNavigation.value = [id]
}
function useSample(key: string) {
  const found = samples.find((sample) => sample.key === key)
  if (found)
    void ElMessageBox.confirm(
      dashboardT.value('replaceDraftConfirm'),
      dashboardT.value('sampleDashboard'),
      {
        type: 'warning',
      },
    )
      .then(() => {
        setDocument(clone(found.document))
        selectedId.value = documentValue.value.components[0]?.id ?? ''
      })
      .catch(() => undefined)
}
async function save() {
  if (!resource.value) return
  busy.value = true
  error.value = ''
  try {
    const result = await getStudioApi().save(resource.value, documentValue.value)
    resource.value = result.resource
    documentValue.value = ensureDocument(result.draftDocument)
    saved.value = JSON.stringify(documentValue.value)
    history.reset(documentValue.value)
    notice.value = dashboardT.value('draftSaved')
  } catch (e) {
    error.value = e instanceof Error ? e.message : t.value('error')
  } finally {
    busy.value = false
  }
}
function fileError(e: unknown): string {
  return e instanceof DashboardDesignFileError
    ? dashboardT.value(e.reason)
    : e instanceof Error
      ? e.message
      : t.value('error')
}
function openImport() {
  importReadVersion++
  importedDesign.value = null
  importError.value = ''
  drawer.value = 'import'
}
async function readDesignFile(event: Event) {
  const version = ++importReadVersion
  importedDesign.value = null
  importError.value = ''
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  if (!file) return
  try {
    if (file.size > MAX_DESIGN_FILE_BYTES) throw new DashboardDesignFileError('designFileSize')
    const parsed = parseDashboardDesign(await file.text())
    if (version === importReadVersion) importedDesign.value = parsed
  } catch (e) {
    if (version === importReadVersion) importError.value = fileError(e)
  } finally {
    input.value = ''
  }
}
async function applyImportedDesign() {
  if (busy.value || !resource.value || !importedDesign.value) return
  busy.value = true
  importError.value = ''
  try {
    const next = importedDesign.value.document
    const result = await getStudioApi().validate(resource.value.nId, next)
    if (!result.valid) {
      importError.value = (result.diagnostics as Json[])
        .map((item) => `${item.path ?? ''}: ${item.message ?? item.code}`)
        .join('；')
      return
    }
    setDocument(clone(next))
    selectedId.value = next.components[0]?.id ?? ''
    preview.value = null
    previewNavigation.value = null
    boundsWarning.value = ''
    await loadInstancePaths()
    drawer.value = ''
    notice.value = dashboardT.value('designFileImported')
  } catch (e) {
    importError.value = fileError(e)
  } finally {
    busy.value = false
  }
}
async function exportDesign() {
  if (busy.value || !resource.value) return
  busy.value = true
  error.value = ''
  try {
    await getStudioApi().draft(resource.value.nId)
    downloadDashboardDesign(resource.value.name, documentValue.value)
    notice.value = dashboardT.value('designFileExported')
  } catch (e) {
    error.value = fileError(e)
  } finally {
    busy.value = false
  }
}
async function validate() {
  if (!resource.value) return
  busy.value = true
  error.value = ''
  try {
    checkBounds()
    if (boundsWarning.value) throw new Error(boundsWarning.value)
    const result = await getStudioApi().validate(resource.value.nId, documentValue.value)
    if (!result.valid)
      throw new Error(
        (result.diagnostics as Json[])
          .map((item) => String(item.message ?? item.code ?? ''))
          .join('；') || dashboardT.value('validationFailed'),
      )
    notice.value = dashboardT.value('validationPassed')
    return result
  } catch (e) {
    error.value = e instanceof Error ? e.message : t.value('error')
  } finally {
    busy.value = false
  }
}
async function publish() {
  if (!resource.value) return
  const result = await validate()
  if (!result) return
  drawer.value = 'publish'
}
async function confirmPublish() {
  if (!resource.value || !releaseNote.value.trim()) return
  busy.value = true
  try {
    if (saved.value !== JSON.stringify(documentValue.value)) {
      const updated = await getStudioApi().save(resource.value, documentValue.value)
      resource.value = updated.resource
      documentValue.value = ensureDocument(updated.draftDocument)
      saved.value = JSON.stringify(documentValue.value)
    }
    const result = await getStudioApi().validate(resource.value.nId, documentValue.value)
    if (!result.valid) throw new Error(dashboardT.value('validationNotPassed'))
    await getStudioApi().publish(resource.value, releaseNote.value, result.dependencyPlanHash)
    drawer.value = ''
    notice.value = dashboardT.value('dashboardPublished')
    await load()
  } catch (e) {
    error.value = e instanceof Error ? e.message : t.value('error')
  } finally {
    busy.value = false
  }
}
async function previewDashboard() {
  drawer.value = 'preview'
  for (const parameter of documentValue.value.parameters) {
    if (
      parameter.source !== 'TrustedContext' &&
      previewParameters.value[String(parameter.name)] === undefined
    )
      previewParameters.value[String(parameter.name)] = parameter.defaultValue ?? ''
  }
  if (
    documentValue.value.parameters.some(
      (parameter) =>
        parameter.required &&
        parameter.source !== 'TrustedContext' &&
        String(previewParameters.value[String(parameter.name)] ?? '').trim() === '',
    )
  ) {
    error.value = dashboardT.value('requiredParameterBeforeRun')
    return
  }
  busy.value = true
  error.value = ''
  previewNavigation.value = null
  const current = ++dataVersion
  clearTimeout(dataTimer)
  dataLoading.value = false
  try {
    const result = await getStudioApi().dashboardPreview(
      clone(documentValue.value),
      clone(previewParameters.value),
      selectedId.value,
      undefined,
      resource.value?.nId,
    )
    if (current === dataVersion) {
      preview.value = result
      canvasData.value = result
      dataError.value = ''
    }
  } catch (e) {
    error.value = e instanceof Error ? e.message : t.value('error')
  } finally {
    busy.value = false
  }
}
async function onNavigate(component: DashboardComponent, row?: Json) {
  const rule = documentValue.value.navigation.find(
    (item) => item.componentId === component.id && item.event === (row ? 'RowClick' : 'Click'),
  )
  if (!rule || !resource.value) return
  let key: unknown = row
  for (const segment of (component.props.rowKeyPath as string[]) ?? ['nId'])
    key = key && typeof key === 'object' ? (key as Json)[segment] : undefined
  busy.value = true
  error.value = ''
  try {
    previewNavigation.value = await getStudioApi().dashboardPreviewNavigation(resource.value.nId, {
      document: documentValue.value,
      parameters: previewParameters.value,
      ruleId: rule.id,
      rowKey: row ? { key } : undefined,
    })
  } catch (e) {
    error.value = e instanceof Error ? e.message : t.value('error')
  } finally {
    busy.value = false
  }
}
onBeforeRouteLeave(async () => {
  if (!saved.value || saved.value === JSON.stringify(documentValue.value)) return true
  try {
    await ElMessageBox.confirm(t.value('unsavedLeave'), t.value('unsaved'))
    return true
  } catch {
    return false
  }
})
onMounted(() => {
  if (typeof ResizeObserver !== 'undefined')
    canvasObserver = new ResizeObserver(() => {
      if (autoFit.value) fitCanvas()
    })
  void load()
})
onBeforeUnmount(() => {
  clearTimeout(canvasRefreshTimer)
  loadVersion++
  dataVersion++
  clearTimeout(dataTimer)
  canvasObserver?.disconnect()
})
</script>

<template>
  <AppPage
    :title="resource?.name ?? dashboardT('dashboardDesign')"
    class="studio-page dashboard-designer"
  >
    <template #actions
      ><el-button @click="router.push('/pc/studio/dashboards')">{{ dashboardT('back') }}</el-button
      ><el-button :disabled="busy || !history.canUndo" @click="undo">{{
        dashboardT('undo')
      }}</el-button
      ><el-button :disabled="busy || !history.canRedo" @click="redo">{{
        dashboardT('redo')
      }}</el-button
      ><el-button :disabled="busy" @click="previewDashboard">{{ dashboardT('preview') }}</el-button
      ><el-button :disabled="busy" @click="openImport">{{ dashboardT('importDesign') }}</el-button
      ><el-button :disabled="busy" @click="exportDesign">{{ dashboardT('exportDesign') }}</el-button
      ><el-button :disabled="busy" @click="save">{{ dashboardT('save') }}</el-button
      ><el-button type="primary" :disabled="busy" @click="publish">{{
        dashboardT('publish')
      }}</el-button></template
    >
    <el-alert v-if="error" :title="error" type="error" :closable="false" /><el-alert
      v-if="notice"
      :title="notice"
      type="success"
      :closable="false"
    /><el-alert v-if="boundsWarning" :title="boundsWarning" type="warning" show-icon
      ><el-button size="small" @click="fixBounds">{{
        dashboardT('autoCorrect')
      }}</el-button></el-alert
    >
    <el-skeleton v-if="!resource && (busy || !error)" :rows="10" animated />
    <div v-if="resource" class="dashboard-designer-toolbar">
      <el-select
        :model-value="''"
        :placeholder="dashboardT('insertSample')"
        @change="(v: string) => useSample(v)"
        ><el-option
          v-for="sample in samples"
          :key="sample.key"
          :label="sample.label"
          :value="sample.key" /></el-select
      ><span>{{ dashboardT('canvas') }}</span
      ><el-input-number
        :model-value="documentValue.canvas.width"
        :min="320"
        :max="16384"
        @update:model-value="(v: number | undefined) => setCanvasNumber('width', v ?? 320)"
      /><span>×</span
      ><el-input-number
        :model-value="documentValue.canvas.height"
        :min="320"
        :max="16384"
        @update:model-value="(v: number | undefined) => setCanvasNumber('height', v ?? 320)"
      /><el-select
        v-model="documentValue.canvas.fit"
        style="width: 130px"
        @change="mutate(() => undefined)"
        ><el-option label="Contain" value="Contain" /><el-option
          label="Cover"
          value="Cover" /><el-option label="Actual" value="Actual" /></el-select
      ><el-slider
        v-model="zoom"
        :min="0.2"
        :max="2"
        :step="0.01"
        style="width: 160px"
        @input="autoFit = false"
      />
      <el-button @click="enableAutoFit">{{ dashboardT('fitCanvas') }}</el-button>
      <el-button :aria-label="dashboardT('actualSize')" @click="showActualSize">100%</el-button>
      <el-button
        :disabled="dataLoading || !documentValue.instances.length"
        :loading="dataLoading"
        @click="refreshCanvasData"
        >{{ dashboardT('refreshCanvasData') }}</el-button
      >
      <el-button @click="openDataSources">{{ dashboardT('pageDataSources') }}</el-button>
      <el-button @click="parametersVisible = true">{{ dashboardT('pageParameters') }}</el-button>
      <el-button @click="leftCollapsed = !leftCollapsed">{{
        leftCollapsed ? dashboardT('showComponents') : dashboardT('hideComponents')
      }}</el-button
      ><el-button @click="rightCollapsed = !rightCollapsed">{{
        rightCollapsed ? dashboardT('showProperties') : dashboardT('hideProperties')
      }}</el-button>
    </div>
    <div
      v-if="resource"
      class="dashboard-designer-grid"
      :class="{ 'left-collapsed': leftCollapsed, 'right-collapsed': rightCollapsed }"
    >
      <aside v-if="!leftCollapsed" class="dashboard-side-panel dashboard-component-panel">
        <div class="dashboard-library-heading">
          <h3>{{ dashboardT('componentRegistry') }}</h3>
          <span>{{ dashboardT('componentCount', { count: registeredComponents.length }) }}</span>
        </div>
        <el-skeleton v-if="catalogLoading && !registry.length" :rows="4" animated />
        <el-input
          v-model="componentSearch"
          clearable
          :placeholder="dashboardT('searchComponents')"
          :aria-label="dashboardT('searchComponents')"
        />
        <div
          class="dashboard-library-categories"
          role="group"
          :aria-label="dashboardT('componentCategories')"
        >
          <button
            v-for="category in componentCategories"
            :key="category"
            type="button"
            :aria-pressed="componentCategory === category"
            @click="selectComponentCategory(category)"
          >
            {{ dashboardT.categoryLabel(category) }}
          </button>
        </div>
        <div class="dashboard-library-content">
          <div class="dashboard-registry-list">
            <button
              v-for="entry in dashboardEntries"
              :key="entry.key"
              type="button"
              :title="`${dashboardT.componentLabel(entry.key)} · ${entry.key}`"
              draggable="true"
              @dragstart="startPaletteDrag($event, 'dashboard', entry.key)"
              @keydown.enter.prevent="addComponent(entry)"
              @keydown.space.prevent="addComponent(entry)"
            >
              <component :is="componentIcons[entry.key] ?? Box" aria-hidden="true" />
              <span>{{ dashboardT.componentLabel(entry.key) }}</span>
            </button>
          </div>
          <p v-if="!dashboardEntries.length && !catalogLoading" class="dashboard-library-hint">
            {{ dashboardT('empty') }}
          </p>
          <p class="dashboard-library-hint">{{ dashboardT('componentLibraryHint') }}</p>
        </div>
      </aside>
      <section class="dashboard-editor-workspace">
        <el-alert v-if="dataError" :title="dataError" type="warning" :closable="false" />
        <div ref="canvasHost" class="dashboard-canvas-scroll">
          <div
            class="dashboard-editor-canvas"
            @dragover="allowPaletteDrop($event)"
            @drop.prevent="dropComponent"
            :style="canvasStyle"
            @pointermove="pointerMove"
            @pointerup="pointerUp"
            @pointercancel="pointerUp"
          >
            <DashboardRuntimeView
              :runtime="canvasRuntime"
              :parameters="canvasParameters"
              :selected-component-id="selectedId"
              editing
              :editor-scale="zoom"
              class="dashboard-editor-runtime"
              @select="selectedId = $event"
              @edit-pointer-down="pointerDown"
            />
          </div>
        </div>
        <div class="dashboard-layer-list">
          <span>{{ dashboardT('layer') }}</span
          ><button
            v-for="component in documentValue.components
              .slice()
              .sort((a, b) => b.zIndex - a.zIndex)"
            :key="component.id"
            :class="{ selected: component.id === selectedId }"
            @click="selectedId = component.id"
          >
            {{ component.name || component.type }}
          </button>
        </div>
      </section>
      <aside v-if="!rightCollapsed" class="dashboard-side-panel dashboard-properties">
        <template v-if="selected"
          ><div class="dashboard-properties-heading">
            <h3>{{ dashboardT('property') }}</h3>
            <el-button link type="danger" @click="removeSelected">{{
              dashboardT('delete')
            }}</el-button>
          </div>
          <div
            class="dashboard-property-categories dashboard-library-categories"
            role="group"
            :aria-label="dashboardT('propertyCategories')"
          >
            <button
              v-for="section in propertySections"
              :key="section"
              type="button"
              :aria-pressed="propertySection === section"
              @click="propertySection = section"
            >
              {{ dashboardT(`propertySection.${section}`) }}
            </button>
          </div>
          <div ref="propertyContent" class="dashboard-property-content">
            <el-form v-if="propertySection === 'basic'" label-position="top"
              ><el-form-item :label="dashboardT('modelName')"
                ><el-input
                  v-model="selected.name"
                  @change="mutate(() => undefined)" /></el-form-item
              ><el-form-item :label="dashboardT('positionSize')"
                ><div class="dashboard-number-grid">
                  <el-input-number
                    v-model="selected.x"
                    :min="-16384"
                    :max="16384"
                    @change="mutate(() => undefined)"
                  /><el-input-number
                    v-model="selected.y"
                    :min="-16384"
                    :max="16384"
                    @change="mutate(() => undefined)"
                  /><el-input-number
                    v-model="selected.width"
                    :min="1"
                    :max="16384"
                    @change="mutate(() => undefined)"
                  /><el-input-number
                    v-model="selected.height"
                    :min="1"
                    :max="16384"
                    @change="mutate(() => undefined)"
                  /></div></el-form-item
              ><el-form-item :label="dashboardT('rotation')"
                ><el-slider
                  v-model="selected.rotation"
                  :min="-180"
                  :max="180"
                  @change="mutate(() => undefined)" /></el-form-item
              ><el-form-item :label="dashboardT('layer')"
                ><el-input-number
                  v-model="selected.zIndex"
                  :min="0"
                  :max="9999"
                  @change="mutate(() => undefined)" /></el-form-item
              ><el-form-item
                ><el-switch
                  v-model="selected.locked"
                  :active-text="dashboardT('lock')"
                  @change="mutate(() => undefined)" /><el-switch
                  v-model="selected.visible"
                  :active-text="dashboardT('visible')"
                  @change="mutate(() => undefined)" /></el-form-item
              ><el-form-item :label="dashboardT('alignment')"
                ><div class="dashboard-align-buttons">
                  <el-button size="small" @click="align('left')">{{ dashboardT('left') }}</el-button
                  ><el-button size="small" @click="align('center')">{{
                    dashboardT('middle')
                  }}</el-button
                  ><el-button size="small" @click="align('right')">{{
                    dashboardT('right')
                  }}</el-button
                  ><el-button size="small" @click="align('top')">{{ dashboardT('top') }}</el-button
                  ><el-button size="small" @click="align('middle')">{{
                    dashboardT('middle')
                  }}</el-button
                  ><el-button size="small" @click="align('bottom')">{{
                    dashboardT('bottom')
                  }}</el-button>
                </div></el-form-item
              ></el-form
            >
            <div
              v-if="propertySection === 'data'"
              class="dashboard-binding-categories dashboard-library-categories"
              role="group"
              :aria-label="dashboardT('bindingCategories')"
            >
              <button
                v-for="section in ['fields', 'source']"
                :key="section"
                type="button"
                :aria-pressed="dataBindingSection === section"
                @click="dataBindingSection = section"
              >
                {{ dashboardT(section === 'fields' ? 'bindingFields' : 'dataSources') }}
              </button>
            </div>
            <p v-if="propertySection === 'data'" class="dashboard-library-hint">
              {{
                dashboardT(
                  dataBindingSection === 'fields' ? 'bindingFieldsHint' : 'dataSourcesHint',
                )
              }}
            </p>
            <el-form v-if="propFields.length" label-position="top">
              <el-form-item
                v-for="field in propFields"
                :key="field.key"
                :label="
                  dashboardT.propertyLabel(
                    field.key === 'pageSeconds' && selected.props.scrollMode === 'Continuous'
                      ? 'scrollPageSeconds'
                      : field.key,
                  )
                "
                ><DashboardAppearanceEditor
                  v-if="field.key === 'appearance'"
                  :key="selectedId"
                  :model-value="selected.props.appearance"
                  @update:model-value="(value) => updateProp('appearance', value, field.schema)"
                />
                <DashboardStructuredEditor
                  v-else-if="structuredKinds.includes(field.key)"
                  :key="`${selectedId}/${field.key}`"
                  :kind="field.key"
                  :component-type="selected.type"
                  :model-value="propValue(field.key)"
                  :columns="selected.props.columns ?? []"
                  :components="documentValue.components.filter((item) => item.id !== selectedId)"
                  :field-options="
                    fieldOptions({ from: 'Instance', instanceId: selected.props.instanceId })
                  "
                  @update:model-value="(value) => updateProp(field.key, value, field.schema)"
                />
                <el-select
                  v-else-if="propertyOptions(field.key, field.schema).length"
                  :model-value="propValue(field.key)"
                  @update:model-value="
                    (value: unknown) => updateProp(field.key, value, field.schema)
                  "
                  ><el-option
                    v-for="option in propertyOptions(field.key, field.schema)"
                    :key="String(option)"
                    :label="dashboardT.optionLabel(option)"
                    :value="option" /></el-select
                ><el-input-number
                  v-else-if="field.schema.type === 'number'"
                  :model-value="propValue(field.key) == null ? undefined : propNumber(field.key)"
                  :min="schemaNumber(field.schema, 'minimum')"
                  :max="schemaNumber(field.schema, 'maximum')"
                  @update:model-value="
                    (value: number | undefined) => updateProp(field.key, value, field.schema)
                  "
                /><el-switch
                  v-else-if="field.schema.type === 'boolean'"
                  :model-value="Boolean(propValue(field.key))"
                  @update:model-value="
                    (value: boolean) => updateProp(field.key, value, field.schema)
                  "
                /><el-input
                  v-else-if="field.schema.type === 'string'"
                  :model-value="propText(field.key)"
                  @update:model-value="
                    (value: string) => updateProp(field.key, value, field.schema)
                  "
                /><el-input
                  v-else-if="field.key.endsWith('Path') || field.key === 'rowKeyPath'"
                  :model-value="propPathText(field.key)"
                  :placeholder="dashboardT('fieldPathWithExample')"
                  @update:model-value="(value: string) => updatePropPath(field.key, value)"
                /><el-input
                  v-else
                  type="textarea"
                  :rows="3"
                  :model-value="propJson(field.key)"
                  @change="(value: string) => updateProp(field.key, value, field.schema)"
                />
                <small
                  v-if="field.key === 'pageSeconds' && selected.props.scrollMode === 'Continuous'"
                >
                  {{ dashboardT.propertyLabel('continuousScrollHint') }}
                </small></el-form-item
              ></el-form
            >
            <p
              v-if="
                ['content', 'appearance'].includes(propertySection) &&
                !propFields.length &&
                !catalogLoading
              "
              class="dashboard-library-hint"
            >
              {{ dashboardT('noSectionProperties') }}
            </p>
            <section v-if="propertySection === 'data'">
              <div class="dashboard-properties-heading">
                <h3>{{ dashboardT('bindings') }}</h3>
                <el-button link @click="addBinding">{{ dashboardT('addBinding') }}</el-button>
              </div>
              <p v-if="!selected.bindings.length" class="dashboard-library-hint">
                {{ dashboardT('noBindings') }}
              </p>
              <el-collapse v-model="expandedBindings">
                <el-collapse-item
                  v-for="(binding, index) in selected.bindings"
                  :key="index"
                  :name="index"
                  :title="
                    binding.property
                      ? dashboardT.propertyLabel(binding.property)
                      : dashboardT('property')
                  "
                >
                  <div class="dashboard-binding-row">
                    <template v-if="dataBindingSection === 'fields'">
                      <label class="dashboard-binding-label">{{
                        dashboardT('bindingFields')
                      }}</label>
                      <el-select
                        v-model="binding.property"
                        :placeholder="dashboardT('bindingFields')"
                        :aria-label="dashboardT('bindingFields')"
                        @change="mutate(() => undefined)"
                        ><el-option
                          v-for="property in bindableProperties"
                          :key="property"
                          :label="dashboardT.propertyLabel(property)"
                          :value="property"
                      /></el-select>
                      <div class="dashboard-binding-source-summary">
                        <small
                          >{{ dashboardT('dataSources') }}：{{
                            bindingSourceLabel(binding.source)
                          }}</small
                        >
                        <el-button link size="small" @click="configureBindingSource(index)">{{
                          dashboardT('configureBindingSource')
                        }}</el-button>
                      </div>
                    </template>
                    <template v-else>
                      <label class="dashboard-binding-label">{{ dashboardT('sourceType') }}</label>
                      <el-select
                        :model-value="bindingType(binding.source)"
                        @update:model-value="setBindingType(binding.source, String($event))"
                        :placeholder="dashboardT('sourceType')"
                        :aria-label="dashboardT('sourceType')"
                        @change="mutate(() => undefined)"
                        ><el-option :label="dashboardT('constant')" value="Constant" /><el-option
                          :label="dashboardT('sharedPageSource')"
                          value="Instance" /><el-option
                          :label="dashboardT('pageParameter')"
                          value="PageParam" /><el-option
                          :label="dashboardT('componentApi')"
                          value="Api" /></el-select
                      ><el-select
                        v-if="bindingType(binding.source) === 'Instance'"
                        v-model="binding.source.instanceId"
                        filterable
                        :placeholder="dashboardT('instance')"
                        :aria-label="dashboardT('dataInstanceTitle')"
                        @change="mutate(() => undefined)"
                        ><el-option
                          v-for="item in documentValue.instances"
                          :key="item.instanceId"
                          :value="item.instanceId"
                          :label="item.label || item.instanceId"
                      /></el-select>
                      <DashboardDataSourceEditor
                        v-if="
                          bindingType(binding.source) === 'Api' && bindingInstance(binding.source)
                        "
                        :model-value="bindingInstance(binding.source)!"
                        :datasets="availableResources('Dataset')"
                        :contracts="contracts"
                        :instances="documentValue.instances"
                        :page-parameters="documentValue.parameters"
                        api-only
                        :can-manage-datasets="auth.hasPermission('studio.dataset.view')"
                        @manage-datasets="router.push('/pc/studio/datasets')"
                        @update:model-value="updateSource"
                        @dataset-change="loadContract($event.datasetNId, $event.revisionNId)"
                      />
                      <el-select
                        v-if="binding.source.from !== 'Constant'"
                        :model-value="pathText(binding.source)"
                        filterable
                        allow-create
                        :placeholder="dashboardT('fieldPath')"
                        @update:model-value="
                          (value: string) => setBindingPath(binding.source, value)
                        "
                        ><el-option
                          v-for="path in pathOptions(binding.source)"
                          :key="path"
                          :label="path"
                          :value="path" /></el-select
                      ><DashboardStructuredEditor
                        v-else-if="
                          binding.property === 'rows' && Array.isArray(binding.source.value)
                        "
                        :key="`${selectedId}/binding/${index}`"
                        kind="rows"
                        :model-value="binding.source.value"
                        :columns="selected.props.columns ?? []"
                        @update:model-value="
                          (value) => {
                            binding.source.value = value
                            mutate(() => undefined)
                          }
                        "
                      />
                      <DashboardConstantEditor
                        v-else
                        :model-value="binding.source.value"
                        @update:model-value="
                          ($event) => {
                            binding.source.value = $event
                            mutate(() => undefined)
                          }
                        "
                      />
                    </template>
                  </div>
                </el-collapse-item>
              </el-collapse>
            </section>
            <section v-if="propertySection === 'interaction'">
              <div class="dashboard-properties-heading">
                <h3>{{ dashboardT('navigation') }}</h3>
                <el-button link @click="addNavigation">{{ dashboardT('addRule') }}</el-button>
              </div>
              <p v-if="!documentValue.navigation.length" class="dashboard-library-hint">
                {{ dashboardT('noNavigation') }}
              </p>
              <el-collapse v-model="expandedNavigation">
                <el-collapse-item
                  v-for="(rule, index) in documentValue.navigation"
                  :key="rule.id"
                  :name="rule.id"
                  :title="`${index + 1} · ${documentValue.components.find((component) => component.id === rule.componentId)?.name || dashboardT('navigation')}`"
                >
                  <div class="dashboard-mini-form">
                    <el-select
                      v-model="rule.componentId"
                      :placeholder="dashboardT('triggerComponent')"
                      @change="mutate(() => undefined)"
                      ><el-option
                        v-for="component in documentValue.components"
                        :key="component.id"
                        :label="component.name || component.type"
                        :value="component.id" /></el-select
                    ><el-select v-model="rule.event" @change="mutate(() => undefined)"
                      ><el-option
                        :label="dashboardT.optionLabel('Click')"
                        value="Click" /><el-option
                        :label="dashboardT.optionLabel('RowClick')"
                        value="RowClick" /></el-select
                    ><el-select v-model="rule.target.kind" @change="targetChanged(rule, true)"
                      ><el-option
                        :label="dashboardT.optionLabel('Dashboard')"
                        value="Dashboard" /><el-option
                        :label="dashboardT.optionLabel('LowCodePage')"
                        value="LowCodePage" /><el-option
                        :label="dashboardT.optionLabel('Report')"
                        value="Report" /><el-option
                        :label="dashboardT.optionLabel('BusinessPage')"
                        value="BusinessPage" /></el-select
                    ><el-select
                      v-model="rule.target.resourceNId"
                      filterable
                      :placeholder="dashboardT('targetResourceNId')"
                      @change="targetChanged(rule)"
                      ><el-option
                        v-for="item in availableResources(rule.target.kind)"
                        :key="item.nId"
                        :value="item.nId"
                        :label="`${item.name} · ${item.nId}`" /></el-select
                    ><el-input
                      v-model="rule.target.revisionNId"
                      :placeholder="dashboardT('fixedTargetRevisionNId')"
                      @change="loadContract(rule.target.resourceNId, rule.target.revisionNId)"
                    /><el-select
                      v-model="rule.openMode"
                      :placeholder="dashboardT('openMode')"
                      @change="mutate(() => undefined)"
                      ><el-option :label="dashboardT('currentPage')" value="Current" /><el-option
                        :label="dashboardT('modal')"
                        value="Modal"
                    /></el-select>
                    <small
                      v-for="parameter in parametersOf(
                        rule.target.resourceNId,
                        rule.target.revisionNId,
                      )"
                      :key="parameter.name"
                      class="dashboard-parameter-contract"
                    >
                      {{ parameter.label || parameter.name }} · {{ parameter.name }} ·
                      {{ dashboardT.optionLabel(parameter.type)
                      }}{{ parameter.required ? ' *' : '' }}
                    </small>
                    <div class="dashboard-properties-heading">
                      <small>{{ dashboardT('targetParameterMapping') }}</small
                      ><el-button link size="small" @click="addNavigationParameter(rule)">{{
                        dashboardT('addBinding')
                      }}</el-button>
                    </div>
                    <div
                      v-for="mapping in (rule.parameterMap as Json[]) ?? []"
                      :key="String(mapping.target)"
                      class="dashboard-binding-row"
                    >
                      <el-select
                        v-model="mapping.target"
                        filterable
                        :placeholder="dashboardT('targetParameter')"
                        ><el-option
                          v-for="parameter in parametersOf(
                            rule.target.resourceNId,
                            rule.target.revisionNId,
                          )"
                          :key="parameter.name"
                          :value="parameter.name"
                          :label="`${parameter.name} · ${dashboardT.optionLabel(parameter.type)}${parameter.required ? ' *' : ''}`" /></el-select
                      ><el-select v-model="mapping.source.from" :placeholder="dashboardT('source')"
                        ><el-option :label="dashboardT('constant')" value="Constant" /><el-option
                          :label="dashboardT('pageParameter')"
                          value="PageParam" /><el-option
                          :label="dashboardT('currentRow')"
                          value="Row" /></el-select
                      ><el-select
                        v-if="mapping.source.from !== 'Constant'"
                        :model-value="pathText(mapping.source)"
                        filterable
                        allow-create
                        :placeholder="dashboardT('fieldPath')"
                        @update:model-value="
                          (value: string) => setBindingPath(mapping.source, value)
                        "
                        ><el-option
                          v-for="path in fieldOptions(mapping.source, rule)"
                          :key="path"
                          :label="path"
                          :value="path" /></el-select
                      ><el-input
                        v-else
                        :model-value="constantText(mapping.source)"
                        :type="typeof mapping.source.value === 'object' ? 'textarea' : 'text'"
                        @change="(value: string) => setConstant(mapping.source, value)"
                        :placeholder="dashboardT('constantValue')"
                      />
                    </div>
                  </div>
                </el-collapse-item>
              </el-collapse>
            </section></div></template
        ><el-empty v-else :description="dashboardT('noComponentSelected')" />
      </aside>
    </div>
    <AppFormDrawer
      :model-value="parametersVisible"
      :title="dashboardT('pageParameters')"
      size="wide"
      hide-footer
      @update:model-value="parametersVisible = $event"
    >
      <p class="dashboard-library-hint">{{ dashboardT('pageParametersHint') }}</p>
      <div class="dashboard-page-parameters">
        <el-button size="small" @click="addParameter">{{ dashboardT('addParameter') }}</el-button>
        <div
          v-for="parameter in documentValue.parameters"
          :key="String(parameter.name)"
          class="dashboard-mini-form"
        >
          <el-input v-model="parameter.name" :placeholder="dashboardT('parameterName')" />
          <el-select v-model="parameter.type" :placeholder="dashboardT('parameterType')">
            <el-option
              v-for="kind in [
                'String',
                'Int32',
                'Int64',
                'Decimal',
                'Boolean',
                'Date',
                'DateTime',
                'Guid',
              ]"
              :key="kind"
              :value="kind"
              :label="dashboardT.optionLabel(kind)"
            />
          </el-select>
          <el-switch v-model="parameter.required" :active-text="dashboardT('parameterRequired')" />
          <el-switch v-if="parameter.type === 'Boolean'" v-model="parameter.defaultValue" />
          <el-input-number
            v-else-if="['Int32', 'Decimal'].includes(parameter.type)"
            v-model="parameter.defaultValue"
          />
          <el-input
            v-else
            v-model="parameter.defaultValue"
            :placeholder="dashboardT('defaultValue')"
          />
        </div>
      </div>
    </AppFormDrawer>
    <AppFormDrawer
      :model-value="sourcesVisible"
      :title="dashboardT('pageDataSources')"
      size="wide"
      hide-footer
      @update:model-value="sourcesVisible = $event"
    >
      <div class="dashboard-mini-form">
        <el-select v-model="activeSourceId" filterable :aria-label="dashboardT('pageDataSources')"
          ><el-option
            v-for="item in documentValue.instances"
            :key="item.instanceId"
            :value="item.instanceId"
            :label="item.label || item.instanceId"
        /></el-select>
        <el-button
          @click="
            () => {
              addInstance()
              activeSourceId = documentValue.instances.at(-1)?.instanceId || ''
            }
          "
          >{{ dashboardT('addInstance') }}</el-button
        >
        <el-button @click="applyDataExample">{{ dashboardT('applyDataExample') }}</el-button>
        <template v-if="activeSource">
          <small>{{ dashboardT('sourceUsage') }}：{{ sourceUsage(activeSource.instanceId) }}</small
          ><small>{{ dashboardT('instanceId') }}：{{ activeSource.instanceId }}</small>
          <DashboardDataSourceEditor
            :key="activeSource.instanceId"
            :model-value="activeSource"
            :datasets="availableResources('Dataset')"
            :contracts="contracts"
            :instances="documentValue.instances"
            :page-parameters="documentValue.parameters"
            :can-manage-datasets="auth.hasPermission('studio.dataset.view')"
            @manage-datasets="router.push('/pc/studio/datasets')"
            @update:model-value="updateSource"
            @dataset-change="loadContract($event.datasetNId, $event.revisionNId)"
          />
        </template>
      </div>
    </AppFormDrawer>
    <AppFormDrawer
      :model-value="drawer === 'import'"
      :title="dashboardT('importDesign')"
      :busy="busy"
      @update:model-value="(value: boolean) => !value && (drawer = '')"
      @submit="applyImportedDesign"
    >
      <el-alert :title="dashboardT('designFileHint')" type="info" :closable="false" />
      <label class="dashboard-design-file">
        {{ dashboardT('designFileSelect') }}
        <input
          type="file"
          accept=".json,application/json"
          :disabled="busy"
          data-testid="dashboard-design-file"
          @change="readDesignFile"
        />
      </label>
      <el-alert v-if="importError" :title="importError" type="error" :closable="false" />
      <p v-if="importedDesign">
        {{
          dashboardT('designFileSummary', {
            name: importedDesign.name || resource?.name || '',
            count: importedDesign.document.components.length,
            width: importedDesign.document.canvas.width,
            height: importedDesign.document.canvas.height,
          })
        }}
      </p>
      <template #footer>
        <el-button :disabled="busy" @click="drawer = ''">{{ dashboardT('cancel') }}</el-button>
        <el-button
          type="primary"
          :loading="busy"
          :disabled="!importedDesign"
          @click="applyImportedDesign"
          >{{ dashboardT('designFileApply') }}</el-button
        >
      </template>
    </AppFormDrawer>
    <AppFormDrawer
      :model-value="drawer === 'preview'"
      :title="dashboardT('previewTitle')"
      size="wide"
      :hide-footer="true"
      @update:model-value="(v: boolean) => !v && (drawer = '')"
    >
      <el-alert v-if="error" :title="error" type="error" :closable="false" />
      <el-form inline>
        <el-form-item
          v-for="parameter in documentValue.parameters.filter(
            (item) => item.source !== 'TrustedContext',
          )"
          :key="String(parameter.name)"
          :label="String(parameter.label ?? parameter.name)"
          :required="parameter.required"
        >
          <el-input v-model="previewParameters[String(parameter.name)]" />
        </el-form-item>
        <el-button :loading="busy" @click="previewDashboard">{{ dashboardT('refresh') }}</el-button>
      </el-form>
      <DashboardRuntimeView
        v-if="preview"
        :runtime="preview"
        :parameters="previewParameters"
        :selected-component-id="selectedId"
        @navigate="onNavigate"
      />
      <section v-if="previewNavigation" class="dashboard-preview-navigation">
        <h3>
          {{
            dashboardT('previewNavigation', { target: String(previewNavigation.targetResourceNId) })
          }}
        </h3>
        <el-descriptions :column="1" border>
          <el-descriptions-item :label="dashboardT('currentRow')">
            <pre>{{ JSON.stringify(previewNavigation.source?.row ?? {}, null, 2) }}</pre>
          </el-descriptions-item>
          <el-descriptions-item :label="dashboardT('targetParameterMapping')">
            <pre>{{ JSON.stringify(previewNavigation.parameters, null, 2) }}</pre>
          </el-descriptions-item>
          <el-descriptions-item :label="dashboardT('instanceParameterMapping')">
            <pre>{{ JSON.stringify(previewNavigation.targetQueryParameters, null, 2) }}</pre>
          </el-descriptions-item>
        </el-descriptions>
        <DashboardRuntimeView
          v-if="previewNavigation.targetRuntime"
          :runtime="previewNavigation.targetRuntime"
          :parameters="previewNavigation.parameters"
        />
      </section> </AppFormDrawer
    ><AppFormDrawer
      :model-value="drawer === 'publish'"
      :title="dashboardT('publishDashboard')"
      :busy="busy"
      @update:model-value="(v: boolean) => !v && (drawer = '')"
      @submit="confirmPublish"
      ><el-form label-position="top"
        ><el-form-item :label="dashboardT('publishNote')" required
          ><el-input v-model="releaseNote" type="textarea" /></el-form-item></el-form
    ></AppFormDrawer>
  </AppPage>
</template>

<style scoped>
.dashboard-design-file {
  display: grid;
  gap: 12px;
  margin: 20px 0;
}
.dashboard-designer-toolbar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
  padding: 10px;
  margin-bottom: 10px;
  border: 1px solid var(--el-border-color);
  background: var(--el-bg-color);
}
.dashboard-designer-grid {
  display: grid;
  grid-template-columns: 250px minmax(420px, 1fr) 300px;
  height: clamp(560px, calc(100dvh - 280px), 1050px);
  min-height: 0;
  gap: 10px;
}
.dashboard-designer-grid.left-collapsed {
  grid-template-columns: 0 minmax(420px, 1fr) 300px;
}
.dashboard-designer-grid.right-collapsed {
  grid-template-columns: 250px minmax(420px, 1fr) 0;
}
.dashboard-designer-grid.left-collapsed.right-collapsed {
  grid-template-columns: 0 minmax(420px, 1fr) 0;
}
.dashboard-side-panel {
  min-height: 0;
  min-width: 0;
  grid-column: 1;
  padding: 12px;
  overflow: auto;
  border: 1px solid var(--el-border-color);
  background: var(--el-bg-color);
}
.dashboard-properties {
  grid-column: 3;
  display: flex;
  flex-direction: column;
  gap: 10px;
  overflow: hidden;
}
.dashboard-properties > .dashboard-properties-heading h3 {
  margin: 0;
}
.dashboard-property-content {
  flex: 1;
  min-height: 0;
  overflow: auto;
  scrollbar-width: thin;
  scrollbar-gutter: stable;
}
.dashboard-property-content :deep(.el-collapse-item__title) {
  min-width: 0;
  overflow-wrap: anywhere;
}
.dashboard-side-panel h3 {
  margin: 4px 0 10px;
}
.dashboard-component-panel {
  display: flex;
  flex-direction: column;
  gap: 10px;
  overflow: hidden;
}
.dashboard-library-heading {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 8px;
}
.dashboard-library-heading h3 {
  margin: 0;
}
.dashboard-library-heading > span {
  flex-shrink: 0;
  padding: 3px 6px;
  border-radius: 4px;
  font-size: 12px;
  color: var(--el-color-primary);
  background: var(--el-color-primary-light-9);
}
.dashboard-library-categories {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}
.dashboard-library-categories button {
  flex: 1 0 auto;
  padding: 8px;
  border: 1px solid var(--el-border-color);
  border-radius: 5px;
  color: var(--el-text-color-primary);
  background: var(--el-bg-color);
  cursor: pointer;
}
.dashboard-library-categories button[aria-pressed='true'] {
  border-color: var(--el-color-primary);
  background: var(--el-color-primary-light-9);
  color: var(--el-color-primary);
}
.dashboard-library-content {
  flex: 1;
  min-height: 0;
  overflow: auto;
  scrollbar-width: thin;
  scrollbar-gutter: stable;
}
.dashboard-registry-list {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 8px;
}
.dashboard-registry-list button {
  display: flex;
  flex-direction: column;
  justify-content: center;
  min-width: 0;
  min-height: 72px;
  gap: 5px;
  text-align: left;
  padding: 8px;
  border: 1px solid var(--el-border-color);
  border-radius: 5px;
  font: inherit;
  font-size: 13px;
  overflow-wrap: anywhere;
  color: var(--el-text-color-primary);
  background: var(--el-fill-color-light);
  cursor: pointer;
}
.dashboard-registry-list button:hover,
.dashboard-library-categories button:hover {
  border-color: var(--el-color-primary);
  color: var(--el-color-primary);
}
.dashboard-registry-list button:focus-visible,
.dashboard-library-categories button:focus-visible {
  outline: 2px solid var(--el-color-primary);
  outline-offset: 1px;
}
.dashboard-registry-list svg {
  width: 20px;
  height: 20px;
  flex-shrink: 0;
}
.dashboard-library-hint {
  margin: 12px 0;
  font-size: 12px;
  line-height: 1.7;
  color: var(--el-text-color-secondary);
}
.dashboard-mini-form {
  display: grid;
  gap: 5px;
  padding: 8px 0;
}
.dashboard-editor-workspace {
  grid-column: 2;
  min-height: 0;
  overflow: hidden;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.dashboard-canvas-scroll {
  flex: 1;
  overflow: auto;
  min-height: 0;
  border: 1px solid var(--el-border-color);
  background: #162435;
  padding: 20px;
}
.dashboard-editor-canvas {
  position: relative;
  transform-origin: top left;
  background-image:
    linear-gradient(rgba(129, 178, 194, 0.08) 1px, transparent 1px),
    linear-gradient(90deg, rgba(129, 178, 194, 0.08) 1px, transparent 1px);
  background-size: 24px 24px;
}
.dashboard-editor-runtime {
  width: 100%;
  height: 100%;
}
.dashboard-layer-list {
  display: flex;
  flex-wrap: wrap;
  gap: 5px;
  align-items: center;
}
.dashboard-layer-list button {
  border: 1px solid var(--el-border-color);
  border-radius: 4px;
  padding: 4px 8px;
  background: var(--el-bg-color);
  color: var(--el-text-color-primary);
}
.dashboard-layer-list button.selected {
  border-color: var(--el-color-primary);
}
.dashboard-properties-heading {
  display: flex;
  align-items: center;
  justify-content: space-between;
}
.dashboard-number-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  min-width: 0;
  gap: 5px;
}
.dashboard-properties :deep(.el-form-item__content),
.dashboard-properties :deep(.el-input-number),
.dashboard-properties :deep(.el-input),
.dashboard-properties :deep(.el-select) {
  min-width: 0;
  max-width: 100%;
}
.dashboard-number-grid :deep(.el-input-number) {
  width: 100%;
}
.dashboard-align-buttons {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
}
.dashboard-align-buttons .el-button {
  margin: 0;
}
.dashboard-binding-row {
  display: grid;
  gap: 5px;
  margin-bottom: 8px;
}
.dashboard-binding-categories {
  margin-bottom: 10px;
}
.dashboard-binding-label {
  font-size: 12px;
  color: var(--el-text-color-secondary);
}
.dashboard-binding-source-summary {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 6px;
}
.dashboard-binding-source-summary small {
  color: var(--el-text-color-secondary);
}
@media (max-width: 1200px) {
  .dashboard-designer-grid {
    grid-template-columns: 180px minmax(0, 1fr) 240px;
  }
  .dashboard-designer-grid.left-collapsed {
    grid-template-columns: 0 minmax(0, 1fr) 240px;
  }
  .dashboard-designer-grid.right-collapsed {
    grid-template-columns: 180px minmax(0, 1fr) 0;
  }
  .dashboard-designer-grid.left-collapsed.right-collapsed {
    grid-template-columns: 0 minmax(0, 1fr) 0;
  }
}
@media (max-width: 760px) {
  .dashboard-designer-grid,
  .dashboard-designer-grid.left-collapsed,
  .dashboard-designer-grid.right-collapsed,
  .dashboard-designer-grid.left-collapsed.right-collapsed {
    grid-template-columns: minmax(0, 1fr);
    height: auto;
  }
  .dashboard-side-panel,
  .dashboard-properties,
  .dashboard-editor-workspace {
    grid-column: 1;
  }
  .dashboard-side-panel {
    max-height: 320px;
  }
  .dashboard-editor-workspace {
    min-height: 520px;
  }
}
</style>
