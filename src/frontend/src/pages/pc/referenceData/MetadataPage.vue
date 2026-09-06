<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, reactive, ref } from 'vue'
import { onBeforeRouteLeave } from 'vue-router'
import { ArrowDown, Delete, Plus } from '@element-plus/icons-vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { ApiError } from '@/api/errors'
import { getReferenceDataApi } from '@/api/referenceData'
import type {
  CreateMetadataSchema,
  EffectiveMetadataSchema,
  MetadataAttribute,
  MetadataAttributeWrite,
  MetadataDataType,
  MetadataPublicationCheck,
  MetadataSchema,
  MetadataSchemaSummary,
  RuntimeMetadataAttribute,
} from '@/api/referenceData/metadataTypes'
import type {
  DictionarySummary,
  PublicationStatus,
  ReferenceDataQuery,
  ReferenceScope,
} from '@/api/referenceData/types'
import type {
  AvailableUnitDimension,
  RuntimeUnitDimension,
} from '@/api/referenceData/unitOfMeasureTypes'
import AppPage from '@/components/base/AppPage.vue'
import AppDataTable from '@/components/management/AppDataTable.vue'
import type {
  AppDataTableColumn,
  AppDataTableQueryMode,
  AppDataTableRequest,
} from '@/components/management/AppDataTable'
import AppFormDrawer from '@/components/management/AppFormDrawer.vue'
import AppQueryPanel from '@/components/management/AppQueryPanel.vue'
import { localeMessages } from '@/localization/i18n'
import { PERMISSIONS, PermissionGate, usePermission } from '@/permissions'
import { useAuthStore } from '@/stores/authStore'
import { useLocalizationStore } from '@/stores/localizationStore'

const api = getReferenceDataApi()
const auth = useAuthStore()
const localization = useLocalizationStore()
const copy = computed(() => localeMessages[localization.locale].referenceData)
const { has } = usePermission()

const table = ref<{
  reload: () => Promise<void>
  setTopQuery: (query: Record<string, unknown>) => void
}>()
const mode = ref<AppDataTableQueryMode>('top')
const query = reactive({ keyword: '', scopeType: '', status: '' })
const total = ref(0)
const firstLoading = ref(true)
const listError = ref('')
const error = ref('')
const traceId = ref('')
const conflict = ref(false)
const validation = ref<Record<string, string>>({})
const busy = ref(false)
let listRequest: AbortController | undefined
let detailRequest: AbortController | undefined
let referenceRequest: AbortController | undefined
let detailSequence = 0
let referenceSequence = 0

const scopeOptions = computed(() => [
  { value: 'Tenant', label: copy.value.tenant },
  { value: 'Platform', label: copy.value.platform },
])
const statusOptions = computed(() => [
  { value: 'Draft', label: copy.value.draft },
  { value: 'Published', label: copy.value.published },
  { value: 'Superseded', label: copy.value.superseded },
  { value: 'Disabled', label: copy.value.disabled },
])
const dataTypeOptions = computed<{ value: MetadataDataType; label: string }[]>(() => [
  { value: 'String', label: copy.value.typeString },
  { value: 'Integer', label: copy.value.typeInteger },
  { value: 'Decimal', label: copy.value.typeDecimal },
  { value: 'Boolean', label: copy.value.typeBoolean },
  { value: 'Date', label: copy.value.typeDate },
  { value: 'DateTime', label: copy.value.typeDateTime },
  { value: 'Enum', label: copy.value.typeEnum },
  { value: 'Reference', label: copy.value.typeReferenceMetadata },
])
const columns = computed<AppDataTableColumn[]>(() => [
  { field: 'name', title: copy.value.name, minWidth: 120, sortable: true },
])
const runtimeColumns = computed<AppDataTableColumn[]>(() => [
  { field: 'nId', title: copy.value.nId, minWidth: 130, filter: false },
  { field: 'name', title: copy.value.name, minWidth: 150, filter: false },
  { field: 'dataType', title: copy.value.dataType, width: 125, filter: false },
  { field: 'required', title: copy.value.metadataRequired, width: 90, filter: false },
  { field: 'isArray', title: copy.value.metadataArray, width: 80, filter: false },
  { field: 'enabled', title: copy.value.enabled, width: 80, filter: false },
  { field: 'defaultValue', title: copy.value.metadataDefault, minWidth: 160, filter: false },
])
const attributePreviewColumns = computed<AppDataTableColumn[]>(() => [
  { field: 'nId', title: copy.value.nId, minWidth: 130, filter: false },
  { field: 'name', title: copy.value.name, minWidth: 150, filter: false },
  { field: 'dataType', title: copy.value.dataType, width: 130, filter: false },
  { field: 'required', title: copy.value.metadataRequired, width: 90, filter: false },
  { field: 'isArray', title: copy.value.metadataArray, width: 80, filter: false },
  { field: 'enabled', title: copy.value.enabled, width: 80, filter: false },
])

function statusLabel(status: PublicationStatus) {
  return statusOptions.value.find((item) => item.value === status)?.label ?? status
}
function typeLabel(type: MetadataDataType) {
  return dataTypeOptions.value.find((item) => item.value === type)?.label ?? type
}
function structurePreview() {
  return JSON.stringify(
    {
      nId: form.nId,
      name: form.name,
      revision: selected.value?.revision ?? null,
      attributes: form.attributes.map((attribute) => ({
        nId: attribute.nId,
        name: attribute.name,
        dataType: attribute.dataType,
        required: attribute.required,
        isArray: attribute.isArray,
        enabled: attribute.enabled,
      })),
    },
    null,
    2,
  )
}
function date(value: string) {
  return new Intl.DateTimeFormat(localization.locale, {
    dateStyle: 'short',
    timeStyle: 'short',
    timeZone: localization.preferences.timeZone,
  }).format(new Date(value))
}
function clearError() {
  error.value = ''
  traceId.value = ''
  conflict.value = false
  validation.value = {}
}
function report(caught: unknown) {
  if (caught instanceof ApiError && caught.kind === 'cancelled') return
  const details = caught instanceof ApiError ? caught.details : undefined
  conflict.value = details?.code === 'REF-CONCURRENCY-CONFLICT'
  const codes: Record<string, string> = {
    'REF-METADATA-DICTIONARY-INVALID': copy.value.metadataDictionaryInvalid,
    'REF-METADATA-UNIT-INVALID': copy.value.metadataUnitInvalid,
    'REF-METADATA-NOT-FOUND': copy.value.notFound,
    'REF-INVALID-STATE': copy.value.stateInvalid,
    'REF-VALIDATION-FAILED': copy.value.invalid,
  }
  error.value = conflict.value
    ? copy.value.conflict
    : details?.status === 403
      ? copy.value.forbidden
      : details?.status === 404
        ? copy.value.notFound
        : (codes[details?.code ?? ''] ??
          (details?.status === 400 || details?.status === 422
            ? copy.value.invalid
            : copy.value.unavailable))
  traceId.value = details?.traceId ?? ''
  const field = details?.parameters?.field
  if (typeof field === 'string') validation.value[field] = error.value
}
function version(row: Pick<MetadataSchemaSummary, 'optimisticVersion' | 'concurrencyVersion'>) {
  return {
    expectedOptimisticVersion: row.optimisticVersion,
    expectedConcurrencyVersion: row.concurrencyVersion,
  }
}
type SchemaLifecycle = Pick<
  MetadataSchemaSummary,
  | 'scopeType'
  | 'status'
  | 'isFrozen'
  | 'isLocked'
  | 'publishedOn'
  | 'optimisticVersion'
  | 'concurrencyVersion'
>
function scopeWritable(row: Pick<SchemaLifecycle, 'scopeType' | 'isFrozen' | 'isLocked'>) {
  return (
    !row.isFrozen &&
    !row.isLocked &&
    (row.scopeType === 'Tenant' || has(PERMISSIONS.referenceDataPlatformManage))
  )
}
function canEdit(row: SchemaLifecycle) {
  return (
    row.status === 'Draft' && scopeWritable(row) && has(PERMISSIONS.referenceDataMetadataUpdate)
  )
}
function canClone(row: SchemaLifecycle | null) {
  return (
    !!row &&
    row.publishedOn !== null &&
    scopeWritable(row) &&
    has(PERMISSIONS.referenceDataMetadataCreate)
  )
}
function canPublish(row: SchemaLifecycle | null) {
  return (
    !!row &&
    row.status === 'Draft' &&
    scopeWritable(row) &&
    has(PERMISSIONS.referenceDataMetadataPublish)
  )
}
function canDisable(row: SchemaLifecycle | null) {
  return (
    !!row &&
    ['Draft', 'Published'].includes(row.status) &&
    scopeWritable(row) &&
    has(PERMISSIONS.referenceDataMetadataDisable)
  )
}
function canReadRuntime(row: MetadataSchemaSummary | null) {
  return row?.publishedOn !== null && row?.publishedOn !== undefined
}

