<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import { ElMessageBox } from 'element-plus'
import AppPage from '@/components/base/AppPage.vue'
import AppQueryPanel from '@/components/management/AppQueryPanel.vue'
import AppDataTable from '@/components/management/AppDataTable.vue'
import AppFormDrawer from '@/components/management/AppFormDrawer.vue'
import type {
  AppDataTableColumn,
  AppDataTableQueryMode,
  AppDataTableRequest,
} from '@/components/management/AppDataTable'
import { getSchedulerApi, type SchedulerRun, type RunDetail } from '@/api/systemData/schedulerApi'
import { PERMISSIONS } from '@/permissions'
import { useAuthStore } from '@/stores/authStore'
import { useLocalizationStore } from '@/stores/localizationStore'
const auth = useAuthStore(),
  locale = useLocalizationStore(),
  route = useRoute()
const text = (zh: string, en: string): string => (locale.locale === 'zh-CN' ? zh : en)
const pageIndex = ref(1),
  pageSize = ref(25)
const rows = ref<SchedulerRun[]>([]),
  total = ref(0),
  loading = ref(false),
  busy = ref(false),
  error = ref('')
const jobNId = ref(typeof route.query.jobNId === 'string' ? route.query.jobNId : ''),
  status = ref(''),
  source = ref(''),
  from = ref(''),
  to = ref('')
const tableQueryMode = ref<AppDataTableQueryMode>('top')
const tableRef = ref<{ currentPage: number; reload: () => Promise<void> } | null>(null)
const detail = ref<RunDetail | null>(null),
  detailOpen = ref(false),
  resolveOpen = ref(false)
const decision = ref('ConfirmedNoEffect'),
  reason = ref(''),
  evidence = ref(''),
  stopped = ref(false)
let sequence = 0,
  detailSequence = 0
const retryKeys = new Map<string, string>()
const detailLabels = computed<Record<string, string>>(() => ({
  nId: text('运行标识', 'Run identifier'),
  jobNId: text('任务标识', 'Job identifier'),
  status: text('状态', 'State'),
  source: text('来源', 'Source'),
  handler: text('处理器', 'Handler'),
  operationKey: text('业务防重键', 'Operation key'),
  traceId: 'TraceId',
  resultCode: text('结果码', 'Result code'),
  message: text('结果说明', 'Result message'),
}))
const runStates = [
  'Queued',
  'Running',
  'RetryWaiting',
  'CancelRequested',
  'Succeeded',
  'Failed',
  'TimedOut',
  'Cancelled',
  'Unknown',
]
const runSources = ['Schedule', 'Manual', 'Retry']
function resetQuery(): void {
  jobNId.value = ''
  status.value = ''
  source.value = ''
  from.value = ''
  to.value = ''
  void reload()
}
function onQueryModeChange(mode: AppDataTableQueryMode): void {
  tableQueryMode.value = mode
  if (mode === 'header') {
    jobNId.value = ''
    status.value = ''
    source.value = ''
    from.value = ''
    to.value = ''
  }
}
const message = (cause: unknown): string =>
  cause instanceof Error ? cause.message : text('操作失败。', 'Operation failed.')
