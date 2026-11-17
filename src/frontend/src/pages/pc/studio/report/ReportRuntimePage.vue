<script setup lang="ts">
import { computed, defineAsyncComponent, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { onBeforeRouteLeave, useRoute, useRouter } from 'vue-router'
import AppPage from '@/components/base/AppPage.vue'
import AppQueryPanel from '@/components/management/AppQueryPanel.vue'
import AppFormDrawer from '@/components/management/AppFormDrawer.vue'
import type { QueryRequest } from '@/components/management/querySpreadsheet/types'
import { useSheetCopy } from '@/components/management/querySpreadsheet/copy'
import { useAuthStore } from '@/stores/authStore'
import type { AppDataTableRequest } from '@/components/management/AppDataTable'
import { PermissionGate } from '@/permissions'
import { getStudioApi, type Json, type StudioResource } from '@/api/studio'
import { safeTotal } from '@/components/studio/shared/model'
import { pendingOperation } from '@/components/studio/shared/pendingOperation'
import { useReportCopy } from '@/components/studio/report/copy'
import ReportFormatSelect from '@/components/studio/report/ReportFormatSelect.vue'
import ReportRunsPanel from '@/components/studio/report/ReportRunsPanel.vue'
const props = defineProps<{
  resourceNId?: string
  revisionNId?: string
  initialParameters?: Json
  embedded?: boolean
}>()
const AppQuerySpreadsheet = defineAsyncComponent(
  () => import('@/components/management/AppQuerySpreadsheet.vue'),
)
const sheetCopy = useSheetCopy(),
  auth = useAuthStore()
let alive = true
const route = useRoute(),
  router = useRouter(),
  api = getStudioApi(),
  t = useReportCopy(),
  nId = props.resourceNId ?? String(route.params.nId)
const fixedRevision = ref(props.revisionNId)
const resource = ref<StudioResource>(),
  document = ref<Json>(),
  parameters = ref<Json>({}),
  error = ref(''),
  busy = ref(false),
  sort = ref<Json[]>(),
  format = ref('Pdf'),
  options = ref<Record<string, Json[]>>({}),
  optionsLoading = ref(false),
  table = ref<{
    reload: () => Promise<void>
    confirmDiscard: () => Promise<boolean>
    exportSnapshot: () => Promise<Json>
  }>()
const canExport = ref(false)
const exportPanel = ref(false)
const isQuery = computed(() => document.value?.mode === 'Query')
const fieldValues = ref<Json>({})
const appliedFilters = ref<Json[]>([])
const appliedParameters = ref<Json>({})
let requestedFilters: Json[] | undefined
const submitting = ref(false)
const fieldFilters = computed<Json[]>(() =>
  (document.value?.query?.filters ?? []).filter((f: Json) => f.fieldId),
)
function fieldInput(id: string): Json {
  return (fieldValues.value[id] ??= {})
}
function submittedFilters(): Json[] {
  return fieldFilters.value
    .map((field) => {
      const input = fieldInput(field.fieldId)
      if (['Date', 'DateTime'].includes(field.dataType))
        return {
          fieldId: field.fieldId,
          ...(input.range?.[0] ? { min: input.range[0], max: input.range[1] } : {}),
        }
      return {
        fieldId: field.fieldId,
        ...Object.fromEntries(
          Object.entries(input).filter(
            ([, value]) => value !== '' && value !== undefined && value !== null,
          ),
        ),
      }
    })
    .filter((filter) => Object.keys(filter).length > 1)
}
async function submitQuery() {
  if (submitting.value) return
  submitting.value = true
  requestedFilters = submittedFilters()
  try {
    await table.value?.reload()
  } finally {
    requestedFilters = undefined
    submitting.value = false
  }
}
const runs = ref<InstanceType<typeof ReportRunsPanel>>()
const optionRequests = new Map<string, Promise<Json>>()
const runRequest = pendingOperation()
async function loadOptions() {
  if (
    !resource.value ||
    !document.value?.query?.filters.some((filter: Json) =>
      ['Select', 'BusinessSelector'].includes(String(filter.control)),
    )
  )
    return
  const input = {
      revisionNId: fixedRevision.value ?? resource.value.publishedRevisionNId,
      parameters: parameters.value,
    },
    key = JSON.stringify(input)
  optionsLoading.value = true
  try {
    let request = optionRequests.get(key)
    if (!request) {
      request = api.reportQueryOptions(nId, input)
      optionRequests.set(key, request)
    }
    const result = await request
    if (
      alive &&
      key ===
        JSON.stringify({
          revisionNId: fixedRevision.value ?? resource.value?.publishedRevisionNId,
          parameters: parameters.value,
        })
    )
      options.value = result.options
  } catch (e) {
    if (
      key ===
      JSON.stringify({
        revisionNId: fixedRevision.value ?? resource.value?.publishedRevisionNId,
        parameters: parameters.value,
      })
    ) {
      options.value = {}
      error.value = e instanceof Error ? e.message : t.value('error')
    }
  } finally {
    optionRequests.delete(key)
    optionsLoading.value = optionRequests.size > 0
  }
}
const filters = computed<Json[]>(
  () =>
    document.value?.query?.filters.filter((f: Json) => !f.fieldId) ??
    document.value?.parameters?.map((p: Json) => ({
      parameterName: p.name,
      label: p.label,
      control: p.type === 'Date' ? 'Date' : p.type === 'Int32' ? 'Number' : 'Text',
    })) ??
    [],
)
const columns = computed(() => {
  const query = document.value?.query
  if (!query) return []
  const band = document.value!.bands.find((b: Json) => b.id === query.resultBandId)
  return (query.columns as Json[])
    .filter((c) => c.visible !== false)
    .map((c) => {
      const source = band?.controls
        .find((s: Json) => s.id === c.controlId)
        ?.table?.columns.find((s: Json) => s.id === c.columnId)
      return {
        field: String(c.columnId),
        title: String(c.label ?? source?.label ?? c.columnId),
        sortable: !!c.sortable,
        width: 150,
      }
    })
})
async function load(request: QueryRequest & Pick<AppDataTableRequest, 'sort'>) {
  if (!document.value || !resource.value) return { items: [], total: 0, pageIndex: 1, pageSize: 20 }
  sort.value = request.sort
    ? [
        {
          fieldId: request.sort.field,
          direction: request.sort.order === 'desc' ? 'Desc' : 'Asc',
          nulls: 'Last',
        },
      ]
    : sort.value === undefined
      ? undefined
      : []
  try {
    const queryFilters = structuredClone(
      requestedFilters ?? JSON.parse(JSON.stringify(appliedFilters.value)),
    )
    const queryParameters = JSON.parse(JSON.stringify(parameters.value))
    const result = await api.reportQuery(
      nId,
      {
        revisionNId: fixedRevision.value ?? resource.value.publishedRevisionNId,
        parameters: queryParameters,
        all: true,
        page: request.pageIndex,
        pageSize: request.pageSize,
        sort: sort.value,
        filters: queryFilters,
      },
      request.signal,
    )
    return {
      accepted: () => {
        appliedFilters.value = queryFilters
        appliedParameters.value = queryParameters
      },
      items: result.items,
      total: safeTotal(result.totalCount),
      pageIndex: result.page,
      pageSize: result.pageSize,
    }
  } catch (e) {
    throw e
  }
}
async function authorizeOutput(signal: AbortSignal) {
  await api.reportQueryOutputAccess(
    nId,
    fixedRevision.value ?? resource.value!.publishedRevisionNId!,
    signal,
  )
}
onBeforeRouteLeave(
  async () => !auth.isAuthenticated || !table.value || (await table.value.confirmDiscard()),
)
watch(
  () => auth.user?.userId,
  (next, previous) => {
    if (previous && next !== previous) {
      alive = false
      document.value = undefined
      resource.value = undefined
      canExport.value = false
    }
  },
)
onBeforeUnmount(() => {
  alive = false
  optionRequests.clear()
})
function setDefaultParameters() {
  parameters.value = Object.fromEntries(
    (document.value?.parameters ?? [])
      .filter((p: Json) => p.defaultValue !== undefined)
      .map((p: Json) => [p.name, p.defaultValue]),
  )
  Object.assign(parameters.value, props.initialParameters ?? {})
}
function resetQuery() {
  setDefaultParameters()
  fieldValues.value = {}
  void submitQuery()
  void loadOptions()
}
async function generate() {
  if (busy.value || !resource.value) return
  busy.value = true
  error.value = ''
  try {
    const input = {
      revisionNId: fixedRevision.value ?? resource.value.publishedRevisionNId,
      parameters: isQuery.value ? appliedParameters.value : parameters.value,
      sort: sort.value,
      filters: appliedFilters.value,
      format: format.value,
      ...(isQuery.value ? { querySnapshot: await table.value!.exportSnapshot() } : {}),
    }
    const accepted = await api.reportRun(nId, input, runRequest.key([nId, input]))
    runRequest.clear()
    await runs.value?.open(String(accepted.runNId))
  } catch (e) {
    error.value = e instanceof Error ? e.message : t.value('error')
  } finally {
    busy.value = false
  }
}
onMounted(async () => {
  try {
    const result = await api.client.get<Json>(
      `/api/v1/studio/reports/${encodeURIComponent(nId)}/runtime${props.revisionNId ? '?revisionNId=' + encodeURIComponent(props.revisionNId) : ''}`,
    )
    if (!alive) return
    fixedRevision.value = result.revisionNId
    resource.value = result.resource
    document.value = result.document
    canExport.value = result.canExport === true
    setDefaultParameters()
    appliedParameters.value = JSON.parse(JSON.stringify(parameters.value))
    await loadOptions()
  } catch (e) {
    error.value = e instanceof Error ? e.message : t.value('error')
  }
})
</script>
<template>
  <AppPage
    :title="resource?.name ?? t('reports')"
    :class="{ 'query-runtime-fill': isQuery && !embedded }"
  >
    <template #heading-meta
      ><el-tag v-if="document" :type="isQuery ? 'primary' : 'info'">{{
        sheetCopy(isQuery ? 'queryReport' : 'fixedReport')
      }}</el-tag></template
    >
    <template #actions>
      <el-button v-if="!embedded" @click="router.back()">{{ t('back') }}</el-button>
      <el-button v-if="isQuery" @click="exportPanel = true">{{
        sheetCopy('fullExport')
      }}</el-button>
    </template>
    <el-alert v-if="error" :title="error" type="error" :closable="false" />
    <AppQueryPanel
      v-if="filters.length || fieldFilters.length"
      :show-actions="true"
      :grid="true"
      @submit="submitQuery"
      @reset="resetQuery"
    >
      <label
        v-for="f in fieldFilters"
        :key="f.fieldId"
        class="query-field-filter"
        :class="{
          'query-field-range': ['Date', 'DateTime', 'Int32', 'Int64', 'Decimal'].includes(
            f.dataType,
          ),
        }"
      >
        <span>{{ f.label }}</span>
        <el-date-picker
          v-if="['Date', 'DateTime'].includes(f.dataType)"
          v-model="fieldInput(f.fieldId).range"
          type="daterange"
          value-format="YYYY-MM-DD"
          :aria-label="f.label"
          :start-placeholder="t('filterFrom')"
          :end-placeholder="t('filterTo')"
        />
        <span
          v-else-if="['Int32', 'Int64', 'Decimal'].includes(f.dataType)"
          class="query-number-range"
        >
          <el-input
            v-model="fieldInput(f.fieldId).min"
            type="number"
            clearable
            :aria-label="f.label + ' ' + t('filterMin')"
            :placeholder="t('filterMin')"
          />
          <span>–</span>
          <el-input
            v-model="fieldInput(f.fieldId).max"
            type="number"
            clearable
            :aria-label="f.label + ' ' + t('filterMax')"
            :placeholder="t('filterMax')"
          />
        </span>
        <el-select
          v-else-if="f.dataType === 'Boolean'"
          v-model="fieldInput(f.fieldId).value"
          clearable
          :aria-label="f.label"
          ><el-option :value="true" :label="t('filterTrue')" /><el-option
            :value="false"
            :label="t('filterFalse')"
        /></el-select>
        <el-input
          v-else
          v-model="fieldInput(f.fieldId).value"
          clearable
          :aria-label="f.label"
          :placeholder="t('filterContains')"
          @keyup.enter="submitQuery"
        />
      </label>
      <label v-for="f in filters" :key="f.parameterName"
        >{{ f.label || f.parameterName
        }}<el-select
          v-if="['Select', 'BusinessSelector'].includes(String(f.control))"
          v-model="parameters[f.parameterName]"
          filterable
          clearable
          :loading="optionsLoading"
          :aria-label="f.label || f.parameterName"
          @visible-change="(visible: boolean) => visible && loadOptions()"
          ><el-option
            v-for="(option, i) in options[f.parameterName] ?? []"
            :key="i"
            :value="option.value"
            :label="String(option.label)" /></el-select
        ><el-date-picker
          v-else-if="f.control === 'DateRange'"
          v-model="parameters[f.parameterName]"
          type="daterange"
          value-format="YYYY-MM-DD"
          :aria-label="f.label || f.parameterName" /><el-date-picker
          v-else-if="f.control === 'Date'"
          v-model="parameters[f.parameterName]"
          type="date"
          value-format="YYYY-MM-DD" /><el-input-number
          v-else-if="f.control === 'Number'"
          v-model="parameters[f.parameterName]" /><el-input
          v-else
          v-model="parameters[f.parameterName]"
          :aria-label="f.label || f.parameterName" /></label
    ></AppQueryPanel>
    <template v-if="document && !isQuery">
      <div class="studio-toolbar">
        <ReportFormatSelect v-model="format" /><PermissionGate
          permission-n-id="studio.report.export"
          ><el-button :loading="busy" @click="generate">{{
            t('queryAll')
          }}</el-button></PermissionGate
        >
      </div>
      <p class="studio-export-hint">{{ t('exportHint') }}</p>
    </template>
    <AppQuerySpreadsheet
      v-if="document?.mode === 'Query'"
      ref="table"
      :key="nId + ':' + fixedRevision"
      :title="resource?.name ?? t('reports')"
      :columns="columns"
      :loader="load"
      :can-export="canExport"
      :authorize-output="authorizeOutput"
    />
    <ReportRunsPanel v-if="document && !isQuery" ref="runs" :resource-n-id="nId" />
    <AppFormDrawer
      v-if="isQuery"
      v-model="exportPanel"
      :title="sheetCopy('fullExport')"
      size="extra-wide"
      hide-footer
    >
      <template v-if="exportPanel">
        <p>{{ sheetCopy('fullExportHelp') }}</p>
        <div class="studio-toolbar">
          <ReportFormatSelect v-model="format" />
          <PermissionGate permission-n-id="studio.report.export"
            ><el-button type="primary" :loading="busy" @click="generate">{{
              sheetCopy('serverOutput')
            }}</el-button></PermissionGate
          >
        </div>
        <details class="report-export-help">
          <summary>{{ sheetCopy('formatHelp') }}</summary>
          <p>{{ t('exportHint') }}</p>
        </details>
        <p>{{ sheetCopy('historyHelp') }}</p>
        <ReportRunsPanel ref="runs" :resource-n-id="nId" />
      </template>
    </AppFormDrawer>
  </AppPage>
