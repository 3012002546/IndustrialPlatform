<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import AppPage from '@/components/base/AppPage.vue'
import AppQueryPanel from '@/components/management/AppQueryPanel.vue'
import AppDataTable from '@/components/management/AppDataTable.vue'
import AppFormDrawer from '@/components/management/AppFormDrawer.vue'
import type {
  AppDataTableColumn,
  AppDataTableQueryMode,
  AppDataTableRequest,
} from '@/components/management/AppDataTable'
import {
  getSchedulerApi,
  type SchedulerJob,
  type SchedulerHandler,
  type SchedulePreview,
} from '@/api/systemData/schedulerApi'
import { PERMISSIONS } from '@/permissions'
import { useAuthStore } from '@/stores/authStore'
import { useLocalizationStore } from '@/stores/localizationStore'

const auth = useAuthStore(),
  locale = useLocalizationStore(),
  router = useRouter()
const text = (zh: string, en: string): string => (locale.locale === 'zh-CN' ? zh : en)
const pageIndex = ref(1),
  pageSize = ref(25)
const rows = ref<SchedulerJob[]>([]),
  total = ref(0),
  loading = ref(false),
  busy = ref(false),
  error = ref('')
const keyword = ref(''),
  status = ref('')
const tableQueryMode = ref<AppDataTableQueryMode>('top')
const tableRef = ref<{ currentPage: number; reload: () => Promise<void> } | null>(null)
const handlers = ref<SchedulerHandler[]>([]),
  drawer = ref(false),
  editing = ref(false),
  readOnly = ref(false)
const preview = ref<SchedulePreview | null>(null),
  previewBusy = ref(false)
let detailSequence = 0,
  pageSequence = 0
const form = reactive({
  nId: '',
  name: '',
  description: '',
  handlerKey: '',
  parameters: '{"schemaVersion":1}',
  cronExpression: '*/15 * * * *',
  timeZoneId: 'Etc/UTC',
  timeoutSeconds: 300,
  maxAttempts: 1,
  optimisticVersion: '',
  concurrencyVersion: '',
})
const handler = computed(() => handlers.value.find((item) => item.handlerKey === form.handlerKey))
const canManage = computed(() => auth.hasPermission(PERMISSIONS.schedulerTaskManage))
const columns = computed<readonly AppDataTableColumn[]>(() => [
  { field: 'nId', title: text('标识', 'Identifier'), minWidth: 180, filter: false as const },
  { field: 'name', title: text('名称', 'Name'), minWidth: 180, filter: { kind: 'text' } },
  { field: 'handlerKey', title: text('处理器', 'Handler'), minWidth: 180, filter: false as const },
  {
    field: 'status',
    title: text('状态', 'State'),
    width: 110,
    filter: {
      kind: 'select',
      options: ['Enabled', 'Disabled', 'Deleted'].map((value) => ({ label: value, value })),
    },
  },
  { field: 'cronExpression', title: 'Cron', minWidth: 140, filter: false as const },
  { field: 'timeZoneId', title: text('时区', 'Time zone'), minWidth: 160, filter: false as const },
  {
    field: 'nextFireOn',
    sortable: false,
    title: text('下次运行', 'Next run'),
    minWidth: 200,
    filter: false as const,
  },
  {
    field: 'syncState',
    sortable: false,
    title: text('计划同步', 'Plan sync'),
    width: 120,
    filter: false as const,
  },
])
const triggerKeys = new Map<string, string>()
function resetQuery(): void {
  keyword.value = ''
  status.value = ''
  void reload()
}
function onQueryModeChange(mode: AppDataTableQueryMode): void {
  tableQueryMode.value = mode
  if (mode === 'header') {
    keyword.value = ''
    status.value = ''
  }
}
const message = (cause: unknown): string =>
  cause instanceof Error
    ? cause.message
    : text('操作失败，请重试。', 'Operation failed. Please retry.')