const columns = computed<readonly AppDataTableColumn[]>(() => [
  {
    field: 'nId',
    title: text('运行标识', 'Run identifier'),
    minWidth: 220,
    filter: false as const,
  },
  {
    field: 'jobNId',
    title: text('任务标识', 'Job identifier'),
    minWidth: 180,
    sortable: false,
    filter: { kind: 'text' },
  },
  { field: 'jobName', title: text('任务', 'Job'), minWidth: 180, filter: false as const },
  {
    field: 'source',
    title: text('来源', 'Source'),
    width: 110,
    filter: { kind: 'select', options: runSources.map((value) => ({ label: value, value })) },
  },
  {
    field: 'status',
    title: text('状态', 'State'),
    minWidth: 140,
    filter: { kind: 'select', options: runStates.map((value) => ({ label: value, value })) },
  },
  {
    field: 'acceptedOn',
    title: text('接收时间', 'Accepted'),
    minWidth: 260,
    filter: { kind: 'date-range' },
  },
  { field: 'startedOn', title: text('开始时间', 'Started'), minWidth: 200, filter: false as const },
  {
    field: 'completedOn',
    title: text('结束/未知判定', 'Completed / marked unknown'),
    minWidth: 200,
    filter: false as const,
  },
  {
    field: 'attemptsUsed',
    title: text('已用尝试', 'Attempts used'),
    width: 110,
    filter: false as const,
  },
  {
    field: 'resultCode',
    title: text('结果码', 'Result code'),
    minWidth: 200,
    filter: false as const,
  },
])
async function loadTable(request: AppDataTableRequest) {
  const current = ++sequence
  loading.value = true
  try {
    const header = request.queryMode === 'header'
    const range = Array.isArray(request.filters.acceptedOn) ? request.filters.acceptedOn : []
    const start = header ? (range[0] ? `${String(range[0])}T00:00:00` : '') : from.value
    const end = header ? (range[1] ? `${String(range[1])}T23:59:59.999` : '') : to.value
    const result = await getSchedulerApi().runs({
      jobNId: header ? String(request.filters.jobNId ?? '') : jobNId.value,
      status: header ? String(request.filters.status ?? '') : status.value,
      source: header ? String(request.filters.source ?? '') : source.value,
      from: start ? new Date(start).toISOString() : undefined,
      to: end ? new Date(end).toISOString() : undefined,
      page: request.pageIndex,
      pageSize: request.pageSize,
      sortBy: request.sort?.field,
      sortOrder: request.sort?.order,
    })
    if (current === sequence) {
      rows.value = result.items
      total.value = Number(result.totalCount)
      pageIndex.value = result.page
      pageSize.value = result.pageSize
      error.value = ''
    }
    return {
      items: result.items,
      total: Number(result.totalCount),
      pageIndex: result.page,
      pageSize: result.pageSize,
    }
  } catch (cause) {
    if (current === sequence) {
      rows.value = []
      error.value = message(cause)
    }
    throw cause
  } finally {
    if (current === sequence) loading.value = false
  }
}
async function reload(): Promise<void> {
  try {
    if (tableRef.value) {
      tableRef.value.currentPage = 1
      await tableRef.value.reload()
      return
    }
    await loadTable({
      pageIndex: 1,
      pageSize: pageSize.value,
      queryMode: tableQueryMode.value,
      filters: {},
      columns: [],
    })
  } catch {
    /* visible alert */
  }
}
async function showDetail(nId: string): Promise<void> {
  const current = ++detailSequence
  detail.value = null
  error.value = ''
  detailOpen.value = true
  try {
    const result = await getSchedulerApi().run(nId)
    if (current === detailSequence && detailOpen.value) detail.value = result
  } catch (cause) {
    if (current === detailSequence) error.value = message(cause)
  }
}
async function act(row: SchedulerRun, action: 'cancel' | 'retry' | 'resolve'): Promise<void> {
  if (busy.value) return
  busy.value = true
  error.value = ''
  try {
    const fresh = await getSchedulerApi().run(row.nId)
    if (action === 'resolve') {
      if (!fresh.run.actions.canResolve)
        throw new Error(text('运行已不再是Unknown，请刷新。', 'Run is no longer Unknown. Refresh.'))
      detail.value = fresh
      decision.value = 'ConfirmedNoEffect'
      reason.value = ''
      evidence.value = ''
      stopped.value = false
      resolveOpen.value = true
      return
    }
    await ElMessageBox.confirm(
      action === 'retry'
        ? text(
            '确认安全重放？会沿用业务OperationKey。',
            'Safely replay using the same operation key?',
          )
        : text(
            '请求合作取消？执行器确认停止后才会结束。',
            'Request cooperative cancellation? Completion requires the handler to stop.',
          ),
      text('确认运行操作', 'Confirm run action'),
    )
    if (action === 'cancel') await getSchedulerApi().cancel(row.nId, fresh.run.stateVersion)
    else {
      const fingerprint = JSON.stringify([row.nId, fresh.run.stateVersion])
      const key = retryKeys.get(fingerprint) ?? crypto.randomUUID()
      retryKeys.set(fingerprint, key)
      await getSchedulerApi().retry(row.nId, fresh.run.stateVersion, key)
      retryKeys.delete(fingerprint)
    }
    await reload()
  } catch (cause) {
    if (cause !== 'cancel' && cause !== 'close') error.value = message(cause)
  } finally {
    busy.value = false
  }
}
async function resolve(): Promise<void> {
  const current = detail.value
  if (
    !current ||
    busy.value ||
    !stopped.value ||
    !auth.hasPermission(PERMISSIONS.schedulerRunResolve)
  )
    return
  if (reason.value.trim().length < 10 || !evidence.value.trim()) {
    error.value = text(
      '核对原因至少10字，并填写旧执行已停止的证据引用。',
      'Enter a reason of at least ten characters and evidence that the old execution stopped.',
    )
    return
  }
  busy.value = true
  try {
    await getSchedulerApi().resolve(current.run.nId, {
      stateVersion: current.run.stateVersion,
      decision: decision.value,
      reason: reason.value,
      evidenceReference: evidence.value,
    })
    resolveOpen.value = false
    await reload()
  } catch (cause) {
    error.value = message(cause)
  } finally {
    busy.value = false
  }
}
const time = (value: string | null): string =>
  value ? new Date(value).toLocaleString(locale.locale) : '—'
