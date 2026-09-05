<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue'
import { onBeforeRouteLeave } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Delete, Plus } from '@element-plus/icons-vue'
import { ApiError } from '@/api/errors'
import { getReferenceDataApi } from '@/api/referenceData'
import type {
  PublicationStatus,
  ReferenceDataQuery,
  ReferenceScope,
} from '@/api/referenceData/types'
import type {
  CreateUnitDimension,
  RuntimeUnitDimension,
  UnitConversionKind,
  UnitConversionResult,
  UnitDefinitionWrite,
  UnitDimension,
  UnitDimensionSummary,
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
import { useLocalizationStore } from '@/stores/localizationStore'

const api = getReferenceDataApi()
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
let detailSequence = 0

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
const conversionKindOptions = computed(() => [
  { value: 'Ratio', label: copy.value.unitRatio },
  { value: 'AbsoluteTemperature', label: copy.value.unitAbsoluteTemperature },
])
const roundingOptions = computed(() => [
  { value: 'ToEven', label: copy.value.unitToEven },
  { value: 'AwayFromZero', label: copy.value.unitAwayFromZero },
])
const columns = computed<AppDataTableColumn[]>(() => [
  { field: 'nId', title: copy.value.nId, minWidth: 140, sortable: true, filter: false },
  { field: 'name', title: copy.value.name, minWidth: 160, sortable: true },
  {
    field: 'scopeType',
    title: copy.value.scope,
    width: 100,
    filter: { kind: 'select', options: scopeOptions.value },
  },
  { field: 'revision', title: copy.value.revision, width: 82, sortable: true, filter: false },
  {
    field: 'status',
    title: copy.value.status,
    width: 112,
    sortable: true,
    filter: { kind: 'select', options: statusOptions.value },
  },
  { field: 'conversionKind', title: copy.value.unitConversionKind, minWidth: 150, filter: false },
  { field: 'baseUnitNId', title: copy.value.unitBase, width: 110, filter: false },
  { field: 'unitCount', title: copy.value.unitCount, width: 86, filter: false },
  { field: 'isSystemDefined', title: copy.value.unitSystemDefined, width: 110, filter: false },
  {
    field: 'lastUpdatedOn',
    title: copy.value.updatedOn,
    minWidth: 170,
    sortable: true,
    filter: false,
  },
])
const unitColumns = computed<AppDataTableColumn[]>(() => [
  { field: 'nId', title: copy.value.nId, minWidth: 120, filter: false },
  { field: 'name', title: copy.value.name, minWidth: 150, filter: false },
  { field: 'symbol', title: copy.value.unitSymbol, width: 100, filter: false },
  { field: 'factorToBase', title: copy.value.unitFactor, minWidth: 150, filter: false },
  { field: 'offsetToBase', title: copy.value.unitOffset, minWidth: 140, filter: false },
  { field: 'decimalPlaces', title: copy.value.unitDecimalPlaces, width: 110, filter: false },
  { field: 'roundingMode', title: copy.value.unitRoundingMode, minWidth: 150, filter: false },
  { field: 'enabled', title: copy.value.enabled, width: 88, filter: false },
  { field: 'sort', title: copy.value.sort, width: 90, filter: false },
])

function statusLabel(status: PublicationStatus) {
  return statusOptions.value.find((item) => item.value === status)?.label ?? status
}
function conversionKindLabel(value: UnitConversionKind) {
  return conversionKindOptions.value.find((item) => item.value === value)?.label ?? value
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
    'REF-UNIT-CONVERSION-INVALID': copy.value.unitInvalid,
    'REF-UNIT-NUMERIC-OVERFLOW': copy.value.unitOverflow,
    'REF-UNIT-SYSTEM-DEFINED': copy.value.unitSystemReadOnly,
    'REF-UNIT-DIMENSION-NOT-FOUND': copy.value.notFound,
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
function version(row: Pick<UnitLifecycle, 'optimisticVersion' | 'concurrencyVersion'>) {
  return {
    expectedOptimisticVersion: row.optimisticVersion,
    expectedConcurrencyVersion: row.concurrencyVersion,
  }
}
type UnitLifecycle = Pick<
  UnitDimensionSummary,
  | 'scopeType'
  | 'status'
  | 'isFrozen'
  | 'isLocked'
  | 'isSystemDefined'
  | 'publishedOn'
  | 'optimisticVersion'
  | 'concurrencyVersion'
>
function scopeWritable(
  row: Pick<UnitLifecycle, 'scopeType' | 'isFrozen' | 'isLocked' | 'isSystemDefined'>,
) {
  return (
    !row.isSystemDefined &&
    !row.isFrozen &&
    !row.isLocked &&
    (row.scopeType === 'Tenant' || has(PERMISSIONS.referenceDataPlatformManage))
  )
}
function canEdit(row: UnitLifecycle) {
  return (
    row.status === 'Draft' &&
    scopeWritable(row) &&
    has(PERMISSIONS.referenceDataUnitOfMeasureUpdate)
  )
}
function canClone(row: UnitLifecycle) {
  return (
    row.publishedOn !== null &&
    scopeWritable(row) &&
    has(PERMISSIONS.referenceDataUnitOfMeasureCreate)
  )
}
function canPublish(row: UnitLifecycle) {
  return (
    row.status === 'Draft' &&
    scopeWritable(row) &&
    has(PERMISSIONS.referenceDataUnitOfMeasurePublish)
  )
}
function canDisable(row: UnitLifecycle) {
  return (
    ['Draft', 'Published'].includes(row.status) &&
    scopeWritable(row) &&
    has(PERMISSIONS.referenceDataUnitOfMeasureDisable)
  )
}
function canConvert(row: UnitDimensionSummary) {
  return row.publishedOn !== null && ['Published', 'Superseded', 'Disabled'].includes(row.status)
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
    return await api.listUnitDimensions(params, { signal: listRequest.signal })
  } finally {
    firstLoading.value = false
  }
}
function onLoaded(page: { total: number }) {
  total.value = page.total
  listError.value = ''
  if (!formOpen.value && !conversionOpen.value) clearError()
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

interface FormUnit extends UnitDefinitionWrite {
  localKey: string
  id?: string
  isFrozen?: boolean
  isLocked?: boolean
}
type FormMode = 'create' | 'edit' | 'view'
const formOpen = ref(false)
const formMode = ref<FormMode>('view')
const selected = ref<UnitDimension | null>(null)
const form = reactive({
  nId: '',
  name: '',
  description: '',
  scopeType: 'Tenant' as ReferenceScope,
  conversionKind: 'Ratio' as UnitConversionKind,
  baseUnitNId: '',
  units: [] as FormUnit[],
})
const savedSnapshot = ref('')
const dirty = computed(
  () => formOpen.value && formMode.value !== 'view' && JSON.stringify(form) !== savedSnapshot.value,
)
const canSubmit = computed(() => {
  if (formMode.value === 'create')
    return (
      has(PERMISSIONS.referenceDataUnitOfMeasureCreate) &&
      (form.scopeType === 'Tenant' || has(PERMISSIONS.referenceDataPlatformManage))
    )
  return formMode.value === 'edit' && !!selected.value && canEdit(selected.value)
})
const isReadOnly = computed(() => !canSubmit.value)
function unitWritable(unit: Pick<FormUnit, 'isFrozen' | 'isLocked'>) {
  return canSubmit.value && !unit.isFrozen && !unit.isLocked
}

function unitWrite(unit: FormUnit): UnitDefinitionWrite {
  return {
    nId: unit.nId.trim(),
    name: unit.name.trim(),
    symbol: unit.symbol.trim(),
    factorToBase: unit.factorToBase.trim(),
    offsetToBase: form.conversionKind === 'Ratio' ? '0' : unit.offsetToBase.trim(),
    decimalPlaces: unit.decimalPlaces,
    roundingMode: unit.roundingMode,
    enabled: unit.enabled,
    sort: unit.sort,
  }
}
function payload(): CreateUnitDimension {
  return {
    scopeType: form.scopeType,
    scopeId: null,
    nId: form.nId.trim(),
    name: form.name.trim(),
    description: form.description.trim() || null,
    conversionKind: form.conversionKind,
    baseUnitNId: form.baseUnitNId.trim(),
    units: form.units.map(unitWrite),
  }
}
function fill(item: UnitDimension | null) {
  selected.value = item
  Object.assign(form, {
    nId: item?.nId ?? '',
    name: item?.name ?? '',
    description: item?.description ?? '',
    scopeType: item?.scopeType ?? 'Tenant',
    conversionKind: item?.conversionKind ?? 'Ratio',
    baseUnitNId: item?.baseUnitNId ?? '',
    units: (item?.units ?? []).map((unit) => ({ ...unit, localKey: crypto.randomUUID() })),
  })
  savedSnapshot.value = JSON.stringify(form)
}
function addUnit() {
  const number = form.units.length + 1
  form.units.push({
    localKey: crypto.randomUUID(),
    nId: '',
    name: '',
    symbol: '',
    factorToBase: number === 1 ? '1' : '',
    offsetToBase: '0',
    decimalPlaces: 3,
    roundingMode: 'ToEven',
    enabled: true,
    sort: form.units.length,
  })
}
function removeUnit(unit: FormUnit) {
  if (!canSubmit.value || unit.isFrozen || unit.isLocked) return
  form.units = form.units.filter((item) => item.localKey !== unit.localKey)
  if (!form.units.some((item) => item.nId.trim() === form.baseUnitNId)) form.baseUnitNId = ''
}
function normalizeBase() {
  const unit = form.units.find((item) => item.nId.trim() === form.baseUnitNId)
  if (!unit) return
  unit.factorToBase = '1'
  unit.offsetToBase = '0'
  unit.enabled = true
}
function normalizeRatio() {
  if (form.conversionKind === 'Ratio') for (const unit of form.units) unit.offsetToBase = '0'
}
watch(() => form.conversionKind, normalizeRatio)

async function create() {
  if (!(await allowDiscard())) return
  detailSequence++
  detailRequest?.abort()
  clearError()
  fill(null)
  formMode.value = 'create'
  addUnit()
  savedSnapshot.value = JSON.stringify(form)
  formOpen.value = true
}
async function open(row: UnitDimensionSummary, edit = false) {
  if (!(await allowDiscard())) return
  detailSequence++
  detailRequest?.abort()
  detailRequest = new AbortController()
  const sequence = detailSequence
  clearError()
  busy.value = true
  try {
    if (!api) throw new Error(copy.value.unavailable)
    const item = await api.getUnitDimension(row.id, { signal: detailRequest.signal })
    if (sequence !== detailSequence) return
    fill(item)
    formMode.value = edit && canEdit(item) ? 'edit' : 'view'
    formOpen.value = true
  } catch (caught) {
    if (sequence === detailSequence) report(caught)
  } finally {
    if (sequence === detailSequence) busy.value = false
  }
}
async function allowDiscard() {
  if (busy.value) return false
  if (!dirty.value) return true
  try {
    await ElMessageBox.confirm(copy.value.discard, copy.value.unitTitle, {
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
  formOpen.value = false
  selected.value = null
  busy.value = false
}
async function closeForm(value = false) {
  if (!value && (await allowDiscard())) {
    closeFormNow()
    clearError()
  }
}
function decimal(value: string) {
  return /^[+-]?(?:\d+(?:\.\d*)?|\.\d+)$/.test(value.trim())
}
function decimalZero(value: string) {
  return decimal(value) && /^[-+]?0*(?:\.0*)?$/.test(value.trim())
}
function decimalOne(value: string) {
  return decimal(value) && /^\+?0*1(?:\.0*)?$/.test(value.trim())
}
function positiveDecimal(value: string) {
  return decimal(value) && !value.trim().startsWith('-') && !decimalZero(value)
}
function unitField(unit: FormUnit, field: string) {
  return `units[${form.units.findIndex((item) => item.localKey === unit.localKey)}].${field}`
}
function validate() {
  validation.value = {}
  if (!/^[A-Za-z][A-Za-z0-9_.-]{1,63}$/.test(form.nId.trim()))
    validation.value.nId = copy.value.invalidNId
  if (!form.name.trim() || form.name.length > 200) validation.value.name = copy.value.required
  if (form.units.length < 1 || form.units.length > 200)
    validation.value.units = copy.value.unitLimit
  const seen = new Set<string>()
  for (const unit of form.units) {
    const prefix = unitField(unit, '')
    const normalized = unit.nId.trim().toUpperCase()
    if (!/^[A-Za-z0-9][A-Za-z0-9_.-]{0,63}$/.test(unit.nId.trim()))
      validation.value[`${prefix}nId`] = copy.value.invalidNId
    if (seen.has(normalized)) validation.value[`${prefix}nId`] = copy.value.unitDuplicate
    seen.add(normalized)
    if (!unit.name.trim() || !unit.symbol.trim())
      validation.value[`${prefix}name`] = copy.value.required
    if (!positiveDecimal(unit.factorToBase))
      validation.value[`${prefix}factorToBase`] = copy.value.unitFactorInvalid
    if (!decimal(unit.offsetToBase))
      validation.value[`${prefix}offsetToBase`] = copy.value.unitDecimalInvalid
    if (form.conversionKind === 'Ratio' && !decimalZero(unit.offsetToBase))
      validation.value[`${prefix}offsetToBase`] = copy.value.unitRatioOffset
    if (!Number.isInteger(unit.decimalPlaces) || unit.decimalPlaces < 0 || unit.decimalPlaces > 12)
      validation.value[`${prefix}decimalPlaces`] = copy.value.unitDecimalPlacesInvalid
    if (!Number.isInteger(unit.sort) || unit.sort < 0)
      validation.value[`${prefix}sort`] = copy.value.invalidSort
  }
  const base = form.units.find(
    (unit) => unit.nId.trim().toUpperCase() === form.baseUnitNId.trim().toUpperCase(),
  )
  if (!base || !base.enabled || !decimalOne(base.factorToBase) || !decimalZero(base.offsetToBase))
    validation.value.baseUnitNId = copy.value.unitBaseInvalid
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
        ? await api.createUnitDimension(request)
        : await api.updateUnitDimension(selected.value!.id, {
            name: request.name,
            description: request.description,
            conversionKind: request.conversionKind,
            baseUnitNId: request.baseUnitNId,
            units: request.units,
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
    const item = await api.getUnitDimension(selected.value.id, { signal: detailRequest.signal })
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
async function clone(row: UnitDimensionSummary) {
  if (!api || !canClone(row) || busy.value) return
  busy.value = true
  clearError()
  try {
    const item = await api.cloneUnitDimension(row.id, version(row))
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
async function publish(row: UnitDimensionSummary) {
  if (!api || !canPublish(row) || busy.value) return
  try {
    await ElMessageBox.confirm(copy.value.unitPublishHint, copy.value.publish, {
      confirmButtonText: copy.value.publish,
      cancelButtonText: copy.value.cancel,
      type: 'warning',
    })
  } catch {
    return
  }
  busy.value = true
  clearError()
  try {
    await api.publishUnitDimension(row.id, version(row))
    ElMessage.success(copy.value.publishedSuccess)
    await table.value?.reload()
  } catch (caught) {
    report(caught)
  } finally {
    busy.value = false
  }
}
async function disable(row: UnitDimensionSummary) {
  if (!api || !canDisable(row) || busy.value) return
  let reason = ''
  try {
    const result = await ElMessageBox.prompt(copy.value.unitDisableHint, copy.value.reason, {
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
    await api.disableUnitDimension(row.id, { ...version(row), changeReason: reason })
    ElMessage.success(copy.value.disabledSuccess)
    await table.value?.reload()
  } catch (caught) {
    report(caught)
  } finally {
    busy.value = false
  }
}

const conversionOpen = ref(false)
const conversionLoading = ref(false)
const conversionMode = ref<'Current' | 'Fixed'>('Fixed')
const conversionRow = ref<UnitDimensionSummary | null>(null)
const runtimeDimension = ref<RuntimeUnitDimension | null>(null)
const conversion = reactive({ fromUnitNId: '', toUnitNId: '', value: '' })
const conversionResult = ref<UnitConversionResult | null>(null)
let runtimeRequest: AbortController | undefined
let runtimeSequence = 0

async function openConversion(row: UnitDimensionSummary) {
  if (!api || !canConvert(row) || !(await allowDiscard())) return
  closeFormNow()
  clearError()
  conversionRow.value = row
  conversionMode.value = 'Fixed'
  conversionOpen.value = true
  await loadRuntimeDimension()
}
async function loadRuntimeDimension() {
  if (!api || !conversionRow.value) return
  runtimeSequence++
  runtimeRequest?.abort()
  runtimeRequest = new AbortController()
  const sequence = runtimeSequence
  conversionLoading.value = true
  conversionResult.value = null
  clearError()
  const row = conversionRow.value
  try {
    const item =
      conversionMode.value === 'Fixed'
        ? await api.getUnitDimensionRevision(row.nId, row.revision, row.scopeType, row.tenantNId, {
            signal: runtimeRequest.signal,
          })
        : await api.getCurrentUnitDimension(row.nId, row.scopeType, row.tenantNId, {
            signal: runtimeRequest.signal,
          })
    if (sequence !== runtimeSequence) return
    runtimeDimension.value = item
    const enabled = item.units.filter((unit) => unit.enabled)
    conversion.fromUnitNId = enabled[0]?.nId ?? ''
    conversion.toUnitNId = enabled[1]?.nId ?? enabled[0]?.nId ?? ''
    conversion.value = ''
  } catch (caught) {
    if (sequence === runtimeSequence) report(caught)
  } finally {
    if (sequence === runtimeSequence) conversionLoading.value = false
  }
}
function closeConversion(value = false) {
  if (value || conversionLoading.value || busy.value) return
  runtimeSequence++
  runtimeRequest?.abort()
  conversionOpen.value = false
  conversionRow.value = null
  runtimeDimension.value = null
  conversionResult.value = null
  clearError()
}
async function convert() {
  if (!api || !runtimeDimension.value || !decimal(conversion.value) || busy.value) {
    if (!decimal(conversion.value)) validation.value.conversionValue = copy.value.unitDecimalInvalid
    return
  }
  runtimeSequence++
  runtimeRequest?.abort()
  runtimeRequest = new AbortController()
  const sequence = runtimeSequence
  busy.value = true
  clearError()
  try {
    const dimension = runtimeDimension.value
    const result = await api.convertUnit(
      {
        sourceScope: dimension.sourceScope,
        sourceTenantNId: dimension.sourceTenantNId,
        unitDimensionNId: dimension.nId,
        unitRevision: dimension.revision,
        fromUnitNId: conversion.fromUnitNId,
        toUnitNId: conversion.toUnitNId,
        value: conversion.value.trim(),
      },
      { signal: runtimeRequest.signal },
    )
    if (sequence === runtimeSequence) conversionResult.value = result
  } catch (caught) {
    if (sequence === runtimeSequence) report(caught)
  } finally {
    if (sequence === runtimeSequence) busy.value = false
  }
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
  runtimeRequest?.abort()
  detailSequence++
  runtimeSequence++
  window.removeEventListener('beforeunload', beforeUnload)
})
</script>

<template>
  <AppPage
    :title="copy.unitTitle"
    :description="copy.unitDescription"
    data-testid="reference-data-units-of-measure"
  >
    <template #actions>
      <PermissionGate :permission-n-id="PERMISSIONS.referenceDataUnitOfMeasureCreate">
        <el-button type="primary" :icon="Plus" data-testid="unit-dimension-create" @click="create">
          {{ copy.unitCreate }}
        </el-button>
      </PermissionGate>
    </template>

    <el-alert v-if="listError" :title="listError" type="error" :closable="false" show-icon />
    <el-alert
      v-if="error && !formOpen && !conversionOpen"
      :title="error"
      type="error"
      :closable="false"
      show-icon
    >
      <p v-if="traceId">{{ copy.traceId }}: {{ traceId }}</p>
    </el-alert>
    <AppQueryPanel show-actions grid @submit="search" @reset="reset">
      <label class="unit-query-field"
        ><span>{{ copy.keyword }}</span
        ><el-input v-model="query.keyword" :aria-label="copy.keyword" @keyup.enter="search"
      /></label>
      <label class="unit-query-field"
        ><span>{{ copy.scope }}</span
        ><el-select v-model="query.scopeType" :aria-label="copy.scope"
          ><el-option value="" :label="copy.all" /><el-option
            v-for="option in scopeOptions"
            :key="option.value"
            v-bind="option" /></el-select
      ></label>
      <label class="unit-query-field"
        ><span>{{ copy.status }}</span
        ><el-select v-model="query.status" :aria-label="copy.status"
          ><el-option value="" :label="copy.all" /><el-option
            v-for="option in statusOptions"
            :key="option.value"
            v-bind="option" /></el-select
      ></label>
    </AppQueryPanel>
    <AppDataTable
      ref="table"
      table-key="reference-data-units-of-measure"
      :columns="columns"
      :loader="load"
      :query-mode="mode"
      selection="none"
      @update:query-mode="switchMode"
      @loaded="onLoaded"
      @error="reportList"
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
      <template #cell-conversionKind="{ row }">{{
        conversionKindLabel(row.conversionKind)
      }}</template>
      <template #cell-isSystemDefined="{ row }">{{
        row.isSystemDefined ? copy.trueValue : copy.falseValue
      }}</template>
      <template #cell-lastUpdatedOn="{ row }">{{ date(row.lastUpdatedOn) }}</template>
      <template #actions="{ row, availableWidth }">
        <div class="unit-actions">
          <el-button link type="primary" @click="open(row, false)">{{ copy.detail }}</el-button>
          <el-button
            v-if="canEdit(row) && availableWidth >= 190"
            link
            type="primary"
            @click="open(row, true)"
            >{{ copy.edit }}</el-button
          >
          <el-dropdown
            v-if="
              canEdit(row) || canClone(row) || canPublish(row) || canDisable(row) || canConvert(row)
            "
            trigger="click"
          >
            <el-button link type="primary" :disabled="busy" data-testid="unit-dimension-more">{{
              copy.more
            }}</el-button>
            <template #dropdown>
              <el-dropdown-menu>
                <el-dropdown-item
                  v-if="canEdit(row) && availableWidth < 190"
                  @click="open(row, true)"
                >
                  {{ copy.edit }}
                </el-dropdown-item>
                <el-dropdown-item v-if="canClone(row)" @click="clone(row)">{{
                  copy.clone
                }}</el-dropdown-item>
                <el-dropdown-item
                  v-if="canPublish(row)"
                  data-testid="unit-dimension-publish"
                  @click="publish(row)"
                >
                  {{ copy.publish }}
                </el-dropdown-item>
                <el-dropdown-item v-if="canDisable(row)" @click="disable(row)">{{
                  copy.disable
                }}</el-dropdown-item>
                <el-dropdown-item
                  v-if="canConvert(row)"
                  data-testid="unit-conversion-open"
                  @click="openConversion(row)"
                >
                  {{ copy.unitTryConvert }}
                </el-dropdown-item>
              </el-dropdown-menu>
            </template>
          </el-dropdown>
        </div>
      </template>
    </AppDataTable>
    <p v-if="!firstLoading && total === 0 && !listError">{{ copy.unitEmpty }}</p>

    <AppFormDrawer
      :model-value="formOpen"
      :title="
        formMode === 'create'
          ? copy.unitCreate
          : formMode === 'edit'
            ? copy.unitEdit
            : copy.unitDetail
      "
      size="wide"
      :busy="busy"
      @update:model-value="closeForm"
    >
      <el-alert v-if="error" :title="error" type="error" :closable="false" show-icon>
        <p v-if="traceId">{{ copy.traceId }}: {{ traceId }}</p>
        <div v-if="conflict" class="unit-actions">
          <el-button @click="reloadForm">{{ copy.reload }}</el-button>
          <el-button @click="copyUnsaved">{{ copy.copyUnsaved }}</el-button>
        </div>
      </el-alert>
      <el-alert
        v-if="selected?.isSystemDefined"
        :title="copy.unitSystemReadOnly"
        type="info"
        :closable="false"
        show-icon
      />
      <el-alert v-else-if="isReadOnly" :title="copy.readOnly" type="info" :closable="false" />
      <el-form label-position="top" :disabled="isReadOnly || busy">
        <div class="unit-form-grid">
          <el-form-item :label="copy.nId" :error="validation.nId" required>
            <el-input
              v-model="form.nId"
              data-testid="unit-dimension-nid"
              :disabled="formMode !== 'create'"
              maxlength="64"
            />
          </el-form-item>
          <el-form-item :label="copy.name" :error="validation.name" required>
            <el-input v-model="form.name" data-testid="unit-dimension-name" maxlength="200" />
          </el-form-item>
          <el-form-item :label="copy.scope">
            <el-select v-model="form.scopeType" :disabled="formMode !== 'create'">
              <el-option value="Tenant" :label="copy.tenant" />
              <el-option
                v-if="has(PERMISSIONS.referenceDataPlatformManage)"
                value="Platform"
                :label="copy.platform"
              />
            </el-select>
          </el-form-item>
          <el-form-item :label="copy.unitConversionKind">
            <el-select v-model="form.conversionKind" data-testid="unit-dimension-conversion-kind">
              <el-option
                v-for="option in conversionKindOptions"
                :key="option.value"
                v-bind="option"
              />
            </el-select>
          </el-form-item>
          <el-form-item :label="copy.unitBase" :error="validation.baseUnitNId" required>
            <el-select
              v-model="form.baseUnitNId"
              data-testid="unit-dimension-base-unit"
              @change="normalizeBase"
            >
              <el-option
                v-for="unit in form.units.filter((item) => item.nId.trim())"
                :key="unit.localKey"
                :value="unit.nId.trim()"
                :label="`${unit.name || unit.nId} (${unit.nId})`"
              />
            </el-select>
          </el-form-item>
        </div>
        <el-form-item :label="copy.description"
          ><el-input v-model="form.description" type="textarea" :rows="2" maxlength="2000"
        /></el-form-item>
        <div class="unit-section-heading">
          <div>
            <h3>{{ copy.unitDefinitions }} ({{ form.units.length }}/200)</h3>
            <p>{{ copy.unitDecimalHint }}</p>
          </div>
          <el-button
            v-if="canSubmit"
            :icon="Plus"
            :disabled="form.units.length >= 200"
            data-testid="unit-definition-add"
            @click="addUnit"
            >{{ copy.unitAdd }}</el-button
          >
        </div>
        <el-alert
          v-if="validation.units"
          :title="validation.units"
          type="error"
          :closable="false"
        />
        <AppDataTable
          table-key="reference-data-unit-definitions"
          :rows="form.units"
          :total="form.units.length"
          :columns="unitColumns"
          toolbar-profile="compact"
          selection="none"
        >
          <template #cell-nId="{ row }"
            ><el-input
              v-model="row.nId"
              :aria-label="copy.unitNId"
              :data-testid="`unit-definition-nid-${form.units.indexOf(row)}`"
              :disabled="!unitWritable(row)"
          /></template>
          <template #cell-name="{ row }"
            ><el-input
              v-model="row.name"
              :aria-label="copy.unitName"
              :data-testid="`unit-definition-name-${form.units.indexOf(row)}`"
              :error="validation[unitField(row, 'name')]"
              :disabled="!unitWritable(row)"
          /></template>
          <template #cell-symbol="{ row }"
            ><el-input
              v-model="row.symbol"
              :aria-label="copy.unitSymbol"
              :data-testid="`unit-definition-symbol-${form.units.indexOf(row)}`"
              maxlength="32"
              :disabled="!unitWritable(row)"
          /></template>
          <template #cell-factorToBase="{ row }"
            ><el-input
              v-model="row.factorToBase"
              :aria-label="copy.unitFactor"
              :data-testid="`unit-definition-factor-${form.units.indexOf(row)}`"
              :disabled="!unitWritable(row) || row.nId.trim() === form.baseUnitNId"
          /></template>
          <template #cell-offsetToBase="{ row }"
            ><el-input
              v-model="row.offsetToBase"
              :aria-label="copy.unitOffset"
              :data-testid="`unit-definition-offset-${form.units.indexOf(row)}`"
              :disabled="
                !unitWritable(row) ||
                form.conversionKind === 'Ratio' ||
                row.nId.trim() === form.baseUnitNId
              "
          /></template>
          <template #cell-decimalPlaces="{ row }"
            ><el-input-number
              v-model="row.decimalPlaces"
              :aria-label="copy.unitDecimalPlaces"
              :data-testid="`unit-definition-decimals-${form.units.indexOf(row)}`"
              :min="0"
              :max="12"
              :precision="0"
              :disabled="!unitWritable(row)"
          /></template>
          <template #cell-roundingMode="{ row }"
            ><el-select
              v-model="row.roundingMode"
              :aria-label="copy.unitRoundingMode"
              :disabled="!unitWritable(row)"
              ><el-option
                v-for="option in roundingOptions"
                :key="option.value"
                v-bind="option" /></el-select
          ></template>
          <template #cell-enabled="{ row }"
            ><el-switch
              v-model="row.enabled"
              :aria-label="copy.enabled"
              :disabled="!unitWritable(row) || row.nId.trim() === form.baseUnitNId"
          /></template>
          <template #cell-sort="{ row }"
            ><el-input-number
              v-model="row.sort"
              :aria-label="copy.sort"
              :min="0"
              :precision="0"
              :disabled="!unitWritable(row)"
          /></template>
          <template #actions="{ row }"
            ><el-button
              v-if="unitWritable(row)"
              link
              type="danger"
              :icon="Delete"
              @click="removeUnit(row)"
              >{{ copy.remove }}</el-button
            ></template
          >
        </AppDataTable>
        <p v-if="form.units.length === 0">{{ copy.unitNoDefinitions }}</p>
      </el-form>
      <template #footer>
        <el-button :disabled="busy" @click="closeForm()">{{ copy.cancel }}</el-button>
        <el-button
          v-if="formMode !== 'view'"
          type="primary"
          :disabled="!canSubmit"
          :loading="busy"
          data-testid="unit-dimension-save"
          @click="save"
          >{{ copy.save }}</el-button
        >
      </template>
    </AppFormDrawer>

    <AppFormDrawer
      :model-value="conversionOpen"
      :title="copy.unitConvertTitle"
      size="medium"
      :busy="conversionLoading || busy"
      @update:model-value="closeConversion"
    >
      <el-alert v-if="error" :title="error" type="error" :closable="false" show-icon />
      <el-form v-if="runtimeDimension" label-position="top" :disabled="conversionLoading || busy">
        <div class="unit-runtime-reference" data-testid="unit-runtime-reference">
          <strong>{{ runtimeDimension.nId }}</strong>
          <span data-testid="unit-runtime-source">{{
            runtimeDimension.sourceScope === 'Tenant' ? copy.tenant : copy.platform
          }}</span>
          <span data-testid="unit-runtime-revision"
            >{{ copy.revision }} {{ runtimeDimension.revision }}</span
          >
        </div>
        <el-form-item :label="copy.unitReadMode">
          <el-radio-group
            v-model="conversionMode"
            data-testid="unit-read-mode"
            @change="loadRuntimeDimension"
          >
            <el-radio-button value="Fixed">{{ copy.unitFixedRevision }}</el-radio-button>
            <el-radio-button value="Current">{{ copy.unitCurrentRevision }}</el-radio-button>
          </el-radio-group>
        </el-form-item>
        <div class="unit-form-grid">
          <el-form-item :label="copy.unitFrom" required
            ><el-select v-model="conversion.fromUnitNId" data-testid="unit-conversion-source-unit"
              ><el-option
                v-for="unit in runtimeDimension.units.filter((item) => item.enabled)"
                :key="unit.nId"
                :value="unit.nId"
                :label="`${unit.name} (${unit.symbol})`" /></el-select
          ></el-form-item>
          <el-form-item :label="copy.unitTo" required
            ><el-select v-model="conversion.toUnitNId" data-testid="unit-conversion-target-unit"
              ><el-option
                v-for="unit in runtimeDimension.units.filter((item) => item.enabled)"
                :key="unit.nId"
                :value="unit.nId"
                :label="`${unit.name} (${unit.symbol})`" /></el-select
          ></el-form-item>
          <el-form-item :label="copy.value" :error="validation.conversionValue" required
            ><el-input
              v-model="conversion.value"
              data-testid="unit-conversion-value"
              :placeholder="copy.unitDecimalHint"
          /></el-form-item>
        </div>
        <el-card
          v-if="conversionResult !== null"
          data-testid="unit-conversion-result"
          shadow="never"
        >
          <strong>{{ conversionResult.resultValue }}</strong>
          <p>
            {{ conversionResult.inputValue }} {{ conversionResult.fromUnitNId }} →
            {{ conversionResult.resultValue }} {{ conversionResult.toUnitNId }}
          </p>
          <p>
            {{ copy.unitRoundingMode }}: {{ conversionResult.conversionSnapshot.roundingMode }} ·
            {{ copy.unitWasRounded }}:
            {{ conversionResult.wasRounded ? copy.trueValue : copy.falseValue }}
          </p>
        </el-card>
      </el-form>
      <el-skeleton v-else-if="conversionLoading" :rows="4" animated />
      <template #footer>
        <el-button
          :disabled="conversionLoading || busy"
          data-testid="unit-conversion-close"
          @click="closeConversion()"
          >{{ copy.close }}</el-button
        >
        <el-button
          type="primary"
          :disabled="!runtimeDimension"
          :loading="busy"
          data-testid="unit-conversion-submit"
          @click="convert"
          >{{ copy.unitConvert }}</el-button
        >
      </template>
    </AppFormDrawer>
  </AppPage>
</template>

<style scoped>
.unit-query-field {
  display: grid;
  flex: 0 0 180px;
  max-width: 100%;
  min-width: 0;
  gap: var(--ip-space-2);
}
.unit-actions {
  display: inline-flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--ip-space-1);
}
.unit-actions > .el-button {
  margin-left: 0;
}
.unit-form-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(210px, 1fr));
  gap: 0 var(--ip-space-4);
}
.unit-section-heading {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--ip-space-3);
  margin-top: var(--ip-space-2);
}
.unit-section-heading h3 {
  margin: 0;
}
.unit-section-heading p {
  margin: var(--ip-space-1) 0 0;
  color: var(--el-text-color-secondary);
}
.unit-runtime-reference {
  display: flex;
  flex-wrap: wrap;
  gap: var(--ip-space-2);
  margin-bottom: var(--ip-space-4);
  padding: var(--ip-space-3);
  background: var(--ip-color-bg-muted);
  border-radius: var(--ip-radius-sm);
}
.unit-runtime-reference strong {
  margin-right: auto;
}
@media (max-width: 768px) {
  .unit-section-heading {
    align-items: flex-start;
    flex-direction: column;
  }
}
</style>