async function loadTable(request: AppDataTableRequest) {
  const sequence = ++pageSequence
  loading.value = true
  try {
    const result = await getSchedulerApi().jobs({
      keyword: request.queryMode === 'top' ? keyword.value : String(request.filters.name ?? ''),
      status: request.queryMode === 'top' ? status.value : String(request.filters.status ?? ''),
      page: request.pageIndex,
      pageSize: request.pageSize,
      sortBy: request.sort?.field,
      sortOrder: request.sort?.order,
    })
    if (sequence === pageSequence) {
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
    if (sequence === pageSequence) {
      rows.value = []
      error.value = message(cause)
    }
    throw cause
  } finally {
    if (sequence === pageSequence) loading.value = false
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
async function open(row: SchedulerJob | null, readonly = false): Promise<void> {
  if (busy.value) return
  const sequence = ++detailSequence
  error.value = ''
  preview.value = null
  try {
    handlers.value = await getSchedulerApi().handlers()
    const fresh = row ? await getSchedulerApi().job(row.nId) : null
    if (sequence !== detailSequence) return
    editing.value = fresh !== null
    readOnly.value = readonly || (fresh !== null && fresh.status !== 'Disabled')
    Object.assign(
      form,
      fresh
        ? {
            ...fresh,
            description: fresh.description ?? '',
            parameters: JSON.stringify(fresh.parameters, null, 2),
          }
        : {
            nId: '',
            name: '',
            description: '',
            handlerKey: handlers.value[0]?.handlerKey ?? '',
            parameters: '{"schemaVersion":1}',
            cronExpression: '*/15 * * * *',
            timeZoneId: 'Etc/UTC',
            timeoutSeconds: 300,
            maxAttempts: 1,
            optimisticVersion: '',
            concurrencyVersion: '',
          },
    )
    drawer.value = true
  } catch (cause) {
    if (sequence === detailSequence) error.value = message(cause)
  }
}
async function previewPlan(): Promise<void> {
  if (previewBusy.value) return
  previewBusy.value = true
  error.value = ''
  preview.value = null
  const cron = form.cronExpression,
    zone = form.timeZoneId
  try {
    const result = await getSchedulerApi().preview(cron, zone)
    if (cron === form.cronExpression && zone === form.timeZoneId) preview.value = result
  } catch (cause) {
    error.value = message(cause)
  } finally {
    previewBusy.value = false
  }
}
async function save(): Promise<void> {
  if (busy.value || readOnly.value || !canManage.value) return
  busy.value = true
  error.value = ''
  try {
    const values = {
      name: form.name,
      description: form.description || null,
      parameters: JSON.parse(form.parameters) as unknown,
      cronExpression: form.cronExpression,
      timeZoneId: form.timeZoneId,
      timeoutSeconds: form.timeoutSeconds,
      maxAttempts: form.maxAttempts,
    }
    if (editing.value)
      await getSchedulerApi().update(form.nId, {
        ...values,
        optimisticVersion: form.optimisticVersion,
        concurrencyVersion: form.concurrencyVersion,
      })
    else await getSchedulerApi().create({ ...values, nId: form.nId, handlerKey: form.handlerKey })
    drawer.value = false
    await reload()
  } catch (cause) {
    error.value = message(cause)
  } finally {
    busy.value = false
  }
}
async function act(
  row: SchedulerJob,
  action: 'enable' | 'disable' | 'delete' | 'restore' | 'run',
): Promise<void> {
  if (busy.value) return
  busy.value = true
  error.value = ''
  try {
    if (action === 'delete')
      await ElMessageBox.confirm(
        text(
          '删除此已停用任务？未结束运行会阻止删除。',
          'Delete this disabled job? Unfinished runs block deletion.',
        ),
        text('删除任务', 'Delete job'),
      )
    const fresh = await getSchedulerApi().job(row.nId)
    const version = {
      optimisticVersion: fresh.optimisticVersion,
      concurrencyVersion: fresh.concurrencyVersion,
    }
    if (action === 'run') {
      const fingerprint = JSON.stringify([row.nId, version])
      const key = triggerKeys.get(fingerprint) ?? crypto.randomUUID()
      triggerKeys.set(fingerprint, key)
      const accepted = await getSchedulerApi().trigger(row.nId, version, key)
      triggerKeys.delete(fingerprint)
      ElMessage.success(text(`运行已接收：${accepted.runNId}`, `Run accepted: ${accepted.runNId}`))
      if (auth.hasPermission(PERMISSIONS.schedulerRunView))
        await router.push({
          name: 'pc-scheduler-runs',
          query: { jobNId: row.nId, runNId: accepted.runNId },
        })
    } else await getSchedulerApi().state(row.nId, action, version)
    await reload()
  } catch (cause) {
    if (cause !== 'cancel' && cause !== 'close') error.value = message(cause)
  } finally {
    busy.value = false
  }
}
function onRowCommand(row: SchedulerJob, command: string): void {
  if (command === 'edit') void open(row)
  else if (
    command === 'enable' ||
    command === 'disable' ||
    command === 'delete' ||
    command === 'restore' ||
    command === 'run'
  )
    void act(row, command)
}
onMounted(() => void reload())
</script>

<template>
  <AppPage
    class="pf07-page"
    :title="text('任务管理', 'Scheduled jobs')"
    :description="
      text(
        '注册处理器，配置一个周期计划；修改前先停用。',
        'Configure one recurring schedule per job. Disable before editing.',
      )
    "
  >
    <template #actions
      ><el-button v-if="canManage" :disabled="busy" type="primary" @click="open(null)">{{
        text('新建', 'Create')
      }}</el-button></template
    >
    <AppQueryPanel
      class="pf07-query"
      v-if="tableQueryMode === 'top'"
      grid
      show-actions
      @submit="reload"
      @reset="resetQuery"
    >
      <el-input
        v-model="keyword"
        :placeholder="text('名称关键词', 'Name keyword')"
        :aria-label="text('名称关键词', 'Name keyword')"
        clearable
        @keyup.enter="reload"
      /><el-select
        v-model="status"
        :placeholder="text('状态', 'State')"
        :aria-label="text('状态', 'State')"
        clearable
        ><el-option
          v-for="state in ['Enabled', 'Disabled', 'Deleted']"
          :key="state"
          :label="state"
          :value="state"
      /></el-select>
    </AppQueryPanel>
    <p v-if="error" role="alert">{{ error }}</p>
    <AppDataTable
      ref="tableRef"
      table-key="scheduler-jobs"
      route-key="pc-scheduler-jobs"
      row-key="nId"
      :rows="rows"
      :total="total"
      :initial-page-index="pageIndex"
      :page-size="pageSize"
      :columns="columns"
      :loading="loading"
      :loader="loadTable"
      :query-mode="tableQueryMode"
      :toolbar-labels="true"
      @query-mode-change="onQueryModeChange"
    >
      <template #actions="{ row, availableWidth }"
        ><div class="row-actions">
          <el-button link type="primary" :disabled="busy" @click="open(row, true)">{{
            text('详情', 'Details')
          }}</el-button
          ><el-button
            v-if="canManage && availableWidth >= 180"
            link
            type="primary"
            :disabled="row.status !== 'Disabled' || busy"
            @click="open(row)"
            >{{ text('编辑', 'Edit') }}</el-button
          ><el-dropdown
            v-if="canManage || auth.hasPermission(PERMISSIONS.schedulerRunTrigger)"
            trigger="click"
            @command="onRowCommand(row, $event)"
          >
            <el-button link type="primary" :disabled="busy">{{ text('更多', 'More') }}</el-button>
            <template #dropdown
              ><el-dropdown-menu>
                <el-dropdown-item
                  v-if="canManage && availableWidth < 180"
                  :disabled="row.status !== 'Disabled' || busy"
                  command="edit"
                  >{{ text('编辑', 'Edit') }}</el-dropdown-item
                >
                <el-dropdown-item
                  v-if="canManage && row.status !== 'Deleted'"
                  :disabled="busy"
                  :command="row.status === 'Enabled' ? 'disable' : 'enable'"
                  >{{
                    row.status === 'Enabled' ? text('停用', 'Disable') : text('启用', 'Enable')
                  }}</el-dropdown-item
                >
                <el-dropdown-item
                  v-if="auth.hasPermission(PERMISSIONS.schedulerRunTrigger)"
                  :disabled="row.status !== 'Enabled' || busy"
                  command="run"
                  >{{ text('手工运行', 'Run now') }}</el-dropdown-item
                >
                <el-dropdown-item
                  v-if="canManage"
                  :disabled="
                    row.isSystemOwned || !['Disabled', 'Deleted'].includes(row.status) || busy
                  "
                  :command="row.status === 'Deleted' ? 'restore' : 'delete'"
                  >{{
                    row.status === 'Deleted' ? text('恢复', 'Restore') : text('删除', 'Delete')
                  }}</el-dropdown-item
                >
              </el-dropdown-menu></template
            >
          </el-dropdown>
        </div></template
      >
      <template #cell-nextFireOn="{ row }">{{
        row.nextFireOn ? new Date(row.nextFireOn).toLocaleString(locale.locale) : '—'
      }}</template>
    </AppDataTable>
  </AppPage>
  <AppFormDrawer
    v-model="drawer"
    :title="
      text(
        readOnly ? '任务详情' : editing ? '编辑任务' : '新建任务',
        readOnly ? 'Job details' : editing ? 'Edit job' : 'Create job',
      )
    "
    size="wide"
    :busy="busy"
    :hide-footer="readOnly"
    @submit="save"
  >
    <el-form label-position="top" :disabled="busy || readOnly"
      ><el-form-item :label="text('标识', 'Identifier')"
        ><el-input v-model="form.nId" :disabled="editing" maxlength="128" /></el-form-item
      ><el-form-item :label="text('名称', 'Name')"
        ><el-input v-model="form.name" maxlength="128" /></el-form-item
      ><el-form-item :label="text('描述', 'Description')"
        ><el-input v-model="form.description" maxlength="1000" /></el-form-item
      ><el-form-item :label="text('处理器', 'Handler')"
        ><el-select v-model="form.handlerKey" :disabled="editing"
          ><el-option
            v-for="item in handlers"
            :key="item.handlerKey"
            :value="item.handlerKey"
            :label="`${item.name} (${item.handlerKey})`"
        /></el-select>
        <p>{{ handler?.description }} · {{ handler?.retrySafety }}</p></el-form-item
      ><el-form-item :label="text('参数 JSON', 'Parameters JSON')"
        ><el-input
          v-model="form.parameters"
          type="textarea"
          :rows="5"
          :disabled="handler?.parametersEditable === false"
      /></el-form-item>
      <div class="query">
        <el-form-item label="Cron"><el-input v-model="form.cronExpression" /></el-form-item
        ><el-form-item :label="text('时区', 'Time zone')"
          ><el-input v-model="form.timeZoneId" /></el-form-item
        ><el-form-item :label="text('超时秒数', 'Timeout seconds')"
          ><el-input-number
            v-model="form.timeoutSeconds"
            :min="5"
            :max="handler?.maxTimeoutSeconds ?? 3600" /></el-form-item
        ><el-form-item :label="text('尝试总上限（含首次）', 'Total attempts, including first')"
          ><el-input-number
            v-model="form.maxAttempts"
            :min="1"
            :max="handler?.retrySafety === 'Idempotent' ? 5 : 1"
        /></el-form-item></div
    ></el-form>
    <el-button :loading="previewBusy" @click="previewPlan">{{
      text('预览十次计划', 'Preview ten occurrences')
    }}</el-button>
    <ol v-if="preview">
      <li v-for="item in preview.occurrences" :key="item.utc">
        {{ item.zonedTime }} · UTC {{ item.utc }}
      </li>
    </ol>
    <p v-if="error" role="alert">{{ error }}</p>
  </AppFormDrawer>
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

.query {
  display: grid;
  grid-template-columns: repeat(2, minmax(180px, 1fr));
  gap: var(--ip-space-3);
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
</style>
