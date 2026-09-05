<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue'
import { onBeforeRouteLeave } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Plus } from '@element-plus/icons-vue'
import { ApiError } from '@/api/errors'
import { getReferenceDataApi } from '@/api/referenceData'
import type { DictionaryItem, ReferenceScope } from '@/api/referenceData/types'
import type {
  ConfigurationDataType,
  ConfigurationDomain,
  ConfigurationDomainSummary,
  ConfigurationHistory,
  ConfigurationKey,
  ConfigurationMultiValue,
  ConfigurationStatus,
  ConfigurationValueMode,
  ConfigurationVersion,
  EffectiveConfiguration,
  WriteConfigurationKey,
} from '@/api/referenceData/parameterTypes'
import AppPage from '@/components/base/AppPage.vue'
import AppQueryPanel from '@/components/management/AppQueryPanel.vue'
import AppDataTable from '@/components/management/AppDataTable.vue'
import AppFormDrawer from '@/components/management/AppFormDrawer.vue'
import type { AppDataTableColumn, AppDataTableRequest } from '@/components/management/AppDataTable'
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
  setTopQuery: (value: Record<string, unknown>) => void
  clearSelection: () => void
}>()
const selectedId = ref<string | null>(null)
const domain = ref<ConfigurationDomain | null>(null)
const detailLoading = ref(false)
const query = reactive({ keyword: '', scopeType: '', status: '' })
const error = ref('')
const traceId = ref('')
const conflict = ref(false)
const fields = ref<Record<string, string>>({})
const busy = ref(false)
const reloadingEditor = ref(false)
const editorKind = ref<'domain' | 'key' | 'value' | 'state' | null>(null)
const editing = ref(false)
const objectId = ref<string | null>(null)
const keyId = ref<string | null>(null)
const editorDomain = ref<ConfigurationDomain | null>(null)
const savedSnapshot = ref('')
const editorTab = ref('basics')
const form = reactive({
  nId: '',
  name: '',
  description: '',
  scopeType: 'Tenant' as ReferenceScope,
  changeReason: '',
  dataType: 'String' as ConfigurationDataType,
  valueMode: 'Single' as ConfigurationValueMode,
  valueJson: null as string | null,
  defaultValueJson: null as string | null,
  isMandatory: false,
  isReadOnly: false,
  dictionaryNId: '',
  referenceTarget: '',
  status: 'Active' as ConfigurationStatus,
  sort: 0,
  isDefault: false,
  enabled: true,
})
const stateTarget = ref<{ keyId: string | null; valueId: string | null; enabled: boolean } | null>(
  null,
)
const enumItems = ref<DictionaryItem[]>([])
const enumLoading = ref(false)
const viewer = ref<'history' | 'effective' | null>(null)
const historyTarget = ref<{ id: string; keyId: string | null } | null>(null)
const effective = ref<EffectiveConfiguration | null>(null)
const historyTable = ref<{ reload: () => Promise<void> }>()
let selectionRequest: AbortController | undefined
let selectionSequence = 0
let listRequest: AbortController | undefined
let enumRequest: AbortController | undefined
let editorRequest: AbortController | undefined
let editorSequence = 0
let viewerRequest: AbortController | undefined
let viewerSequence = 0
let lastQuery = ''

