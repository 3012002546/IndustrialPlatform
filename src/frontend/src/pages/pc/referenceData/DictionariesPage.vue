<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, reactive, ref } from 'vue'
import { onBeforeRouteLeave } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { ArrowDown, Plus } from '@element-plus/icons-vue'
import { ApiError } from '@/api/errors'
import { getReferenceDataApi } from '@/api/referenceData'
import type {
  CreateDictionaryRequest,
  DictionaryDetail,
  DictionaryItem,
  DictionaryPublicationCheck,
  DictionarySummary,
  PublicationStatus,
  ReferenceDataQuery,
  ReferenceScope,
} from '@/api/referenceData/types'
import AppPage from '@/components/base/AppPage.vue'
import AppQueryPanel from '@/components/management/AppQueryPanel.vue'
import AppDataTable from '@/components/management/AppDataTable.vue'
import AppFormDrawer from '@/components/management/AppFormDrawer.vue'
import type {
  AppDataTableColumn,
  AppDataTableQueryMode,
  AppDataTableRequest,
} from '@/components/management/AppDataTable'
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
const error = ref('')
const listError = ref('')
const traceId = ref('')
const conflict = ref(false)
const fields = ref<Record<string, string>>({})
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
const directoryColumns = computed<AppDataTableColumn[]>(() => [
  { field: 'name', title: copy.value.name, minWidth: 120, sortable: true },
])
function statusLabel(status: PublicationStatus) {
  return statusOptions.value.find((item) => item.value === status)?.label ?? status
}
function date(value: string) {
  return new Intl.DateTimeFormat(localization.locale, {
    dateStyle: 'short',
    timeStyle: 'short',
    timeZone: localization.preferences.timeZone,
  }).format(new Date(value))
}
let listRequest: AbortController | undefined
let detailRequest: AbortController | undefined
let detailSequence = 0
function cancelDetail() {
  detailSequence++
  detailRequest?.abort()
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
    ...(request.sort === undefined
      ? {}
      : { sortField: request.sort.field, descending: request.sort.order === 'desc' }),
  }
  try {
    if (!api) throw new Error(copy.value.unavailable)
    const result = await api.listDictionaries(params, { signal: listRequest.signal })
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
  if (!formOpen.value && !publicationOpen.value) clearError()
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
function clearError() {
  error.value = ''
  traceId.value = ''
  conflict.value = false
  fields.value = {}
  itemFields.value = {}
}
function report(caught: unknown) {
  if (caught instanceof ApiError && caught.kind === 'cancelled') return
  const details = caught instanceof ApiError ? caught.details : undefined
  conflict.value = details?.code === 'REF-CONCURRENCY-CONFLICT'
  const codes: Record<string, string> = {
    'REF-DICT-DUPLICATE-NID': copy.value.duplicateItem,
    'REF-DICT-HISTORICAL-ITEM-REMOVED': copy.value.historyProtected,
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
          (details?.status === 400 ? copy.value.invalid : copy.value.unavailable))
  traceId.value = details?.traceId ?? ''
  const field = details?.parameters?.field
  if (typeof field === 'string') fields.value[field] = error.value
}
function canWrite(row: Pick<DictionarySummary, 'scopeType' | 'isFrozen' | 'isLocked'>) {
  return (
    !row.isFrozen &&
    !row.isLocked &&
    (row.scopeType === 'Tenant' || has(PERMISSIONS.referenceDataPlatformManage))
  )
}
function canEdit(row: Pick<DictionarySummary, 'status' | 'scopeType' | 'isFrozen' | 'isLocked'>) {
  return row.status === 'Draft' && canWrite(row) && has(PERMISSIONS.referenceDataDictionaryUpdate)
}
function canClone(row: DictionarySummary | null) {
  return (
    !!row &&
    row.publishedBy !== null &&
    canWrite(row) &&
    has(PERMISSIONS.referenceDataDictionaryCreate)
  )
}
function canPublish(row: DictionarySummary | null) {
  return (
    !!row &&
    row.status === 'Draft' &&
    canWrite(row) &&
    has(PERMISSIONS.referenceDataDictionaryPublish)
  )
}
function canDisable(row: DictionarySummary | null) {
  return (
    !!row &&
    ['Draft', 'Published'].includes(row.status) &&
    canWrite(row) &&
    has(PERMISSIONS.referenceDataDictionaryDisable)
  )
}
function version(row: Pick<DictionarySummary, 'optimisticVersion' | 'concurrencyVersion'>) {
  return {
    expectedOptimisticVersion: row.optimisticVersion,
    expectedConcurrencyVersion: row.concurrencyVersion,
  }
}

interface FormItem extends DictionaryItem {
  localKey: string
}
const form = reactive({
  nId: '',
  name: '',
  description: '',
  scopeType: 'Tenant' as ReferenceScope,
  items: [] as FormItem[],
})
const selected = ref<DictionaryDetail | null>(null)
const selectedSummary = computed<DictionarySummary | null>(() =>
  selected.value
    ? {
        ...selected.value,
        enabledItemCount: selected.value.items.filter((item) => item.enabled).length,
      }
    : null,
)
const formOpen = ref(false)
const editing = ref(false)
const busy = ref(false)
const savedSnapshot = ref('')
const historicalItemKeys = ref(new Set<string>())
const activeId = ref<string | null>(null)
const itemEditorOpen = ref(false)
const itemEditorMode = ref<'create' | 'edit'>('create')
const itemEditorLocalKey = ref<string | null>(null)
const itemFields = ref<Record<string, string>>({})
const itemForm = reactive<FormItem>({
  localKey: '',
  nId: '',
  name: '',
  description: null,
  sort: 0,
  enabled: true,
})
const itemSavedSnapshot = ref('')
const dirty = computed(
  () => formOpen.value && editing.value && JSON.stringify(form) !== savedSnapshot.value,
)
const itemDirty = computed(
  () => itemEditorOpen.value && JSON.stringify(itemForm) !== itemSavedSnapshot.value,
)
const canSubmit = computed(
  () =>
    editing.value &&
    (selected.value
      ? selected.value.status === 'Draft' &&
        canWrite(selected.value) &&
        has(PERMISSIONS.referenceDataDictionaryUpdate)
      : has(PERMISSIONS.referenceDataDictionaryCreate) &&
        (form.scopeType === 'Tenant' || has(PERMISSIONS.referenceDataPlatformManage))),
)
const canEditItems = computed(() => selected.value !== null && canEdit(selected.value))
const itemPreviewColumns = computed<AppDataTableColumn[]>(() => [
  { field: 'nId', title: copy.value.nId, minWidth: 110, filter: false },
  { field: 'name', title: copy.value.name, minWidth: 130, filter: false },
  { field: 'sort', title: copy.value.sort, width: 72, filter: false },
  { field: 'enabled', title: copy.value.enabled, width: 80, filter: false },
])
function itemWrite(item: FormItem) {
  return {
    nId: item.nId.trim(),
    name: item.name.trim(),
    description: item.description?.trim() || null,
    sort: item.sort,
    enabled: item.enabled,
  }
}
function payload(items: readonly FormItem[] = form.items): CreateDictionaryRequest {
  return {
    nId: form.nId.trim(),
    name: form.name.trim(),
    description: form.description.trim() || null,
    scopeType: form.scopeType,
    scopeId: null,
    items: items.map(itemWrite),
  }
}
function fill(dto: DictionaryDetail) {
  selected.value = dto
  Object.assign(form, {
    nId: dto.nId,
    name: dto.name,
    description: dto.description ?? '',
    scopeType: dto.scopeType,
    items: dto.items.map((item) => ({ ...item, localKey: crypto.randomUUID() })),
  })
  savedSnapshot.value = JSON.stringify(form)
}
function startCreate() {
  cancelDetail()
  clearError()
  selected.value = null
  editing.value = true
  historicalItemKeys.value = new Set()
  Object.assign(form, { nId: '', name: '', description: '', scopeType: 'Tenant', items: [] })
  savedSnapshot.value = JSON.stringify(form)
  formOpen.value = true
}
function create() {
  if (!dirty.value && !itemDirty.value) {
    startCreate()
    return
  }
  void (async () => {
    if (await allowDiscardAll()) startCreate()
  })()
}
async function open(
  row: DictionarySummary,
  edit = false,
  showForm = true,
  markActive = false,
  guardDiscard = false,
) {
  if (guardDiscard && !(await allowDiscardAll())) return
  if (markActive) activeId.value = row.id
  itemEditorOpen.value = false
  clearError()
  cancelDetail()
  detailRequest = new AbortController()
  const sequence = detailSequence
  try {
    if (!api) throw new Error(copy.value.unavailable)
    const dto = await api.getDictionary(row.id, { signal: detailRequest.signal })
    if (sequence !== detailSequence) return
    const check =
      dto.status === 'Draft'
        ? await api.checkDictionaryPublication(dto.id, { signal: detailRequest.signal })
        : null
    if (sequence !== detailSequence) return
    fill(dto)
    historicalItemKeys.value = new Set(
      form.items
        .filter((item) => !check?.addedItems.includes(item.nId))
        .map((item) => item.localKey),
    )
    editing.value =
      edit &&
      dto.status === 'Draft' &&
      canWrite(dto) &&
      has(PERMISSIONS.referenceDataDictionaryUpdate)
    formOpen.value = showForm
  } catch (caught) {
    if (sequence === detailSequence) report(caught)
  }
}
async function select(row: DictionarySummary) {
  await open(row, false, false, true, true)
}
function validate() {
  fields.value = {}
  if (!/^[A-Za-z][A-Za-z0-9_.-]{1,63}$/.test(form.nId)) fields.value.nId = copy.value.invalidNId
  if (!form.name.trim() || form.name.length > 200) fields.value.name = copy.value.required
  return Object.keys(fields.value).length === 0
}
function openItemEditor(item?: FormItem) {
  if (!canEditItems.value || (!item && form.items.length >= 1000)) return
  clearError()
  itemFields.value = {}
  itemEditorMode.value = item === undefined ? 'create' : 'edit'
  itemEditorLocalKey.value = item?.localKey ?? null
  Object.assign(
    itemForm,
    item === undefined
      ? {
          localKey: crypto.randomUUID(),
          nId: '',
          name: '',
          description: null,
          sort: form.items.length,
          enabled: true,
        }
      : { ...item },
  )
  itemSavedSnapshot.value = JSON.stringify(itemForm)
  itemEditorOpen.value = true
}
function validateItem(): boolean {
  itemFields.value = {}
  const id = itemForm.nId.trim().toUpperCase()
  if (!/^[A-Z0-9][A-Z0-9_.-]{0,63}$/.test(id)) itemFields.value.nId = copy.value.invalidNId
  else if (
    form.items.some(
      (item) => item.localKey !== itemEditorLocalKey.value && item.nId.trim().toUpperCase() === id,
    )
  )
    itemFields.value.nId = copy.value.duplicateItem
  if (!itemForm.name.trim() || itemForm.name.length > 200)
    itemFields.value.name = copy.value.required
  if (!Number.isInteger(itemForm.sort) || itemForm.sort < 0)
    itemFields.value.sort = copy.value.invalidSort
  return Object.keys(itemFields.value).length === 0
}
async function saveItem() {
  if (busy.value || !canEditItems.value || !validateItem() || !selected.value || !api) return
  const item = { ...itemForm }
  const historicalNIds = new Set(
    form.items
      .filter((candidate) => historicalItemKeys.value.has(candidate.localKey))
      .map((candidate) => candidate.nId),
  )
  const items =
    itemEditorMode.value === 'edit'
      ? form.items.map((candidate) =>
          candidate.localKey === itemEditorLocalKey.value ? item : candidate,
        )
      : [...form.items, item]
  busy.value = true
  clearError()
  try {
    const dto = await api.updateDictionary(selected.value.id, {
      ...payload(items),
      ...version(selected.value),
    })
    fill(dto)
    historicalItemKeys.value = new Set(
      form.items
        .filter((candidate) => historicalNIds.has(candidate.nId))
        .map((candidate) => candidate.localKey),
    )
    itemEditorOpen.value = false
    itemEditorLocalKey.value = null
    ElMessage.success(`${copy.value.saved}: ${dto.nId}`)
    await table.value?.reload()
  } catch (caught) {
    report(caught)
  } finally {
    busy.value = false
  }
}
async function save() {
  if (busy.value || !canSubmit.value || !validate()) return
  busy.value = true
  clearError()
  try {
    if (!api) throw new Error(copy.value.unavailable)
    const dto = selected.value
      ? await api.updateDictionary(selected.value.id, { ...payload(), ...version(selected.value) })
      : await api.createDictionary(payload())
    fill(dto)
    formOpen.value = false
    ElMessage.success(`${copy.value.saved}: ${dto.nId}`)
    await table.value?.reload()
  } catch (caught) {
    report(caught)
  } finally {
    busy.value = false
  }
}
async function allowDiscard() {
  if (busy.value) return false
  if (!dirty.value) return true
  try {
    await ElMessageBox.confirm(copy.value.discard, copy.value.formTitle, {
      confirmButtonText: copy.value.close,
      cancelButtonText: copy.value.cancel,
    })
    return true
  } catch {
    return false
  }
}
async function allowItemDiscard() {
  if (busy.value) return false
  if (!itemDirty.value) return true
  try {
    await ElMessageBox.confirm(copy.value.discard, copy.value.items, {
      confirmButtonText: copy.value.close,
      cancelButtonText: copy.value.cancel,
    })
    return true
  } catch {
    return false
  }
}
async function allowDiscardAll() {
  return (await allowItemDiscard()) && (await allowDiscard())
}
async function closeForm(value = false) {
  if (!value && (await allowDiscard())) {
    cancelDetail()
    formOpen.value = false
  }
}
async function closeItemEditor(value = false) {
  if (value) return
  if (await allowItemDiscard()) {
    itemEditorOpen.value = false
    itemEditorLocalKey.value = null
    itemFields.value = {}
    if (!formOpen.value) clearError()
  }
}
async function reloadForm() {
  if (selected.value && (await allowDiscard()))
    await open(
      {
        ...selected.value,
        enabledItemCount: selected.value.items.filter((item) => item.enabled).length,
      },
      editing.value,
    )
}
async function copyUnsaved() {
  try {
    await navigator.clipboard.writeText(JSON.stringify(payload(), null, 2))
    ElMessage.success(copy.value.copied)
  } catch {
    ElMessage.error(copy.value.copyFailed)
  }
}
async function copyUnsavedItem() {
  try {
    await navigator.clipboard.writeText(JSON.stringify(itemWrite(itemForm), null, 2))
    ElMessage.success(copy.value.copied)
  } catch {
    ElMessage.error(copy.value.copyFailed)
  }
}
async function reloadItemEditor() {
  if (!selected.value || !api || busy.value) return
  const targetNId = itemForm.nId.trim().toUpperCase()
  const mode = itemEditorMode.value
  busy.value = true
  clearError()
  try {
    const dto = await api.getDictionary(selected.value.id)
    fill(dto)
    const fresh = form.items.find((item) => item.nId.trim().toUpperCase() === targetNId)
    if (mode === 'edit' && fresh !== undefined) {
      itemEditorLocalKey.value = fresh.localKey
      Object.assign(itemForm, fresh)
      itemSavedSnapshot.value = JSON.stringify(itemForm)
      itemEditorOpen.value = true
    } else {
      itemEditorOpen.value = false
      itemEditorLocalKey.value = null
    }
  } catch (caught) {
    report(caught)
  } finally {
    busy.value = false
  }
}
async function clone(row: DictionarySummary) {
  if (!api || busy.value) return
  busy.value = true
  clearError()
  try {
    const dto = await api.cloneDictionary(row.id, version(row))
    await table.value?.reload()
    await open({ ...dto, enabledItemCount: dto.items.filter((item) => item.enabled).length }, true)
    ElMessage.success(copy.value.cloned)
  } catch (caught) {
    report(caught)
  } finally {
    busy.value = false
  }
}
const publicationOpen = ref(false)
const publication = ref<DictionaryPublicationCheck | null>(null)
const publicationRow = ref<DictionarySummary | null>(null)
function publicationIssue(code: string) {
  return code === 'REF-DICT-HISTORICAL-ITEM-REMOVED'
    ? copy.value.historyProtected
    : code === 'REF-INVALID-STATE'
      ? copy.value.stateInvalid
      : copy.value.enabledRequired
}
async function preparePublication(row: DictionarySummary) {
  if (!api) return
  clearError()
  cancelDetail()
  detailRequest = new AbortController()
  const sequence = detailSequence
  try {
    const check = await api.checkDictionaryPublication(row.id, { signal: detailRequest.signal })
    if (sequence !== detailSequence) return
    publication.value = check
    publicationRow.value = row
    publicationOpen.value = true
  } catch (caught) {
    if (sequence === detailSequence) report(caught)
  }
}
function closePublication(value = false) {
  if (!value && !busy.value) {
    cancelDetail()
    publicationOpen.value = false
  }
}
async function publish() {
  if (
    !api ||
    !publicationRow.value ||
    !canPublish(publicationRow.value) ||
    busy.value ||
    publication.value?.errors.length
  )
    return
  busy.value = true
  clearError()
  try {
    const result = await api.publishDictionary(
      publicationRow.value.id,
      version(publicationRow.value),
    )
    if (result) fill(result)
    publicationOpen.value = false
    ElMessage.success(copy.value.publishedSuccess)
    await table.value?.reload()
  } catch (caught) {
    report(caught)
  } finally {
    busy.value = false
  }
}
async function disable(row: DictionarySummary) {
  if (!api || busy.value) return
  let reason: string
  try {
    const response = await ElMessageBox.prompt(copy.value.disableHint, copy.value.reason, {
      inputValidator: (value) =>
        (Boolean(value?.trim()) && value!.length <= 1000) || copy.value.required,
      confirmButtonText: copy.value.disable,
      cancelButtonText: copy.value.cancel,
    })
    reason = response.value
  } catch {
    return
  }
  busy.value = true
  clearError()
  try {
    const result = await api.disableDictionary(row.id, { ...version(row), changeReason: reason })
    if (result) fill(result)
    ElMessage.success(copy.value.disabledSuccess)
    await table.value?.reload()
  } catch (caught) {
    report(caught)
  } finally {
    busy.value = false
  }
}
function more(row: DictionarySummary, command: string) {
  if (command === 'edit') void open(row, true)
  if (command === 'clone') void clone(row)
  if (command === 'publish') void preparePublication(row)
  if (command === 'disable') void disable(row)
}
function moreSelected(command: string) {
  if (selectedSummary.value) more(selectedSummary.value, command)
}
function editSelected() {
  if (selectedSummary.value) void open(selectedSummary.value, true)
}
function beforeUnload(event: BeforeUnloadEvent) {
  if (dirty.value || itemDirty.value) event.preventDefault()
}
onBeforeRouteLeave(allowDiscardAll)
onMounted(async () => {
  window.addEventListener('beforeunload', beforeUnload)
  await nextTick()
  await table.value?.reload()
})
onBeforeUnmount(() => {
  listRequest?.abort()
  cancelDetail()
  window.removeEventListener('beforeunload', beforeUnload)
})
</script>

<template>
  <AppPage
    class="dictionary-page"
    data-testid="reference-data-dictionaries"
    :title="copy.dictionaryTitle"
    :description="copy.dictionaryDescription"
  >
    <template #actions
      ><PermissionGate :permission-n-id="PERMISSIONS.referenceDataDictionaryCreate"
        ><el-button type="primary" :icon="Plus" data-testid="dictionary-create" @click="create">{{
          copy.create
        }}</el-button></PermissionGate
      ></template
    >
    <el-alert
      v-if="(error || listError) && !formOpen && !publicationOpen"
      :title="error || listError"
      type="error"
      :closable="false"
      show-icon
      ><p v-if="traceId">{{ copy.traceId }}: {{ traceId }}</p>
      <el-button v-if="conflict" @click="table?.reload()">{{ copy.reload }}</el-button></el-alert
    >
    <el-skeleton v-if="firstLoading" :rows="3" animated :aria-label="copy.loading" />
    <div class="dictionary-master-detail">
      <section class="dictionary-directory" :aria-label="copy.dictionaryTitle">
        <AppQueryPanel v-if="mode === 'top'" show-actions grid @submit="search" @reset="reset">
          <label class="dictionary-query-field"
            ><span>{{ copy.keyword }}</span
            ><el-input
              v-model="query.keyword"
              :aria-label="copy.keyword"
              maxlength="200"
              clearable
              @keyup.enter="search"
          /></label>
          <label class="dictionary-query-field"
            ><span>{{ copy.scope }}</span
            ><el-select
              v-model="query.scopeType"
              :aria-label="copy.scope"
              :placeholder="copy.all"
              clearable
              ><el-option
                v-for="option in scopeOptions"
                :key="option.value"
                v-bind="option" /></el-select
          ></label>
          <label class="dictionary-query-field"
            ><span>{{ copy.status }}</span
            ><el-select
              v-model="query.status"
              :aria-label="copy.status"
              :placeholder="copy.all"
              clearable
              ><el-option
                v-for="option in statusOptions"
                :key="option.value"
                v-bind="option" /></el-select
          ></label>
        </AppQueryPanel>
        <AppDataTable
          ref="table"
          table-key="reference-data-dictionaries"
          :columns="directoryColumns"
          :loader="load"
          :query-mode="mode"
          toolbar-profile="compact"
          :toolbar-labels="true"
          :quick-search-enabled="false"
          selection="none"
          :active-row-key="activeId"
          @row-click="select"
          @query-mode-change="switchMode"
          @loaded="onLoaded"
          @load-error="reportList"
        >
          <template #cell-name="{ row }"
            ><div class="dictionary-directory-name">
              <strong>{{ row.name }}</strong
              ><small :title="`${row.nId} · ${statusLabel(row.status)}`"
                ><span class="directory-status">{{ statusLabel(row.status) }}</span> ·
                {{ row.nId }}</small
              >
            </div></template
          >
          <template #cell-lastUpdatedOn="{ row }">{{ date(row.lastUpdatedOn) }}</template>
        </AppDataTable>
        <p v-if="!firstLoading && total === 0 && !error">{{ copy.empty }}</p>
      </section>
      <section class="dictionary-content" :aria-label="copy.items">
        <el-empty
          v-if="!selected"
          :description="
            !firstLoading && total === 0 && !listError ? copy.empty : copy.dictionarySelect
          "
        >
          <el-button
            v-if="!firstLoading && total === 0 && !listError"
            type="primary"
            data-testid="dictionary-empty-create"
            @click="create"
            >{{ copy.create }}</el-button
          >
        </el-empty>
        <template v-else>
          <header class="dictionary-content-heading">
            <div>
              <h2>{{ selected.name }}</h2>
              <p>
                {{ selected.nId }} ·
                {{ selected.scopeType === 'Tenant' ? copy.tenant : copy.platform }} ·
                {{ copy.revision }} {{ selected.revision }} ·
                {{ statusLabel(selected.status) }}
              </p>
            </div>
            <div class="dictionary-actions">
              <el-button v-if="canEdit(selected)" @click="editSelected">{{ copy.edit }}</el-button>
              <el-dropdown
                v-if="
                  canClone(selectedSummary) ||
                  canPublish(selectedSummary) ||
                  canDisable(selectedSummary)
                "
                trigger="click"
                @command="moreSelected"
              >
                <el-button :disabled="busy" data-testid="dictionary-detail-more">
                  {{ copy.more }}<el-icon><ArrowDown /></el-icon>
                </el-button>
                <template #dropdown>
                  <el-dropdown-menu>
                    <el-dropdown-item v-if="canClone(selectedSummary)" command="clone">{{
                      copy.clone
                    }}</el-dropdown-item>
                    <el-dropdown-item v-if="canPublish(selectedSummary)" command="publish">{{
                      copy.publish
                    }}</el-dropdown-item>
                    <el-dropdown-item v-if="canDisable(selectedSummary)" command="disable">{{
                      copy.disable
                    }}</el-dropdown-item>
                  </el-dropdown-menu>
                </template>
              </el-dropdown>
              <el-button
                v-if="canEditItems"
                type="primary"
                :icon="Plus"
                data-testid="dictionary-add-item-inline"
                @click="openItemEditor()"
                >{{ copy.addItem }}</el-button
              >
            </div>
          </header>
          <el-alert v-if="!canEditItems" :title="copy.readOnly" type="info" :closable="false" />
          <AppDataTable
            table-key="reference-data-dictionary-items-inline"
            :rows="form.items"
            :total="form.items.length"
            :columns="itemPreviewColumns"
            row-key="localKey"
            toolbar-profile="compact"
            selection="none"
            :action-column-width="140"
          >
            <template #cell-enabled="{ row }">{{
              row.enabled ? copy.trueValue : copy.falseValue
            }}</template>
            <template #actions="{ row }"
              ><div v-if="canEditItems" class="dictionary-actions">
                <el-button
                  link
                  type="primary"
                  :data-testid="`dictionary-item-edit-${row.nId}`"
                  @click="openItemEditor(row)"
                  >{{ copy.edit }}</el-button
                >
                <el-dropdown trigger="click">
                  <el-button link type="primary" :disabled="busy">{{ copy.more }}</el-button>
                  <template #dropdown
                    ><el-dropdown-menu
                      ><el-dropdown-item @click="openItemEditor(row)">{{
                        copy.edit
                      }}</el-dropdown-item></el-dropdown-menu
                    ></template
                  >
                </el-dropdown>
              </div></template
            >
          </AppDataTable>
        </template>
      </section>
    </div>
    <AppFormDrawer
      :model-value="formOpen"
      :title="`${copy.formTitle}${selected ? ` · ${selected.nId} · ${copy.revision} ${selected.revision}` : ''}`"
      size="medium"
      :busy="busy"
      @update:model-value="closeForm"
    >
      <el-alert v-if="error" :title="error" type="error" :closable="false" show-icon
        ><p v-if="traceId">{{ copy.traceId }}: {{ traceId }}</p>
        <div v-if="conflict" class="dictionary-actions">
          <el-button @click="reloadForm">{{ copy.reload }}</el-button
          ><el-button @click="copyUnsaved">{{ copy.copyUnsaved }}</el-button>
        </div></el-alert
      >
      <el-alert
        v-if="!canSubmit"
        :title="selected?.status === 'Draft' ? copy.protected : copy.readOnly"
        type="info"
        :closable="false"
      />
      <el-form label-position="top" :disabled="!canSubmit || busy">
        <el-form-item :label="copy.nId" :error="fields.nId" required
          ><el-input
            v-model="form.nId"
            data-testid="dictionary-nid"
            :aria-label="copy.nId"
            :disabled="selected !== null"
            maxlength="64"
        /></el-form-item>
        <el-form-item :label="copy.name" :error="fields.name" required
          ><el-input
            v-model="form.name"
            data-testid="dictionary-name"
            :aria-label="copy.name"
            maxlength="200"
        /></el-form-item>
        <el-form-item :label="copy.scope"
          ><el-select
            v-model="form.scopeType"
            :aria-label="copy.scope"
            :disabled="selected !== null"
            ><el-option value="Tenant" :label="copy.tenant" /><el-option
              v-if="has(PERMISSIONS.referenceDataPlatformManage)"
              value="Platform"
              :label="copy.platform" /></el-select
        ></el-form-item>
        <el-form-item :label="copy.description" :error="fields.description"
          ><el-input
            v-model="form.description"
            :aria-label="copy.description"
            type="textarea"
            :rows="3"
            maxlength="2000"
        /></el-form-item>
      </el-form>
      <template #footer
        ><el-button :disabled="busy" @click="closeForm()">{{
          editing ? copy.cancel : copy.close
        }}</el-button
        ><el-button
          v-if="editing"
          type="primary"
          :disabled="!canSubmit"
          :loading="busy"
          data-testid="dictionary-save"
          @click="save"
          >{{ copy.save }}</el-button
        ></template
      >
    </AppFormDrawer>
    <AppFormDrawer
      :model-value="itemEditorOpen"
      :title="itemEditorMode === 'create' ? copy.addItem : copy.edit"
      size="medium"
      :busy="busy"
      @update:model-value="closeItemEditor"
    >
      <el-alert v-if="error" :title="error" type="error" :closable="false" show-icon
        ><p v-if="traceId">{{ copy.traceId }}: {{ traceId }}</p>
        <div v-if="conflict" class="dictionary-actions">
          <el-button @click="reloadItemEditor">{{ copy.reload }}</el-button>
          <el-button @click="copyUnsavedItem">{{ copy.copyUnsaved }}</el-button>
        </div></el-alert
      >
      <el-form label-position="top" :disabled="!canEditItems || busy">
        <el-form-item :label="copy.nId" :error="itemFields.nId" required>
          <el-input
            v-model="itemForm.nId"
            data-testid="dictionary-item-nid"
            :aria-label="copy.nId"
            :disabled="
              busy ||
              (itemEditorMode === 'edit' &&
                itemEditorLocalKey !== null &&
                historicalItemKeys.has(itemEditorLocalKey))
            "
            maxlength="64"
          />
        </el-form-item>
        <el-form-item :label="copy.name" :error="itemFields.name" required>
          <el-input
            v-model="itemForm.name"
            data-testid="dictionary-item-name"
            :aria-label="copy.name"
            maxlength="200"
          />
        </el-form-item>
        <el-form-item :label="copy.description">
          <el-input
            v-model="itemForm.description"
            data-testid="dictionary-item-description"
            :aria-label="copy.description"
            type="textarea"
            :rows="3"
            maxlength="2000"
          />
        </el-form-item>
        <el-form-item :label="copy.sort" :error="itemFields.sort">
          <el-input-number
            v-model="itemForm.sort"
            data-testid="dictionary-item-sort"
            :aria-label="copy.sort"
            :min="0"
            :step="1"
            :precision="0"
            :controls="false"
            style="width: 100%"
          />
        </el-form-item>
        <el-form-item :label="copy.enabled">
          <el-switch
            v-model="itemForm.enabled"
            data-testid="dictionary-item-enabled"
            :aria-label="copy.enabled"
          />
        </el-form-item>
      </el-form>
      <template #footer
        ><el-button
          data-testid="dictionary-item-cancel"
          :disabled="busy"
          @click="closeItemEditor()"
          >{{ copy.cancel }}</el-button
        ><el-button
          type="primary"
          data-testid="dictionary-item-save"
          :disabled="!canEditItems"
          :loading="busy"
          @click="saveItem"
          >{{ copy.save }}</el-button
        ></template
      >
    </AppFormDrawer>
    <AppFormDrawer
      :model-value="publicationOpen"
      @update:model-value="closePublication"
      :title="copy.publicationTitle"
      size="medium"
      :busy="busy"
    >
      <p>{{ copy.publicationHint }}</p>
      <el-alert v-if="error" :title="error" type="error" :closable="false" /><template
        v-if="publication"
      >
        <p>
          {{ copy.previousRevision }}: {{ publication.previousRevision ?? copy.firstPublication }}
        </p>
        <dl class="dictionary-differences">
          <dt>{{ copy.added }}</dt>
          <dd>{{ publication.addedItems.join(', ') || copy.noChanges }}</dd>
          <dt>{{ copy.changed }}</dt>
          <dd>{{ publication.changedItems.join(', ') || copy.noChanges }}</dd>
          <dt>{{ copy.disabledItems }}</dt>
          <dd>{{ publication.disabledItems.join(', ') || copy.noChanges }}</dd>
        </dl>
        <el-alert
          v-for="issue in publication.errors"
          :key="issue.code + issue.field"
          :title="publicationIssue(issue.code)"
          type="error"
          :closable="false"
          show-icon
        />
        <el-alert
          v-if="publication.errors.length === 0"
          :title="copy.validationPassed"
          type="success"
          :closable="false"
          show-icon
        />
      </template>
      <template #footer
        ><el-button :disabled="busy" @click="closePublication()">{{ copy.cancel }}</el-button
        ><el-button
          type="primary"
          :disabled="!publication || publication.errors.length > 0 || conflict"
          :loading="busy"
          data-testid="dictionary-publish-confirm"
          @click="publish"
          >{{ copy.publish }}</el-button
        ></template
      >
    </AppFormDrawer>
  </AppPage>
