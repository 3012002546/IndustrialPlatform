<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue'
import { onBeforeRouteLeave } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Delete, Plus } from '@element-plus/icons-vue'
import { ApiError } from '@/api/errors'
import { getReferenceDataApi } from '@/api/referenceData'
import type {
  DynamicConfiguration,
  DynamicConfigurationSummary,
  DynamicField,
  DynamicFieldWrite,
  DynamicPublicationCheck,
  DynamicRecord,
} from '@/api/referenceData/dynamicTypes'
import type { ConfigurationDataType } from '@/api/referenceData/parameterTypes'
import type { DictionaryItem, PublicationStatus, ReferenceScope } from '@/api/referenceData/types'
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
import { useLocalizationStore } from '@/stores/localizationStore'
import ConfigurationValueEditor from './ConfigurationValueEditor.vue'

const api = getReferenceDataApi()
const localization = useLocalizationStore()
const copy = computed(() => localeMessages[localization.locale].referenceData)
const { has } = usePermission()

const master = ref<{
  reload: () => Promise<void>
  setTopQuery: (query: Record<string, unknown>) => void
  clearSelection: () => void
}>()
const recordsTable = ref<{
  reload: () => Promise<void>
  setTopQuery: (query: Record<string, unknown>) => void
}>()
const mode = ref<AppDataTableQueryMode>('top')
const query = reactive({ keyword: '', scopeType: '', status: '' })
const total = ref(0)
const firstLoading = ref(true)
const selectedId = ref<string | null>(null)
const definition = ref<DynamicConfiguration | null>(null)
const detailLoading = ref(false)
const detailTab = ref('fields')
const error = ref('')
const listError = ref('')
const traceId = ref('')
const conflict = ref(false)
const validation = ref<Record<string, string>>({})
const busy = ref(false)

let listRequest: AbortController | undefined
let detailRequest: AbortController | undefined
let detailSequence = 0
let recordsRequest: AbortController | undefined
let recordsSequence = 0
let editorRequest: AbortController | undefined
let editorSequence = 0
let publicationRequest: AbortController | undefined
let publicationSequence = 0
let enumRequest: AbortController | undefined
let enumSequence = 0
let lastQuery = ''

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
const typeOptions = computed(() =>
  (
    [
      'String',
      'Integer',
      'Decimal',
      'Boolean',
      'Date',
      'DateTime',
      'Enum',
      'Json',
      'Reference',
    ] as ConfigurationDataType[]
  ).map((value) => ({ value, label: copy.value[`type${value}`] })),
)
const masterColumns = computed<AppDataTableColumn[]>(() => [
  { field: 'nId', title: copy.value.nId, minWidth: 150, sortable: true, filter: false },
  { field: 'name', title: copy.value.name, minWidth: 170, sortable: true },
  {
    field: 'scopeType',
    title: copy.value.scope,
    width: 100,
    filter: { kind: 'select', options: scopeOptions.value },
  },
  { field: 'revision', title: copy.value.revision, width: 86, sortable: true, filter: false },
  {
    field: 'status',
    title: copy.value.status,
    width: 112,
    sortable: true,
    filter: { kind: 'select', options: statusOptions.value },
  },
  { field: 'fieldCount', title: copy.value.dynamicFieldCount, width: 86, filter: false },
  { field: 'recordCount', title: copy.value.dynamicRecordCount, width: 86, filter: false },
  { field: 'publishedOn', title: copy.value.publishedOn, minWidth: 170, filter: false },
])
const fieldColumns = computed<AppDataTableColumn[]>(() => [
  { field: 'nId', title: copy.value.nId, minWidth: 120, filter: false },
  { field: 'name', title: copy.value.name, minWidth: 150, filter: false },
  { field: 'dataType', title: copy.value.dataType, width: 120, filter: false },
  { field: 'required', title: copy.value.dynamicRequired, width: 85, filter: false },
  { field: 'enabled', title: copy.value.enabled, width: 75, filter: false },
  { field: 'defaultValueJson', title: copy.value.defaultValue, minWidth: 150, filter: false },
  { field: 'sort', title: copy.value.sort, width: 70, filter: false },
])
const recordColumns = computed<AppDataTableColumn[]>(() => [
  { field: 'nId', title: copy.value.nId, minWidth: 130, filter: false },
  { field: 'name', title: copy.value.name, minWidth: 150, filter: false },
  { field: 'category', title: copy.value.dynamicCategory, minWidth: 120, filter: false },
  { field: 'values', title: copy.value.dynamicValues, minWidth: 220, filter: false },
  { field: 'sort', title: copy.value.sort, width: 70, filter: false },
  { field: 'enabled', title: copy.value.enabled, width: 80, filter: false },
])

function statusLabel(status: PublicationStatus) {
  return statusOptions.value.find((item) => item.value === status)?.label ?? status
}
function date(value: string | null) {
  if (!value) return copy.value.noChanges
  return new Intl.DateTimeFormat(localization.locale, {
    dateStyle: 'short',
    timeStyle: 'short',
    timeZone: localization.preferences.timeZone,
  }).format(new Date(value))
}
type DynamicLifecycle = Pick<
  DynamicConfigurationSummary,
  'scopeType' | 'status' | 'isFrozen' | 'isLocked' | 'publishedOn'
