<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessageBox } from 'element-plus'
import { ApiError } from '@/api/errors'
import { getStudioApi, type Json, type LedgerRecord, type StudioRuntime } from '@/api/studio'
import AppQueryPanel from '@/components/management/AppQueryPanel.vue'
import AppDataTable from '@/components/management/AppDataTable.vue'
import type { AppDataTableRequest } from '@/components/management/AppDataTable'
import { PermissionGate } from '@/permissions'
import { useAuthStore } from '@/stores/authStore'
import StudioLayoutRenderer from './StudioLayoutRenderer.vue'
import StudioFileField from './StudioFileField.vue'
import { useStudioCopy } from './copy'
import {
  type LayoutNode,
  nodes,
  safeTotal,
  clone,
  evaluateCondition,
  validateFieldBindings,
  mappedFormValues,
} from './model'
const props = withDefaults(
  defineProps<{
    runtime: StudioRuntime
    preview?: boolean
    parameters?: Json
    recordMode?: 'new' | 'edit' | 'detail' | undefined
    recordNId?: string | undefined
  }>(),
  { parameters: () => ({}) },
)
const emit = defineEmits<{ refresh: [] }>()
const route = useRoute(),
  router = useRouter(),
  t = useStudioCopy(),
  error = ref(''),
  busy = ref(false),
  form = ref<Json>({}),
  current = ref<LedgerRecord | undefined>(),
  table = ref<{
    reload: () => Promise<void>
    setHeaderFilter: (field: string, value: unknown) => void
  } | null>(null),
  filters = ref<Json>({}),
  queryMode = ref<'top' | 'header'>('top'),
  deleted = ref(false),
  result = ref<Json | null>(null),
  unknown = ref(false),
  operationKey = ref(''),
  selectedRow = ref<{ instanceId: string; key: unknown } | undefined>(),
  selectedRowData = ref<Json | undefined>(),
  parameters = ref<Json>({ ...props.parameters })
const recordKey = ref(crypto.randomUUID())
watch(table, (value) => {
  if (value) void value.reload()
})
const recordNumber = ref('')
const auth = useAuthStore(),
  listPage = ref(1),
  listPageSize = ref(20)
const stateKey = `studio.query.${auth.user?.tenantId ?? ''}.${auth.user?.userId ?? ''}.${props.runtime.resource.nId}.${props.runtime.revisionNId}`
const pendingOperationKey = stateKey + '.pending-operation'
const recentActions = new Map<string, { key: string; started: number }>()
try {
  operationKey.value = sessionStorage.getItem(pendingOperationKey) ?? ''
  unknown.value = !!operationKey.value
} catch {
  /* querying remains available without browser storage */
}
function rememberOperation(key: string) {
  try {
    if (key) sessionStorage.setItem(pendingOperationKey, key)
    else sessionStorage.removeItem(pendingOperationKey)
  } catch {
    /* in-memory Unknown guard remains active */
  }
}
try {
  const state = JSON.parse(sessionStorage.getItem(stateKey) ?? '{}') as Json
  filters.value = state.filters ?? {}
  queryMode.value = state.queryMode === 'header' ? 'header' : 'top'
  deleted.value = state.deleted === true
  listPage.value = Number(state.page ?? 1)
  listPageSize.value = Number(state.pageSize ?? 20)
} catch {
  /* unavailable browser storage does not block queries */
}
function rememberQuery() {
  try {
    sessionStorage.setItem(
      stateKey,
      JSON.stringify({
        filters: filters.value,
        queryMode: queryMode.value,
        deleted: deleted.value,
        page: listPage.value,
        pageSize: listPageSize.value,
      }),
    )
  } catch {
    /* optional UI preference */
  }
}
watch([filters, queryMode, deleted], rememberQuery, { deep: true })
function changeQueryMode(mode: 'top' | 'header') {
  queryMode.value = mode
  if (mode === 'header')
    for (const field of queryFields.value)
      table.value?.setHeaderFilter(String(field.id), filters.value[String(field.id)] ?? null)
}
function tableQuery(request: AppDataTableRequest) {
  if (request.queryMode === 'header') filters.value = { ...request.filters }
  listPage.value = request.pageIndex
  listPageSize.value = request.pageSize
  rememberQuery()
}
const model = computed(() => props.runtime.ledgerModel),
  fields = computed(() =>
    ((model.value?.document.fields as Json[]) ?? []).filter((f) => !f.retired),
  ),
  queryFields = computed(() => fields.value.filter((f) => f.queryable)),
  hasLayoutFields = computed(
    () =>
      props.runtime.document.layout &&
      nodes(props.runtime.document.layout as unknown as LayoutNode).some(
        (n) => n.type === 'FormField',
      ),
  )
