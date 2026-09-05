<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, reactive, ref } from 'vue'
import { onBeforeRouteLeave } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { ArrowDown, ArrowUp, Delete, Plus } from '@element-plus/icons-vue'
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
const columns = computed<AppDataTableColumn[]>(() => [
  { field: 'nId', title: copy.value.nId, minWidth: 150, sortable: true, filter: false },
  {
    field: 'name',
    title: copy.value.name,
    minWidth: 170,
    sortable: true,
    filter: { kind: 'text' },
  },
  {
    field: 'scopeType',
    title: copy.value.scope,
    width: 100,
    sortable: false,
    filter: { kind: 'select', options: scopeOptions.value },
  },
  { field: 'revision', title: copy.value.revision, width: 90, sortable: true, filter: false },
  {
    field: 'status',
    title: copy.value.status,
    width: 120,
    sortable: true,
    filter: { kind: 'select', options: statusOptions.value },
  },
  {
    field: 'enabledItemCount',
    title: copy.value.enabledCount,
    width: 110,
    sortable: false,
    filter: false,
  },
  {
    field: 'lastUpdatedOn',
    title: copy.value.updatedOn,
    minWidth: 180,
    sortable: true,
    filter: false,
  },
  {
    field: 'publishedBy',
    title: copy.value.publishedBy,
    minWidth: 130,
    sortable: false,
    filter: false,
  },
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
    return await api.listDictionaries(params, { signal: listRequest.signal })
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
function canEdit(row: DictionarySummary) {
  return row.status === 'Draft' && canWrite(row) && has(PERMISSIONS.referenceDataDictionaryUpdate)
}
function canClone(row: DictionarySummary) {
  return row.publishedBy !== null && canWrite(row) && has(PERMISSIONS.referenceDataDictionaryCreate)
}
function canPublish(row: DictionarySummary) {
  return row.status === 'Draft' && canWrite(row) && has(PERMISSIONS.referenceDataDictionaryPublish)
}
function canDisable(row: DictionarySummary) {
  return (
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
const formOpen = ref(false)
const editing = ref(false)
const busy = ref(false)
const tab = ref('basics')
const savedSnapshot = ref('')
const historicalItemKeys = ref(new Set<string>())
const dirty = computed(
  () => formOpen.value && editing.value && JSON.stringify(form) !== savedSnapshot.value,
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
const itemColumns = computed<AppDataTableColumn[]>(() => [
  { field: 'nId', title: copy.value.nId, width: 110, sortable: false, filter: false },
  { field: 'name', title: copy.value.name, width: 135, sortable: false, filter: false },
  {
    field: 'description',
    title: copy.value.description,
    width: 140,
    sortable: false,
    filter: false,
  },
  { field: 'sort', title: copy.value.sort, width: 70, sortable: false, filter: false },
  { field: 'enabled', title: copy.value.enabled, width: 65, sortable: false, filter: false },
  {
    field: 'itemActions',
    title: localeMessages[localization.locale].common.table.actions,
    width: 100,
    sortable: false,
    filter: false,
  },
])
function payload(): CreateDictionaryRequest {
  return {
    nId: form.nId.trim(),
    name: form.name.trim(),
    description: form.description.trim() || null,
    scopeType: form.scopeType,
    scopeId: null,
    items: form.items.map((item) => ({
      nId: item.nId.trim(),
      name: item.name.trim(),
      description: item.description?.trim() || null,
      sort: item.sort,
      enabled: item.enabled,
    })),
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
function create() {
  cancelDetail()
  clearError()
  selected.value = null
  editing.value = true
  historicalItemKeys.value = new Set()
  Object.assign(form, { nId: '', name: '', description: '', scopeType: 'Tenant', items: [] })
  savedSnapshot.value = JSON.stringify(form)
  tab.value = 'basics'
  formOpen.value = true
}
async function open(row: DictionarySummary, edit = false) {
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
    tab.value = 'basics'
    formOpen.value = true
  } catch (caught) {
    if (sequence === detailSequence) report(caught)
  }
}
function addItem() {
  form.items.push({
    localKey: crypto.randomUUID(),
    nId: '',
    name: '',
    description: null,
    sort: form.items.length,
    enabled: true,
  })
}
function itemField(row: FormItem, field: string) {
  return `items[${form.items.findIndex((item) => item.localKey === row.localKey)}].${field}`
}
function move(row: FormItem, direction: number) {
  const index = form.items.findIndex((item) => item.localKey === row.localKey)
  const target = index + direction
  if (target < 0 || target >= form.items.length) return
  const [item] = form.items.splice(index, 1)
  if (item) form.items.splice(target, 0, item)
  form.items.forEach((item, order) => {
    item.sort = order
  })
}
function remove(row: FormItem) {
  form.items = form.items.filter((item) => item.localKey !== row.localKey)
}
function validate() {
  fields.value = {}
  if (!/^[A-Za-z][A-Za-z0-9_.-]{1,63}$/.test(form.nId)) fields.value.nId = copy.value.invalidNId
  if (!form.name.trim() || form.name.length > 200) fields.value.name = copy.value.required
  const ids = new Set<string>()
  form.items.forEach((item, index) => {
    const id = item.nId.trim().toUpperCase()
    if (!/^[A-Z0-9][A-Z0-9_.-]{0,63}$/.test(id))
      fields.value[`items[${index}].nId`] = copy.value.invalidNId
    else if (ids.has(id)) fields.value[`items[${index}].nId`] = copy.value.duplicateItem
    ids.add(id)
    if (!item.name.trim() || item.name.length > 200)
      fields.value[`items[${index}].name`] = copy.value.required
    if (!Number.isInteger(item.sort) || item.sort < 0)
      fields.value[`items[${index}].sort`] = copy.value.invalidSort
  })
  if (form.items.length > 1000) fields.value.items = copy.value.itemLimit
  if (Object.keys(fields.value).length) {
    tab.value = Object.keys(fields.value).some((key) => key.startsWith('items'))
      ? 'items'
      : 'basics'
    return false
  }
  return true
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
async function closeForm(value = false) {
  if (!value && (await allowDiscard())) {
    cancelDetail()
    formOpen.value = false
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
    await api.publishDictionary(publicationRow.value.id, version(publicationRow.value))
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
    await api.disableDictionary(row.id, { ...version(row), changeReason: reason })
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
  cancelDetail()
  window.removeEventListener('beforeunload', beforeUnload)
})
</script>

<template>
  <AppPage
    data-testid="reference-data-dictionaries"
    :title="copy.dictionaryTitle"
    :description="copy.dictionaryDescription"
  >
    <template #heading-meta
      ><span>{{ total }}</span></template
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
    <el-skeleton v-if="firstLoading" :rows="3" animated :aria-label="copy.loading" />
    <AppDataTable
      ref="table"
      table-key="reference-data-dictionaries"
      :columns="columns"
      :loader="load"
      :query-mode="mode"
      :toolbar-labels="true"
      selection="none"
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
      <template #cell-lastUpdatedOn="{ row }">{{ date(row.lastUpdatedOn) }}</template>
      <template #actions="{ row, availableWidth }"
        ><div class="dictionary-actions">
          <el-button link type="primary" @click="open(row)">{{ copy.detail }}</el-button>
          <el-button
            v-if="canEdit(row) && availableWidth >= 190"
            link
            type="primary"
            @click="open(row, true)"
            >{{ copy.edit }}</el-button
          >
          <el-dropdown
            v-if="canEdit(row) || canClone(row) || canPublish(row) || canDisable(row)"
            trigger="click"
            @command="(command: string) => more(row, command)"
            ><el-button link type="primary" :disabled="busy"
              >{{ copy.more }}<el-icon><ArrowDown /></el-icon></el-button
            ><template #dropdown
              ><el-dropdown-menu>
                <el-dropdown-item v-if="canEdit(row) && availableWidth < 190" command="edit">{{
                  copy.edit
                }}</el-dropdown-item>
                <el-dropdown-item v-if="canClone(row)" command="clone">{{
                  copy.clone
                }}</el-dropdown-item
                ><el-dropdown-item v-if="canPublish(row)" command="publish">{{
                  copy.publish
                }}</el-dropdown-item
                ><el-dropdown-item v-if="canDisable(row)" command="disable">{{
                  copy.disable
                }}</el-dropdown-item>
              </el-dropdown-menu></template
            ></el-dropdown
          >
        </div></template
      >
    </AppDataTable>
    <p v-if="!firstLoading && total === 0 && !error">{{ copy.empty }}</p>
    <AppFormDrawer
      :model-value="formOpen"
      :title="`${copy.formTitle}${selected ? ` · ${selected.nId} · ${copy.revision} ${selected.revision}` : ''}`"
      size="wide"
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
      <el-tabs v-model="tab">
        <el-tab-pane :label="copy.basics" name="basics"
          ><el-form label-position="top" :disabled="!canSubmit || busy">
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
            /></el-form-item> </el-form
        ></el-tab-pane>
        <el-tab-pane :label="copy.items" name="items">
          <div class="dictionary-section-heading">
            <h3>{{ copy.items }}</h3>
            <el-button
              v-if="editing"
              :disabled="busy || form.items.length >= 1000"
              :icon="Plus"
              data-testid="dictionary-add-item"
              @click="addItem"
              >{{ copy.addItem }}</el-button
            >
          </div>
          <p>{{ copy.itemIdentityHint }}</p>
          <p v-if="fields.items" role="alert">{{ fields.items }}</p>
          <AppDataTable
            table-key="reference-data-dictionary-items"
            :rows="form.items"
            :total="form.items.length"
            :columns="itemColumns"
            row-key="localKey"
            toolbar-profile="compact"
            selection="none"
          >
            <template #cell-nId="{ row }"
              ><el-input
                v-model="row.nId"
                :aria-label="`${copy.nId} ${row.nId}`"
                :disabled="!canSubmit || busy || historicalItemKeys.has(row.localKey)"
                maxlength="64"
              /><span
                v-if="fields[itemField(row, 'nId')]"
                class="dictionary-field-error"
                role="alert"
                >{{ fields[itemField(row, 'nId')] }}</span
              ></template
            >
            <template #cell-name="{ row }"
              ><el-input
                v-model="row.name"
                :aria-label="`${copy.name} ${row.nId}`"
                :disabled="!canSubmit || busy"
                maxlength="200"
              /><span
                v-if="fields[itemField(row, 'name')]"
                class="dictionary-field-error"
                role="alert"
                >{{ fields[itemField(row, 'name')] }}</span
              ></template
            >
            <template #cell-description="{ row }"
              ><el-input
                v-model="row.description"
                :aria-label="`${copy.description} ${row.nId}`"
                :disabled="!canSubmit || busy"
                maxlength="2000"
            /></template>
            <template #cell-sort="{ row }"
              ><el-input-number
                v-model="row.sort"
                :aria-label="`${copy.sort} ${row.nId}`"
                :min="0"
                :step="1"
                :precision="0"
                :controls="false"
                :disabled="!canSubmit || busy"
                style="width: 100%"
              /><span v-if="fields[itemField(row, 'sort')]" class="dictionary-field-error">{{
                fields[itemField(row, 'sort')]
              }}</span></template
            >
            <template #cell-enabled="{ row }"
              ><el-switch
                v-model="row.enabled"
                :aria-label="`${copy.enabled} ${row.nId}`"
                :disabled="!canSubmit || busy"
            /></template>
            <template #cell-itemActions="{ row }"
              ><div v-if="canSubmit" class="dictionary-actions">
                <el-button
                  link
                  :disabled="busy"
                  :icon="ArrowUp"
                  :title="copy.moveUp"
                  :aria-label="`${copy.moveUp} ${row.nId}`"
                  @click="move(row, -1)"
                />
                <el-button
                  link
                  :disabled="busy"
                  :icon="ArrowDown"
                  :title="copy.moveDown"
                  :aria-label="`${copy.moveDown} ${row.nId}`"
                  @click="move(row, 1)"
                />
                <el-button
                  v-if="!historicalItemKeys.has(row.localKey)"
                  link
                  type="danger"
                  :disabled="busy"
                  :icon="Delete"
                  :title="copy.remove"
                  :aria-label="`${copy.remove} ${row.nId}`"
                  @click="remove(row)"
                /></div
            ></template>
          </AppDataTable>
        </el-tab-pane>
      </el-tabs>
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
.dictionary-query-field {
  display: grid;
  gap: var(--ip-space-2);
  min-width: 0;
  flex: 0 0 180px;
  max-width: 100%;
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
</style>