async function load(request: AppDataTableRequest) {
  listRequest?.abort()
  listRequest = new AbortController()
  const params: ReferenceDataQuery = {
    pageIndex: request.pageIndex,
    pageSize: Math.min(100, request.pageSize),
    keyword: String(
      request.queryMode === 'top' ? (request.filters.keyword ?? '') : (request.filters.name ?? ''),
    ),
    scopeType: String(request.filters.scopeType ?? ''),
    status: String(request.filters.status ?? ''),
    ...(request.sort
      ? { sortField: request.sort.field, descending: request.sort.order === 'desc' }
      : {}),
  }
  try {
    if (!api) throw new Error(copy.value.unavailable)
    const result = await api.listMetadataSchemas(params, { signal: listRequest.signal })
    if (activeId.value && !result.items.some((item) => item.id === activeId.value))
      activeId.value = null
    return result
  } finally {
    firstLoading.value = false
  }
}
function onLoaded(page: { total: number }) {
  total.value = page.total
  listError.value = ''
  if (!formOpen.value && !publicationOpen.value && !runtimeOpen.value) clearError()
}
function reportList(caught: unknown) {
  if (!(caught instanceof ApiError && caught.kind === 'cancelled'))
    listError.value = copy.value.unavailable
}
function search() {
  table.value?.setTopQuery({ ...query })
}
function reset() {
  Object.assign(query, { keyword: '', scopeType: '', status: '' })
  search()
}
function switchMode(value: AppDataTableQueryMode) {
  mode.value = value
  Object.assign(query, { keyword: '', scopeType: '', status: '' })
}

interface FormAttribute extends Omit<MetadataAttributeWrite, 'minValue' | 'maxValue'> {
  localKey: string
  id?: string
  minValue: string
  maxValue: string
  unitSelectionKey: string
  defaultConfigured: boolean
  wasPublished?: boolean
  isFrozen?: boolean
  isLocked?: boolean
}
type FormMode = 'create' | 'edit' | 'view'
const formOpen = ref(false)
const formMode = ref<FormMode>('view')
const selected = ref<MetadataSchema | null>(null)
const activeId = ref<string | null>(null)
const selectedSummary = computed<MetadataSchemaSummary | null>(() =>
  selected.value ? { ...selected.value, attributeCount: selected.value.attributes.length } : null,
)
const form = reactive({
  nId: '',
  name: '',
  description: '',
  scopeType: 'Tenant' as ReferenceScope,
  attributes: [] as FormAttribute[],
})
const metadataDetailTab = ref('attributes')
const savedSnapshot = ref('')
const referenceLoading = ref(false)
const dictionaryOptions = ref<DictionarySummary[]>([])
const unitDimensionOptions = ref<AvailableUnitDimension[]>([])
const unitSnapshots = ref(new Map<string, RuntimeUnitDimension>())
const dirty = computed(
  () => formOpen.value && formMode.value !== 'view' && JSON.stringify(form) !== savedSnapshot.value,
)
const canSubmit = computed(() => {
  if (formMode.value === 'create')
    return (
      has(PERMISSIONS.referenceDataMetadataCreate) &&
      (form.scopeType === 'Tenant' || has(PERMISSIONS.referenceDataPlatformManage))
    )
  return formMode.value === 'edit' && !!selected.value && canEdit(selected.value)
})
const isReadOnly = computed(() => !canSubmit.value)
function attributeWritable(attribute: FormAttribute) {
  return canSubmit.value && !attribute.isFrozen && !attribute.isLocked
}
function currentTenant() {
  return selected.value?.tenantNId ?? auth.user?.tenantId ?? ''
}
function unitKey(
  nId: string | null,
  revision: number | null,
  sourceScope: ReferenceScope | null,
  sourceTenantNId: string | null,
) {
  return nId && revision && sourceScope
    ? JSON.stringify([nId, revision, sourceScope, sourceTenantNId])
    : ''
}
function blankAttribute(): FormAttribute {
  return {
    localKey: crypto.randomUUID(),
    nId: '',
    name: '',
    dataType: 'String',
    required: false,
    isArray: false,
    enabled: true,
    sort: form.attributes.length,
    defaultConfigured: false,
    defaultValue: '',
    minLength: null,
    maxLength: null,
    minValue: '',
    maxValue: '',
    unitSelectionKey: '',
    pattern: null,
    dictionaryNId: null,
    referenceTarget: null,
    precision: null,
    scale: null,
    unitDimensionNId: null,
    defaultUnitNId: null,
    unitRevision: null,
    unitSourceScope: null,
    unitSourceTenantNId: null,
    description: null,
  }
}
function toFormAttribute(attribute: MetadataAttribute): FormAttribute {
  return {
    ...attribute,
    localKey: crypto.randomUUID(),
    minValue: attribute.minValue === null ? '' : String(attribute.minValue),
    maxValue: attribute.maxValue === null ? '' : String(attribute.maxValue),
    unitSelectionKey: unitKey(
      attribute.unitDimensionNId,
      attribute.unitRevision,
      attribute.unitSourceScope,
      attribute.unitSourceTenantNId,
    ),
    defaultConfigured: attribute.defaultValue !== null,
    defaultValue: attribute.defaultValue ?? '',
  }
}
function fill(item: MetadataSchema | null) {
  selected.value = item
  Object.assign(form, {
    nId: item?.nId ?? '',
    name: item?.name ?? '',
    description: item?.description ?? '',
    scopeType: item?.scopeType ?? 'Tenant',
    attributes: (item?.attributes ?? []).map(toFormAttribute),
  })
  savedSnapshot.value = JSON.stringify(form)
}
function addAttribute() {
  if (form.attributes.length < 200) form.attributes.push(blankAttribute())
}
function removeAttribute(attribute: FormAttribute) {
  if (!attributeWritable(attribute) || attribute.wasPublished) return
  form.attributes = form.attributes.filter((item) => item.localKey !== attribute.localKey)
}
function normalizeAttribute(attribute: FormAttribute, resetDefault = true) {
  if (attribute.dataType !== 'String') {
    attribute.minLength = null
    attribute.maxLength = null
    attribute.pattern = null
  }
  if (!['Integer', 'Decimal'].includes(attribute.dataType)) {
    attribute.minValue = ''
    attribute.maxValue = ''
  }
  if (attribute.dataType !== 'Decimal') {
    attribute.precision = null
    attribute.scale = null
    attribute.unitSelectionKey = ''
    attribute.unitDimensionNId = null
    attribute.defaultUnitNId = null
    attribute.unitRevision = null
    attribute.unitSourceScope = null
    attribute.unitSourceTenantNId = null
  }
  if (attribute.dataType !== 'Enum') attribute.dictionaryNId = null
  if (attribute.dataType !== 'Reference') attribute.referenceTarget = null
  if (resetDefault) {
    attribute.defaultConfigured = false
    attribute.defaultValue = ''
  }
}
function changeArray(attribute: FormAttribute) {
  attribute.defaultConfigured = false
  attribute.defaultValue = ''
}
function sourceLabel(scope: ReferenceScope, revision: number) {
  const scopeName = scope === 'Tenant' ? copy.value.tenant : copy.value.platform
  return `${scopeName} · ${copy.value.revision} ${revision}`
}
function dictionaryLabel(option: DictionarySummary) {
  return `${option.name} (${option.nId} · ${sourceLabel(option.scopeType, option.revision)})`
}
function unitDimensionLabel(option: AvailableUnitDimension) {
  return `${option.name} (${option.nId} · ${sourceLabel(option.sourceScope, option.revision)})`
}
function defaultUnitOptions(attribute: FormAttribute) {
  const units = unitSnapshots.value.get(attribute.unitSelectionKey)?.units ?? []
  const enabled = units.filter((unit) => unit.enabled)
  if (attribute.defaultUnitNId && !enabled.some((unit) => unit.nId === attribute.defaultUnitNId)) {
    return [
      ...enabled,
      {
        nId: attribute.defaultUnitNId,
        name: attribute.defaultUnitNId,
        symbol: attribute.defaultUnitNId,
        factorToBase: '',
        offsetToBase: '',
        decimalPlaces: 0,
        roundingMode: 'ToEven' as const,
        enabled: true,
        sort: Number.MAX_SAFE_INTEGER,
      },
    ]
  }
  return enabled
}
function seedHistoricalUnitOptions(options: AvailableUnitDimension[]) {
  const byKey = new Map(
    options.map((option) => [
      unitKey(option.nId, option.revision, option.sourceScope, option.sourceTenantNId),
      option,
    ]),
  )
  for (const attribute of form.attributes) {
    if (!attribute.unitSelectionKey || byKey.has(attribute.unitSelectionKey)) continue
    byKey.set(attribute.unitSelectionKey, {
      nId: attribute.unitDimensionNId!,
      name: attribute.unitDimensionNId!,
      sourceScope: attribute.unitSourceScope!,
      sourceTenantNId: attribute.unitSourceTenantNId,
      revision: attribute.unitRevision!,
      publishedOn: '',
      isSystemDefined: false,
      conversionKind: 'Ratio',
      baseUnitNId: attribute.defaultUnitNId ?? '',
      unitCount: 0,
    })
  }
  return [...byKey.values()]
}
async function loadUnitSnapshot(
  option: Pick<AvailableUnitDimension, 'nId' | 'revision' | 'sourceScope' | 'sourceTenantNId'>,
  signal: AbortSignal,
  sequence: number,
) {
  if (!api || !has(PERMISSIONS.referenceDataUnitOfMeasureView)) return
  const key = unitKey(option.nId, option.revision, option.sourceScope, option.sourceTenantNId)
  if (unitSnapshots.value.has(key)) return
  const snapshot = await api.getUnitDimensionRevision(
    option.nId,
    option.revision,
    option.sourceScope,
    option.sourceTenantNId,
    { signal },
  )
  if (sequence !== referenceSequence) return
  const next = new Map(unitSnapshots.value)
  next.set(key, snapshot)
  unitSnapshots.value = next
}
async function refreshReferenceOptions() {
  referenceSequence++
  referenceRequest?.abort()
  referenceRequest = new AbortController()
  const sequence = referenceSequence
  const signal = referenceRequest.signal
  referenceLoading.value = true
  try {
    if (!api) throw new Error(copy.value.unavailable)
    const [dictionaryPage, dimensionPage] = await Promise.all([
      has(PERMISSIONS.referenceDataDictionaryView)
        ? api.listDictionaries({ pageIndex: 1, pageSize: 100, status: 'Published' }, { signal })
        : Promise.resolve({ items: [] as DictionarySummary[] }),
      has(PERMISSIONS.referenceDataUnitOfMeasureView)
        ? api.listAvailableUnitDimensions({ pageIndex: 1, pageSize: 100 }, { signal })
        : Promise.resolve({ items: [] as AvailableUnitDimension[] }),
    ])
    if (sequence !== referenceSequence) return
    const tenant = currentTenant()
    const dictionaries = dictionaryPage.items.filter(
      (item) =>
        item.scopeType === 'Platform' ||
        (form.scopeType === 'Tenant' && item.scopeType === 'Tenant' && item.tenantNId === tenant),
    )
    const effectiveDictionaries = new Map<string, DictionarySummary>()
    for (const item of dictionaries.filter((item) => item.scopeType === 'Platform'))
      effectiveDictionaries.set(item.nId, item)
    if (form.scopeType === 'Tenant')
      for (const item of dictionaries.filter((item) => item.scopeType === 'Tenant'))
        effectiveDictionaries.set(item.nId, item)
    dictionaryOptions.value = [...effectiveDictionaries.values()].sort((left, right) =>
      left.nId.localeCompare(right.nId),
    )
    const dimensions = dimensionPage.items.filter(
      (item) =>
        item.sourceScope === 'Platform' ||
        (form.scopeType === 'Tenant' &&
          item.sourceScope === 'Tenant' &&
          item.sourceTenantNId === tenant),
    )
    unitDimensionOptions.value = seedHistoricalUnitOptions(dimensions)
    unitSnapshots.value = new Map()
    const fixedOptions = unitDimensionOptions.value.filter((option) =>
      form.attributes.some(
        (attribute) =>
          attribute.unitSelectionKey ===
          unitKey(option.nId, option.revision, option.sourceScope, option.sourceTenantNId),
      ),
    )
    await Promise.all(fixedOptions.map((option) => loadUnitSnapshot(option, signal, sequence)))
  } catch (caught) {
    if (!(caught instanceof ApiError && caught.kind === 'cancelled')) report(caught)
  } finally {
    if (sequence === referenceSequence) referenceLoading.value = false
  }
}
async function changeUnitDimension(attribute: FormAttribute) {
  const option = unitDimensionOptions.value.find(
    (item) =>
      unitKey(item.nId, item.revision, item.sourceScope, item.sourceTenantNId) ===
      attribute.unitSelectionKey,
  )
  attribute.unitDimensionNId = option?.nId ?? null
  attribute.unitRevision = option?.revision ?? null
  attribute.unitSourceScope = option?.sourceScope ?? null
  attribute.unitSourceTenantNId = option?.sourceTenantNId ?? null
  attribute.defaultUnitNId = null
  if (!option) return
  const sequence = referenceSequence
  const signal = referenceRequest?.signal ?? new AbortController().signal
  referenceLoading.value = true
  try {
    await loadUnitSnapshot(option, signal, sequence)
  } catch (caught) {
    if (!(caught instanceof ApiError && caught.kind === 'cancelled')) report(caught)
  } finally {
    if (sequence === referenceSequence) referenceLoading.value = false
  }
}
async function changeFormScope() {
  for (const attribute of form.attributes) {
    attribute.dictionaryNId = null
    attribute.unitSelectionKey = ''
    attribute.unitDimensionNId = null
    attribute.unitRevision = null
    attribute.unitSourceScope = null
    attribute.unitSourceTenantNId = null
    attribute.defaultUnitNId = null
  }
  await refreshReferenceOptions()
}
function text(value: string | null) {
  return value?.trim() || null
}
function decimalText(value: string) {
  const source = value.trim()
  if (!source) return null
  const negative = source.startsWith('-')
  const unsigned = source.replace(/^[+-]/, '')
  const [rawWhole = '', fraction] = unsigned.split('.')
  const whole = (rawWhole || '0').replace(/^0+(?=\d)/, '')
  const decimalPart = fraction ? `.${fraction}` : ''
  return `${negative ? '-' : ''}${whole}${decimalPart}`
}
function attributeWrite(attribute: FormAttribute): MetadataAttributeWrite {
  const stringType = attribute.dataType === 'String'
  const numericType = attribute.dataType === 'Integer' || attribute.dataType === 'Decimal'
  const decimalType = attribute.dataType === 'Decimal'
  return {
    nId: attribute.nId.trim(),
    name: attribute.name.trim(),
    dataType: attribute.dataType,
    required: attribute.required,
    isArray: attribute.isArray,
    enabled: attribute.enabled,
    sort: attribute.sort,
    defaultValue: attribute.defaultConfigured ? attribute.defaultValue : null,
    minLength: stringType ? attribute.minLength : null,
    maxLength: stringType ? attribute.maxLength : null,
    minValue: numericType ? decimalText(attribute.minValue) : null,
    maxValue: numericType ? decimalText(attribute.maxValue) : null,
    pattern: stringType ? text(attribute.pattern) : null,
    dictionaryNId: attribute.dataType === 'Enum' ? text(attribute.dictionaryNId) : null,
    referenceTarget: attribute.dataType === 'Reference' ? text(attribute.referenceTarget) : null,
    precision: decimalType ? attribute.precision : null,
    scale: decimalType ? attribute.scale : null,
    unitDimensionNId: decimalType ? text(attribute.unitDimensionNId) : null,
    defaultUnitNId: decimalType ? text(attribute.defaultUnitNId) : null,
    unitRevision: decimalType ? attribute.unitRevision : null,
    unitSourceScope: decimalType ? attribute.unitSourceScope : null,
    unitSourceTenantNId:
      decimalType && attribute.unitSourceScope === 'Tenant'
        ? text(attribute.unitSourceTenantNId)
        : null,
    description: text(attribute.description),
  }
}
function payload(): CreateMetadataSchema {
  return {
    scopeType: form.scopeType,
    nId: form.nId.trim(),
    name: form.name.trim(),
    description: text(form.description),
    attributes: form.attributes.map(attributeWrite),
  }
}

