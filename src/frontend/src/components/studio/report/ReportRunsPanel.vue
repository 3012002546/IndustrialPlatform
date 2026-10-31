<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import AppDataTable from '@/components/management/AppDataTable.vue'
import AppFormDrawer from '@/components/management/AppFormDrawer.vue'
import { useRouter } from 'vue-router'
import { ElMessageBox } from 'element-plus'
import type { AppDataTableRequest } from '@/components/management/AppDataTable'
import { downloadBlob } from '@/components/management/download'
import { getStudioApi, type Json } from '@/api/studio'
import { PermissionGate } from '@/permissions'
import { safeTotal } from '@/components/studio/shared/model'
import { pendingOperation } from '@/components/studio/shared/pendingOperation'
import { useReportCopy } from './copy'

const props = defineProps<{ resourceNId: string }>()
const api = getStudioApi(),
  t = useReportCopy(),
  router = useRouter()
const table = ref<{ reload: () => Promise<void> }>(),
  detail = ref<Json>(),
  error = ref(''),
  busy = ref(false),
  visible = ref(false),
  activeRows = ref(false)
const active = (status: unknown) =>
  ['PendingDispatch', 'Queued', 'Running', 'CancelRequested'].includes(String(status))
onMounted(() => {
  void table.value?.reload()
})
const hasActive = computed(
  () => activeRows.value || (visible.value && active(detail.value?.status)),
)
const statusKeys = {
  PendingDispatch: 'pending',
  Queued: 'queued',
  Running: 'running',
  CancelRequested: 'cancelling',
  Succeeded: 'succeeded',
  Failed: 'failed',
  Cancelled: 'cancelled',
  Unknown: 'unknown',
} as const
const statusLabel = (status: string) =>
  status in statusKeys ? t.value(statusKeys[status as keyof typeof statusKeys]) : status
let timer: ReturnType<typeof setTimeout> | undefined
let disposed = false
let detailGeneration = 0
const workingCopyRequest = pendingOperation(),
  retryRequest = pendingOperation()