</template>

<style scoped>
.dictionary-page {
  display: flex;
  flex: 1 1 auto;
  flex-direction: column;
  min-height: 0;
  overflow: hidden;
}
.dictionary-page :deep(.app-page__body) {
  display: flex;
  flex: 1 1 auto;
  flex-direction: column;
  min-height: 0;
  overflow: hidden;
}
.dictionary-page :deep(.app-query-panel) {
  flex: 0 0 auto;
}
.dictionary-query-field {
  display: grid;
  gap: var(--ip-space-2);
  min-width: 0;
  flex: 0 0 180px;
  max-width: 100%;
}
.dictionary-master-detail {
  display: grid;
  flex: 1 1 0;
  grid-template-columns: minmax(250px, 280px) minmax(0, 1fr);
  gap: var(--ip-space-4);
  min-height: 0;
  align-items: stretch;
}
.dictionary-directory,
.dictionary-content {
  display: flex;
  flex-direction: column;
  min-width: 0;
  min-height: 0;
}
.dictionary-directory {
  gap: var(--ip-space-3);
  overflow: hidden;
}
.dictionary-directory :deep(.app-data-table),
.dictionary-content :deep(.app-data-table) {
  flex: 1 1 0;
  min-height: 0;
}
.dictionary-directory :deep(.app-data-table__card),
.dictionary-content :deep(.app-data-table__card) {
  display: flex;
  flex: 1 1 0;
  flex-direction: column;
  min-height: 0;
}
.dictionary-directory-name {
  display: grid;
  min-width: 0;
  gap: 2px;
}
.dictionary-directory-name strong,
.dictionary-directory-name small {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.dictionary-directory-name small {
  color: var(--el-text-color-secondary);
  font-size: 12px;
}
.dictionary-content {
  min-height: 0;
  padding: var(--ip-space-4);
  border: 1px solid var(--el-border-color-light);
  border-radius: var(--ip-radius-md);
  background: var(--el-bg-color);
}
.dictionary-content > :deep(.el-empty) {
  flex: 1 1 auto;
  min-height: 0;
}
.dictionary-content-heading {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: var(--ip-space-3);
  margin-bottom: var(--ip-space-3);
}
.dictionary-content-heading h2 {
  margin: 0;
}
.dictionary-content-heading p {
  margin: var(--ip-space-1) 0 0;
  color: var(--el-text-color-secondary);
}
.dictionary-actions {
  display: inline-flex;
  align-items: center;
  gap: var(--ip-space-1);
  white-space: nowrap;
}
.dictionary-actions > .el-button {
  margin-left: 0;
}
.dictionary-section-heading {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--ip-space-3);
}
.dictionary-section-heading h3 {
  margin: 0;
}
.dictionary-field-error {
  display: block;
  color: var(--el-color-danger);
  font-size: 12px;
}
.dictionary-differences {
  display: grid;
  grid-template-columns: auto 1fr;
  gap: var(--ip-space-3);
}
.dictionary-differences dd {
  margin: 0;
  overflow-wrap: anywhere;
}
@media (max-width: 768px) {
  .dictionary-master-detail {
    grid-template-columns: 1fr;
  }
}
</style>