</template>
<style scoped>
.query-field-filter {
  display: flex;
  flex-direction: column;
  gap: 6px;
  min-width: 0;
  width: 220px;
  max-width: 100%;
  flex: 0 0 auto;
}
.query-field-range {
  width: 320px;
}
.query-field-filter :deep(.el-date-editor) {
  width: 100%;
  min-width: 0;
  box-sizing: border-box;
}
.query-number-range {
  display: flex;
  align-items: center;
  gap: 6px;
}
.query-runtime-fill :deep(.app-query-panel) {
  flex-shrink: 0;
  max-height: 35vh;
  overflow: auto;
}
.query-runtime-fill {
  min-height: 0;
  height: 100%;
}
.query-runtime-fill :deep(.app-page__body) {
  display: flex;
  flex-direction: column;
  gap: 10px;
  min-height: 0;
}
.query-runtime-fill :deep(.app-query-spreadsheet) {
  flex: 1;
  min-height: 0;
}
.query-runtime-fill :deep(.query-sheet-workspace) {
  flex: 1;
  min-height: 240px;
  display: flex;
}
.query-runtime-fill :deep(.query-sheet-interaction) {
  flex: 1;
  min-height: 0;
  display: flex;
  min-width: 0;
}
.query-runtime-fill :deep(.query-sheet-canvas) {
  flex: 1;
  height: auto;
  min-height: 0;
}
.report-export-help {
  margin: 16px 0;
  color: var(--el-text-color-secondary);
}
.report-export-help summary {
  cursor: pointer;
}
</style>