const activeKey = computed(
  () => editorDomain.value?.keys.find((item) => item.id === keyId.value) ?? null,
)
const selectedKey = computed(
  () => domain.value?.keys.find((item) => item.id === keyId.value) ?? null,
)
const dirty = computed(
  () => editorKind.value !== null && editing.value && JSON.stringify(form) !== savedSnapshot.value,
)
const scopeOptions = computed(() => [
  { value: 'Tenant', label: copy.value.tenant },
  { value: 'Platform', label: copy.value.platform },
])
const statusOptions = computed(() => [
  { value: 'Active', label: copy.value.active },
  { value: 'Disabled', label: copy.value.disabled },
])
const types = computed(() =>
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
const domainColumns = computed<AppDataTableColumn[]>(() => [
  { field: 'name', title: copy.value.domains, minWidth: 165, filter: false },
  { field: 'scopeType', title: copy.value.scope, width: 90, filter: false },
  { field: 'status', title: copy.value.status, width: 95, filter: false },
])
const keyColumns = computed<AppDataTableColumn[]>(() => [
  { field: 'nId', title: copy.value.nId, minWidth: 160 },
  { field: 'name', title: copy.value.name, minWidth: 150 },
  {
    field: 'dataType',
    title: copy.value.dataType,
    width: 120,
    filter: { kind: 'select', options: types.value },
  },
  { field: 'valueMode', title: copy.value.valueMode, width: 100 },
  { field: 'valueJson', title: copy.value.value, minWidth: 180 },
  {
    field: 'status',
    title: copy.value.status,
    width: 110,
    filter: { kind: 'select', options: statusOptions.value },
  },
  { field: 'isReadOnly', title: copy.value.readOnlyKey, width: 105 },
])
const valueColumns = computed<AppDataTableColumn[]>(() => [
  { field: 'nId', title: copy.value.nId, minWidth: 105 },
  { field: 'valueJson', title: copy.value.value, minWidth: 160 },
  { field: 'sort', title: copy.value.sort, width: 70 },
  { field: 'isDefault', title: copy.value.isDefault, width: 85 },
  { field: 'enabled', title: copy.value.enabled, width: 75 },
])
const historyColumns = computed<AppDataTableColumn[]>(() => [
  { field: 'revision', title: copy.value.revision, width: 90 },
  { field: 'fullNId', title: copy.value.fullPath, minWidth: 190 },
  { field: 'changeType', title: copy.value.changeType, width: 100 },
  { field: 'changeReason', title: copy.value.changeReason, minWidth: 210 },
  { field: 'userNId', title: copy.value.changedBy, minWidth: 130 },
  { field: 'createdOn', title: copy.value.updatedOn, minWidth: 180 },
  { field: 'beforeSummary', title: copy.value.historyBefore, minWidth: 270 },
  { field: 'afterSummary', title: copy.value.historyAfter, minWidth: 270 },
  { field: 'traceId', title: copy.value.traceId, minWidth: 200 },
])
function scopeWritable(
  row: Pick<ConfigurationDomainSummary, 'scopeType' | 'isFrozen' | 'isLocked'>,
) {
  return (
    !row.isFrozen &&
    !row.isLocked &&
    (row.scopeType === 'Tenant' || has(PERMISSIONS.referenceDataPlatformManage))
  )
}
function keyWritable(key: ConfigurationKey) {
  return !key.isReadOnly && !key.isFrozen && !key.isLocked
}
function canEditKey(key: ConfigurationKey) {
  return (
    !!domain.value &&
    scopeWritable(domain.value) &&
    keyWritable(key) &&
    has(PERMISSIONS.referenceDataParameterUpdate)
  )
}
function canState(key?: ConfigurationKey, enabled = true) {
  return (
    !!domain.value &&
    scopeWritable(domain.value) &&
    (!key || keyWritable(key)) &&
    has(
      enabled
        ? PERMISSIONS.referenceDataParameterUpdate
        : PERMISSIONS.referenceDataParameterDisable,
    )
  )
}
const canSubmit = computed(() => {
  if (!editing.value || !editorKind.value || reloadingEditor.value) return false
  if (editorKind.value === 'domain' && !objectId.value)
    return (
      has(PERMISSIONS.referenceDataParameterCreate) &&
      (form.scopeType === 'Tenant' || has(PERMISSIONS.referenceDataPlatformManage))
    )
  if (!editorDomain.value || !scopeWritable(editorDomain.value)) return false
  if (editorKind.value !== 'domain' && activeKey.value && !keyWritable(activeKey.value))
    return false
  if (editorKind.value === 'state')
    return has(
      stateTarget.value?.enabled
        ? PERMISSIONS.referenceDataParameterUpdate
        : PERMISSIONS.referenceDataParameterDisable,
    )
  return has(
    objectId.value
      ? PERMISSIONS.referenceDataParameterUpdate
      : PERMISSIONS.referenceDataParameterCreate,
  )
})
const title = computed(() =>
  editorKind.value === 'domain'
    ? copy.value.domains
    : editorKind.value === 'key'
      ? copy.value.keys
      : editorKind.value === 'value'
        ? copy.value.valueDetails
        : stateTarget.value?.enabled
          ? copy.value.enable
          : copy.value.disable,
)
function clearError() {
  error.value = ''
  traceId.value = ''
  conflict.value = false
  fields.value = {}
}
function report(caught: unknown) {
  if (caught instanceof ApiError && caught.kind === 'cancelled') return
  const details = caught instanceof ApiError ? caught.details : undefined
  conflict.value = details?.code === 'REF-CONCURRENCY-CONFLICT'
  const codes: Record<string, string> = {
    'REF-CONFIG-READ-ONLY': copy.value.readOnlyHint,
    'REF-CONFIG-TYPE-CHANGE-NOT-ALLOWED': copy.value.typeLocked,
    'REF-CONFIG-NID-IMMUTABLE': copy.value.parameterIdentityHint,
    'REF-CONFIG-DUPLICATE-NID': copy.value.duplicateConfiguration,
    'REF-CONFIG-DUPLICATE-VALUE': copy.value.duplicateValue,
    'REF-CONFIG-MANDATORY-VALUE-MISSING': copy.value.mandatoryMissing,
    'REF-CONFIG-MULTI-VALUE-LIMIT': copy.value.multiLimit,
    'REF-CONFIG-KEY-LIMIT': copy.value.keyLimit,
    'REF-CONFIG-VALUE-MODE-CONFLICT': copy.value.modeConflict,
    'REF-CONFIG-DEFAULT-MUST-BE-ENABLED': copy.value.defaultMustBeEnabled,
    'REF-CONFIG-SENSITIVE-REJECTED': copy.value.sensitiveRejected,
  }
  error.value = conflict.value
    ? copy.value.conflict
    : details?.status === 403
      ? copy.value.forbidden
      : details?.status === 404
        ? copy.value.notFound
        : (codes[details?.code ?? ''] ??
          (details?.status === 400 || details?.status === 422 || caught instanceof SyntaxError
            ? copy.value.invalid
            : copy.value.unavailable))
  traceId.value = details?.traceId ?? ''
  if (typeof details?.parameters?.field === 'string')
    fields.value[details.parameters.field] = error.value
}
async function allowDiscard() {
  if (busy.value) return false
  if (!dirty.value) return true
  try {
    await ElMessageBox.confirm(copy.value.discard, copy.value.parameterTitle, {
      confirmButtonText: copy.value.close,
      cancelButtonText: copy.value.cancel,
    })
    return true
  } catch {
    return false
  }
}
function clearSelection() {
  selectionSequence++
  selectionRequest?.abort()
  selectedId.value = null
  domain.value = null
  detailLoading.value = false
}
async function loadDomains(request: AppDataTableRequest) {
  const signature = JSON.stringify({
    page: request.pageIndex,
    size: request.pageSize,
    filters: request.filters,
    sort: request.sort,
  })
  if (lastQuery && signature !== lastQuery) clearSelection()
  lastQuery = signature
  listRequest?.abort()
  listRequest = new AbortController()
  if (!api) throw new Error(copy.value.unavailable)
  const result = await api.listConfigurationDomains(
    {
      pageIndex: request.pageIndex,
      pageSize: Math.min(100, request.pageSize),
      keyword: String(request.filters.keyword ?? ''),
      scopeType: String(request.filters.scopeType ?? ''),
      status: String(request.filters.status ?? ''),
      ...(request.sort
        ? { sortField: request.sort.field, descending: request.sort.order === 'desc' }
        : {}),
    },
    { signal: listRequest.signal },
  )
  if (selectedId.value && !result.items.some((item) => item.id === selectedId.value))
    clearSelection()
  return result
}
function search() {
  master.value?.setTopQuery({ ...query })
}
function reset() {
  Object.assign(query, { keyword: '', scopeType: '', status: '' })
  search()
}
async function selectDomain(rows: ConfigurationDomainSummary[]) {
  const id = rows[0]?.id ?? null
  if (id === selectedId.value) return
  if (!(await allowDiscard())) return
  editorKind.value = null
  viewer.value = null
  clearSelection()
  clearError()
  if (!id || !api) return
  selectedId.value = id
  detailLoading.value = true
  selectionRequest = new AbortController()
  const sequence = selectionSequence
  try {
    const result = await api.getConfigurationDomain(id, { signal: selectionRequest.signal })
    if (sequence === selectionSequence) domain.value = result
  } catch (caught) {
    if (sequence === selectionSequence) report(caught)
  } finally {
    if (sequence === selectionSequence) detailLoading.value = false
  }
}
function resetForm() {
  cancelEditorReload()
  Object.assign(form, {
    nId: '',
    name: '',
    description: '',
    scopeType: 'Tenant',
    changeReason: '',
    dataType: 'String',
    valueMode: 'Single',
    valueJson: null,
    defaultValueJson: null,
    isMandatory: false,
    isReadOnly: false,
    dictionaryNId: '',
    referenceTarget: '',
    status: 'Active',
    sort: 0,
    isDefault: false,
    enabled: true,
  })
  objectId.value = null
  keyId.value = null
  enumItems.value = []
  clearError()
  editorTab.value = 'basics'
}
function snapshot() {
  savedSnapshot.value = JSON.stringify(form)
}
async function openDomain(create = false) {
  if (!(await allowDiscard()) || (!create && !domain.value)) return
  resetForm()
  editorDomain.value = create ? null : domain.value
  if (!create && domain.value) {
    const item = domain.value
    objectId.value = item.id
    Object.assign(form, {
      nId: item.nId,
      name: item.name,
      description: item.description ?? '',
      scopeType: item.scopeType,
    })
  }
  editing.value = true
  editorKind.value = 'domain'
  snapshot()
}
async function openKey(key: ConfigurationKey | null, edit = false) {
  if (!domain.value || !(await allowDiscard())) return
  resetForm()
  editorDomain.value = domain.value
  objectId.value = key?.id ?? null
  keyId.value = key?.id ?? null
  if (key)
    Object.assign(form, {
      nId: key.nId,
      name: key.name,
      description: key.description ?? '',
      dataType: key.dataType,
      valueMode: key.valueMode,
      valueJson: key.valueJson ?? null,
      defaultValueJson: key.defaultValueJson ?? null,
      isMandatory: key.isMandatory,
      isReadOnly: key.isReadOnly,
      dictionaryNId: key.dictionaryNId ?? '',
      referenceTarget: key.referenceTarget ?? '',
      status: key.status,
      sort: key.sort,
    })
  editing.value = edit
  editorKind.value = 'key'
  snapshot()
  if (form.dataType === 'Enum') void loadEnum()
}
async function openValue(value: ConfigurationMultiValue | null) {
  const key = selectedKey.value
  if (!key || !domain.value || !(await allowDiscard())) return
  resetForm()
  editorDomain.value = domain.value
  keyId.value = key.id
  objectId.value = value?.id ?? null
  Object.assign(form, {
    nId: value?.nId ?? '',
    name: value?.name ?? '',
    dataType: key.dataType,
    dictionaryNId: key.dictionaryNId ?? '',
    valueJson:
      value?.valueJson ??
      (key.dataType === 'Boolean' ? 'false' : key.dataType === 'String' ? '""' : ''),
    sort: value?.sort ?? 0,
    isDefault: value?.isDefault ?? false,
    enabled: value?.enabled ?? true,
  })
  editing.value = true
  editorKind.value = 'value'
  snapshot()
  if (form.dataType === 'Enum') void loadEnum()
}
async function loadEnum() {
  enumRequest?.abort()
  enumRequest = new AbortController()
  const request = enumRequest
  enumItems.value = []
  if (!api || !form.dictionaryNId.trim()) return
  enumLoading.value = true
  try {
    const nId = form.dictionaryNId.trim()
    const options = { signal: request.signal }
    const platform = editorDomain.value?.scopeType === 'Platform'
    const listing = platform
      ? await api.listDictionaries(
          { pageIndex: 1, pageSize: 100, keyword: nId, scopeType: 'Platform', status: 'Published' },
          options,
        )
      : null
    const platformId = listing?.items.find((item) => item.nId === nId.toUpperCase())?.id
    if (platform && !platformId) {
      error.value = copy.value.notFound
      return
    }
    const result = platformId
      ? await api.getDictionary(platformId, options)
      : await api.getEffectiveDictionary(nId, options)
    if (!request.signal.aborted) enumItems.value = result.items.filter((item) => item.enabled)
  } catch (caught) {
    if (!request.signal.aborted) report(caught)
  } finally {
    if (request === enumRequest) enumLoading.value = false
  }
}
watch(
  () => form.dictionaryNId,
  () => {
    enumItems.value = []
    enumRequest?.abort()
  },
)
watch(
  () => form.valueMode,
  (value, previous) => {
    if (value !== previous && value === 'Multi') {
      form.valueJson = null
      form.defaultValueJson = null
    }
  },
)
function version(): ConfigurationVersion {
  return {
    changeReason: form.changeReason.trim(),
    expectedAppDomainOptimisticVersion: editorDomain.value!.optimisticVersion,
    expectedAppDomainConcurrencyVersion: editorDomain.value!.concurrencyVersion,
  }
}
function validate() {
  fields.value = {}
  if (!form.changeReason.trim() || form.changeReason.length > 500)
    fields.value.changeReason = copy.value.required
  if (editorKind.value !== 'state') {
    if (!/^[A-Za-z][A-Za-z0-9_-]{1,63}$/.test(form.nId.trim()))
      fields.value.nId = copy.value.invalidNId
    if (editorKind.value !== 'value' && !form.name.trim()) fields.value.name = copy.value.required
    if (!Number.isInteger(form.sort) || form.sort < 0) fields.value.sort = copy.value.invalidSort
    if (editorKind.value === 'key' && form.dataType === 'Enum' && !form.dictionaryNId.trim())
      fields.value.dictionaryNId = copy.value.required
    if (editorKind.value === 'key' && form.dataType === 'Reference' && !form.referenceTarget.trim())
      fields.value.referenceTarget = copy.value.required
    if (editorKind.value === 'value' || (editorKind.value === 'key' && form.valueMode === 'Single'))
      for (const name of editorKind.value === 'value'
        ? (['valueJson'] as const)
        : (['valueJson', 'defaultValueJson'] as const)) {
        if (form[name] === null) continue
        try {
          if (JSON.parse(form[name]!) === null)
            fields.value[name === 'valueJson' ? 'value' : 'defaultValue'] = copy.value.jsonInvalid
        } catch {
          fields.value[name === 'valueJson' ? 'value' : 'defaultValue'] = copy.value.jsonInvalid
        }
      }
  }
  return Object.keys(fields.value).length === 0
}
async function save() {
  if (!api || busy.value || !canSubmit.value || !validate()) return
  busy.value = true
  clearError()
  try {
    let result: ConfigurationDomain
    if (editorKind.value === 'domain') {
      const request = {
        nId: form.nId.trim(),
        name: form.name.trim(),
        description: form.description.trim() || null,
        scopeType: form.scopeType,
        scopeId: null,
        changeReason: form.changeReason.trim(),
      }
      result = objectId.value
        ? await api.updateConfigurationDomain(objectId.value, {
            ...request,
            expectedOptimisticVersion: editorDomain.value!.optimisticVersion,
            expectedConcurrencyVersion: editorDomain.value!.concurrencyVersion,
          })
        : await api.createConfigurationDomain(request)
    } else if (editorKind.value === 'key') {
      const request: WriteConfigurationKey = {
        ...version(),
        nId: form.nId.trim(),
        name: form.name.trim(),
        description: form.description.trim() || null,
        dataType: form.dataType,
        valueMode: form.valueMode,
        valueJson: form.valueMode === 'Multi' ? null : form.valueJson,
        defaultValueJson: form.valueMode === 'Multi' ? null : form.defaultValueJson,
        isMandatory: form.isMandatory,
        isReadOnly: form.isReadOnly,
        dictionaryNId: form.dataType === 'Enum' ? form.dictionaryNId.trim() : null,
        referenceTarget: form.dataType === 'Reference' ? form.referenceTarget.trim() : null,
        status: form.status,
        sort: form.sort,
      }
      result = objectId.value
        ? await api.updateConfigurationKey(editorDomain.value!.id, objectId.value, request)
        : await api.addConfigurationKey(editorDomain.value!.id, request)
    } else if (editorKind.value === 'value') {
      const request = {
        ...version(),
        nId: form.nId.trim(),
        name: form.name.trim() || null,
        valueJson: form.valueJson!,
        sort: form.sort,
        isDefault: form.isDefault,
        enabled: form.enabled,
      }
      result = objectId.value
        ? await api.updateConfigurationValue(
            editorDomain.value!.id,
            keyId.value!,
            objectId.value,
            request,
          )
        : await api.addConfigurationValue(editorDomain.value!.id, keyId.value!, request)
    } else {
      const target = stateTarget.value!
      const id = editorDomain.value!.id
      result = target.valueId
        ? await api.setConfigurationValueEnabled(
            id,
            target.keyId!,
            target.valueId,
            target.enabled,
            version(),
          )
        : target.keyId
          ? await api.setConfigurationKeyStatus(id, target.keyId, target.enabled, version())
          : await api.setConfigurationDomainStatus(id, target.enabled, version())
    }
    domain.value = result
    selectedId.value = result.id
    editorKind.value = null
    savedSnapshot.value = ''
    ElMessage.success(copy.value.saved)
    await master.value?.reload()
  } catch (caught) {
    report(caught)
  } finally {
    busy.value = false
  }
}
async function closeEditor(value = false) {
  if (!value && (await allowDiscard())) {
    cancelEditorReload()
    editorKind.value = null
    enumRequest?.abort()
    clearError()
  }
}
function cancelEditorReload() {
  editorSequence++
  editorRequest?.abort()
  reloadingEditor.value = false
}
async function reloadEditor() {
  if (!api || !editorDomain.value || busy.value || !(await allowDiscard())) return
  const id = editorDomain.value.id
  const previous = editorKind.value
  const previousObject = objectId.value
  const previousKey = keyId.value
  cancelEditorReload()
  editorRequest = new AbortController()
  const sequence = editorSequence
  reloadingEditor.value = true
  try {
    const result = await api.getConfigurationDomain(id, { signal: editorRequest.signal })
    if (sequence !== editorSequence) return
    reloadingEditor.value = false
    domain.value = result
    editorKind.value = null
    if (previous === 'domain') await openDomain()
    else if (previous === 'key')
      await openKey(result.keys.find((item) => item.id === previousObject) ?? null, true)
    else if (previous === 'value') {
      keyId.value = previousKey
      await openValue(
        result.keys
          .find((item) => item.id === previousKey)
          ?.multiValues.find((item) => item.id === previousObject) ?? null,
      )
    }
  } catch (caught) {
    if (sequence === editorSequence) report(caught)
  } finally {
    if (sequence === editorSequence) reloadingEditor.value = false
  }
}
async function copyUnsaved() {
  try {
    await navigator.clipboard.writeText(JSON.stringify(form, null, 2))
    ElMessage.success(copy.value.copied)
  } catch {
    ElMessage.warning(copy.value.copyFailed)
  }
}
async function changeState(
  key: ConfigurationKey | null,
  value: ConfigurationMultiValue | null,
  enabled: boolean,
) {
  if (!domain.value || !(await allowDiscard()) || !canState(key ?? undefined, enabled)) return
  resetForm()
  editorDomain.value = domain.value
  keyId.value = key?.id ?? null
  stateTarget.value = { keyId: key?.id ?? null, valueId: value?.id ?? null, enabled }
  form.nId = value?.nId ?? key?.nId ?? domain.value.nId
  editing.value = true
  editorKind.value = 'state'
  snapshot()
}
async function openHistory(key: ConfigurationKey | null) {
  if (!domain.value || !(await allowDiscard())) return
  editorKind.value = null
  clearError()
  historyTarget.value = { id: domain.value.id, keyId: key?.id ?? null }
  viewer.value = 'history'
  await nextTick()
  await historyTable.value?.reload()
}
async function loadHistory(request: AppDataTableRequest) {
  if (!api || !historyTarget.value)
    return { items: [] as ConfigurationHistory[], total: 0, pageIndex: 1, pageSize: 20 }
  viewerRequest?.abort()
  viewerRequest = new AbortController()
  return api.configurationHistory(
    historyTarget.value.id,
    historyTarget.value.keyId,
    request.pageIndex,
    Math.min(request.pageSize, 100),
    { signal: viewerRequest.signal },
  )
}
async function openEffective(key: ConfigurationKey) {
  if (!api || !domain.value || !(await allowDiscard())) return
  editorKind.value = null
  clearError()
  effective.value = null
  viewer.value = 'effective'
  viewerRequest?.abort()
  viewerRequest = new AbortController()
  const sequence = ++viewerSequence
  try {
    const result = await api.resolveConfiguration(domain.value.nId, key.nId, {
      signal: viewerRequest.signal,
    })
    if (sequence === viewerSequence) effective.value = result
  } catch (caught) {
    if (sequence === viewerSequence) report(caught)
  }
}
function closeViewer(value = false) {
  if (!value) {
    viewerSequence++
    viewerRequest?.abort()
    viewer.value = null
    clearError()
  }
}
function valueDisplay(key: ConfigurationKey) {
  return key.valueMode === 'Multi'
    ? String(key.multiValues.filter((item) => item.enabled).length)
    : (key.valueJson ?? key.defaultValueJson ?? copy.value.blocksInheritance)
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
  cancelEditorReload()
  listRequest?.abort()
  selectionRequest?.abort()
  enumRequest?.abort()
  viewerRequest?.abort()
  selectionSequence++
  viewerSequence++
  window.removeEventListener('beforeunload', beforeUnload)
})
</script>

