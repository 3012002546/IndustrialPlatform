<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { ElDropdown, ElDropdownItem, ElDropdownMenu, ElMessage } from 'element-plus'
import AppDataTable from '@/components/management/AppDataTable.vue'
import AppPage from '@/components/base/AppPage.vue'
import AppQueryPanel from '@/components/management/AppQueryPanel.vue'
import PermissionGate from '@/permissions/PermissionGate.vue'
import { PERMISSIONS } from '@/permissions'
import { getPf04Api } from '@/api/systemData/pf04Registry'
import type { AuditFactDto, AuditLifecycleRequest } from '@/api/systemData/pf04Types'
import { downloadBlob } from '@/components/management/download'
import { useLocalizationStore } from '@/stores/localizationStore'
import { useAuthStore } from '@/stores/authStore'
import type { AppDataTableRequest } from '@/components/management/AppDataTable'

const localization = useLocalizationStore()
const authStore = useAuthStore()
type AuditRow = AuditFactDto & { rowKey: string }
const rows = ref<AuditRow[]>([])
const total = ref(0)
const producer = ref('')
const action = ref('')
const from = ref('')
const until = ref('')
const pageIndex = ref(1)
const pageSize = ref(25)
const detail = ref<AuditFactDto | null>(null)
const detailOpen = ref(false)
const loading = ref(false)
const errorMessage = ref('')
const lifecycleOpen = ref(false)
const lifecycleRow = ref<AuditRow | null>(null)
const lifecycleBusy = ref(false)
const lifecycleError = ref('')
const lifecycleForm = reactive<{ state: AuditLifecycleRequest['state']; retentionUntil: string; legalHold: boolean; legalHoldReason: string }>({ state: 'Archived', retentionUntil: '', legalHold: false, legalHoldReason: '' })
const canRetentionManage = computed(() => authStore.hasPermission(PERMISSIONS.systemDataAuditRetentionManage))
const canReadAudit = computed(() => authStore.hasPermission(PERMISSIONS.systemDataAuditRead))
const title = computed(() => (localization.locale === 'zh-CN' ? '审计查询' : 'Audit query'))
const copy = computed(() => localization.locale === 'zh-CN' ? {
  description: '追加式事实、冲突检测与 CSV 注入防护。', query: '查询范围', producer: '生产者服务', action: '动作',
  export: '导出 CSV', details: '详情', more: '更多', lifecycle: '生命周期', lifecycleTitle: '更新审计生命周期',
  state: '状态', retention: '保留截止', legalHold: '法律保全', reason: '保全原因', cancel: '取消', save: '保存',
  updated: '生命周期已更新。', loadFailed: '审计加载失败，请重试。', updateFailed: '生命周期更新失败，请重试。', retry: '重试',
} : {
  description: 'Append-only facts, conflict detection, and safe CSV export.', query: 'Query scope', producer: 'Producer service', action: 'Action',
  export: 'Export CSV', details: 'Details', more: 'More', lifecycle: 'Lifecycle', lifecycleTitle: 'Update audit lifecycle',
  state: 'State', retention: 'Retention until', legalHold: 'Legal hold', reason: 'Hold reason', cancel: 'Cancel', save: 'Save',
  updated: 'Lifecycle updated.', loadFailed: 'Unable to load audit facts.', updateFailed: 'Unable to update lifecycle.', retry: 'Retry',
})
const columns = computed(() => [
  { field: 'occurredOn', title: localization.locale === 'zh-CN' ? '发生时间' : 'Occurred', minWidth: 180 },
  { field: 'producerServiceKey', title: localization.locale === 'zh-CN' ? '生产者' : 'Producer', minWidth: 160 },
  { field: 'action', title: localization.locale === 'zh-CN' ? '动作' : 'Action', minWidth: 150 },
  { field: 'objectType', title: localization.locale === 'zh-CN' ? '对象类型' : 'Object', minWidth: 150 },
  { field: 'severity', title: localization.locale === 'zh-CN' ? '级别' : 'Severity', width: 110 },
  { field: 'actions', title: localization.locale === 'zh-CN' ? '操作' : 'Actions', width: 120 },
])

async function load(): Promise<void> {
  pageIndex.value = 1
  try { await loadPage(pageIndex.value, pageSize.value) } catch { /* rendered by errorMessage and the table retry */ }
}

async function loadPage(page: number, size: number): Promise<void> {
  if (!canReadAudit.value) { rows.value = []; total.value = 0; return }
  const api = getPf04Api()
  if (api === null) return
  loading.value = true
  try {
    const result = await api.listAudits({ producerServiceKey: producer.value, action: action.value, from: from.value || undefined, until: until.value || undefined, page, pageSize: size })
    rows.value = result.items.map((row) => ({ ...row, rowKey: row.producerServiceKey + ':' + row.auditEventNId }))
    total.value = result.total
    pageIndex.value = result.page
    pageSize.value = result.pageSize
    errorMessage.value = ''
  } catch (error) {
    errorMessage.value = error instanceof Error ? error.message : copy.value.loadFailed
    throw error
  } finally { loading.value = false }
}

async function loadTable(request: AppDataTableRequest): Promise<{ items: AuditRow[]; total: number; pageIndex: number; pageSize: number }> {
  await loadPage(request.pageIndex, request.pageSize)
  return { items: rows.value, total: total.value, pageIndex: pageIndex.value, pageSize: pageSize.value }
}