>
function scopeWritable(row: Pick<DynamicLifecycle, 'scopeType' | 'isFrozen' | 'isLocked'>) {
  return (
    !row.isFrozen &&
    !row.isLocked &&
    (row.scopeType === 'Tenant' || has(PERMISSIONS.referenceDataPlatformManage))
  )
}
function canEdit(row: DynamicLifecycle) {
  return (
    row.status === 'Draft' &&
    scopeWritable(row) &&
    has(PERMISSIONS.referenceDataDynamicPropertyUpdate)
  )
}
function canClone(row: DynamicLifecycle) {
  return (
    row.publishedOn !== null &&
    scopeWritable(row) &&
    has(PERMISSIONS.referenceDataDynamicPropertyCreate)
  )
}
function canPublish(row: DynamicLifecycle) {
  return (
    row.status === 'Draft' &&
    scopeWritable(row) &&
    has(PERMISSIONS.referenceDataDynamicPropertyPublish)
  )
}
function canDisable(row: DynamicLifecycle) {
  return (
    ['Draft', 'Published'].includes(row.status) &&
    scopeWritable(row) &&
    has(PERMISSIONS.referenceDataDynamicPropertyDisable)
  )
}
function canDisableRecord(row: DynamicRecord) {
  return (
    !!definition.value &&
    definition.value.status === 'Draft' &&
    scopeWritable(definition.value) &&
    row.enabled &&
    !row.isFrozen &&
    !row.isLocked &&
    has(PERMISSIONS.referenceDataDynamicPropertyDisable)
  )
}
function version(
  row: Pick<
    DynamicConfigurationSummary,
    'optimisticVersion' | 'concurrencyVersion'
  > = definition.value!,
) {
  return {
    expectedOptimisticVersion: row.optimisticVersion,
    expectedConcurrencyVersion: row.concurrencyVersion,
  }
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
    'REF-DYNAMIC-CONFIG-FIELD-INVALID': copy.value.dynamicInvalid,
    'REF-DYNAMIC-CONFIG-LIMIT-EXCEEDED': copy.value.dynamicLimit,
    'REF-DYNAMIC-CONFIG-NOT-FOUND': copy.value.notFound,
    'REF-CONFIG-ENUM-VALUE-INVALID': copy.value.dynamicEnumInvalid,
    'REF-INVALID-STATE': copy.value.stateInvalid,
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

function clearSelection() {
  detailSequence++
  detailRequest?.abort()
  recordsSequence++
  recordsRequest?.abort()
  publicationSequence++
  publicationRequest?.abort()
  selectedId.value = null
  definition.value = null
  detailLoading.value = false
}
async function loadDefinitions(request: AppDataTableRequest) {
  const signature = JSON.stringify({
    mode: request.queryMode,
    filters: request.filters,
    sort: request.sort,
  })
  if (lastQuery && lastQuery !== signature) clearSelection()
  lastQuery = signature
  listRequest?.abort()
  listRequest = new AbortController()
  const params = {
    pageIndex: request.pageIndex,
    pageSize: Math.min(request.pageSize, 100),
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
    const result = await api.listDynamicConfigurations(params, { signal: listRequest.signal })
    if (selectedId.value && !result.items.some((item) => item.id === selectedId.value))
      clearSelection()
    return result
  } finally {
    firstLoading.value = false
  }
}
function search() {
  master.value?.setTopQuery({ ...query })
}
function reset() {
  Object.assign(query, { keyword: '', scopeType: '', status: '' })
  search()
}
function switchMode(value: AppDataTableQueryMode) {
  mode.value = value
  Object.assign(query, { keyword: '', scopeType: '', status: '' })
}
function onLoaded(page: { total: number }) {
  total.value = page.total
  listError.value = ''
}
function reportList(caught: unknown) {
  if (!(caught instanceof ApiError && caught.kind === 'cancelled'))
    listError.value = copy.value.unavailable
}
async function selectDefinition(rows: DynamicConfigurationSummary[]) {
  const id = rows[0]?.id ?? null
  if (id === selectedId.value) return
  if (!(await allowDiscard())) return
  closeEditorNow()
  closePublicationNow()
  clearSelection()
  clearError()
  if (!id || !api) return
  selectedId.value = id
  detailLoading.value = true
  detailRequest = new AbortController()
  const sequence = detailSequence
  try {
    const result = await api.getDynamicConfiguration(id, { signal: detailRequest.signal })
    if (sequence !== detailSequence) return
    definition.value = result
    await loadEnumOptions(result.fields, result.scopeType)
    await nextTick()
    if (sequence === detailSequence) await recordsTable.value?.reload()
  } catch (caught) {
    if (sequence === detailSequence) report(caught)
  } finally {
    if (sequence === detailSequence) detailLoading.value = false
  }
}

const recordQuery = reactive({ keyword: '', nId: '', category: '' })
async function loadRecords(request: AppDataTableRequest) {
  const id = selectedId.value
  if (!api || !id) return { items: [], total: 0, pageIndex: 1, pageSize: request.pageSize }
  recordsRequest?.abort()
  recordsRequest = new AbortController()
  const sequence = ++recordsSequence
  const result = await api.listDynamicRecords(
    id,
    {
      pageIndex: request.pageIndex,
      pageSize: Math.min(request.pageSize, 100),
      keyword: String(request.filters.keyword ?? ''),
      nId: String(request.filters.nId ?? ''),
      category: String(request.filters.category ?? ''),
    },
    { signal: recordsRequest.signal },
  )
  if (sequence !== recordsSequence || id !== selectedId.value)
    return { items: [], total: 0, pageIndex: request.pageIndex, pageSize: request.pageSize }
  return result
}
function searchRecords() {
  recordsTable.value?.setTopQuery({ ...recordQuery })
}
function resetRecords() {
  Object.assign(recordQuery, { keyword: '', nId: '', category: '' })
  searchRecords()
}
function recordValues(row: DynamicRecord) {
  return Object.entries(row.valuesJson)
    .map(([key, value]) => `${key}: ${value}`)
    .join(', ')
}

type EditorKind = 'definition' | 'field' | 'record'
const editorKind = ref<EditorKind | null>(null)
const creatingDefinition = ref(false)
const editingFieldId = ref<string | null>(null)
const editingRecord = ref<DynamicRecord | null>(null)
const editorSnapshot = ref('')
const definitionForm = reactive({
  nId: '',
  name: '',
  description: '',
  scopeType: 'Tenant' as ReferenceScope,
})
const fieldForm = reactive<DynamicFieldWrite>({
  nId: '',
  name: '',
  dataType: 'String',
  required: false,
  enabled: true,
  sort: 0,
  defaultValueJson: null,
  minLength: null,
  maxLength: null,
  minValueJson: null,
  maxValueJson: null,
  scale: null,
  pattern: null,
  dictionaryNId: null,
  referenceTarget: null,
  description: null,
})
const recordForm = reactive({
  nId: '',
  name: '',
  category: '',
  sort: 0,
  enabled: true,
  valuesJson: {} as Record<string, string | null>,
})
const editorBusy = ref(false)
const enumItems = ref<Record<string, DictionaryItem[]>>({})
const enumLoading = ref(false)

function currentEditorState() {
  if (editorKind.value === 'definition') return definitionForm
  if (editorKind.value === 'field') return fieldForm
  return recordForm
}
const dirty = computed(
  () => editorKind.value !== null && JSON.stringify(currentEditorState()) !== editorSnapshot.value,
)
const canSubmit = computed(() => {
  if (!editorKind.value || editorBusy.value) return false
  if (editorKind.value === 'definition' && creatingDefinition.value)
    return (
      has(PERMISSIONS.referenceDataDynamicPropertyCreate) &&
      (definitionForm.scopeType === 'Tenant' || has(PERMISSIONS.referenceDataPlatformManage))
    )
  if (
    editorKind.value === 'record' &&
    editingRecord.value?.enabled &&
    !recordForm.enabled &&
    !has(PERMISSIONS.referenceDataDynamicPropertyDisable)
  )
    return false
  return !!definition.value && canEdit(definition.value)
})
function snapshotEditor() {
  editorSnapshot.value = JSON.stringify(currentEditorState())
}
function writeField(item: DynamicField | DynamicFieldWrite): DynamicFieldWrite {
  return {
    nId: item.nId,
    name: item.name,
    dataType: item.dataType,
    required: item.required,
    enabled: item.enabled,
    sort: item.sort,
    defaultValueJson: item.defaultValueJson,
    minLength: item.dataType === 'String' ? item.minLength : null,
    maxLength: item.dataType === 'String' ? item.maxLength : null,
    minValueJson: ['Integer', 'Decimal'].includes(item.dataType) ? item.minValueJson : null,
    maxValueJson: ['Integer', 'Decimal'].includes(item.dataType) ? item.maxValueJson : null,
    scale: item.dataType === 'Decimal' ? item.scale : null,
    pattern: item.dataType === 'String' ? item.pattern : null,
    dictionaryNId: item.dataType === 'Enum' ? item.dictionaryNId : null,
    referenceTarget: item.dataType === 'Reference' ? item.referenceTarget : null,
    description: item.description,
  }
}
function fillDefinition(item: DynamicConfiguration | null) {
  Object.assign(definitionForm, {
    nId: item?.nId ?? '',
    name: item?.name ?? '',
    description: item?.description ?? '',
    scopeType: item?.scopeType ?? 'Tenant',
  })
}
function fillField(item: DynamicField | null) {
  Object.assign(fieldForm, {
    nId: item?.nId ?? '',
    name: item?.name ?? '',
    dataType: item?.dataType ?? 'String',
    required: item?.required ?? false,
    enabled: item?.enabled ?? true,
    sort: item?.sort ?? definition.value?.fields.length ?? 0,
    defaultValueJson: item?.defaultValueJson ?? null,
    minLength: item?.minLength ?? null,
    maxLength: item?.maxLength ?? null,
    minValueJson: item?.minValueJson ?? null,
    maxValueJson: item?.maxValueJson ?? null,
    scale: item?.scale ?? null,
    pattern: item?.pattern ?? null,
    dictionaryNId: item?.dictionaryNId ?? null,
    referenceTarget: item?.referenceTarget ?? null,
    description: item?.description ?? null,
  })
}
function fillRecord(item: DynamicRecord | null) {
  Object.assign(recordForm, {
    nId: item?.nId ?? '',
    name: item?.name ?? '',
    category: item?.category ?? '',
    sort: item?.sort ?? 0,
    enabled: item?.enabled ?? true,
    valuesJson: Object.fromEntries(
      (definition.value?.fields.filter((field) => field.enabled) ?? []).map((field) => [
        field.nId,
        item?.valuesJson[field.nId] ?? null,
      ]),
    ),
  })
}
async function openDefinition(create = false) {
  if (!(await allowDiscard()) || (!create && !definition.value)) return
  cancelEditorRequest()
  clearError()
  fillDefinition(create ? null : definition.value)
  creatingDefinition.value = create
  editingFieldId.value = null
  editingRecord.value = null
  editorKind.value = 'definition'
  snapshotEditor()
}
async function openField(item: DynamicField | null) {
  if (!definition.value || !(await allowDiscard()) || !canEdit(definition.value)) return
  cancelEditorRequest()
  clearError()
  editingFieldId.value = item?.id ?? null
  editingRecord.value = null
  fillField(item)
  editorKind.value = 'field'
  snapshotEditor()
  if (fieldForm.dataType === 'Enum') await loadEnumOptions([fieldForm], definition.value.scopeType)
}
async function openRecord(item: DynamicRecord | null) {
  if (!definition.value || !(await allowDiscard()) || !canEdit(definition.value)) return
  cancelEditorRequest()
  clearError()
  editingRecord.value = item
  editingFieldId.value = null
  fillRecord(item)
  editorKind.value = 'record'
  snapshotEditor()
  await loadEnumOptions(definition.value.fields, definition.value.scopeType)
}
function closeEditorNow() {
  editorSequence++
  editorRequest?.abort()
  editorKind.value = null
  creatingDefinition.value = false
  editingFieldId.value = null
  editingRecord.value = null
  editorBusy.value = false
}
async function allowDiscard() {
  if (busy.value || editorBusy.value) return false
  if (!dirty.value) return true
  try {
    await ElMessageBox.confirm(copy.value.discard, copy.value.dynamicTitle, {
      confirmButtonText: copy.value.close,
      cancelButtonText: copy.value.cancel,
    })
    return true
  } catch {
    return false
  }
}
async function closeEditor(value = false) {
  if (!value && (await allowDiscard())) {
    closeEditorNow()
    clearError()
  }
}
function cancelEditorRequest() {
  editorSequence++
  editorRequest?.abort()
}
function validateJson(value: string | null, key: string, numeric = false) {
  if (value === null) return
  try {
    const parsed: unknown = JSON.parse(value)
    if (parsed === null || (numeric && typeof parsed !== 'number'))
      validation.value[key] = copy.value.jsonInvalid
  } catch {
    validation.value[key] = copy.value.jsonInvalid
  }
}
function validateEditor() {
  validation.value = {}
  if (editorKind.value === 'definition') {
    if (!/^[A-Za-z][A-Za-z0-9_.-]{1,63}$/.test(definitionForm.nId.trim()))
      validation.value.nId = copy.value.invalidNId
    if (!definitionForm.name.trim() || definitionForm.name.length > 200)
      validation.value.name = copy.value.required
  } else if (editorKind.value === 'field') {
    if (!/^[A-Za-z0-9][A-Za-z0-9_.-]{0,63}$/.test(fieldForm.nId.trim()))
      validation.value.nId = copy.value.invalidNId
    if (!fieldForm.name.trim() || fieldForm.name.length > 200)
      validation.value.name = copy.value.required
    if (!Number.isInteger(fieldForm.sort) || fieldForm.sort < 0)
      validation.value.sort = copy.value.invalidSort
    if (
      fieldForm.minLength !== null &&
      fieldForm.maxLength !== null &&
      fieldForm.minLength > fieldForm.maxLength
    )
      validation.value.maxLength = copy.value.dynamicConstraintInvalid
    if (fieldForm.scale !== null && (fieldForm.scale < 0 || fieldForm.scale > 10))
      validation.value.scale = copy.value.dynamicConstraintInvalid
    if (fieldForm.dataType === 'Enum' && !fieldForm.dictionaryNId?.trim())
      validation.value.dictionaryNId = copy.value.required
    if (fieldForm.dataType === 'Reference' && !fieldForm.referenceTarget?.trim())
      validation.value.referenceTarget = copy.value.required
    validateJson(fieldForm.defaultValueJson, 'defaultValue')
    validateJson(fieldForm.minValueJson, 'minValue', true)
    validateJson(fieldForm.maxValueJson, 'maxValue', true)
  } else {
    if (!/^[A-Za-z0-9][A-Za-z0-9_.-]{0,63}$/.test(recordForm.nId.trim()))
      validation.value.nId = copy.value.invalidNId
    if (!Number.isInteger(recordForm.sort) || recordForm.sort < 0)
      validation.value.sort = copy.value.invalidSort
    for (const field of definition.value?.fields.filter((item) => item.enabled) ?? []) {
      const value = recordForm.valuesJson[field.nId] ?? null
      if (field.required && value === null)
        validation.value[`values.${field.nId}`] = copy.value.required
      validateJson(value, `values.${field.nId}`)
    }
  }
  return Object.keys(validation.value).length === 0
}
async function saveEditor() {
  if (!api || !canSubmit.value || !validateEditor()) return
  editorBusy.value = true
  clearError()
  try {
    if (editorKind.value === 'definition') {
      const request = {
        nId: definitionForm.nId.trim(),
        name: definitionForm.name.trim(),
        description: definitionForm.description.trim() || null,
        scopeType: definitionForm.scopeType,
        scopeId: null as null,
        fields: creatingDefinition.value ? [] : (definition.value?.fields.map(writeField) ?? []),
      }
      const result = !creatingDefinition.value
        ? await api.updateDynamicConfiguration(definition.value!.id, {
            name: request.name,
            description: request.description,
            fields: request.fields,
            ...version(),
          })
        : await api.createDynamicConfiguration(request)
      await acceptDefinition(result)
    } else if (editorKind.value === 'field') {
      const fields = definition.value!.fields.map(writeField)
      const currentIndex = definition.value!.fields.findIndex(
        (item) => item.id === editingFieldId.value,
      )
      const next = writeField(fieldForm)
      if (currentIndex < 0) fields.push(next)
      else fields.splice(currentIndex, 1, next)
      const result = await api.updateDynamicConfiguration(definition.value!.id, {
        name: definition.value!.name,
        description: definition.value!.description,
        fields,
        ...version(),
      })
      await acceptDefinition(result)
    } else {
      const valuesJson = Object.fromEntries(
        Object.entries(recordForm.valuesJson).filter(
          (entry): entry is [string, string] => entry[1] !== null,
        ),
      )
      const request = {
        nId: recordForm.nId.trim(),
        name: recordForm.name.trim() || null,
        category: recordForm.category.trim() || null,
        sort: recordForm.sort,
        enabled: recordForm.enabled,
        valuesJson,
        ...version(),
      }
      const mutation = editingRecord.value
        ? await api.updateDynamicRecord(definition.value!.id, editingRecord.value.id, request)
        : await api.addDynamicRecord(definition.value!.id, request)
      definition.value = {
        ...definition.value!,
        optimisticVersion: mutation.optimisticVersion,
        concurrencyVersion: mutation.concurrencyVersion,
      }
      closeEditorNow()
      ElMessage.success(copy.value.saved)
      await refreshDefinition(definition.value.id)
      await recordsTable.value?.reload()
      await master.value?.reload()
    }
  } catch (caught) {
    report(caught)
  } finally {
    editorBusy.value = false
  }
}
async function refreshDefinition(id: string) {
  if (!api || id !== selectedId.value) return
  detailRequest?.abort()
  detailRequest = new AbortController()
  const sequence = ++detailSequence
  try {
    const result = await api.getDynamicConfiguration(id, { signal: detailRequest.signal })
    if (sequence === detailSequence && id === selectedId.value) definition.value = result
  } catch (caught) {
    if (sequence === detailSequence) report(caught)
  }
}
async function acceptDefinition(result: DynamicConfiguration) {
  definition.value = result
  selectedId.value = result.id
  closeEditorNow()
  ElMessage.success(`${copy.value.saved}: ${result.nId}`)
  await master.value?.reload()
  await nextTick()
  await recordsTable.value?.reload()
}
async function removeField(item: DynamicField) {
  if (
    !api ||
    !definition.value ||
    item.hasHadValue ||
    item.wasPublished ||
    !canEdit(definition.value)
  )
    return
  try {
    await ElMessageBox.confirm(copy.value.dynamicRemoveFieldHint, copy.value.remove, {
      confirmButtonText: copy.value.remove,
      cancelButtonText: copy.value.cancel,
      type: 'warning',
    })
  } catch {
    return
  }
  busy.value = true
  clearError()
  try {
    const result = await api.updateDynamicConfiguration(definition.value.id, {
      name: definition.value.name,
      description: definition.value.description,
      fields: definition.value.fields.filter((field) => field.id !== item.id).map(writeField),
      ...version(),
    })
    definition.value = result
    await master.value?.reload()
  } catch (caught) {
    report(caught)
  } finally {
    busy.value = false
  }
}
async function reloadEditor() {
  if (!api || !definition.value || !(await allowDiscard())) return
  const id = definition.value.id
  const kind = editorKind.value
  const fieldId = editingFieldId.value
  const recordId = editingRecord.value?.id
  const recordNId = editingRecord.value?.nId
  cancelEditorRequest()
  editorRequest = new AbortController()
  const sequence = editorSequence
  editorBusy.value = true
  try {
    const result = await api.getDynamicConfiguration(id, { signal: editorRequest.signal })
    if (sequence !== editorSequence) return
    definition.value = result
    clearError()
    if (kind === 'definition') fillDefinition(result)
    if (kind === 'field') fillField(result.fields.find((item) => item.id === fieldId) ?? null)
    if (kind === 'record' && recordId && recordNId) {
      const records = await api.listDynamicRecords(
        id,
        { pageIndex: 1, pageSize: 100, nId: recordNId },
        { signal: editorRequest.signal },
      )
      if (sequence !== editorSequence) return
      const record = records.items.find((item) => item.id === recordId) ?? null
      editingRecord.value = record
      fillRecord(record)
    }
    if (kind === 'record' && !recordId) {
      editingRecord.value = null
      fillRecord(null)
    }
    snapshotEditor()
  } catch (caught) {
    if (sequence === editorSequence) report(caught)
  } finally {
    if (sequence === editorSequence) editorBusy.value = false
  }
}
async function copyUnsaved() {
  try {
    await navigator.clipboard.writeText(JSON.stringify(currentEditorState(), null, 2))
    ElMessage.success(copy.value.copied)
  } catch {
    ElMessage.warning(copy.value.copyFailed)
  }
}

async function loadEnumOptions(
  items: Pick<DynamicFieldWrite, 'nId' | 'dataType' | 'dictionaryNId'>[],
  scope: ReferenceScope,
) {
  enumSequence++
  enumRequest?.abort()
  enumRequest = new AbortController()
  const sequence = enumSequence
  const request = enumRequest
  enumLoading.value = true
  const next: Record<string, DictionaryItem[]> = {}
  try {
    for (const field of items.filter((item) => item.dataType === 'Enum' && item.dictionaryNId)) {
      const nId = field.dictionaryNId!.trim()
      if (!api) break
      if (scope === 'Platform') {
        const page = await api.listDictionaries(
          { pageIndex: 1, pageSize: 100, keyword: nId, scopeType: 'Platform', status: 'Published' },
          { signal: request.signal },
        )
        const summary = page.items.find((item) => item.nId === nId.toUpperCase())
        if (summary) {
          const dictionary = await api.getDictionary(summary.id, { signal: request.signal })
          next[field.nId] = dictionary.items.filter((item) => item.enabled)
        }
      } else {
        const dictionary = await api.getEffectiveDictionary(nId, { signal: request.signal })
        next[field.nId] = dictionary.items.filter((item) => item.enabled)
      }
    }
    if (sequence === enumSequence) enumItems.value = next
  } catch (caught) {
    if (sequence === enumSequence && !(caught instanceof ApiError && caught.kind === 'cancelled'))
      report(caught)
  } finally {
    if (sequence === enumSequence) enumLoading.value = false
  }
}
watch(
  () => [fieldForm.nId, fieldForm.dataType, fieldForm.dictionaryNId] as const,
  ([, type]) => {
    if (editorKind.value === 'field' && type === 'Enum' && definition.value)
      void loadEnumOptions([fieldForm], definition.value.scopeType)
  },
)

const publicationOpen = ref(false)
const publication = ref<DynamicPublicationCheck | null>(null)
function closePublicationNow() {
  publicationSequence++
  publicationRequest?.abort()
  publicationOpen.value = false
  publication.value = null
}
async function preparePublication() {
  if (!api || !definition.value || !canPublish(definition.value) || !(await allowDiscard())) return
  clearError()
  publicationSequence++
  publicationRequest?.abort()
  publicationRequest = new AbortController()
  const sequence = publicationSequence
  try {
    const result = await api.checkDynamicPublication(definition.value.id, {
      signal: publicationRequest.signal,
    })
    if (sequence !== publicationSequence) return
    publication.value = result
    publicationOpen.value = true
  } catch (caught) {
    if (sequence === publicationSequence) report(caught)
  }
}
function closePublication(value = false) {
  if (!value && !busy.value) closePublicationNow()
}
function publicationIssue(code: string) {
  if (code === 'REF-DYNAMIC-CONFIG-LIMIT-EXCEEDED') return copy.value.dynamicLimit
  if (code === 'REF-INVALID-STATE') return copy.value.stateInvalid
  if (code === 'REF-CONFIG-ENUM-VALUE-INVALID') return copy.value.dynamicEnumInvalid
  return copy.value.dynamicInvalid
}
function typeLabel(type: ConfigurationDataType) {
  return copy.value[`type${type}`]
}
async function publish() {
  if (
    !api ||
    !definition.value ||
    !canPublish(definition.value) ||
    !publication.value ||
    publication.value.errors.length ||
    busy.value
  )
    return
  busy.value = true
  clearError()
  try {
    const result = await api.publishDynamicConfiguration(definition.value.id, version())
    definition.value = result
    closePublicationNow()
    ElMessage.success(copy.value.publishedSuccess)
    await master.value?.reload()
  } catch (caught) {
    report(caught)
  } finally {
    busy.value = false
  }
}
async function clone() {
  if (!api || !definition.value || !canClone(definition.value) || busy.value) return
  busy.value = true
  clearError()
  try {
    const result = await api.cloneDynamicConfiguration(definition.value.id, version())
    definition.value = result
    selectedId.value = result.id
    ElMessage.success(copy.value.cloned)
    await master.value?.reload()
    await nextTick()
    await recordsTable.value?.reload()
  } catch (caught) {
    report(caught)
  } finally {
    busy.value = false
  }
}
async function disableDefinition() {
  if (!api || !definition.value || !canDisable(definition.value) || busy.value) return
  let reason = ''
  try {
    const result = await ElMessageBox.prompt(copy.value.disableHint, copy.value.reason, {
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
    definition.value = await api.disableDynamicConfiguration(definition.value.id, {
      ...version(),
      changeReason: reason,
    })
    ElMessage.success(copy.value.disabledSuccess)
    await master.value?.reload()
  } catch (caught) {
    report(caught)
  } finally {
    busy.value = false
  }
}
async function disableRecord(row: DynamicRecord) {
  if (!api || !definition.value || !canDisableRecord(row) || busy.value) return
  let reason = ''
  try {
    const result = await ElMessageBox.prompt(
      copy.value.dynamicDisableRecordHint,
      copy.value.reason,
      {
        confirmButtonText: copy.value.disable,
        cancelButtonText: copy.value.cancel,
        inputValidator: (value) =>
          (Boolean(value?.trim()) && value!.length <= 1000) || copy.value.required,
      },
    )
    reason = result.value
  } catch {
    return
  }
  busy.value = true
  clearError()
  try {
    const mutation = await api.disableDynamicRecord(definition.value.id, row.id, {
      ...version(),
      changeReason: reason,
    })
    definition.value = {
      ...definition.value,
      optimisticVersion: mutation.optimisticVersion,
      concurrencyVersion: mutation.concurrencyVersion,
    }
    await refreshDefinition(definition.value.id)
    await recordsTable.value?.reload()
    await master.value?.reload()
  } catch (caught) {
    report(caught)
  } finally {
    busy.value = false
  }
}

function beforeUnload(event: BeforeUnloadEvent) {
  if (dirty.value) event.preventDefault()
}
onBeforeRouteLeave(allowDiscard)
onMounted(async () => {
  window.addEventListener('beforeunload', beforeUnload)
  await nextTick()
  await master.value?.reload()
})
onBeforeUnmount(() => {
  listRequest?.abort()
  detailRequest?.abort()
  recordsRequest?.abort()
  editorRequest?.abort()
  publicationRequest?.abort()
  enumRequest?.abort()
  detailSequence++
  recordsSequence++
  editorSequence++
  publicationSequence++
  enumSequence++
  window.removeEventListener('beforeunload', beforeUnload)
})
</script>

<template>
  <AppPage
    data-testid="reference-data-dynamic-properties"
    :title="copy.dynamicTitle"
    :description="copy.dynamicDescription"
  >
    <template #heading-meta
      ><span>{{ total }}</span></template
    >
    <template #actions>
      <PermissionGate :permission-n-id="PERMISSIONS.referenceDataDynamicPropertyCreate">
        <el-button
          type="primary"
          :icon="Plus"
          data-testid="dynamic-property-create"
          @click="openDefinition(true)"
          >{{ copy.dynamicCreate }}</el-button
        >
      </PermissionGate>
    </template>

    <el-alert
      v-if="error || listError"
      :title="error || listError"
      type="error"
      :closable="false"
      show-icon
    >
      <p v-if="traceId">{{ copy.traceId }}: {{ traceId }}</p>
    </el-alert>
    <AppQueryPanel v-if="mode === 'top'" show-actions grid @submit="search" @reset="reset">
      <label class="dynamic-query-field"
        ><span>{{ copy.keyword }}</span
        ><el-input
          v-model="query.keyword"
          :aria-label="copy.keyword"
          maxlength="200"
          clearable
          @keyup.enter="search"
      /></label>
      <label class="dynamic-query-field"
        ><span>{{ copy.scope }}</span
        ><el-select v-model="query.scopeType" :aria-label="copy.scope" clearable
          ><el-option
            v-for="option in scopeOptions"
            :key="option.value"
            v-bind="option" /></el-select
      ></label>
      <label class="dynamic-query-field"
        ><span>{{ copy.status }}</span
        ><el-select v-model="query.status" :aria-label="copy.status" clearable
          ><el-option
            v-for="option in statusOptions"
            :key="option.value"
            v-bind="option" /></el-select
      ></label>
    </AppQueryPanel>
    <el-skeleton v-if="firstLoading" :rows="3" animated :aria-label="copy.dynamicLoading" />
    <AppDataTable
      ref="master"
      table-key="reference-data-dynamic-properties"
      :columns="masterColumns"
      :loader="loadDefinitions"
      :query-mode="mode"
      selection="single"
      :selected-row-key="selectedId"
      :toolbar-labels="true"
      @selection-change="selectDefinition"
      @query-mode-change="switchMode"
      @loaded="onLoaded"
      @load-error="reportList"
    >
      <template #cell-scopeType="{ row }">{{
        row.scopeType === 'Tenant' ? copy.tenant : copy.platform
      }}</template>
      <template #cell-status="{ row }"
        ><el-tag
          :type="
            row.status === 'Published' ? 'success' : row.status === 'Draft' ? 'info' : 'warning'
          "
          >{{ statusLabel(row.status) }}</el-tag
        ></template
      >
      <template #cell-publishedOn="{ row }">{{ date(row.publishedOn) }}</template>
    </AppDataTable>
    <p v-if="!firstLoading && total === 0 && !listError">{{ copy.dynamicEmpty }}</p>

    <section class="dynamic-property-detail" :aria-busy="detailLoading">
      <p v-if="!selectedId">{{ copy.dynamicSelect }}</p>
      <el-skeleton v-else-if="detailLoading && !definition" :rows="4" animated />
      <template v-else-if="definition">
        <header class="dynamic-property-context">
          <div>
            <h2>{{ definition.name }}</h2>
            <p>
              {{ definition.nId }} · {{ copy.revision }} {{ definition.revision }} ·
              {{ statusLabel(definition.status) }}
            </p>
          </div>
          <div class="dynamic-actions">
            <el-button v-if="canEdit(definition)" @click="openDefinition()">{{
              copy.dynamicEditDefinition
            }}</el-button>
            <el-button v-if="canClone(definition)" @click="clone">{{ copy.clone }}</el-button>
            <el-button
              v-if="canDisable(definition)"
              type="danger"
              plain
              @click="disableDefinition"
              >{{ copy.disable }}</el-button
            >
            <el-button v-if="canPublish(definition)" type="primary" @click="preparePublication">{{
              copy.dynamicPublicationCheck
            }}</el-button>
          </div>
        </header>
        <el-alert
          v-if="definition.status !== 'Draft' || !scopeWritable(definition)"
          :title="definition.status === 'Draft' ? copy.protected : copy.readOnly"
          type="info"
          :closable="false"
        />
        <el-alert :title="copy.dynamicReplacementHint" type="info" :closable="false" />
        <el-tabs v-model="detailTab">
          <el-tab-pane :label="copy.dynamicFields" name="fields">
            <div class="dynamic-section-heading">
              <h3>{{ copy.dynamicFields }} ({{ definition.fields.length }}/100)</h3>
              <el-button
                v-if="canEdit(definition)"
                :disabled="definition.fields.length >= 100"
                :icon="Plus"
                @click="openField(null)"
                >{{ copy.dynamicAddField }}</el-button
              >
            </div>
            <p>{{ copy.dynamicFieldIdentityHint }}</p>
            <AppDataTable
              table-key="reference-data-dynamic-fields"
              :rows="definition.fields"
              :total="definition.fields.length"
              :columns="fieldColumns"
              toolbar-profile="compact"
              selection="none"
            >
              <template #cell-dataType="{ row }">{{ typeLabel(row.dataType) }}</template>
              <template #cell-required="{ row }">{{
                row.required ? copy.trueValue : copy.falseValue
              }}</template>
              <template #cell-enabled="{ row }">{{
                row.enabled ? copy.trueValue : copy.falseValue
              }}</template>
              <template #actions="{ row }">
                <div v-if="canEdit(definition)" class="dynamic-actions">
                  <el-button link type="primary" @click="openField(row)">{{
                    copy.dynamicEditField
                  }}</el-button>
                  <el-button
                    v-if="!row.hasHadValue && !row.wasPublished"
                    link
                    type="danger"
                    :icon="Delete"
                    @click="removeField(row)"
                    >{{ copy.remove }}</el-button
                  >
                </div>
              </template>
            </AppDataTable>
            <p v-if="definition.fields.length === 0">{{ copy.dynamicNoFields }}</p>
          </el-tab-pane>
          <el-tab-pane :label="copy.dynamicRecords" name="records">
            <div class="dynamic-section-heading">
              <h3>{{ copy.dynamicRecords }}</h3>
              <el-button v-if="canEdit(definition)" :icon="Plus" @click="openRecord(null)">{{
                copy.dynamicAddRecord
              }}</el-button>
            </div>
            <AppQueryPanel show-actions grid @submit="searchRecords" @reset="resetRecords">
              <label class="dynamic-query-field"
                ><span>{{ copy.keyword }}</span
                ><el-input
                  v-model="recordQuery.keyword"
                  :aria-label="copy.keyword"
                  @keyup.enter="searchRecords"
              /></label>
              <label class="dynamic-query-field"
                ><span>{{ copy.nId }}</span
                ><el-input
                  v-model="recordQuery.nId"
                  :aria-label="copy.nId"
                  @keyup.enter="searchRecords"
              /></label>
              <label class="dynamic-query-field"
                ><span>{{ copy.dynamicCategory }}</span
                ><el-input
                  v-model="recordQuery.category"
                  :aria-label="copy.dynamicCategory"
                  @keyup.enter="searchRecords"
              /></label>
            </AppQueryPanel>
            <AppDataTable
              :key="definition.id"
              ref="recordsTable"
              table-key="reference-data-dynamic-records"
              :columns="recordColumns"
              :loader="loadRecords"
              query-mode="top"
              toolbar-profile="compact"
              selection="none"
            >
              <template #cell-values="{ row }">{{ recordValues(row) }}</template>
              <template #cell-enabled="{ row }">{{
                row.enabled ? copy.trueValue : copy.falseValue
              }}</template>
              <template #actions="{ row }">
                <div
                  v-if="
                    (canEdit(definition) && !row.isFrozen && !row.isLocked) || canDisableRecord(row)
                  "
                  class="dynamic-actions"
                >
                  <el-button
                    v-if="canEdit(definition) && !row.isFrozen && !row.isLocked"
                    link
                    type="primary"
                    @click="openRecord(row)"
                    >{{ copy.dynamicEditRecord }}</el-button
                  >
                  <el-button
                    v-if="canDisableRecord(row)"
                    link
                    type="danger"
                    @click="disableRecord(row)"
                    >{{ copy.disable }}</el-button
                  >
                </div>
              </template>
            </AppDataTable>
          </el-tab-pane>
          <el-tab-pane :label="copy.dynamicPublication" name="publication">
            <p>{{ copy.publicationHint }}</p>
            <dl class="dynamic-summary">
              <dt>{{ copy.dynamicFieldCount }}</dt>
              <dd>{{ definition.fields.length }}</dd>
              <dt>{{ copy.dynamicRecordCount }}</dt>
              <dd>{{ definition.recordCount }}</dd>
              <dt>{{ copy.dynamicValueCount }}</dt>
              <dd>{{ definition.valueCount }}</dd>
              <dt>{{ copy.publishedOn }}</dt>
              <dd>{{ date(definition.publishedOn) }}</dd>
            </dl>
            <el-button v-if="canPublish(definition)" type="primary" @click="preparePublication">{{
              copy.dynamicPublicationCheck
            }}</el-button>
          </el-tab-pane>
        </el-tabs>
      </template>
    </section>

    <AppFormDrawer
      :model-value="editorKind !== null"
      :title="
        editorKind === 'definition'
          ? copy.dynamicDefinitionDetails
          : editorKind === 'field'
            ? copy.dynamicFieldDetails
            : copy.dynamicRecordDetails
      "
      size="wide"
      :busy="editorBusy"
      @update:model-value="closeEditor"
    >
      <el-alert v-if="error" :title="error" type="error" :closable="false" show-icon>
        <p v-if="traceId">{{ copy.traceId }}: {{ traceId }}</p>
        <div v-if="conflict" class="dynamic-actions">
          <el-button @click="reloadEditor">{{ copy.reload }}</el-button>
          <el-button @click="copyUnsaved">{{ copy.copyUnsaved }}</el-button>
        </div>
      </el-alert>
      <el-alert v-if="!canSubmit" :title="copy.readOnly" type="info" :closable="false" />

      <el-form
        v-if="editorKind === 'definition'"
        label-position="top"
        :disabled="!canSubmit || editorBusy"
      >
        <el-form-item :label="copy.nId" :error="validation.nId" required>
          <el-input
            v-model="definitionForm.nId"
            data-testid="dynamic-definition-nid"
            :disabled="!creatingDefinition"
            maxlength="64"
          />
        </el-form-item>
        <el-form-item :label="copy.name" :error="validation.name" required>
          <el-input
            v-model="definitionForm.name"
            data-testid="dynamic-definition-name"
            maxlength="200"
          />
        </el-form-item>
        <el-form-item :label="copy.scope">
          <el-select v-model="definitionForm.scopeType" :disabled="!creatingDefinition">
            <el-option value="Tenant" :label="copy.tenant" />
            <el-option
              v-if="has(PERMISSIONS.referenceDataPlatformManage)"
              value="Platform"
              :label="copy.platform"
            />
          </el-select>
        </el-form-item>
        <el-form-item :label="copy.description">
          <el-input
            v-model="definitionForm.description"
            type="textarea"
            :rows="3"
            maxlength="2000"
          />
        </el-form-item>
      </el-form>

      <el-form
        v-else-if="editorKind === 'field'"
        label-position="top"
        :disabled="!canSubmit || editorBusy"
      >
        <div class="dynamic-form-grid">
          <el-form-item :label="copy.nId" :error="validation.nId" required>
            <el-input v-model="fieldForm.nId" :disabled="editingFieldId !== null" maxlength="64" />
          </el-form-item>
          <el-form-item :label="copy.name" :error="validation.name" required>
            <el-input v-model="fieldForm.name" data-testid="dynamic-field-name" maxlength="200" />
          </el-form-item>
          <el-form-item :label="copy.dataType">
            <el-select
              v-model="fieldForm.dataType"
              :disabled="
                !!definition?.fields.find((item) => item.id === editingFieldId)?.hasHadValue ||
                !!definition?.fields.find((item) => item.id === editingFieldId)?.wasPublished
              "
            >
              <el-option v-for="option in typeOptions" :key="option.value" v-bind="option" />
            </el-select>
          </el-form-item>
          <el-form-item :label="copy.sort" :error="validation.sort">
            <el-input-number v-model="fieldForm.sort" :min="0" :step="1" :precision="0" />
          </el-form-item>
          <el-form-item :label="copy.dynamicRequired"
            ><el-switch v-model="fieldForm.required"
          /></el-form-item>
          <el-form-item :label="copy.enabled"
            ><el-switch v-model="fieldForm.enabled"
          /></el-form-item>
        </div>
        <el-form-item :label="copy.defaultValue" :error="validation.defaultValue">
          <ConfigurationValueEditor
            v-model="fieldForm.defaultValueJson"
            :data-type="fieldForm.dataType"
            :label="copy.defaultValue"
            :enum-items="enumItems[fieldForm.nId] ?? []"
            :disabled="enumLoading"
          />
        </el-form-item>
        <div v-if="fieldForm.dataType === 'String'" class="dynamic-form-grid">
          <el-form-item :label="copy.dynamicMinLength" :error="validation.minLength"
            ><el-input-number v-model="fieldForm.minLength" :min="0"
          /></el-form-item>
          <el-form-item :label="copy.dynamicMaxLength" :error="validation.maxLength"
            ><el-input-number v-model="fieldForm.maxLength" :min="0"
          /></el-form-item>
          <el-form-item :label="copy.dynamicPattern" :error="validation.pattern"
            ><el-input v-model="fieldForm.pattern" maxlength="256"
          /></el-form-item>
        </div>
        <div
          v-if="fieldForm.dataType === 'Integer' || fieldForm.dataType === 'Decimal'"
          class="dynamic-form-grid"
        >
          <el-form-item :label="copy.dynamicMinValue" :error="validation.minValue"
            ><el-input v-model="fieldForm.minValueJson"
          /></el-form-item>
          <el-form-item :label="copy.dynamicMaxValue" :error="validation.maxValue"
            ><el-input v-model="fieldForm.maxValueJson"
          /></el-form-item>
          <el-form-item
            v-if="fieldForm.dataType === 'Decimal'"
            :label="copy.dynamicScale"
            :error="validation.scale"
            ><el-input-number v-model="fieldForm.scale" :min="0" :max="10"
          /></el-form-item>
        </div>
        <el-form-item
          v-if="fieldForm.dataType === 'Enum'"
          :label="copy.dictionaryNId"
          :error="validation.dictionaryNId"
          required
        >
          <el-input v-model="fieldForm.dictionaryNId" maxlength="64" />
        </el-form-item>
        <el-form-item
          v-if="fieldForm.dataType === 'Reference'"
          :label="copy.referenceTarget"
          :error="validation.referenceTarget"
          required
        >
          <el-input v-model="fieldForm.referenceTarget" maxlength="128" />
        </el-form-item>
        <el-form-item :label="copy.description"
          ><el-input v-model="fieldForm.description" type="textarea" :rows="3" maxlength="2000"
        /></el-form-item>
      </el-form>

      <el-form
        v-else-if="editorKind === 'record'"
        label-position="top"
        :disabled="!canSubmit || editorBusy"
      >
        <div class="dynamic-form-grid">
          <el-form-item :label="copy.nId" :error="validation.nId" required>
            <el-input
              v-model="recordForm.nId"
              data-testid="dynamic-record-nid"
              :disabled="editingRecord !== null"
              maxlength="64"
            />
          </el-form-item>
          <el-form-item :label="copy.name"
            ><el-input v-model="recordForm.name" maxlength="200"
          /></el-form-item>
          <el-form-item :label="copy.dynamicCategory"
            ><el-input v-model="recordForm.category" maxlength="100"
          /></el-form-item>
          <el-form-item :label="copy.sort" :error="validation.sort"
            ><el-input-number v-model="recordForm.sort" :min="0" :step="1" :precision="0"
          /></el-form-item>
          <el-form-item :label="copy.enabled"
            ><el-switch
              v-model="recordForm.enabled"
              :disabled="
                !!editingRecord?.enabled && !has(PERMISSIONS.referenceDataDynamicPropertyDisable)
              "
          /></el-form-item>
        </div>
        <div class="dynamic-values-grid">
          <el-form-item
            v-for="field in definition?.fields.filter((item) => item.enabled) ?? []"
            :key="field.id"
            :label="`${field.name} (${field.nId})`"
            :error="validation[`values.${field.nId}`]"
            :required="field.required"
          >
            <ConfigurationValueEditor
              :model-value="recordForm.valuesJson[field.nId] ?? null"
              @update:model-value="recordForm.valuesJson[field.nId] = $event"
              :data-type="field.dataType"
              :label="field.name"
              :enum-items="enumItems[field.nId] ?? []"
              :disabled="enumLoading"
            />
          </el-form-item>
        </div>
      </el-form>
      <template #footer>
        <el-button :disabled="editorBusy" @click="closeEditor()">{{ copy.cancel }}</el-button>
        <el-button
          v-if="editorKind"
          type="primary"
          :disabled="!canSubmit"
          :loading="editorBusy"
          :data-testid="
            editorKind === 'field'
              ? 'dynamic-field-save'
              : editorKind === 'record'
                ? 'dynamic-record-save'
                : 'dynamic-definition-save'
          "
          @click="saveEditor"
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
      <p>{{ copy.publicationHint }}</p>
      <el-alert v-if="error" :title="error" type="error" :closable="false" />
      <template v-if="publication">
        <p>
          {{ copy.previousRevision }}: {{ publication.previousRevision ?? copy.firstPublication }}
        </p>
        <dl class="dynamic-summary">
          <dt>{{ copy.added }}</dt>
          <dd>{{ publication.addedFields.join(', ') || copy.noChanges }}</dd>
          <dt>{{ copy.changed }}</dt>
          <dd>{{ publication.changedFields.join(', ') || copy.noChanges }}</dd>
          <dt>{{ copy.disabledItems }}</dt>
          <dd>{{ publication.disabledFields.join(', ') || copy.noChanges }}</dd>
          <dt>{{ copy.dynamicRecordCount }}</dt>
          <dd>{{ publication.recordCount }}</dd>
          <dt>{{ copy.dynamicValueCount }}</dt>
          <dd>{{ publication.valueCount }}</dd>
        </dl>
        <el-alert
          v-for="issue in publication.errors"
          :key="issue.code + issue.field"
          :title="`${publicationIssue(issue.code)}${issue.field ? ` · ${issue.field}` : ''}`"
          type="error"
          :closable="false"
        />
        <el-alert
          v-if="publication.errors.length === 0"
          :title="copy.validationPassed"
          type="success"
          :closable="false"
        />
      </template>
      <template #footer>
        <el-button :disabled="busy" @click="closePublication()">{{ copy.cancel }}</el-button>
        <el-button
          type="primary"
          :disabled="!publication || publication.errors.length > 0 || conflict"
          :loading="busy"
          data-testid="dynamic-publish-confirm"
          @click="publish"
          >{{ copy.publish }}</el-button
        >
      </template>
    </AppFormDrawer>
  </AppPage>