const columns = computed(() =>
  fields.value
    .filter((f) => f.type !== 'FileReference')
    .map((f) => ({
      field: String(f.id),
      title: String(f.label || f.name),
      minWidth: 140,
      sortable: !!f.sortable,
      filter: f.queryable
        ? f.type === 'Date' || f.type === 'DateTime'
          ? { kind: 'date-range' as const }
          : f.type === 'Boolean'
            ? {
                kind: 'select' as const,
                options: [
                  { label: t.value('yes'), value: true },
                  { label: t.value('no'), value: false },
                ],
              }
            : f.type === 'Enum'
              ? {
                  kind: 'select' as const,
                  options: ((f.enumValues as string[]) ?? []).map((v) => ({ label: v, value: v })),
                }
              : { kind: 'text' as const }
        : (false as const),
    })),
)
const actions = computed(() => (props.runtime.document.actions as Json[]) ?? [])
const bindingContext = computed(() => ({
  Form: form.value,
  Row: selectedRowData.value,
  PageParam: parameters.value,
  Instance: Object.fromEntries(
    Object.entries(props.runtime.instances).map(([key, instance]) => [key, instance.data]),
  ),
}))
const displayedForm = computed(() =>
  mappedFormValues(props.runtime.document.fields ?? [], form.value, bindingContext.value, false),
)
const isCrud = computed(() => props.runtime.document.mode === 'Crud')
function resetFilters() {
  filters.value = {}
  void table.value?.reload()
}
async function guarded(fn: () => Promise<void>, mutating = false) {
  if (busy.value) return
  busy.value = true
  error.value = ''
  try {
    await fn()
  } catch (e) {
    error.value = e instanceof Error ? e.message : t.value('error')
    if (
      mutating &&
      e instanceof ApiError &&
      (e.kind === 'timeout' ||
        e.kind === 'network' ||
        e.details.code === 'STUDIO_OPERATION_UNKNOWN')
    )
      unknown.value = true
  } finally {
    busy.value = false
  }
}
function recordRoute(mode: 'new' | 'edit' | 'detail', record?: Json) {
  const base = `/pc/studio/low-code/${encodeURIComponent(props.runtime.resource.nId)}/records`
  const path =
    mode === 'new'
      ? base + '/new'
      : base + '/' + encodeURIComponent(String(record?.nId)) + (mode === 'edit' ? '/edit' : '')
  void router.push({
    path,
    query: { ...route.query, revisionNId: props.runtime.revisionNId, returnTo: route.fullPath },
  })
}
function filterValue(field: Json, value: unknown, end = false): unknown {
  if (field.type === 'DateTime' && typeof value === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(value))
    return new Date(value + (end ? 'T23:59:59.999' : 'T00:00:00.000')).toISOString()
  if (field.type === 'Int32') return Number(value)
  if (field.type === 'Boolean') {
    if (value === true || value === 'true') return true
    if (value === false || value === 'false') return false
    throw new Error('TYPE_MISMATCH: ' + field.id)
  }
  return value
}
async function loadRecords(request: AppDataTableRequest) {
  if (!model.value) throw new Error(t.value('noPublishedModel'))
  if (props.preview)
    return { items: [], total: 0, pageIndex: request.pageIndex, pageSize: request.pageSize }
  const conditions: Json[] = []
  const source = request.queryMode === 'header' ? request.filters : filters.value
  for (const f of queryFields.value) {
    const value = source[String(f.id)]
    if (value == null || value === '') continue
    if (Array.isArray(value)) {
      if (value[0])
        conditions.push({ fieldId: f.id, operator: 'ge', value: filterValue(f, value[0]) })
      if (value[1])
        conditions.push({ fieldId: f.id, operator: 'le', value: filterValue(f, value[1], true) })
    } else
      conditions.push({
        fieldId: f.id,
        operator: 'eq',
        value: filterValue(f, value),
      })
  }
  try {
    const page = await getStudioApi().ledgerQuery(model.value.nId, model.value.revisionNId, {
      page: request.pageIndex,
      pageSize: request.pageSize,
      sortFieldId: request.sort?.field ?? null,
      sortOrder: request.sort?.order ?? 'desc',
      deleted: deleted.value,
      filters: conditions,
    })
    return {
      items: page.items.map((r) => ({ ...r.values, nId: r.nId, _record: r })),
      total: safeTotal(page.totalCount),
      pageIndex: page.page,
      pageSize: page.pageSize,
    }
  } catch (e) {
    error.value = e instanceof Error ? e.message : t.value('error')
    throw e
  }
}
function back() {
  const target =
    typeof route.query.returnTo === 'string' &&
    route.query.returnTo.startsWith('/pc/studio/low-code/')
      ? route.query.returnTo
      : `/pc/studio/low-code/${encodeURIComponent(props.runtime.resource.nId)}/run`
  void router.push(target)
}
function writableValues(): Json {
  const effective = mappedFormValues(
    props.runtime.document.fields ?? [],
    form.value,
    bindingContext.value,
  )
  validateFieldBindings(props.runtime.document.fields ?? [], effective, {
    ...bindingContext.value,
    Form: effective,
  })
  const values: Json = {}
  for (const field of fields.value) {
    if (field.retired) continue
    let value = effective[String(field.id)]
    if (field.required && (value == null || (typeof value === 'string' && !value.trim())))
      throw new Error(String(field.label || field.name) + ': ' + t.value('required'))
    if (value === '' && !field.required) value = null
    if (field.type === 'Int32' && value != null) value = Number(value)
    if (value !== undefined) values[String(field.id)] = value
  }
  return values
}
function save() {
  void guarded(async () => {
    if (!model.value || props.preview || unknown.value) return
    if (!current.value && !recordNumber.value.trim())
      throw new Error(t.value('resourceId') + ': ' + t.value('required'))
    operationKey.value = recordKey.value
    await getStudioApi().ledgerWrite(
      model.value.nId,
      model.value.revisionNId,
      writableValues(),
      current.value,
      recordKey.value,
      recordNumber.value.trim(),
    )
    back()
  }, true)
}
function lifecycle(row: Json, action: string) {
  void guarded(async () => {
    if (!model.value) return
    await ElMessageBox.confirm(t.value('confirm') + ' ' + t.value(action), t.value(action))
    await getStudioApi().ledgerAction(model.value.nId, row._record as LedgerRecord, action)
    void table.value?.reload()
  }, true)
}
async function selectRow(row: Json, instanceId: string) {
  const layoutNodes = nodes(props.runtime.document.layout as unknown as LayoutNode)
  const target = layoutNodes.find((n) => n.props.instanceId === instanceId)
  const keyPath = Array.isArray(target?.props.rowKeyPath)
    ? target.props.rowKeyPath
    : String(target?.props.rowKeyPath ?? 'nId').split('.')
  let key: unknown = row
  for (const part of keyPath) key = key && typeof key === 'object' ? (key as Json)[part] : undefined
  selectedRow.value = { instanceId, key }
  selectedRowData.value = row
  for (const action of actions.value.filter((a) => a.event === 'RowClick')) await execute(action)
}
async function execute(action: Json) {
  await guarded(async () => {
    if (unknown.value) return
    const effective = mappedFormValues(
      props.runtime.document.fields ?? [],
      form.value,
      bindingContext.value,
    )
    const contextValues = { ...bindingContext.value, Form: effective }
    if (!evaluateCondition(action.enabledWhen, contextValues))
      throw new Error(t.value('notAllowed'))
    validateFieldBindings(props.runtime.document.fields ?? [], effective, contextValues)
    if (action.confirmText && !props.preview)
      await ElMessageBox.confirm(String(action.confirmText), String(action.label))
    const context = {
      pageParameters: parameters.value,
      form: effective,
      ...(selectedRow.value ? { rowKey: selectedRow.value } : {}),
    }
    const fingerprint = JSON.stringify({ actionId: action.id, context })
    const recent = recentActions.get(fingerprint),
      started = Date.now()
    operationKey.value =
      recent && started - recent.started < 1000 ? recent.key : crypto.randomUUID()
    recentActions.set(fingerprint, { key: operationKey.value, started })
    if (!props.preview) rememberOperation(operationKey.value)
    try {
      result.value = await getStudioApi().action(
        props.runtime.resource.nId,
        String(action.id),
        props.preview
          ? { pageDocument: props.runtime.document, context }
          : { revisionNId: props.runtime.revisionNId, context },
        props.preview,
        operationKey.value,
      )
    } catch (error) {
      if (!(error instanceof ApiError) || !['timeout', 'network'].includes(error.kind)) {
        if (!(error instanceof ApiError) || error.details.code !== 'STUDIO_OPERATION_UNKNOWN')
          rememberOperation('')
      }
      throw error
    }
    if (['Unknown', 'Pending'].includes(String(result.value.status))) {
      unknown.value = true
      return
    }
    if (!props.preview) rememberOperation('')
    if (result.value.status === 'Failed')
      throw new Error(String(result.value.errorCode ?? t.value('error')))
    if (!props.preview) {
      if ((action.success as Json | undefined)?.closeForm && props.recordMode) back()
      else {
        emit('refresh')
        void table.value?.reload()
      }
    }
  }, !props.preview)
}
async function submit() {
  if (props.recordMode === 'detail') return
  const configured = actions.value.filter((action) => action.event === 'Submit')
  if (configured.length) {
    for (const action of configured) await execute(action)
  } else save()
}
function checkOperation() {
  void guarded(async () => {
    if (!operationKey.value) return
    result.value = await getStudioApi().operation(operationKey.value)
    if (['Succeeded', 'Failed', 'Cancelled'].includes(String(result.value.status))) {
      unknown.value = false
      rememberOperation('')
      if (result.value.status === 'Succeeded') emit('refresh')
    }
  })
}
function reload() {
  void guarded(async () => {
    emit('refresh')
    await table.value?.reload()
  })
}
watch(
  () => [props.recordNId, props.runtime.revisionNId],
  async () => {
    error.value = ''
    form.value = Object.fromEntries(
      fields.value
        .filter(
          (f) =>
            f.defaultValue !== undefined &&
            !((props.runtime.document.fields as Json[]) ?? []).some(
              (binding) => binding.writeTarget === f.id && binding.source?.from !== 'Form',
            ),
        )
        .map((f) => [String(f.id), clone(f.defaultValue)]),
    )
    if (props.recordNId && model.value) {
      try {
        current.value = await getStudioApi().ledgerGet(
          model.value.nId,
          props.recordNId,
          model.value.revisionNId,
        )
        form.value = clone(current.value.values)
      } catch (e) {
        error.value = e instanceof Error ? e.message : t.value('error')
      }
    }
  },
  { immediate: true },
)
watch(
  () => props.parameters,
  (value) => (parameters.value = { ...value }),
  { deep: true },
)
defineExpose({ save, back })
</script>
<template>
  <div class="studio-runtime">
    <el-alert v-if="error" :title="error" type="error" :closable="false" />
    <el-alert v-if="unknown" :title="t('unknown')" type="warning" :closable="false" /><el-button
      v-if="unknown"
      :disabled="busy"
      @click="checkOperation"
      >{{ t('check') }} · {{ operationKey }}</el-button
    >
    <template v-if="runtime.document.mode === 'Code'"
      ><div v-if="runtime.document.codeComponentKey === 'studio.summary'">
        <h2>{{ runtime.resource.name }}</h2>
        <p>{{ t('codeSummary') }}</p>
        <pre class="studio-json">{{
          JSON.stringify(
            { revisionNId: runtime.revisionNId, instances: runtime.instances },
            null,
            2,
          )
        }}</pre>
      </div>
      <el-alert v-else :title="t('codeMissing')" type="error" :closable="false"
    /></template>
    <template v-else>
      <el-form
        v-if="recordMode || !isCrud || preview"
        label-position="top"
        @submit.prevent="submit"
      >
        <el-form-item v-if="recordMode === 'new'" :label="t('resourceId')" required
          ><el-input v-model="recordNumber" maxlength="128" :aria-label="t('resourceId')"
        /></el-form-item>
        <StudioLayoutRenderer
          v-if="runtime.document.layout"
          :node="runtime.document.layout as unknown as LayoutNode"
          :runtime="runtime"
          :form="displayedForm"
          @update:form="(value) => (form = value)"
          :parameters="parameters"
          :row="selectedRowData"
          :preview="preview"
          :read-only="recordMode === 'detail'"
          @row="selectRow"
        />
        <div
          v-if="!hasLayoutFields && model && (recordMode || preview)"
          class="studio-runtime-form"
        >
          <el-form-item
            v-for="field in fields"
            :key="String(field.id)"
            :label="String(field.label || field.name)"
            :required="field.required"
            ><StudioFileField
              v-if="field.type === 'FileReference'"
              v-model="form[String(field.id)]"
              :model-n-id="model.nId"
              :disabled="recordMode === 'detail' || preview" /><el-switch
              v-else-if="field.type === 'Boolean'"
              v-model="form[String(field.id)]"
              :disabled="recordMode === 'detail'" /><el-select
              v-else-if="field.type === 'Enum'"
              v-model="form[String(field.id)]"
              :disabled="recordMode === 'detail'"
              ><el-option
                v-for="value in field.enumValues as string[]"
                :key="value"
                :label="value"
                :value="value" /></el-select
            ><el-date-picker
              v-else-if="field.type === 'Date'"
              v-model="form[String(field.id)]"
              type="date"
              value-format="YYYY-MM-DD"
              :disabled="recordMode === 'detail'" /><el-date-picker
              v-else-if="field.type === 'DateTime'"
              v-model="form[String(field.id)]"
              type="datetime"
              value-format="YYYY-MM-DDTHH:mm:ssZ"
              :disabled="recordMode === 'detail'" /><el-input-number
              v-else-if="field.type === 'Int32'"
              v-model="form[String(field.id)]"
              :disabled="recordMode === 'detail'" /><el-input
              v-else
              v-model="form[String(field.id)]"
              :type="field.type === 'Text' ? 'textarea' : 'text'"
              :disabled="recordMode === 'detail'"
              :aria-label="String(field.label || field.name)"
          /></el-form-item>
        </div>
        <div v-if="recordMode" class="studio-tools">
          <el-button @click="back">{{ t('back') }}</el-button
          ><PermissionGate permission-n-id="studio.ledger.write"
            ><el-button
              v-if="recordMode !== 'detail'"
              type="primary"
              :loading="busy"
              :disabled="unknown || preview"
              @click="submit"
              >{{ t('submit') }}</el-button
            ></PermissionGate
          >
        </div>
      </el-form>
      <template v-if="isCrud && !recordMode">
        <el-alert v-if="!model" :title="t('noPublishedModel')" type="warning" :closable="false" />
        <template v-else
          ><AppQueryPanel
            v-if="queryMode === 'top'"
            show-actions
            @submit="table?.reload()"
            @reset="resetFilters"
            ><div class="studio-form-grid">
              <div v-for="field in queryFields" :key="String(field.id)">
                <el-date-picker
                  v-if="field.type === 'Date' || field.type === 'DateTime'"
                  v-model="filters[String(field.id)]"
                  type="daterange"
                  value-format="YYYY-MM-DD"
                  :aria-label="String(field.label || field.name)"
                /><el-select
                  v-else-if="field.type === 'Enum'"
                  v-model="filters[String(field.id)]"
                  :placeholder="String(field.label || field.name)"
                  :aria-label="String(field.label || field.name)"
                  clearable
                  ><el-option
                    v-for="value in field.enumValues as string[]"
                    :key="value"
                    :label="value"
                    :value="value" /></el-select
                ><el-input
                  v-else
                  v-model="filters[String(field.id)]"
                  :placeholder="String(field.label || field.name)"
                  :aria-label="String(field.label || field.name)"
                  clearable
                />
              </div></div
          ></AppQueryPanel>
          <AppDataTable
            ref="table"
            :table-key="'studio.ledger.' + runtime.resource.nId"
            :columns="columns"
            :loader="loadRecords"
            row-key="nId"
            selection="none"
            :query-mode="queryMode"
            :initial-header-filters="filters"
            :initial-page-index="listPage"
            :page-size="listPageSize"
            :action-column-width="200"
            @query-mode-change="changeQueryMode"
            @query-change="tableQuery"
          >
            <template #toolbar-actions
              ><PermissionGate permission-n-id="studio.ledger.write"
                ><el-button type="primary" :disabled="preview" @click="recordRoute('new')">{{
                  t('newRecord')
                }}</el-button></PermissionGate
              ><el-checkbox v-model="deleted" @change="table?.reload()">{{
                t('deleted')
              }}</el-checkbox></template
            >
            <template #actions="{ row }"
              ><el-button link type="primary" @click="recordRoute('detail', row)">{{
                t('detail')
              }}</el-button
              ><PermissionGate permission-n-id="studio.ledger.write"
                ><el-button v-if="!deleted" link type="primary" @click="recordRoute('edit', row)">{{
                  t('edit')
                }}</el-button
                ><el-button
                  link
                  :disabled="busy || preview"
                  @click="lifecycle(row, deleted ? 'restore' : 'delete')"
                  >{{ t(deleted ? 'restore' : 'delete') }}</el-button
                ></PermissionGate
              ></template
            >
          </AppDataTable></template
        >
      </template>
      <div v-if="actions.length" class="studio-tools">
        <template
          v-for="action in actions.filter((a) => a.event !== 'RowClick')"
          :key="String(action.id)"
          ><PermissionGate :permission-n-id="String(action.permissionKey ?? 'studio.lowcode.run')"
            ><el-button
              :loading="busy"
              :disabled="unknown || recordMode === 'detail'"
              @click="execute(action)"
              >{{ action.label || action.actionKey }}
              {{ preview ? '(' + t('mock') + ')' : '' }}</el-button
            ></PermissionGate
          ></template
        ><el-button :disabled="busy" @click="reload">{{ t('refresh') }}</el-button>
      </div>
    </template>
    <el-collapse v-if="runtime.diagnostics?.length || result"
      ><el-collapse-item :title="t('diagnostic')">
        <pre class="studio-json">{{ JSON.stringify(result ?? runtime.diagnostics, null, 2) }}</pre>
      </el-collapse-item></el-collapse
    >
  </div>
</template>