async function exportCsv(): Promise<void> {
  const api = getPf04Api()
  if (api === null) return
  downloadBlob(await api.exportAudits({ producerServiceKey: producer.value, action: action.value, from: from.value || undefined, until: until.value || undefined }), 'systemdata-audit.csv')
}

async function openDetail(row: AuditRow): Promise<void> {
  const api = getPf04Api()
  if (api === null) return
  detail.value = await api.getAudit(row.producerServiceKey, row.auditEventNId)
  detailOpen.value = true
}

function openLifecycle(row: AuditRow): void {
  if (!canRetentionManage.value) return
  lifecycleRow.value = row
  lifecycleError.value = ''
  Object.assign(lifecycleForm, { state: 'Archived', retentionUntil: '', legalHold: false, legalHoldReason: '' })
  lifecycleOpen.value = true
}

function handleRowAction(row: AuditRow, command: string | number | object): void {
  if (command === 'detail') void openDetail(row)
  if (command === 'lifecycle') openLifecycle(row)
}

defineExpose({ openLifecycle })

async function updateLifecycle(): Promise<void> {
  const api = getPf04Api()
  const row = lifecycleRow.value
  if (api === null || row === null || lifecycleBusy.value || !canRetentionManage.value) return
  lifecycleBusy.value = true
  lifecycleError.value = ''
  try {
    await api.updateAuditLifecycle(row.producerServiceKey, row.auditEventNId, {
      state: lifecycleForm.state,
      ...(lifecycleForm.retentionUntil ? { retentionUntil: new Date(lifecycleForm.retentionUntil).toISOString() } : {}),
      legalHold: lifecycleForm.legalHold,
      ...(lifecycleForm.legalHoldReason ? { legalHoldReason: lifecycleForm.legalHoldReason } : {}),
    })
    lifecycleOpen.value = false
    ElMessage.success(copy.value.updated)
    await load()
  } catch (error) {
    lifecycleError.value = error instanceof Error ? error.message : copy.value.updateFailed
  } finally { lifecycleBusy.value = false }
}

onMounted(() => void load())
</script>

<template>
  <AppPage :title="title" :description="copy.description">
    <template #actions><PermissionGate :permission-n-id="PERMISSIONS.systemDataAuditExport"><el-button @click="exportCsv">{{ copy.export }}</el-button></PermissionGate></template>
    <AppQueryPanel :title="copy.query" show-actions @submit="load" @reset="producer = ''; action = ''; from = ''; until = ''; load()"><div class="pf04-query-grid"><el-input v-model="producer" :placeholder="copy.producer" /><el-input v-model="action" :placeholder="copy.action" /><el-input v-model="from" type="datetime-local" /><el-input v-model="until" type="datetime-local" /></div></AppQueryPanel>
    <p v-if="errorMessage" role="alert" class="pf04-error">{{ errorMessage }} <el-button link type="danger" @click="load">{{ copy.retry }}</el-button></p>
    <AppDataTable table-key="systemdata-audits" route-key="systemdata-audits" row-key="rowKey" :rows="rows" :total="total" :columns="columns" :loading="loading" :initial-page-index="pageIndex" :page-size="pageSize" :loader="loadTable" @query-change="(request) => { pageIndex = request.pageIndex; pageSize = request.pageSize }"><template #cell-objectType="{ row }">{{ row.objectType }}{{ row.objectNId ? ` / ${row.objectNId}` : '' }}</template><template #cell-actions="{ row }"><ElDropdown trigger="click" @command="(command) => handleRowAction(row, command)"><el-button link type="primary">{{ copy.more }}</el-button><template #dropdown><ElDropdownMenu><ElDropdownItem command="detail">{{ copy.details }}</ElDropdownItem><ElDropdownItem v-if="canRetentionManage" command="lifecycle" divided>{{ copy.lifecycle }}</ElDropdownItem></ElDropdownMenu></template></ElDropdown></template></AppDataTable>
  </AppPage>
  <el-dialog v-model="detailOpen" :title="copy.details" width="720px"><pre v-if="detail" class="audit-detail">{{ JSON.stringify(detail, null, 2) }}</pre></el-dialog>
  <el-dialog v-model="lifecycleOpen" :title="copy.lifecycleTitle" width="520px"><el-form label-width="120px"><el-form-item :label="copy.state"><el-select v-model="lifecycleForm.state"><el-option label="Active" value="Active" /><el-option label="Archived" value="Archived" /><el-option label="Deleted" value="Deleted" /></el-select></el-form-item><el-form-item :label="copy.retention"><el-input v-model="lifecycleForm.retentionUntil" type="datetime-local" /></el-form-item><el-form-item :label="copy.legalHold"><el-switch v-model="lifecycleForm.legalHold" /></el-form-item><el-form-item :label="copy.reason"><el-input v-model="lifecycleForm.legalHoldReason" /></el-form-item><p v-if="lifecycleError" role="alert" class="pf04-error">{{ lifecycleError }}</p></el-form><template #footer><el-button @click="lifecycleOpen = false">{{ copy.cancel }}</el-button><el-button type="primary" :loading="lifecycleBusy" @click="updateLifecycle">{{ copy.save }}</el-button></template></el-dialog>
</template>

<style scoped>.pf04-query-grid { display: grid; grid-template-columns: repeat(2, minmax(180px, 1fr)); gap: var(--ip-space-3); max-width: 640px; }</style>