</template>

<style scoped>
.dynamic-query-field {
  display: grid;
  gap: var(--ip-space-2);
  min-width: 0;
  flex: 0 0 180px;
  max-width: 100%;
}
.dynamic-property-detail {
  display: grid;
  gap: var(--ip-space-3);
  margin-top: var(--ip-space-4);
  min-height: 120px;
}
.dynamic-property-context,
.dynamic-section-heading {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--ip-space-3);
}
.dynamic-property-context h2,
.dynamic-section-heading h3 {
  margin: 0;
}
.dynamic-property-context p {
  margin: var(--ip-space-1) 0 0;
  color: var(--el-text-color-secondary);
}
.dynamic-actions {
  display: inline-flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--ip-space-1);
}
.dynamic-actions > .el-button {
  margin-left: 0;
}
.dynamic-form-grid,
.dynamic-values-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(210px, 1fr));
  gap: 0 var(--ip-space-4);
}
.dynamic-values-grid {
  grid-template-columns: repeat(auto-fit, minmax(280px, 1fr));
}
.dynamic-summary {
  display: grid;
  grid-template-columns: max-content 1fr;
  gap: var(--ip-space-2) var(--ip-space-4);
}
.dynamic-summary dd {
  margin: 0;
  overflow-wrap: anywhere;
}
@media (max-width: 768px) {
  .dynamic-property-context,
  .dynamic-section-heading {
    align-items: flex-start;
    flex-direction: column;
  }
}
</style>