<template>
  <AppPage
    data-testid="reference-data-parameters"
    :title="copy.parameterTitle"
    :description="copy.parameterDescription"
  >
    <template #actions
      ><PermissionGate :permission-n-id="PERMISSIONS.referenceDataParameterCreate"
        ><el-button
          type="primary"
          :icon="Plus"
          data-testid="parameter-create-domain"
          @click="openDomain(true)"
          >{{ copy.newDomain }}</el-button
        ></PermissionGate
      ></template
    >
    <el-alert
      v-if="error && !editorKind && !viewer"
      :title="error"
      type="error"
      :closable="false"
      show-icon
      ><p v-if="traceId">{{ copy.traceId }}: {{ traceId }}</p></el-alert
    >
    <AppQueryPanel show-actions grid @submit="search" @reset="reset">
      <label class="parameter-query"
        ><span>{{ copy.keyword }}</span
        ><el-input
          v-model="query.keyword"
          :aria-label="copy.keyword"
          maxlength="200"
          clearable
          @keyup.enter="search"
      /></label>
      <label class="parameter-query"
        ><span>{{ copy.scope }}</span
        ><el-select
          v-model="query.scopeType"
          :aria-label="copy.scope"
          clearable
          :placeholder="copy.all"
          ><el-option v-for="item in scopeOptions" :key="item.value" v-bind="item" /></el-select
      ></label>
      <label class="parameter-query"
        ><span>{{ copy.status }}</span
        ><el-select
          v-model="query.status"
          :aria-label="copy.status"
          clearable
          :placeholder="copy.all"
          ><el-option v-for="item in statusOptions" :key="item.value" v-bind="item" /></el-select
      ></label>
    </AppQueryPanel>
    <div class="parameter-layout">
      <section class="parameter-master" :aria-label="copy.domains">
        <AppDataTable
          ref="master"
          table-key="reference-data-parameter-domains"
          :columns="domainColumns"
          :loader="loadDomains"
          row-key="id"
          selection="single"
          :selected-row-key="selectedId"
          toolbar-profile="compact"
          :toolbar-title="copy.domains"
          :page-size="20"
          @selection-change="selectDomain"
          @load-error="report"
        >
          <template
            v-for="col in domainColumns"
            :key="col.field"
            #[`cell-${col.field}`]="{ row, column }"
            ><div v-if="column.field === 'name'">
              <strong>{{ row.name }}</strong
              ><small class="parameter-id">{{ row.nId }}</small>
            </div>
            <span v-else-if="column.field === 'scopeType'">{{
              row.scopeType === 'Tenant' ? copy.tenant : copy.platform
            }}</span
            ><span v-else-if="column.field === 'status'">{{
              row.status === 'Active' ? copy.active : copy.disabled
            }}</span
            ><span v-else>{{ row[column.field] }}</span></template
          >
        </AppDataTable>
      </section>
      <section class="parameter-detail" :aria-label="copy.keys" :aria-busy="detailLoading">
        <el-skeleton v-if="detailLoading" :rows="5" animated />
        <el-empty v-else-if="!domain" :description="copy.selectDomain" />
        <template v-else>
          <header class="parameter-context">
            <div>
              <h2>{{ domain.name }}</h2>
              <p>
                {{ domain.nId }} ·
                {{ domain.scopeType === 'Tenant' ? copy.tenant : copy.platform }} ·
                {{ copy.revision }} {{ domain.revision }} ·
                {{ domain.status === 'Active' ? copy.active : copy.disabled }}
              </p>
            </div>
            <div class="parameter-actions">
              <el-button
                v-if="scopeWritable(domain) && has(PERMISSIONS.referenceDataParameterUpdate)"
                @click="openDomain()"
                >{{ copy.edit }}</el-button
              ><el-button @click="openHistory(null)">{{ copy.history }}</el-button
              ><el-button
                v-if="canState(undefined, domain.status !== 'Active')"
                @click="changeState(null, null, domain.status !== 'Active')"
                >{{ domain.status === 'Active' ? copy.disable : copy.enable }}</el-button
              >
            </div>
          </header>
          <AppDataTable
            :key="domain.id"
            table-key="reference-data-parameter-keys"
            :columns="keyColumns"
            :rows="domain.keys"
            row-key="id"
            :page-size="20"
          >
            <template #toolbar-actions
              ><el-button
                v-if="scopeWritable(domain) && has(PERMISSIONS.referenceDataParameterCreate)"
                type="primary"
                :icon="Plus"
                :disabled="domain.keys.length >= 500"
                data-testid="parameter-create-key"
                @click="openKey(null, true)"
                >{{ copy.newKey }}</el-button
              ></template
            >
            <template
              v-for="col in keyColumns"
              :key="col.field"
              #[`cell-${col.field}`]="{ row, column }"
              ><span v-if="column.field === 'valueJson'" :title="valueDisplay(row)">{{
                valueDisplay(row)
              }}</span
              ><span v-else-if="column.field === 'valueMode'">{{
                row.valueMode === 'Single' ? copy.singleValue : copy.multiValue
              }}</span
              ><span v-else-if="column.field === 'status'">{{
                row.status === 'Active' ? copy.active : copy.disabled
              }}</span
              ><span v-else-if="column.field === 'isReadOnly'">{{
                row.isReadOnly ? copy.readOnlyKey : '—'
              }}</span
              ><span v-else-if="column.field === 'dataType'">{{
                types.find((item) => item.value === row.dataType)?.label
              }}</span
              ><span v-else>{{ row[column.field] }}</span></template
            >
            <template #actions="{ row, availableWidth }"
              ><el-button link @click="openKey(row)">{{ copy.detail }}</el-button
              ><el-button
                v-if="canEditKey(row) && availableWidth >= 180"
                link
                type="primary"
                @click="openKey(row, true)"
                >{{ copy.edit }}</el-button
              ><el-dropdown trigger="click"
                ><el-button link>{{ copy.more }}</el-button
                ><template #dropdown
                  ><el-dropdown-menu
                    ><el-dropdown-item
                      v-if="canEditKey(row) && availableWidth < 180"
                      @click="openKey(row, true)"
                      >{{ copy.edit }}</el-dropdown-item
                    ><el-dropdown-item @click="openHistory(row)">{{
                      copy.history
                    }}</el-dropdown-item
                    ><el-dropdown-item @click="openEffective(row)">{{
                      copy.viewEffective
                    }}</el-dropdown-item
                    ><el-dropdown-item
                      v-if="canState(row, row.status !== 'Active')"
                      @click="changeState(row, null, row.status !== 'Active')"
                      >{{ row.status === 'Active' ? copy.disable : copy.enable }}</el-dropdown-item
                    ></el-dropdown-menu
                  ></template
                ></el-dropdown
              ></template
            >
          </AppDataTable>
        </template>
      </section>
    </div>

    <AppFormDrawer
      :model-value="editorKind !== null"
      :title="title"
      size="wide"
      :busy="busy"
      @update:model-value="closeEditor"
      @submit="save"
    >
      <el-alert v-if="error" :title="error" type="error" :closable="false" show-icon
        ><p v-if="traceId">{{ copy.traceId }}: {{ traceId }}</p>
        <el-button v-if="conflict" :disabled="busy" @click="reloadEditor">{{
          copy.reload
        }}</el-button
        ><el-button v-if="conflict" @click="copyUnsaved">{{
          copy.copyUnsaved
        }}</el-button></el-alert
      >
      <el-alert
        v-if="activeKey?.isReadOnly"
        :title="copy.readOnlyHint"
        type="info"
        :closable="false"
      />
      <el-alert
        v-else-if="!canSubmit && editing"
        :title="copy.protected"
        type="info"
        :closable="false"
      />
      <p v-if="editorDomain" class="parameter-path">
        {{ editorDomain.nId }}<template v-if="activeKey">.{{ activeKey.nId }}</template> ·
        {{ copy.revision }} {{ editorDomain.revision }}
      </p>
      <el-tabs v-if="editorKind === 'key' && activeKey?.valueMode === 'Multi'" v-model="editorTab"
        ><el-tab-pane :label="copy.basics" name="basics" /><el-tab-pane
          :label="copy.valueDetails"
          name="values"
      /></el-tabs>
      <template v-if="editorKind === 'key' && editorTab === 'values' && activeKey">
        <el-alert :title="copy.multiValueHint" type="info" :closable="false" />
        <el-button
          v-if="canSubmit && has(PERMISSIONS.referenceDataParameterCreate)"
          :disabled="busy || activeKey.multiValues.length >= 1000"
          :icon="Plus"
          @click="openValue(null)"
          >{{ copy.newValue }}</el-button
        >
        <AppDataTable
          :key="activeKey.id"
          table-key="reference-data-parameter-values"
          :columns="valueColumns"
          :rows="activeKey.multiValues"
          row-key="id"
          mode="detail"
          toolbar-profile="compact"
          :page-size="20"
        >
          <template
            v-for="col in valueColumns"
            :key="col.field"
            #[`cell-${col.field}`]="{ row, column }"
            ><span v-if="column.field === 'isDefault' || column.field === 'enabled'">{{
              row[column.field] ? copy.trueValue : copy.falseValue
            }}</span
            ><span v-else>{{ row[column.field] }}</span></template
          >
          <template #actions="{ row }"
            ><el-button
              v-if="canSubmit"
              link
              type="primary"
              :disabled="busy"
              @click="openValue(row)"
              >{{ copy.edit }}</el-button
            ><el-button
              v-if="canSubmit && canState(activeKey, !row.enabled)"
              link
              :disabled="busy || (row.enabled && row.isDefault)"
              @click="changeState(activeKey, row, !row.enabled)"
              >{{ row.enabled ? copy.disable : copy.enable }}</el-button
            ></template
          >
        </AppDataTable>
      </template>
      <el-form v-else label-position="top" @submit.prevent="save">
        <el-alert
          v-if="editorKind === 'state'"
          :title="copy.parameterStateHint"
          type="info"
          :closable="false"
        />
        <template v-else>
          <p v-if="editorKind === 'domain' || editorKind === 'key'" class="parameter-hint">
            {{ copy.parameterIdentityHint }}
          </p>
          <div class="parameter-form-grid">
            <el-form-item :label="copy.nId" :error="fields.nId" required
              ><el-input
                v-model="form.nId"
                :aria-label="copy.nId"
                :disabled="!canSubmit || busy || !!objectId"
                maxlength="64"
            /></el-form-item>
            <el-form-item :label="copy.name" :error="fields.name" :required="editorKind !== 'value'"
              ><el-input
                v-model="form.name"
                :aria-label="copy.name"
                :disabled="!canSubmit || busy"
                maxlength="200"
            /></el-form-item>
          </div>
          <el-form-item v-if="editorKind === 'domain'" :label="copy.scope"
            ><el-select
              v-model="form.scopeType"
              :aria-label="copy.scope"
              :disabled="!canSubmit || busy || !!objectId"
              ><el-option value="Tenant" :label="copy.tenant" /><el-option
                v-if="has(PERMISSIONS.referenceDataPlatformManage) || form.scopeType === 'Platform'"
                value="Platform"
                :label="copy.platform" /></el-select
          ></el-form-item>
          <el-form-item
            v-if="editorKind !== 'value'"
            :label="copy.description"
            :error="fields.description"
            ><el-input
              v-model="form.description"
              :aria-label="copy.description"
              :disabled="!canSubmit || busy"
              type="textarea"
              maxlength="2000"
              :rows="2"
          /></el-form-item>
          <template v-if="editorKind === 'key'">
            <el-alert
              v-if="activeKey?.hasHadValue"
              :title="copy.typeLocked"
              type="info"
              :closable="false"
            />
            <div class="parameter-form-grid">
              <el-form-item :label="copy.dataType" :error="fields.dataType"
                ><el-select
                  v-model="form.dataType"
                  :aria-label="copy.dataType"
                  :disabled="!canSubmit || busy || activeKey?.hasHadValue"
                  ><el-option v-for="item in types" :key="item.value" v-bind="item" /></el-select
              ></el-form-item>
              <el-form-item :label="copy.valueMode" :error="fields.valueMode"
                ><el-select
                  v-model="form.valueMode"
                  :aria-label="copy.valueMode"
                  :disabled="!canSubmit || busy || activeKey?.hasHadValue"
                  ><el-option value="Single" :label="copy.singleValue" /><el-option
                    value="Multi"
                    :label="copy.multiValue" /></el-select
              ></el-form-item>
            </div>
            <el-form-item
              v-if="form.dataType === 'Enum'"
              :label="copy.dictionaryNId"
              :error="fields.dictionaryNId"
              required
              ><div class="parameter-enum">
                <el-input
                  v-model="form.dictionaryNId"
                  :aria-label="copy.dictionaryNId"
                  :disabled="!canSubmit || busy"
                  maxlength="64"
                /><el-button
                  :loading="enumLoading"
                  :disabled="busy || !form.dictionaryNId"
                  @click="loadEnum"
                  >{{ copy.enumLoad }}</el-button
                >
              </div></el-form-item
            >
            <el-form-item
              v-if="form.dataType === 'Reference'"
              :label="copy.referenceTarget"
              :error="fields.referenceTarget"
              required
              ><el-input
                v-model="form.referenceTarget"
                :aria-label="copy.referenceTarget"
                :disabled="!canSubmit || busy"
                maxlength="128"
            /></el-form-item>
            <el-alert
              :title="form.valueMode === 'Multi' ? copy.multiValueHint : copy.emptyValueHint"
              type="info"
              :closable="false"
            />
            <template v-if="form.valueMode === 'Single'">
              <el-form-item :label="copy.value" :error="fields.value"
                ><ConfigurationValueEditor
                  v-model="form.valueJson"
                  :data-type="form.dataType"
                  :label="copy.value"
                  :enum-items="enumItems"
                  :disabled="!canSubmit || busy"
              /></el-form-item>
              <el-form-item :label="copy.defaultValue" :error="fields.defaultValue"
                ><ConfigurationValueEditor
                  v-model="form.defaultValueJson"
                  :data-type="form.dataType"
                  :label="copy.defaultValue"
                  :enum-items="enumItems"
                  :disabled="!canSubmit || busy"
              /></el-form-item>
            </template>
            <div class="parameter-form-grid">
              <el-form-item :label="copy.mandatory"
                ><el-switch
                  v-model="form.isMandatory"
                  :aria-label="copy.mandatory"
                  :disabled="!canSubmit || busy" /></el-form-item
              ><el-form-item :label="copy.readOnlyKey"
                ><el-switch
                  v-model="form.isReadOnly"
                  :aria-label="copy.readOnlyKey"
                  :disabled="!canSubmit || busy"
              /></el-form-item>
            </div>
          </template>
          <template v-if="editorKind === 'value'">
            <el-form-item :label="copy.value" :error="fields.value" required
              ><ConfigurationValueEditor
                v-model="form.valueJson"
                :data-type="form.dataType"
                :label="copy.value"
                :enum-items="enumItems"
                :disabled="!canSubmit || busy"
                :allow-unset="false"
            /></el-form-item>
            <div class="parameter-form-grid">
              <el-form-item :label="copy.isDefault" :error="fields.isDefault"
                ><el-switch
                  v-model="form.isDefault"
                  :aria-label="copy.isDefault"
                  :disabled="!canSubmit || busy" /></el-form-item
              ><el-form-item :label="copy.enabled"
                ><el-switch
                  v-model="form.enabled"
                  :aria-label="copy.enabled"
                  :disabled="
                    !canSubmit ||
                    busy ||
                    (!!objectId &&
                      !!activeKey?.multiValues.find((item) => item.id === objectId)?.enabled &&
                      !has(PERMISSIONS.referenceDataParameterDisable))
                  "
              /></el-form-item>
            </div>
          </template>
          <div v-if="editorKind !== 'domain'" class="parameter-form-grid">
            <el-form-item :label="copy.sort" :error="fields.sort"
              ><el-input-number
                v-model="form.sort"
                :aria-label="copy.sort"
                :min="0"
                :precision="0"
                :disabled="!canSubmit || busy" /></el-form-item
            ><el-form-item v-if="editorKind === 'key'" :label="copy.status"
              ><el-select
                v-model="form.status"
                :aria-label="copy.status"
                :disabled="!canSubmit || busy"
                ><el-option
                  v-for="item in statusOptions"
                  :key="item.value"
                  :disabled="
                    item.value === 'Disabled' &&
                    activeKey?.status === 'Active' &&
                    !has(PERMISSIONS.referenceDataParameterDisable)
                  "
                  v-bind="item" /></el-select
            ></el-form-item>
          </div>
        </template>
        <el-form-item
          v-if="editing"
          :label="copy.changeReason"
          :error="fields.changeReason"
          required
          ><el-input
            v-model="form.changeReason"
            :aria-label="copy.changeReason"
            :disabled="!canSubmit || busy"
            type="textarea"
            maxlength="500"
            :rows="2"
        /></el-form-item>
      </el-form>
      <template #footer
        ><el-button :disabled="busy" @click="closeEditor()">{{ copy.close }}</el-button
        ><el-button
          v-if="editing && !(editorKind === 'key' && editorTab === 'values')"
          type="primary"
          :disabled="!canSubmit"
          :loading="busy"
          data-testid="parameter-save"
          @click="save"
          >{{ copy.save }}</el-button
        ></template
      >
    </AppFormDrawer>

    <AppFormDrawer
      :model-value="viewer !== null"
      :title="viewer === 'history' ? copy.history : copy.effective"
      size="wide"
      @update:model-value="closeViewer"
    >
      <el-alert v-if="error" :title="error" type="error" :closable="false" />
      <p v-if="traceId">{{ copy.traceId }}: {{ traceId }}</p>
      <AppDataTable
        v-if="viewer === 'history'"
        ref="historyTable"
        :key="`${historyTarget?.id}:${historyTarget?.keyId}`"
        table-key="reference-data-parameter-history"
        :columns="historyColumns"
        :loader="loadHistory"
        row-key="id"
        :page-size="20"
        toolbar-profile="compact"
        @load-error="report"
      />
      <el-descriptions v-else-if="effective" :column="1" border>
        <el-descriptions-item :label="copy.fullPath">{{ effective.fullNId }}</el-descriptions-item
        ><el-descriptions-item :label="copy.dataType">{{ effective.dataType }}</el-descriptions-item
        ><el-descriptions-item :label="copy.scope">{{
          effective.sourceScope === 'Tenant' ? copy.tenant : copy.platform
        }}</el-descriptions-item
        ><el-descriptions-item :label="copy.revision">{{
          effective.revision
        }}</el-descriptions-item>
        <el-descriptions-item :label="copy.valueSource">{{
          effective.blocksInheritance
            ? copy.blocksInheritance
            : effective.usesDefaultValue
              ? copy.usesDefault
              : copy.configuredValue
        }}</el-descriptions-item
        ><el-descriptions-item :label="copy.value">
          <pre class="parameter-effective-value">{{
            effective.valueMode === 'Multi'
              ? effective.multiValues.map((item) => item.valueJson).join('\n') || '[]'
              : (effective.valueJson ?? 'null')
          }}</pre>
        </el-descriptions-item>
      </el-descriptions>
      <el-skeleton v-else-if="viewer === 'effective' && !error" :rows="4" animated />
      <template #footer
        ><el-button @click="closeViewer()">{{ copy.close }}</el-button></template
      >
    </AppFormDrawer>
  </AppPage>
