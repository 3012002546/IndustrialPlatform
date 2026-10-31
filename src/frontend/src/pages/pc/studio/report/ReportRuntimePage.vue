<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import AppPage from '@/components/base/AppPage.vue'
import AppQueryPanel from '@/components/management/AppQueryPanel.vue'
import AppDataTable from '@/components/management/AppDataTable.vue'
import type { AppDataTableRequest } from '@/components/management/AppDataTable'
import { PermissionGate } from '@/permissions'
import { getStudioApi, type Json, type StudioResource } from '@/api/studio'
import { safeTotal } from '@/components/studio/shared/model'
import { pendingOperation } from '@/components/studio/shared/pendingOperation'
import { useReportCopy } from '@/components/studio/report/copy'
import ReportRunsPanel from '@/components/studio/report/ReportRunsPanel.vue'
const route = useRoute(),
  router = useRouter(),
  api = getStudioApi(),
  t = useReportCopy(),
  nId = String(route.params.nId)
const resource = ref<StudioResource>(),
  document = ref<Json>(),
  parameters = ref<Json>({}),
  error = ref(''),
  busy = ref(false),
  sort = ref<Json[]>(),
  format = ref('Pdf'),
  options = ref<Record<string, Json[]>>({}),
  optionsLoading = ref(false),
  table = ref<{ reload: () => Promise<void> }>()
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
  const input = { revisionNId: resource.value.publishedRevisionNId, parameters: parameters.value },
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
      key ===
      JSON.stringify({
        revisionNId: resource.value.publishedRevisionNId,
        parameters: parameters.value,
      })
    )
      options.value = result.options
  } catch (e) {
    if (
      key ===
      JSON.stringify({
        revisionNId: resource.value?.publishedRevisionNId,
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
    document.value?.query?.filters ??
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
        title: String(source?.label ?? c.columnId),
        sortable: !!c.sortable,
        minWidth: 120,
      }
    })
})
async function load(request: AppDataTableRequest) {
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
    const result = await api.reportQuery(nId, {
      revisionNId: resource.value.publishedRevisionNId,
      parameters: parameters.value,
      page: request.pageIndex,
      pageSize: request.pageSize,
      sort: sort.value,
    })
    return {
      items: result.items,
      total: safeTotal(result.totalCount),
      pageIndex: result.page,
      pageSize: result.pageSize,
    }
  } catch (e) {
    error.value = e instanceof Error ? e.message : t.value('error')
    throw e
  }
}
function setDefaultParameters() {
  parameters.value = Object.fromEntries(
    (document.value?.parameters ?? [])
      .filter((p: Json) => p.defaultValue !== undefined)
      .map((p: Json) => [p.name, p.defaultValue]),
  )
}
function resetQuery() {
  setDefaultParameters()
  void table.value?.reload()
  void loadOptions()
}
async function generate() {
  if (busy.value || !resource.value) return
  busy.value = true
  error.value = ''
  try {
    const input = {
      revisionNId: resource.value.publishedRevisionNId,
      parameters: parameters.value,
      sort: sort.value,
      format: format.value,
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
      `/api/v1/studio/reports/${encodeURIComponent(nId)}/runtime`,
    )
    resource.value = result.resource
    document.value = result.document
    setDefaultParameters()
    await Promise.all([table.value?.reload(), loadOptions()])
  } catch (e) {
    error.value = e instanceof Error ? e.message : t.value('error')
  }
})
</script>
<template>
  <AppPage :title="resource?.name ?? t('reports')"
    ><el-alert v-if="error" :title="error" type="error" :closable="false" /><AppQueryPanel
      :show-actions="true"
      @submit="table?.reload()"
      @reset="resetQuery"
      ><label v-for="f in filters" :key="f.parameterName"
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
    <div class="studio-toolbar">
      <el-button @click="router.back()">{{ t('back') }}</el-button
      ><el-select v-model="format" :aria-label="t('output')" style="width: 130px"
        ><el-option label="PDF" value="Pdf" /><el-option label="HTML" value="Html" /><el-option
          label="Excel"
          value="Xlsx" /></el-select
      ><PermissionGate permission-n-id="studio.report.export"
        ><el-button :loading="busy" @click="generate">{{
          t('queryAll')
        }}</el-button></PermissionGate
      >
    </div>
    <AppDataTable
      v-if="document?.mode === 'Query'"
      ref="table"
      :table-key="'studio.report.' + nId"
      row-key="__rowKey"
      :columns="columns"
      :loader="load"
    />
    <ReportRunsPanel ref="runs" :resource-n-id="nId" />
  </AppPage>
</template>