async function create() {
  if (!(await allowDiscard())) return
  detailSequence++
  detailRequest?.abort()
  clearError()
  fill(null)
  formMode.value = 'create'
  addAttribute()
  savedSnapshot.value = JSON.stringify(form)
  formOpen.value = true
  await refreshReferenceOptions()
}
async function open(row: MetadataSchemaSummary, edit = false, showForm = true, markActive = false) {
  if (!(await allowDiscard())) return
  if (markActive) activeId.value = row.id
  detailSequence++
  detailRequest?.abort()
  detailRequest = new AbortController()
  const sequence = detailSequence
  clearError()
  busy.value = true
  try {
    if (!api) throw new Error(copy.value.unavailable)
    const item = await api.getMetadataSchema(row.id, { signal: detailRequest.signal })
    if (sequence !== detailSequence) return
    fill(item)
    formMode.value = edit && canEdit(item) ? 'edit' : 'view'
    formOpen.value = showForm
    await refreshReferenceOptions()
  } catch (caught) {
    if (sequence === detailSequence) report(caught)
  } finally {
    if (sequence === detailSequence) busy.value = false
  }
}
async function select(row: MetadataSchemaSummary) {
  await open(row, false, false, true)
}
async function allowDiscard() {
  if (busy.value) return false
  if (!dirty.value) return true
  try {
    await ElMessageBox.confirm(copy.value.discard, copy.value.metadataTitle, {
      confirmButtonText: copy.value.close,
      cancelButtonText: copy.value.cancel,
    })
    return true
  } catch {
    return false
  }
}
function closeFormNow() {
  detailSequence++
  detailRequest?.abort()
  referenceSequence++
  referenceRequest?.abort()
  formOpen.value = false
  dictionaryOptions.value = []
  unitDimensionOptions.value = []
  unitSnapshots.value = new Map()
  referenceLoading.value = false
  busy.value = false
}
async function closeForm(value = false) {
  if (!value && (await allowDiscard())) {
    closeFormNow()
    clearError()
  }
}

