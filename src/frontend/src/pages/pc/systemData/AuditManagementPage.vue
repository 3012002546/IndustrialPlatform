<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import AppDataTable from '@/components/management/AppDataTable.vue'
import AppPage from '@/components/base/AppPage.vue'
import AppQueryPanel from '@/components/management/AppQueryPanel.vue'
import PermissionGate from '@/permissions/PermissionGate.vue'
import { PERMISSIONS } from '@/permissions'
import { getPf04Api } from '@/api/systemData/pf04Registry'
import type { AuditFactDto } from '@/api/systemData/pf04Types'
import { downloadBlob } from '@/components/management/download'
import { useLocalizationStore } from '@/stores/localizationStore'
import type { AppDataTableRequest } from '@/components/management/AppDataTable'

const localization = useLocalizationStore()
const api = getPf04Api()
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
const title = computed(() => (localization.locale === 'zh-CN' ? '审计查询' : 'Audit query'))
const columns = computed(() => [
  { field: 'occurredOn', title: localization.locale === 'zh-CN' ? '发生时间' : 'Occurred', minWidth: 180 },
  { field: 'producerServiceKey', title: localization.locale === 'zh-CN' ? '生产者' : 'Producer', minWidth: 160 },
  { field: 'action', title: localization.locale === 'zh-CN' ? '动作' : 'Action', minWidth: 150 },
  { field: 'objectType', title: localization.locale === 'zh-CN' ? '对象类型' : 'Object', minWidth: 150 },
  { field: 'severity', title: localization.locale === 'zh-CN' ? '级别' : 'Severity', width: 110 },
  { field: 'actions', title: localization.locale === 'zh-CN' ? '详情' : 'Details', width: 100 },
])
async function load(): Promise<void> { pageIndex.value = 1; await loadPage(pageIndex.value, pageSize.value) }
async function loadPage(page: number, size: number): Promise<void> { if (api === null) return; loading.value = true; try { const result = await api.listAudits({ producerServiceKey: producer.value, action: action.value, from: from.value || undefined, until: until.value || undefined, page, pageSize: size }); rows.value = result.items.map((row) => ({ ...row, rowKey: row.producerServiceKey + ':' + row.auditEventNId })); total.value = result.total; pageIndex.value = result.page; pageSize.value = result.pageSize } finally { loading.value = false } }
async function loadTable(request: AppDataTableRequest): Promise<{ items: AuditRow[]; total: number; pageIndex: number; pageSize: number }> { await loadPage(request.pageIndex, request.pageSize); return { items: rows.value, total: total.value, pageIndex: pageIndex.value, pageSize: pageSize.value } }
async function exportCsv(): Promise<void> { if (api === null) return; downloadBlob(await api.exportAudits({ producerServiceKey: producer.value, action: action.value, from: from.value || undefined, until: until.value || undefined }), 'systemdata-audit.csv') }
async function openDetail(row: AuditRow): Promise<void> { if (api === null) return; detail.value = await api.getAudit(row.producerServiceKey, row.auditEventNId); detailOpen.value = true }
onMounted(() => void load())
</script>

<template>
  <AppPage :title="title" :description="localization.locale === 'zh-CN' ? '追加式事实、冲突检测与 CSV 注入防护。' : 'Append-only facts, conflict detection, and safe CSV export.'">
    <template #actions><PermissionGate :permission-n-id="PERMISSIONS.systemDataAuditExport"><el-button @click="exportCsv">{{ localization.locale === 'zh-CN' ? '导出 CSV' : 'Export CSV' }}</el-button></PermissionGate></template>
    <AppQueryPanel :title="localization.locale === 'zh-CN' ? '查询范围' : 'Query scope'" show-actions @submit="load" @reset="producer = ''; action = ''; from = ''; until = ''; load()"><div class="pf04-query-grid"><el-input v-model="producer" :placeholder="localization.locale === 'zh-CN' ? '生产者服务' : 'Producer service'" /><el-input v-model="action" :placeholder="localization.locale === 'zh-CN' ? '动作' : 'Action'" /><el-input v-model="from" type="datetime-local" /><el-input v-model="until" type="datetime-local" /></div></AppQueryPanel>
    <AppDataTable table-key="systemdata-audits" route-key="systemdata-audits" row-key="rowKey" :rows="rows" :total="total" :columns="columns" :loading="loading" :initial-page-index="pageIndex" :page-size="pageSize" :loader="loadTable" @query-change="(request) => { pageIndex = request.pageIndex; pageSize = request.pageSize }"><template #cell-objectType="{ row }">{{ row.objectType }}{{ row.objectNId ? ` / ${row.objectNId}` : '' }}</template><template #cell-actions="{ row }"><el-button link type="primary" @click="openDetail(row)">详情</el-button></template></AppDataTable>
  </AppPage>
  <el-dialog v-model="detailOpen" title="审计详情" width="720px"><pre v-if="detail" class="audit-detail">{{ JSON.stringify(detail, null, 2) }}</pre></el-dialog>
</template>

<style scoped>.pf04-query-grid { display: grid; grid-template-columns: repeat(2, minmax(180px, 1fr)); gap: var(--ip-space-3); max-width: 640px; }</style>