onMounted(() => {
  void reload()
  if (typeof route.query.runNId === 'string') void showDetail(route.query.runNId)
})
</script>
<template>
  <AppPage
    class="pf07-page"
    :title="text('运行记录', 'Job runs')"
    :description="
      text(
        'Unknown保留占用；旧执行停止并完成核对后才可释放。',
        'Unknown runs retain their slot until the execution stops and its effect is verified.',
      )
    "
  >
    <AppQueryPanel
      class="pf07-query"
      v-if="tableQueryMode === 'top'"
      :title="
        text('查询（默认近7天，上限31天）', 'Query (last 7 days by default, maximum 31 days)')
      "
      show-actions
      grid
      @submit="reload"
      @reset="resetQuery"
    >
      <el-input
        v-model="jobNId"
        :placeholder="text('任务标识', 'Job identifier')"
        :aria-label="text('任务标识', 'Job identifier')"
        clearable
        @keyup.enter="reload"
      /><el-select
        v-model="status"
        clearable
        :placeholder="text('状态', 'State')"
        :aria-label="text('状态', 'State')"
        ><el-option
          v-for="state in runStates"
          :key="state"
          :label="state"
          :value="state" /></el-select
      ><el-select
        v-model="source"
        clearable
        :placeholder="text('来源', 'Source')"
        :aria-label="text('来源', 'Source')"
        ><el-option v-for="item in runSources" :key="item" :label="item" :value="item" /></el-select
      ><el-input
        v-model="from"
        type="datetime-local"
        :aria-label="text('起始时间', 'Start time')"
      /><el-input v-model="to" type="datetime-local" :aria-label="text('结束时间', 'End time')" />
    </AppQueryPanel>
    <p v-if="error" role="alert">{{ error }}</p>
    <AppDataTable
      ref="tableRef"
      table-key="scheduler-runs"
      route-key="pc-scheduler-runs"
      row-key="nId"
      :rows="rows"
      :columns="columns"
      :total="total"
      :initial-page-index="pageIndex"
      :page-size="pageSize"
      :loading="loading"
      :loader="loadTable"
      :query-mode="tableQueryMode"
      :toolbar-labels="true"
      :action-column-width="160"
      @query-mode-change="onQueryModeChange"
      ><template #actions="{ row }"
        ><div class="row-actions">
          <el-button link type="primary" :disabled="busy" @click="showDetail(row.nId)">{{
            text('详情', 'Details')
          }}</el-button
          ><el-dropdown
            v-if="
              auth.hasPermission(PERMISSIONS.schedulerRunCancel) ||
              auth.hasPermission(PERMISSIONS.schedulerRunRetry) ||
              auth.hasPermission(PERMISSIONS.schedulerRunResolve)
            "
            trigger="click"
            @command="act(row, $event)"
          >
            <el-button link type="primary" :disabled="busy">{{ text('更多', 'More') }}</el-button>
            <template #dropdown
              ><el-dropdown-menu
                ><el-dropdown-item
                  v-if="auth.hasPermission(PERMISSIONS.schedulerRunCancel)"
                  :disabled="!row.actions.canCancel || busy"
                  command="cancel"
                  >{{ text('取消', 'Cancel') }}</el-dropdown-item
                >
                <el-dropdown-item
                  v-if="auth.hasPermission(PERMISSIONS.schedulerRunRetry)"
                  :disabled="!row.actions.canRetry || busy"
                  command="retry"
                  >{{ text('手工重试', 'Retry') }}</el-dropdown-item
                >
                <el-dropdown-item
                  v-if="auth.hasPermission(PERMISSIONS.schedulerRunResolve)"
                  :disabled="!row.actions.canResolve || busy"
                  command="resolve"
                  >{{ text('核对结果', 'Verify effect') }}</el-dropdown-item
                >
              </el-dropdown-menu></template
            >
          </el-dropdown>
        </div></template
      ><template #cell-acceptedOn="{ row }">{{ time(row.acceptedOn) }}</template
      ><template #cell-startedOn="{ row }">{{ time(row.startedOn) }}</template
      ><template #cell-completedOn="{ row }">{{ time(row.completedOn) }}</template></AppDataTable
    >
  </AppPage>
  <AppFormDrawer
    v-model="detailOpen"
    :title="text('运行详情', 'Run details')"
    size="wide"
    hide-footer
    ><template v-if="detail"
      ><el-descriptions :column="1" border
        ><el-descriptions-item
          v-for="(value, key) in {
            nId: detail.run.nId,
            jobNId: detail.run.jobNId,
            status: detail.run.status,
            source: detail.run.source,
            handler: detail.handlerKey,
            operationKey: detail.operationKey,
            traceId: detail.run.traceId,
            resultCode: detail.run.resultCode,
            message: detail.run.resultSummary?.message,
          }"
          :key="key"
          :label="detailLabels[key] ?? key"
          >{{ value ?? '—' }}</el-descriptions-item
        ></el-descriptions
      >
      <h3>{{ text('冻结参数', 'Parameter snapshot') }}</h3>
      <pre>{{ JSON.stringify(detail.parameters, null, 2) }}</pre>
      <h3>{{ text('实际尝试历史', 'Actual attempts') }}</h3>
      <p v-if="detail.attemptHistory.availability !== 'Available'">
        {{ text('原生历史不可用', 'Native history unavailable') }} ·
        {{ detail.attemptHistory.reasonCode }}
      </p>
      <el-timeline v-else
        ><el-timeline-item
          v-for="attempt in detail.attemptHistory.items"
          :key="attempt.number"
          :timestamp="time(attempt.startedOn)"
          >#{{ attempt.number }} · {{ attempt.status }} · {{ attempt.resultCode ?? '—' }} ·
          {{ time(attempt.completedOn) }}
          <p>{{ attempt.errorSummary }}</p></el-timeline-item
        ></el-timeline
      ><template v-if="detail.resolution"
        ><h3>{{ text('核对审计', 'Verification audit') }}</h3>
        <p>{{ detail.resolution.decision }} · {{ detail.resolution.reason }}</p>
        <p>
          {{ detail.resolution.evidenceReference }} · {{ detail.resolution.actorUserNId }} ·
          {{ detail.resolution.traceId }}
        </p></template
      ></template
    >
    <p v-if="error" role="alert">{{ error }}</p></AppFormDrawer
  >
  <AppFormDrawer
    v-model="resolveOpen"
    :title="text('核对未知结果', 'Verify unknown effect')"
    :busy="busy"
    @submit="resolve"
    ><el-form label-position="top" :disabled="busy"
      ><el-form-item :label="text('结论', 'Decision')"
        ><el-select v-model="decision"
          ><el-option
            v-for="item in ['ConfirmedSucceeded', 'ConfirmedFailed', 'ConfirmedNoEffect']"
            :key="item"
            :label="item"
            :value="item" /></el-select></el-form-item
      ><el-form-item :label="text('核对原因（至少10字）', 'Reason (at least ten characters)')"
        ><el-input v-model="reason" type="textarea" maxlength="1000" /></el-form-item
      ><el-form-item :label="text('旧执行停止的证据引用', 'Evidence of stopped execution')"
        ><el-input v-model="evidence" maxlength="256" /></el-form-item
      ><el-checkbox v-model="stopped">{{
        text(
          '已核实旧执行停止及业务副作用',
          'I verified the old execution stopped and its business effect',
        )
      }}</el-checkbox></el-form
    >
    <p v-if="error" role="alert">{{ error }}</p></AppFormDrawer
  >