const definitionNId = /^[A-Za-z][A-Za-z0-9_.-]{1,63}$/
const itemNId = /^[A-Za-z0-9][A-Za-z0-9_.-]{0,63}$/
const decimal = /^[+-]?(?:\d+(?:\.\d*)?|\.\d+)$/
const integer = /^[+-]?\d+$/
const dateOnly = /^\d{4}-\d{2}-\d{2}$/
const dateTime = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})$/
function attributeField(attribute: FormAttribute, field: string) {
  return `attributes[${form.attributes.indexOf(attribute)}].${field}`
}
function attributeErrors(attribute: FormAttribute) {
  const prefix = `attributes[${form.attributes.indexOf(attribute)}].`
  return [
    ...new Set(
      Object.entries(validation.value)
        .filter(([key]) => key.startsWith(prefix))
        .map(([, value]) => value),
    ),
  ]
}
function validDate(value: string) {
  if (!dateOnly.test(value)) return false
  const [year, month, day] = value.split('-').map(Number)
  const parsed = new Date(Date.UTC(year!, month! - 1, day))
  return (
    parsed.getUTCFullYear() === year &&
    parsed.getUTCMonth() === month! - 1 &&
    parsed.getUTCDate() === day
  )
}
function plainDecimal(value: string | number) {
  const source = String(value)
  if (!/[eE]/.test(source)) return source
  const [coefficient = '', exponentText = '0'] = source.toLowerCase().split('e')
  const exponent = Number(exponentText)
  if (!Number.isInteger(exponent)) return source
  const sign = coefficient.startsWith('-') || coefficient.startsWith('+') ? coefficient[0]! : ''
  const unsigned = sign ? coefficient.slice(1) : coefficient
  const point = unsigned.indexOf('.')
  const digits = unsigned.replace('.', '')
  const decimalIndex = (point < 0 ? unsigned.length : point) + exponent
  if (decimalIndex <= 0) return `${sign}0.${'0'.repeat(-decimalIndex)}${digits}`
  if (decimalIndex >= digits.length)
    return `${sign}${digits}${'0'.repeat(decimalIndex - digits.length)}`
  return `${sign}${digits.slice(0, decimalIndex)}.${digits.slice(decimalIndex)}`
}
function compareDecimals(left: string, right: string) {
  function parts(source: string) {
    const trimmed = source.trim()
    const negative = trimmed.startsWith('-')
    const unsigned = trimmed.replace(/^[+-]/, '')
    const [rawWhole = '', rawFraction = ''] = unsigned.split('.')
    const whole = rawWhole.replace(/^0+/, '') || '0'
    const fraction = rawFraction.replace(/0+$/, '')
    const zero = whole === '0' && fraction === ''
    return { negative: negative && !zero, whole, fraction }
  }
  function compareMagnitude(
    leftParts: ReturnType<typeof parts>,
    rightParts: ReturnType<typeof parts>,
  ) {
    if (leftParts.whole.length !== rightParts.whole.length)
      return leftParts.whole.length < rightParts.whole.length ? -1 : 1
    const whole = leftParts.whole.localeCompare(rightParts.whole)
    if (whole !== 0) return whole < 0 ? -1 : 1
    const width = Math.max(leftParts.fraction.length, rightParts.fraction.length)
    const leftFraction = leftParts.fraction.padEnd(width, '0')
    const rightFraction = rightParts.fraction.padEnd(width, '0')
    const fraction = leftFraction.localeCompare(rightFraction)
    return fraction === 0 ? 0 : fraction < 0 ? -1 : 1
  }
  const leftParts = parts(left)
  const rightParts = parts(right)
  if (leftParts.negative !== rightParts.negative) return leftParts.negative ? -1 : 1
  const magnitude = compareMagnitude(leftParts, rightParts)
  return leftParts.negative ? -magnitude : magnitude
}
function validNumericDefault(attribute: FormAttribute, value: unknown) {
  if (typeof value !== 'string' && typeof value !== 'number') return false
  if (typeof value === 'number' && !Number.isFinite(value)) return false
  if (typeof value === 'string' && /[eE]/.test(value)) return false
  const textValue = plainDecimal(value)
  const integerOnly = attribute.dataType === 'Integer'
  if (!validDecimalBound(textValue, integerOnly)) return false
  if (attribute.minValue !== '' && compareDecimals(textValue, attribute.minValue) < 0) return false
  if (attribute.maxValue !== '' && compareDecimals(textValue, attribute.maxValue) > 0) return false
  if (!integerOnly) {
    const unsigned = textValue.replace(/^[+-]/, '')
    const [whole = '', fraction = ''] = unsigned.split('.')
    const digits = `${whole}${fraction}`.replace(/^0+/, '').length || 1
    if (attribute.scale !== null && fraction.length > attribute.scale) return false
    if (attribute.precision !== null && digits > attribute.precision) return false
  }
  return true
}
function validPattern(value: string | null) {
  if (!value) return true
  if (value.length > 256 || value.includes('\0')) return false
  if (/\\(?:[1-9]|k<)/.test(value)) return false
  if (/\(\?(?:[=!]|<[=!]|>|\()/.test(value)) return false
  if (/\([^)]*(?:[*+]|\{\d+(?:,\d*)?\})[^)]*\)(?:[*+]|\{\d+(?:,\d*)?\})/.test(value)) return false
  try {
    new RegExp(value, 'u')
    return true
  } catch {
    return false
  }
}
function validScalar(attribute: FormAttribute, value: unknown) {
  if (attribute.dataType === 'String')
    return (
      typeof value === 'string' &&
      !value.includes('\0') &&
      (attribute.minLength === null || value.length >= attribute.minLength) &&
      (attribute.maxLength === null || value.length <= attribute.maxLength)
    )
  if (attribute.dataType === 'Integer' || attribute.dataType === 'Decimal')
    return validNumericDefault(attribute, value)
  if (attribute.dataType === 'Boolean')
    return typeof value === 'string'
      ? value === 'true' || value === 'false'
      : typeof value === 'boolean'
  if (attribute.dataType === 'Date') return typeof value === 'string' && validDate(value)
  if (attribute.dataType === 'DateTime')
    return typeof value === 'string' && dateTime.test(value) && !Number.isNaN(Date.parse(value))
  if (attribute.dataType === 'Enum') return typeof value === 'string' && itemNId.test(value)
  return typeof value === 'string' && value.trim().length > 0 && value.length <= 128
}
function validDefault(attribute: FormAttribute) {
  if (!attribute.defaultConfigured) return true
  if (!attribute.isArray) return validScalar(attribute, attribute.defaultValue)
  if (typeof attribute.defaultValue !== 'string') return false
  try {
    const values: unknown = JSON.parse(
      attribute.defaultValue,
      (_key: string, value: unknown, context?: { source?: string }) => {
        if (typeof value !== 'number') return value
        if (context?.source === undefined) throw new Error('Exact JSON number source unavailable')
        return context.source
      },
    )
    return (
      Array.isArray(values) &&
      values.length <= 1000 &&
      values.every((value) => validScalar(attribute, value))
    )
  } catch {
    return false
  }
}
function validDecimalBound(value: string, integerOnly: boolean) {
  if (value.trim() === '') return true
  if (!(integerOnly ? integer : decimal).test(value.trim())) return false
  const [rawWhole = '', fraction = ''] = value.trim().replace(/^[+-]/, '').split('.')
  const whole = rawWhole.replace(/^0+/, '') || '0'
  return whole.length <= 16 && fraction.length <= 12
}
function validate() {
  validation.value = {}
  if (!definitionNId.test(form.nId.trim())) validation.value.nId = copy.value.invalidNId
  if (!form.name.trim() || form.name.length > 200) validation.value.name = copy.value.required
  if (form.description.length > 2000) validation.value.description = copy.value.invalid
  if (form.attributes.length > 200) validation.value.attributes = copy.value.metadataLimit
  const seen = new Set<string>()
  for (const attribute of form.attributes) {
    const nIdPath = attributeField(attribute, 'nId')
    const normalized = attribute.nId.trim().toUpperCase()
    if (!itemNId.test(attribute.nId.trim())) validation.value[nIdPath] = copy.value.invalidNId
    if (seen.has(normalized)) validation.value[nIdPath] = copy.value.metadataDuplicate
    seen.add(normalized)
    if (!attribute.name.trim() || attribute.name.length > 200)
      validation.value[attributeField(attribute, 'name')] = copy.value.required
    if (!Number.isInteger(attribute.sort) || attribute.sort < 0)
      validation.value[attributeField(attribute, 'sort')] = copy.value.invalidSort
    if (!validDefault(attribute))
      validation.value[attributeField(attribute, 'defaultValue')] =
        copy.value.metadataDefaultInvalid
    if (attribute.dataType === 'String') {
      if (
        (attribute.minLength !== null &&
          (!Number.isInteger(attribute.minLength) || attribute.minLength < 0)) ||
        (attribute.maxLength !== null &&
          (!Number.isInteger(attribute.maxLength) || attribute.maxLength < 0)) ||
        (attribute.minLength !== null &&
          attribute.maxLength !== null &&
          attribute.minLength > attribute.maxLength) ||
        !validPattern(attribute.pattern)
      )
        validation.value[attributeField(attribute, 'constraints')] =
          copy.value.metadataConstraintInvalid
    }
    if (attribute.dataType === 'Integer' || attribute.dataType === 'Decimal') {
      const integerOnly = attribute.dataType === 'Integer'
      if (
        !validDecimalBound(attribute.minValue, integerOnly) ||
        !validDecimalBound(attribute.maxValue, integerOnly) ||
        (attribute.minValue !== '' &&
          attribute.maxValue !== '' &&
          compareDecimals(attribute.minValue, attribute.maxValue) > 0)
      )
        validation.value[attributeField(attribute, 'constraints')] =
          copy.value.metadataConstraintInvalid
    }
    if (attribute.dataType === 'Decimal') {
      if (
        (attribute.precision !== null &&
          (!Number.isInteger(attribute.precision) ||
            attribute.precision < 1 ||
            attribute.precision > 28)) ||
        (attribute.scale !== null &&
          (!Number.isInteger(attribute.scale) || attribute.scale < 0 || attribute.scale > 12)) ||
        (attribute.precision !== null &&
          attribute.scale !== null &&
          attribute.scale > attribute.precision)
      )
        validation.value[attributeField(attribute, 'precision')] =
          copy.value.metadataConstraintInvalid
      const hasDimension = Boolean(attribute.unitDimensionNId?.trim())
      const hasUnitCoordinate =
        hasDimension ||
        attribute.unitRevision !== null ||
        attribute.unitSourceScope !== null ||
        Boolean(attribute.defaultUnitNId?.trim()) ||
        Boolean(attribute.unitSourceTenantNId?.trim())
      if (
        (hasUnitCoordinate &&
          (!hasDimension ||
            attribute.unitRevision === null ||
            attribute.unitRevision < 1 ||
            !attribute.unitSourceScope)) ||
        (!hasDimension && Boolean(attribute.defaultUnitNId?.trim())) ||
        (attribute.unitSourceScope === 'Tenant' && !attribute.unitSourceTenantNId?.trim()) ||
        (attribute.unitSourceScope === 'Platform' && Boolean(attribute.unitSourceTenantNId?.trim()))
      )
        validation.value[attributeField(attribute, 'unitDimensionNId')] =
          copy.value.metadataUnitInvalid
    }
    if (attribute.dataType === 'Enum' && !definitionNId.test(attribute.dictionaryNId?.trim() ?? ''))
      validation.value[attributeField(attribute, 'dictionaryNId')] = copy.value.required
    if (
      attribute.dataType === 'Reference' &&
      !definitionNId.test(attribute.referenceTarget?.trim() ?? '')
    )
      validation.value[attributeField(attribute, 'referenceTarget')] = copy.value.required
  }
  return Object.keys(validation.value).length === 0
}