</template>

<style scoped>
.parameter-layout {
  display: grid;
  grid-template-columns: minmax(310px, 360px) minmax(0, 1fr);
  gap: 16px;
  align-items: start;
}
.parameter-master,
.parameter-detail {
  min-width: 0;
}
.parameter-detail {
  padding: 16px;
  border: 1px solid var(--el-border-color-light);
  border-radius: 8px;
  background: var(--el-bg-color);
}
.parameter-query {
  display: grid;
  gap: 6px;
  min-width: 180px;
}
.parameter-id {
  display: block;
  margin-top: 4px;
  color: var(--el-text-color-secondary);
}
.parameter-context {
  display: flex;
  gap: 12px;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 12px;
}
.parameter-context h2 {
  margin: 0;
  font-size: 18px;
}
.parameter-context p,
.parameter-path,
.parameter-hint {
  color: var(--el-text-color-secondary);
  font-size: 13px;
  line-height: 1.6;
}
.parameter-context p {
  margin: 6px 0 0;
}
.parameter-actions,
.parameter-enum {
  display: flex;
  gap: 8px;
  align-items: center;
}
.parameter-actions > .el-button {
  margin-left: 0;
}
.parameter-form-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 16px;
}
.parameter-effective-value {
  white-space: pre-wrap;
  overflow-wrap: anywhere;
  margin: 0;
}
.el-alert {
  margin-bottom: 16px;
}
@media (max-width: 1100px) {
  .parameter-layout {
    grid-template-columns: 1fr;
  }
}
@media (max-width: 600px) {
  .parameter-form-grid {
    grid-template-columns: 1fr;
  }
}
</style>