watch(
  visible,
  (value) => {
    if (!value) detailGeneration++
  },
  { flush: 'sync' },
)
function schedule() {
  if (timer || disposed || !hasActive.value) return
  timer = setTimeout(async () => {
    timer = undefined
    const generationAtStart = detailGeneration
    try {
      await table.value?.reload()
      if (visible.value && detail.value) {
        const own = detailGeneration,
          runNId = String(detail.value.runNId)
        const state = await api.reportState(runNId)
        if (!disposed && visible.value && own === detailGeneration) detail.value = state
      }
    } catch (e) {
      if (!disposed && generationAtStart === detailGeneration)
        error.value = e instanceof Error ? e.message : t.value('error')
    } finally {
      schedule()
    }
  }, 4000)
}
watch(hasActive, (value) => {
  if (value) schedule()
  else if (timer) {
    clearTimeout(timer)
    timer = undefined
  }
})
onBeforeUnmount(() => {
  disposed = true
  detailGeneration++
  if (timer) clearTimeout(timer)
})
async function load(request: AppDataTableRequest) {
  const result = await api.reportRuns(props.resourceNId, request.pageIndex, request.pageSize)
  activeRows.value = result.items.some((row: Json) => active(row.status))
  return {
    items: result.items,
    total: safeTotal(result.totalCount),
    pageIndex: result.page,
    pageSize: result.pageSize,
  }
}
async function open(runNId: string) {
  const own = ++detailGeneration
  error.value = ''
  detail.value = undefined
  visible.value = true
  try {
    const state = await api.reportState(runNId)
    if (disposed || own !== detailGeneration) return
    detail.value = state
    await table.value?.reload()
  } catch (e) {
    if (!disposed && own === detailGeneration)
      error.value = e instanceof Error ? e.message : t.value('error')
  }
}
async function cancel(row: Json) {
  if (busy.value) return
  busy.value = true
  error.value = ''
  try {
    await api.reportCancel(String(row.runNId), String(row.stateVersion))
    await open(String(row.runNId))
  } catch (e) {
    error.value = e instanceof Error ? e.message : t.value('error')
  } finally {
    busy.value = false
  }
}
async function download(row: Json) {
  if (busy.value || !detail.value) return
  const runNId = String(detail.value.runNId)
  busy.value = true
  error.value = ''
  try {
    const blob = await api.client.getBlob?.(String(row.contentUrl))
    if (!blob) throw new Error(t.value('error'))
    downloadBlob(blob, `${runNId}-${row.purpose}.${String(row.format).toLowerCase()}`)
  } catch (e) {
    error.value = e instanceof Error ? e.message : t.value('error')
  } finally {
    busy.value = false
  }
}
async function workingCopy(row: Json) {
  if (busy.value) return
  busy.value = true
  try {
    const artifactNId = String(row.nId)
    const input = {
      name: `${t.value('outputFile')}-${detail.value?.runNId}`,
      sourceArtifactNId: row.nId,
    }
    await api.importWorkbook(input, workingCopyRequest.key([artifactNId, input]))
    workingCopyRequest.clear()
    await router.push({ name: 'studio-workbooks' })
  } catch (e) {
    error.value = e instanceof Error ? e.message : t.value('error')
  } finally {
    busy.value = false
  }
}
async function retry(mode: string) {
  if (busy.value || !detail.value) return
  busy.value = true
  error.value = ''
  try {
    const accepted = await api.reportRetry(
      String(detail.value.runNId),
      String(detail.value.stateVersion),
      mode,
      retryRequest.key([detail.value.runNId, detail.value.stateVersion, mode]),
    )
    retryRequest.clear()
    await open(String(accepted.runNId))
  } catch (e) {
    error.value = e instanceof Error ? e.message : t.value('error')
  } finally {
    busy.value = false
  }
}
defineExpose({ open, reload: () => table.value?.reload() })
async function resolve() {
  if (!detail.value || busy.value) return
  const own = detailGeneration,
    runNId = String(detail.value.runNId),
    stateVersion = String(detail.value.stateVersion)
  let reason: string
  busy.value = true
  try {
    reason = (
      await ElMessageBox.prompt(t.value('resolutionReason'), t.value('resolveUnknown'), {
        inputValidator: (value) => value.trim().length >= 10 || t.value('resolutionReason'),
      })
    ).value
  } catch {
    busy.value = false
    return
  }
  if (disposed || own !== detailGeneration) {
    busy.value = false
    return
  }
  error.value = ''
  try {
    await api.reportResolve(runNId, stateVersion, reason)
    if (!disposed && own === detailGeneration) await open(runNId)
  } catch (e) {
    error.value = e instanceof Error ? e.message : t.value('error')
  } finally {
    busy.value = false
  }
}
</script>
<template>
  <section>
    <h2>{{ t('runs') }}</h2>
    <el-alert v-if="error" :title="error" type="error" :closable="false" />
    <AppDataTable
      ref="table"
      :table-key="`studio.report-runs.${resourceNId}`"
      row-key="runNId"
      :loader="load"
      :columns="[
        { field: 'runNId', title: t('runDetails'), minWidth: 200 },
        { field: 'status', title: t('status') },
      ]"
    >
      <template #cell-status="{ row }">{{ statusLabel(String(row.status)) }}</template>
      <template #actions="{ row }">
        <el-button link @click="open(String(row.runNId))">{{ t('runDetails') }}</el-button>
        <PermissionGate permission-n-id="studio.report.run"
          ><el-button
            v-if="active(row.status) && row.status !== 'CancelRequested'"
            link
            :disabled="busy"
            @click="cancel(row)"
            >{{ t('cancel') }}</el-button
          ></PermissionGate
        >
      </template>
    </AppDataTable>
    <AppFormDrawer
      v-model="visible"
      :title="t('runDetails')"
      size="wide"
      :hide-footer="true"
      :busy="busy"
    >
      <template v-if="detail">
        <PermissionGate permission-n-id="studio.report.reconcile"
          ><el-button v-if="detail.status === 'Unknown'" :loading="busy" @click="resolve">{{
            t('resolveUnknown')
          }}</el-button></PermissionGate
        >
        <PermissionGate permission-n-id="studio.report.export"
          ><div
            v-if="['Succeeded', 'Failed', 'Cancelled'].includes(String(detail.status))"
            class="studio-toolbar"
          >
            <el-button
              :disabled="busy || !detail.hasDataSnapshot"
              @click="retry('RenderSnapshot')"
              >{{ t('renderSnapshot') }}</el-button
            ><el-button :disabled="busy" @click="retry('NewData')">{{ t('newData') }}</el-button>
          </div></PermissionGate
        >
        <el-descriptions :column="1" border>
          <el-descriptions-item :label="t('status')">{{
            statusLabel(String(detail.status))
          }}</el-descriptions-item>
          <el-descriptions-item :label="t('started')">{{
            detail.startedOn || '—'
          }}</el-descriptions-item>
          <el-descriptions-item :label="t('completed')">{{
            detail.completedOn || '—'
          }}</el-descriptions-item>
        </el-descriptions>
        <el-alert
          v-if="detail.errorCode"
          :title="String(detail.errorCode)"
          :description="detail.errorLocation"
          type="error"
          :closable="false"
        />
        <AppDataTable
          table-key="studio.report-run.artifacts"
          row-key="nId"
          :title="t('artifacts')"
          :rows="detail.artifacts ?? []"
          :columns="[
            { field: 'purpose', title: t('kind') },
            { field: 'format', title: t('output') },
            { field: 'contentLength', title: t('bytes') },
            { field: 'pageCount', title: t('pages') },
          ]"
        >
          <template #cell-purpose="{ row }">{{
            row.purpose === 'Output'
              ? t('outputFile')
              : row.purpose === 'DataSnapshot'
                ? t('sourceSnapshot')
                : row.purpose
          }}</template>
          <template #actions="{ row }"
            ><PermissionGate permission-n-id="studio.report.export"
              ><el-button link :disabled="busy" @click="download(row)">{{
                t('download')
              }}</el-button
              ><PermissionGate permission-n-id="studio.workbook.edit"
                ><el-button
                  v-if="row.format === 'Xlsx' && row.purpose === 'Output'"
                  link
                  :disabled="busy"
                  @click="workingCopy(row)"
                  >{{ t('workingCopy') }}</el-button
                ></PermissionGate
              ></PermissionGate
            ></template
          >
        </AppDataTable>
      </template>
    </AppFormDrawer>
  </section>
</template>