async function save() {
  if (!api || !canSubmit.value || !validate() || busy.value) return
  busy.value = true
  clearError()
  try {
    const request = payload()
    const result =
      formMode.value === 'create'
        ? await api.createMetadataSchema(request)
        : await api.updateMetadataSchema(selected.value!.id, {
            name: request.name,
            description: request.description,
            attributes: request.attributes,
            ...version(selected.value!),
          })
    closeFormNow()
    ElMessage.success(`${copy.value.saved}: ${result.nId}`)
    await table.value?.reload()
  } catch (caught) {
    report(caught)
  } finally {
    busy.value = false
  }
}
async function reloadForm() {
  if (!api || !selected.value) return
  detailSequence++
  detailRequest?.abort()
  detailRequest = new AbortController()
  const sequence = detailSequence
  busy.value = true
  try {
    const item = await api.getMetadataSchema(selected.value.id, { signal: detailRequest.signal })
    if (sequence !== detailSequence) return
    fill(item)
    formMode.value = canEdit(item) ? 'edit' : 'view'
    clearError()
  } catch (caught) {
    if (sequence === detailSequence) report(caught)
  } finally {
    if (sequence === detailSequence) busy.value = false
  }
}
async function copyUnsaved() {
  try {
    await navigator.clipboard.writeText(JSON.stringify(payload(), null, 2))
    ElMessage.success(copy.value.copied)
  } catch {
    ElMessage.warning(copy.value.copyFailed)
  }
}
async function clone(row: MetadataSchemaSummary | null) {
  if (!api || !row || !canClone(row) || busy.value) return
  busy.value = true
  clearError()
  try {
    const item = await api.cloneMetadataSchema(row.id, version(row))
    fill(item)
    formMode.value = 'edit'
    formOpen.value = true
    ElMessage.success(copy.value.cloned)
    await table.value?.reload()
  } catch (caught) {
    report(caught)
  } finally {
    busy.value = false
  }
}

const publicationOpen = ref(false)
const publication = ref<MetadataPublicationCheck | null>(null)
const publicationRow = ref<MetadataSchemaSummary | null>(null)
async function preparePublication(row: MetadataSchemaSummary | null) {
  if (!api || !row || !canPublish(row) || busy.value || !(await allowDiscard())) return
  detailSequence++
  detailRequest?.abort()
  detailRequest = new AbortController()
  const sequence = detailSequence
  busy.value = true
  clearError()
  try {
    const result = await api.checkMetadataPublication(row.id, { signal: detailRequest.signal })
    if (sequence !== detailSequence) return
    publication.value = result
    publicationRow.value = row
    publicationOpen.value = true
  } catch (caught) {
    if (sequence === detailSequence) report(caught)
  } finally {
    if (sequence === detailSequence) busy.value = false
  }
}
function closePublication(value = false) {
  if (value || busy.value) return
  detailSequence++
  detailRequest?.abort()
  publicationOpen.value = false
  publication.value = null
  publicationRow.value = null
  clearError()
}
async function publish() {
  if (!api || !publicationRow.value || !publication.value || publication.value.errors.length > 0)
    return
  busy.value = true
  clearError()
  try {
    const result = await api.publishMetadataSchema(
      publicationRow.value.id,
      version(publicationRow.value),
    )
    if (result) fill(result)
    publicationOpen.value = false
    publication.value = null
    publicationRow.value = null
    ElMessage.success(copy.value.publishedSuccess)
    await table.value?.reload()
  } catch (caught) {
    report(caught)
  } finally {
    busy.value = false
  }
}
async function disable(row: MetadataSchemaSummary | null) {
  if (!api || !row || !canDisable(row) || busy.value) return
  let reason = ''
  try {
    const result = await ElMessageBox.prompt(copy.value.metadataDisableHint, copy.value.reason, {
      confirmButtonText: copy.value.disable,
      cancelButtonText: copy.value.cancel,
      inputValidator: (value) =>
        (Boolean(value?.trim()) && value!.length <= 1000) || copy.value.required,
    })
    reason = result.value
  } catch {
    return
  }
  busy.value = true
  clearError()
  try {
    const result = await api.disableMetadataSchema(row.id, {
      ...version(row),
      changeReason: reason,
    })
    if (result) fill(result)
    ElMessage.success(copy.value.disabledSuccess)
    await table.value?.reload()
  } catch (caught) {
    report(caught)
  } finally {
    busy.value = false
  }
}

const runtimeOpen = ref(false)
const runtimeLoading = ref(false)
const runtimeMode = ref<'Current' | 'Fixed'>('Fixed')
const runtimeRow = ref<MetadataSchemaSummary | null>(null)
const runtimeSchema = ref<EffectiveMetadataSchema | null>(null)
let runtimeRequest: AbortController | undefined
let runtimeSequence = 0
async function openRuntime(row: MetadataSchemaSummary | null) {
  if (!api || !row || !canReadRuntime(row) || !(await allowDiscard())) return
  closeFormNow()
  clearError()
  runtimeRow.value = row
  runtimeMode.value = 'Fixed'
  runtimeOpen.value = true
  await loadRuntime()
}
async function loadRuntime() {
  if (!api || !runtimeRow.value) return
  runtimeSequence++
  runtimeRequest?.abort()
  runtimeRequest = new AbortController()
  const sequence = runtimeSequence
  runtimeLoading.value = true
  clearError()
  const row = runtimeRow.value
  try {
    const result =
      runtimeMode.value === 'Current'
        ? await api.getEffectiveMetadataSchema(row.nId, { signal: runtimeRequest.signal })
        : await api.getMetadataSchemaRevision(row.nId, row.revision, row.scopeType, row.tenantNId, {
            signal: runtimeRequest.signal,
          })
    if (sequence === runtimeSequence) runtimeSchema.value = result
  } catch (caught) {
    if (sequence === runtimeSequence) report(caught)
  } finally {
    if (sequence === runtimeSequence) runtimeLoading.value = false
  }
}
function closeRuntime(value = false) {
  if (value || runtimeLoading.value || busy.value) return
  runtimeSequence++
  runtimeRequest?.abort()
  runtimeOpen.value = false
  runtimeRow.value = null
  runtimeSchema.value = null
  clearError()
}
function displayDefault(attribute: RuntimeMetadataAttribute) {
  return attribute.defaultValue === null ? copy.value.notConfigured : attribute.defaultValue
}

function beforeUnload(event: BeforeUnloadEvent) {
  if (dirty.value) event.preventDefault()
}
onBeforeRouteLeave(allowDiscard)
onMounted(async () => {
  window.addEventListener('beforeunload', beforeUnload)
  await nextTick()
  await table.value?.reload()
})
onBeforeUnmount(() => {
  listRequest?.abort()
  detailRequest?.abort()
  referenceRequest?.abort()
  runtimeRequest?.abort()
  detailSequence++
  referenceSequence++
  runtimeSequence++
  window.removeEventListener('beforeunload', beforeUnload)
})
</script>