</template>
<style scoped>
.pf07-page :deep(.vxe-table) {
  --vxe-ui-layout-background-color: var(--el-bg-color);
  --vxe-ui-table-header-background-color: var(--el-fill-color-light);
  --vxe-ui-table-footer-background-color: var(--el-bg-color);
  --vxe-ui-font-color: var(--el-text-color-primary);
  --vxe-ui-table-header-font-color: var(--el-text-color-primary);
  --vxe-ui-table-border-color: var(--el-border-color);
  --vxe-ui-table-row-hover-background-color: var(--el-fill-color-light);
  --vxe-ui-table-row-radio-checked-background-color: var(--el-color-primary-light-9);
  --vxe-ui-table-row-hover-radio-checked-background-color: var(--el-color-primary-light-8);
}

.row-actions {
  display: inline-flex;
  align-items: center;
  gap: var(--ip-space-1);
  white-space: nowrap;
}
.pf07-query :deep(.app-query-panel__body > .el-input) {
  flex: 0 1 240px;
  width: 240px;
  max-width: 100%;
}
.pf07-query :deep(.app-query-panel__body > .el-select) {
  flex: 0 1 160px;
  width: 160px;
  max-width: 100%;
}
[role='alert'] {
  color: var(--el-color-danger);
}
pre {
  white-space: pre-wrap;
  overflow-wrap: anywhere;
}
</style>