<template>
  <AppPage
    class="metadata-page"
    :title="copy.metadataTitle"
    :description="copy.metadataDescription"
    data-testid="reference-data-metadata"
  >
    <template #actions>
      <PermissionGate :permission-n-id="PERMISSIONS.referenceDataMetadataCreate">
        <el-button type="primary" :icon="Plus" data-testid="metadata-schema-create" @click="create">
          {{ copy.metadataCreate }}
        </el-button>
      </PermissionGate>
    </template>

    <el-alert v-if="listError" :title="listError" type="error" :closable="false" show-icon />
    <el-alert
      v-if="error && !formOpen && !publicationOpen && !runtimeOpen"
      :title="error"
      type="error"
      :closable="false"
      show-icon
    >
      <p v-if="traceId">{{ copy.traceId }}: {{ traceId }}</p>
    </el-alert>
    <div class="metadata-master-detail">
      <section class="metadata-master" :aria-label="copy.metadataTitle">
        <AppQueryPanel show-actions grid @submit="search" @reset="reset">
          <label class="metadata-query-field">
            <span>{{ copy.keyword }}</span>
            <el-input v-model="query.keyword" :aria-label="copy.keyword" @keyup.enter="search" />
          </label>
          <label class="metadata-query-field">
            <span>{{ copy.scope }}</span>
            <el-select v-model="query.scopeType" :aria-label="copy.scope">
              <el-option value="" :label="copy.all" />
              <el-option v-for="option in scopeOptions" :key="option.value" v-bind="option" />
            </el-select>
          </label>
          <label class="metadata-query-field">
            <span>{{ copy.status }}</span>
            <el-select v-model="query.status" :aria-label="copy.status">
              <el-option value="" :label="copy.all" />
              <el-option v-for="option in statusOptions" :key="option.value" v-bind="option" />
            </el-select>
          </label>
        </AppQueryPanel>
        <AppDataTable
          ref="table"
          table-key="reference-data-metadata"
          :columns="columns"
          :loader="load"
          :query-mode="mode"
          toolbar-profile="compact"
          :quick-search-enabled="false"
          selection="none"
          :active-row-key="activeId"
          @row-click="select"
          @query-mode-change="switchMode"
          @loaded="onLoaded"
          @load-error="reportList"
        >
          <template #cell-name="{ row }"
            ><div class="metadata-directory-name">
              <strong>{{ row.name }}</strong
              ><small :title="`${row.nId} · ${statusLabel(row.status)}`"
                ><span class="directory-status">{{ statusLabel(row.status) }}</span> ·
                {{ row.nId }}</small
              >
            </div></template
          >
          <template #cell-lastUpdatedOn="{ row }">{{ date(row.lastUpdatedOn) }}</template>
        </AppDataTable>
        <p v-if="!firstLoading && total === 0 && !listError">{{ copy.metadataEmpty }}</p>
      </section>
      <section class="metadata-detail-panel" :aria-label="copy.metadataTitle">
        <el-empty v-if="!selected" :description="copy.metadataSelect" />
        <template v-else>
          <header class="metadata-detail-context">
            <div>
              <h2>{{ selected.name }}</h2>
              <p>
                {{ selected.nId }} ·
                {{ selected.scopeType === 'Tenant' ? copy.tenant : copy.platform }} ·
                {{ copy.revision }} {{ selected.revision }} · {{ statusLabel(selected.status) }} ·
                {{ copy.metadataAttributeCount }}
                {{ selected.attributes.length }}
              </p>
            </div>
            <div class="metadata-actions">
              <el-button
                v-if="canEdit(selected)"
                @click="((formOpen = true), (formMode = 'edit'))"
                >{{ copy.edit }}</el-button
              >
              <el-dropdown
                v-if="
                  canClone(selectedSummary) ||
                  canPublish(selectedSummary) ||
                  canDisable(selectedSummary) ||
                  canReadRuntime(selectedSummary)
                "
                trigger="click"
              >
                <el-button :disabled="busy" data-testid="metadata-more">
                  {{ copy.more }}<el-icon><ArrowDown /></el-icon>
                </el-button>
                <template #dropdown>
                  <el-dropdown-menu>
                    <el-dropdown-item
                      v-if="canClone(selectedSummary)"
                      @click="clone(selectedSummary)"
                      >{{ copy.clone }}</el-dropdown-item
                    >
                    <el-dropdown-item
                      v-if="canPublish(selectedSummary)"
                      data-testid="metadata-publication-open"
                      @click="preparePublication(selectedSummary)"
                    >
                      {{ copy.metadataPublicationCheck }}
                    </el-dropdown-item>
                    <el-dropdown-item
                      v-if="canDisable(selectedSummary)"
                      @click="disable(selectedSummary)"
                      >{{ copy.disable }}</el-dropdown-item
                    >
                    <el-dropdown-item
                      v-if="canReadRuntime(selectedSummary)"
                      data-testid="metadata-runtime-open"
                      @click="openRuntime(selectedSummary)"
                    >
                      {{ copy.metadataRuntime }}
                    </el-dropdown-item>
                  </el-dropdown-menu>
                </template>
              </el-dropdown>
            </div>
          </header>
          <el-tabs v-model="metadataDetailTab">
            <el-tab-pane :label="copy.metadataAttributeTable" name="attributes">
              <AppDataTable
                table-key="reference-data-metadata-inline-attributes"
                :rows="form.attributes"
                :total="form.attributes.length"
                :columns="attributePreviewColumns"
                row-key="localKey"
                toolbar-profile="compact"
                selection="none"
              >
                <template #cell-dataType="{ row }">{{ typeLabel(row.dataType) }}</template>
                <template #cell-required="{ row }">{{
                  row.required ? copy.trueValue : copy.falseValue
                }}</template>
                <template #cell-isArray="{ row }">{{
                  row.isArray ? copy.trueValue : copy.falseValue
                }}</template>
                <template #cell-enabled="{ row }">{{
                  row.enabled ? copy.trueValue : copy.falseValue
                }}</template>
              </AppDataTable>
            </el-tab-pane>
            <el-tab-pane :label="copy.metadataStructurePreview" name="preview">
              <pre class="metadata-structure-preview">{{ structurePreview() }}</pre>
            </el-tab-pane>
            <el-tab-pane :label="copy.metadataPublicationDiff" name="publication">
              <el-alert
                :title="selected.status === 'Draft' ? copy.metadataPublicationCheck : copy.readOnly"
                :type="selected.status === 'Draft' ? 'info' : 'success'"
                :closable="false"
              />
              <p class="metadata-detail-note">{{ copy.metadataPublishHint }}</p>
              <el-button
                v-if="canPublish(selectedSummary)"
                type="primary"
                :loading="busy"
                data-testid="metadata-detail-publication-open"
                @click="preparePublication(selectedSummary)"
                >{{ copy.metadataPublicationCheck }}</el-button
              >
            </el-tab-pane>
          </el-tabs>
        </template>
      </section>
    </div>

    <AppFormDrawer
      :model-value="formOpen"
      :title="
        formMode === 'create'
          ? copy.metadataCreate
          : formMode === 'edit'
            ? copy.metadataEdit
            : copy.metadataDetail
      "
      size="wide"
      :busy="busy"
      @update:model-value="closeForm"
    >
      <el-alert v-if="error" :title="error" type="error" :closable="false" show-icon>
        <p v-if="traceId">{{ copy.traceId }}: {{ traceId }}</p>
        <div v-if="conflict" class="metadata-actions">
          <el-button @click="reloadForm">{{ copy.reload }}</el-button>
          <el-button @click="copyUnsaved">{{ copy.copyUnsaved }}</el-button>
        </div>
      </el-alert>
      <el-alert v-if="isReadOnly" :title="copy.readOnly" type="info" :closable="false" />
      <el-form label-position="top" :disabled="isReadOnly || busy">
        <div class="metadata-form-grid">
          <el-form-item :label="copy.nId" :error="validation.nId" required>
            <el-input
              v-model="form.nId"
              data-testid="metadata-schema-nid"
              :disabled="formMode !== 'create'"
              maxlength="64"
            />
          </el-form-item>
          <el-form-item :label="copy.name" :error="validation.name" required>
            <el-input v-model="form.name" data-testid="metadata-schema-name" maxlength="200" />
          </el-form-item>
          <el-form-item :label="copy.scope">
            <el-select
              v-model="form.scopeType"
              :disabled="formMode !== 'create'"
              @change="changeFormScope"
            >
              <el-option value="Tenant" :label="copy.tenant" />
              <el-option
                v-if="has(PERMISSIONS.referenceDataPlatformManage)"
                value="Platform"
                :label="copy.platform"
              />
            </el-select>
          </el-form-item>
        </div>
        <el-form-item :label="copy.description" :error="validation.description">
          <el-input v-model="form.description" type="textarea" :rows="2" maxlength="2000" />
        </el-form-item>
        <div class="metadata-section-heading">
          <div>
            <h3>{{ copy.metadataAttributes }} ({{ form.attributes.length }}/200)</h3>
            <p>{{ copy.metadataAttributeIdentityHint }}</p>
          </div>
          <el-button
            v-if="canSubmit"
            :icon="Plus"
            :disabled="form.attributes.length >= 200"
            data-testid="metadata-attribute-add"
            @click="addAttribute"
            >{{ copy.metadataAddAttribute }}</el-button
          >
        </div>
        <el-alert
          v-if="validation.attributes"
          :title="validation.attributes"
          type="error"
          :closable="false"
        />
        <el-alert :title="copy.metadataConstraintHint" type="info" :closable="false" />

        <div class="metadata-attribute-list">
          <el-card
            v-for="(attribute, index) in form.attributes"
            :key="attribute.localKey"
            shadow="never"
            class="metadata-attribute-card"
          >
            <template #header>
              <div class="metadata-attribute-header">
                <strong>{{
                  attribute.name || attribute.nId || `${copy.metadataAttributes} ${index + 1}`
                }}</strong>
                <el-button
                  v-if="attributeWritable(attribute) && !attribute.wasPublished"
                  link
                  type="danger"
                  :icon="Delete"
                  @click="removeAttribute(attribute)"
                  >{{ copy.remove }}</el-button
                >
              </div>
            </template>
            <p
              v-if="attributeErrors(attribute).length"
              :data-testid="`metadata-attribute-errors-${index}`"
              class="metadata-field-errors"
              role="alert"
            >
              {{ attributeErrors(attribute).join(' ') }}
            </p>
            <div class="metadata-form-grid metadata-identity-grid">
              <el-form-item
                :label="copy.nId"
                :error="validation[attributeField(attribute, 'nId')]"
                required
              >
                <el-input
                  v-model="attribute.nId"
                  :data-testid="`metadata-attribute-nid-${index}`"
                  :disabled="!attributeWritable(attribute) || Boolean(attribute.id)"
                  maxlength="64"
                />
              </el-form-item>
              <el-form-item
                :label="copy.name"
                :error="validation[attributeField(attribute, 'name')]"
                required
              >
                <el-input
                  v-model="attribute.name"
                  :data-testid="`metadata-attribute-name-${index}`"
                  :disabled="!attributeWritable(attribute)"
                  maxlength="200"
                />
              </el-form-item>
              <el-form-item :label="copy.dataType" required>
                <el-select
                  v-model="attribute.dataType"
                  :data-testid="`metadata-attribute-type-${index}`"
                  :disabled="!attributeWritable(attribute)"
                  @change="normalizeAttribute(attribute)"
                >
                  <el-option
                    v-for="option in dataTypeOptions"
                    :key="option.value"
                    v-bind="option"
                  />
                </el-select>
              </el-form-item>
              <el-form-item
                :label="copy.sort"
                :error="validation[attributeField(attribute, 'sort')]"
              >
                <el-input-number
                  v-model="attribute.sort"
                  :min="0"
                  :precision="0"
                  :disabled="!attributeWritable(attribute)"
                />
              </el-form-item>
            </div>
            <div class="metadata-switches">
              <el-checkbox v-model="attribute.required" :disabled="!attributeWritable(attribute)">{{
                copy.metadataRequired
              }}</el-checkbox>
              <el-checkbox
                v-model="attribute.isArray"
                :disabled="!attributeWritable(attribute)"
                @change="changeArray(attribute)"
                >{{ copy.metadataArray }}</el-checkbox
              >
              <el-checkbox v-model="attribute.enabled" :disabled="!attributeWritable(attribute)">{{
                copy.enabled
              }}</el-checkbox>
              <el-checkbox
                v-model="attribute.defaultConfigured"
                :data-testid="`metadata-attribute-default-configured-${index}`"
                :disabled="!attributeWritable(attribute)"
                >{{ copy.metadataDefault }}</el-checkbox
              >
            </div>

            <div v-if="attribute.dataType === 'String'" class="metadata-form-grid">
              <el-form-item
                :label="copy.dynamicMinLength"
                :error="validation[attributeField(attribute, 'constraints')]"
              >
                <el-input-number
                  v-model="attribute.minLength"
                  :min="0"
                  :precision="0"
                  :disabled="!attributeWritable(attribute)"
                />
              </el-form-item>
              <el-form-item :label="copy.dynamicMaxLength">
                <el-input-number
                  v-model="attribute.maxLength"
                  :min="0"
                  :precision="0"
                  :disabled="!attributeWritable(attribute)"
                />
              </el-form-item>
              <el-form-item :label="copy.dynamicPattern">
                <el-input
                  v-model="attribute.pattern"
                  :data-testid="`metadata-attribute-pattern-${index}`"
                  maxlength="256"
                  :disabled="!attributeWritable(attribute)"
                />
              </el-form-item>
            </div>
            <div
              v-if="attribute.dataType === 'Integer' || attribute.dataType === 'Decimal'"
              class="metadata-form-grid"
            >
              <el-form-item
                :label="copy.dynamicMinValue"
                :error="validation[attributeField(attribute, 'constraints')]"
              >
                <el-input
                  v-model="attribute.minValue"
                  :data-testid="`metadata-attribute-min-value-${index}`"
                  :disabled="!attributeWritable(attribute)"
                />
              </el-form-item>
              <el-form-item :label="copy.dynamicMaxValue">
                <el-input
                  v-model="attribute.maxValue"
                  :data-testid="`metadata-attribute-max-value-${index}`"
                  :disabled="!attributeWritable(attribute)"
                />
              </el-form-item>
            </div>
            <div v-if="attribute.dataType === 'Decimal'" class="metadata-form-grid">
              <el-form-item
                :label="copy.metadataPrecision"
                :error="validation[attributeField(attribute, 'precision')]"
              >
                <el-input-number
                  v-model="attribute.precision"
                  :data-testid="`metadata-attribute-precision-${index}`"
                  :min="1"
                  :max="28"
                  :precision="0"
                  :disabled="!attributeWritable(attribute)"
                />
              </el-form-item>
              <el-form-item :label="copy.dynamicScale">
                <el-input-number
                  v-model="attribute.scale"
                  :data-testid="`metadata-attribute-scale-${index}`"
                  :min="0"
                  :max="12"
                  :precision="0"
                  :disabled="!attributeWritable(attribute)"
                />
              </el-form-item>
              <el-form-item
                :label="copy.metadataUnitDimension"
                :error="validation[attributeField(attribute, 'unitDimensionNId')]"
              >
                <el-select
                  v-model="attribute.unitSelectionKey"
                  :data-testid="`metadata-attribute-unit-dimension-${index}`"
                  filterable
                  :loading="referenceLoading"
                  :disabled="!attributeWritable(attribute)"
                  @change="changeUnitDimension(attribute)"
                >
                  <el-option value="" :label="copy.notConfigured" />
                  <el-option
                    v-for="option in unitDimensionOptions"
                    :key="
                      unitKey(
                        option.nId,
                        option.revision,
                        option.sourceScope,
                        option.sourceTenantNId,
                      )
                    "
                    :value="
                      unitKey(
                        option.nId,
                        option.revision,
                        option.sourceScope,
                        option.sourceTenantNId,
                      )
                    "
                    :label="unitDimensionLabel(option)"
                  />
                </el-select>
              </el-form-item>
              <el-form-item :label="copy.metadataUnitRevision">
                <el-input-number
                  v-model="attribute.unitRevision"
                  :data-testid="`metadata-attribute-unit-revision-${index}`"
                  :min="1"
                  :precision="0"
                  :disabled="true"
                />
              </el-form-item>
              <el-form-item :label="copy.metadataUnitSource">
                <el-select
                  v-model="attribute.unitSourceScope"
                  :data-testid="`metadata-attribute-unit-source-${index}`"
                  :disabled="true"
                >
                  <el-option :value="null" :label="copy.notConfigured" />
                  <el-option value="Platform" :label="copy.platform" />
                  <el-option value="Tenant" :label="copy.tenant" />
                </el-select>
              </el-form-item>
              <el-form-item :label="copy.metadataSourceTenant">
                <el-input
                  v-model="attribute.unitSourceTenantNId"
                  :data-testid="`metadata-attribute-unit-source-tenant-${index}`"
                  :disabled="true"
                />
              </el-form-item>
              <el-form-item :label="copy.metadataDefaultUnit">
                <el-select
                  v-model="attribute.defaultUnitNId"
                  :data-testid="`metadata-attribute-default-unit-${index}`"
                  filterable
                  :loading="referenceLoading"
                  :disabled="!attributeWritable(attribute) || !attribute.unitSelectionKey"
                >
                  <el-option :value="null" :label="copy.notConfigured" />
                  <el-option
                    v-for="unit in defaultUnitOptions(attribute)"
                    :key="unit.nId"
                    :value="unit.nId"
                    :label="`${unit.name} (${unit.nId} · ${unit.symbol})`"
                  />
                </el-select>
              </el-form-item>
            </div>
            <el-form-item
              v-if="attribute.dataType === 'Enum'"
              :label="copy.dictionaryNId"
              :error="validation[attributeField(attribute, 'dictionaryNId')]"
              required
            >
              <el-select
                v-model="attribute.dictionaryNId"
                :data-testid="`metadata-attribute-dictionary-${index}`"
                filterable
                :loading="referenceLoading"
                :disabled="!attributeWritable(attribute)"
              >
                <el-option
                  v-for="option in dictionaryOptions"
                  :key="option.nId"
                  :value="option.nId"
                  :label="dictionaryLabel(option)"
                />
              </el-select>
            </el-form-item>
            <el-form-item
              v-if="attribute.dataType === 'Reference'"
              :label="copy.referenceTarget"
              :error="validation[attributeField(attribute, 'referenceTarget')]"
              required
            >
              <el-input
                v-model="attribute.referenceTarget"
                :data-testid="`metadata-attribute-reference-${index}`"
                :disabled="!attributeWritable(attribute)"
                maxlength="64"
              />
            </el-form-item>
            <el-form-item
              v-if="attribute.defaultConfigured"
              :label="copy.metadataDefault"
              :error="validation[attributeField(attribute, 'defaultValue')]"
            >
              <el-input
                v-if="attribute.isArray"
                v-model="attribute.defaultValue"
                type="textarea"
                :rows="2"
                :placeholder="copy.metadataArrayDefaultHint"
                :disabled="!attributeWritable(attribute)"
              />
              <el-select
                v-else-if="attribute.dataType === 'Boolean'"
                v-model="attribute.defaultValue"
                :disabled="!attributeWritable(attribute)"
              >
                <el-option value="true" :label="copy.trueValue" />
                <el-option value="false" :label="copy.falseValue" />
              </el-select>
              <el-input
                v-else
                v-model="attribute.defaultValue"
                :data-testid="`metadata-attribute-default-${index}`"
                :placeholder="attribute.dataType === 'DateTime' ? '2026-09-05T08:00:00+08:00' : ''"
                :disabled="!attributeWritable(attribute)"
              />
            </el-form-item>
            <el-form-item :label="copy.description">
              <el-input
                v-model="attribute.description"
                type="textarea"
                :rows="2"
                maxlength="2000"
                :disabled="!attributeWritable(attribute)"
              />
            </el-form-item>
          </el-card>
        </div>
        <p v-if="form.attributes.length === 0">{{ copy.metadataNoAttributes }}</p>
      </el-form>
      <template #footer>
        <el-button :disabled="busy" @click="closeForm()">{{ copy.cancel }}</el-button>
        <el-button
          v-if="formMode !== 'view'"
          type="primary"
          :disabled="!canSubmit"
          :loading="busy"
          data-testid="metadata-schema-save"
          @click="save"
          >{{ copy.save }}</el-button
        >
      </template>
    </AppFormDrawer>

    <AppFormDrawer
      :model-value="publicationOpen"
      :title="copy.publicationTitle"
      size="medium"
      :busy="busy"
      @update:model-value="closePublication"
    >
      <el-alert v-if="error" :title="error" type="error" :closable="false" show-icon />
      <div v-if="publication" data-testid="metadata-publish-confirm" class="metadata-publication">
        <p>
          <strong>{{ copy.previousRevision }}:</strong>
          {{ publication.previousRevision ?? copy.firstPublication }}
        </p>
        <section>
          <strong>{{ copy.added }}</strong>
          <p>{{ publication.addedAttributes.join(', ') || copy.noChanges }}</p>
        </section>
        <section>
          <strong>{{ copy.metadataTightened }}</strong>
          <p>{{ publication.tightenedAttributes.join(', ') || copy.noChanges }}</p>
        </section>
        <section>
          <strong>{{ copy.metadataRelaxed }}</strong>
          <p>{{ publication.relaxedAttributes.join(', ') || copy.noChanges }}</p>
        </section>
        <section>
          <strong>{{ copy.disabledItems }}</strong>
          <p>{{ publication.disabledAttributes.join(', ') || copy.noChanges }}</p>
        </section>
        <section>
          <strong>{{ copy.metadataIncompatible }}</strong>
          <p>
            {{
              publication.incompatibleChanges
                .map((item) => `${item.attributeNId}: ${item.code}`)
                .join(', ') || copy.noChanges
            }}
          </p>
        </section>
        <section>
          <strong>{{ copy.metadataErrors }}</strong>
          <p>
            {{
              publication.errors
                .map((item) => `${item.code}${item.field ? ` (${item.field})` : ''}`)
                .join(', ') || copy.validationPassed
            }}
          </p>
        </section>
        <el-alert :title="copy.metadataPublishHint" type="warning" :closable="false" />
      </div>
      <template #footer>
        <el-button :disabled="busy" @click="closePublication()">{{ copy.cancel }}</el-button>
        <el-button
          type="primary"
          :disabled="!publication || publication.errors.length > 0"
          :loading="busy"
          data-testid="metadata-publish"
          @click="publish"
          >{{ copy.publish }}</el-button
        >
      </template>
    </AppFormDrawer>

    <AppFormDrawer
      :model-value="runtimeOpen"
      :title="copy.metadataRuntime"
      size="wide"
      :busy="runtimeLoading"
      @update:model-value="closeRuntime"
    >
      <el-alert v-if="error" :title="error" type="error" :closable="false" show-icon />
      <el-form label-position="top" :disabled="runtimeLoading">
        <el-form-item :label="copy.metadataReadMode">
          <el-select
            v-model="runtimeMode"
            data-testid="metadata-runtime-mode"
            @change="loadRuntime"
          >
            <el-option value="Fixed" :label="copy.metadataFixed" />
            <el-option value="Current" :label="copy.metadataCurrent" />
          </el-select>
        </el-form-item>
      </el-form>
      <el-alert :title="copy.metadataRuntimeHint" type="info" :closable="false" />
      <div v-if="runtimeSchema" class="metadata-runtime-reference">
        <strong>{{ runtimeSchema.nId }}</strong>
        <span data-testid="metadata-runtime-source">{{
          runtimeSchema.sourceScope === 'Tenant' ? copy.tenant : copy.platform
        }}</span>
        <span data-testid="metadata-runtime-revision"
          >{{ copy.revision }} {{ runtimeSchema.revision }}</span
        >
      </div>
      <AppDataTable
        v-if="runtimeSchema"
        table-key="reference-data-metadata-runtime-attributes"
        :rows="runtimeSchema.attributes"
        :total="runtimeSchema.attributes.length"
        :columns="runtimeColumns"
        toolbar-profile="compact"
        selection="none"
      >
        <template #cell-dataType="{ row }">{{ typeLabel(row.dataType) }}</template>
        <template #cell-required="{ row }">{{
          row.required ? copy.trueValue : copy.falseValue
        }}</template>
        <template #cell-isArray="{ row }">{{
          row.isArray ? copy.trueValue : copy.falseValue
        }}</template>
        <template #cell-enabled="{ row }">{{
          row.enabled ? copy.trueValue : copy.falseValue
        }}</template>
        <template #cell-defaultValue="{ row }">{{ displayDefault(row) }}</template>
      </AppDataTable>
      <el-skeleton v-else-if="runtimeLoading" :rows="5" animated />
      <template #footer>
        <el-button :disabled="runtimeLoading" @click="closeRuntime()">{{ copy.close }}</el-button>
      </template>
    </AppFormDrawer>
  </AppPage>
</template>

<style scoped>
.metadata-page {
  display: flex;
  flex: 1 1 auto;
  flex-direction: column;
  min-height: 0;
  overflow: hidden;
}
.metadata-page :deep(.app-page__body) {
  display: flex;
  flex: 1 1 auto;
  flex-direction: column;
  min-height: 0;
  overflow: hidden;
}
.metadata-page :deep(.app-query-panel) {
  flex: 0 0 auto;
}
.metadata-query-field {
  display: grid;
  flex: 0 0 180px;
  max-width: 100%;
  min-width: 0;
  gap: var(--ip-space-2);
}
.metadata-master-detail {
  display: grid;
  flex: 1 1 0;
  grid-template-columns: minmax(250px, 280px) minmax(0, 1fr);
  gap: var(--ip-space-4);
  min-height: 0;
  align-items: stretch;
}
.metadata-master,
.metadata-detail-panel {
  display: flex;
  flex-direction: column;
  min-width: 0;
  min-height: 0;
}
.metadata-master {
  gap: var(--ip-space-3);
  overflow: hidden;
}
.metadata-master :deep(.app-data-table),
.metadata-detail-panel :deep(.app-data-table) {
  flex: 1 1 0;
  min-height: 0;
}
.metadata-master :deep(.app-data-table__card),
.metadata-detail-panel :deep(.app-data-table__card) {
  display: flex;
  flex: 1 1 0;
  flex-direction: column;
  min-height: 0;
}
.metadata-directory-name {
  display: grid;
  min-width: 0;
  gap: 2px;
}
.metadata-directory-name strong,
.metadata-directory-name small {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.metadata-directory-name small {
  color: var(--el-text-color-secondary);
  font-size: 12px;
}
.metadata-detail-panel {
  min-height: 0;
  padding: var(--ip-space-4);
  border: 1px solid var(--el-border-color-light);
  border-radius: var(--ip-radius-md);
  background: var(--el-bg-color);
}
.metadata-detail-panel > :deep(.el-empty) {
  flex: 1 1 auto;
  min-height: 0;
}
.metadata-detail-panel :deep(.el-tabs) {
  display: flex;
  flex: 1 1 0;
  flex-direction: column;
  min-height: 0;
}
.metadata-detail-panel :deep(.el-tabs__content) {
  display: flex;
  flex: 1 1 0;
  flex-direction: column;
  min-height: 0;
  overflow: hidden;
}
.metadata-detail-panel :deep(.el-tab-pane) {
  display: flex;
  flex: 1 1 0;
  flex-direction: column;
  min-height: 0;
  overflow: hidden;
}
.metadata-detail-context {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: var(--ip-space-3);
  margin-bottom: var(--ip-space-3);
}
.metadata-detail-context h2 {
  margin: 0;
}
.metadata-detail-context p,
.metadata-detail-note {
  margin: var(--ip-space-1) 0 0;
  color: var(--el-text-color-secondary);
}
.metadata-structure-preview {
  max-height: 480px;
  margin: 0;
  overflow: auto;
  padding: var(--ip-space-3);
  color: var(--ip-color-text-primary);
  background: var(--ip-color-bg-muted);
  border-radius: var(--ip-radius-sm);
  font-family: var(--ip-font-family-mono);
  white-space: pre-wrap;
}
.metadata-actions,
.metadata-switches,
.metadata-attribute-header,
.metadata-section-heading,
.metadata-runtime-reference {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--ip-space-2);
}
.metadata-actions > .el-button {
  margin-left: 0;
}
.metadata-form-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(190px, 1fr));
  gap: 0 var(--ip-space-4);
}
.metadata-identity-grid {
  grid-template-columns: minmax(160px, 1fr) minmax(180px, 1.4fr) minmax(150px, 1fr) minmax(
      100px,
      0.6fr
    );
}
.metadata-section-heading,
.metadata-attribute-header {
  justify-content: space-between;
}
.metadata-section-heading {
  margin: var(--ip-space-3) 0;
}
.metadata-section-heading h3,
.metadata-section-heading p,
.metadata-publication p {
  margin: 0;
}
.metadata-section-heading p {
  margin-top: var(--ip-space-1);
  color: var(--el-text-color-secondary);
}
.metadata-attribute-list,
.metadata-publication {
  display: grid;
  gap: var(--ip-space-3);
  margin-top: var(--ip-space-3);
}
.metadata-switches {
  margin-bottom: var(--ip-space-3);
}
.metadata-field-errors {
  margin: 0 0 var(--ip-space-3);
  color: var(--el-color-danger);
}
.metadata-runtime-reference {
  margin: var(--ip-space-3) 0;
  padding: var(--ip-space-3);
  background: var(--ip-color-bg-muted);
  border-radius: var(--ip-radius-sm);
}
.metadata-runtime-reference strong {
  margin-right: auto;
}
@media (max-width: 768px) {
  .metadata-master-detail {
    grid-template-columns: 1fr;
  }
  .metadata-section-heading {
    align-items: flex-start;
    flex-direction: column;
  }
  .metadata-identity-grid {
    grid-template-columns: 1fr;
  }
}
</style>
