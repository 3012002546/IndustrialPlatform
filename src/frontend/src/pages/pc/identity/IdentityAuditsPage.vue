<script setup lang="ts">
/**
 * 登录审计页(TASK-ID-012,§16.3):分页过滤查看登录审计。
 * 安全约束(§20):仅展示 IP/UserAgent 哈希摘要与登录名快照,不含原始 IP/UA。
 */
import { computed, onMounted, reactive, ref } from 'vue'

import type { LoginAuditItemDto } from '@/api/identity/management'
import { getManagementApi } from '@/api/identity/managementRegistry'
import AppPage from '@/components/base/AppPage.vue'
import AppDataTable from '@/components/management/AppDataTable.vue'
import AppQueryPanel from '@/components/management/AppQueryPanel.vue'
import type {
  AppDataTableColumn,
  AppDataTableExportRequest,
  AppDataTableQueryMode,
  AppDataTableRequest,
} from '@/components/management/AppDataTable'

import { downloadBlob, formatTime, reportManagementError } from './shared'
import { localeMessages } from '@/localization/i18n'
import { usePlatformLocale } from '@/localization/localeContext'

const management = getManagementApi()
const locale = usePlatformLocale()
const copy = computed(() => localeMessages[locale.value].identity.management.audits)
const commonCopy = computed(() => localeMessages[locale.value].identity.management.common)

const loading = ref(false)
const rows = ref<LoginAuditItemDto[]>([])
const total = ref(0)
const query = reactive({ userNId: '', success: '' })
const pageIndex = ref(1)
const pageSize = ref(25)
const tableQueryMode = ref<AppDataTableQueryMode>('top')

const auditColumns = computed<readonly AppDataTableColumn[]>(() => [
  { field: 'occurredOn', title: copy.value.time, width: 175, sortable: true, filter: { kind: 'date-range' as const } },
  { field: 'loginNameSnapshot', title: copy.value.loginName, minWidth: 140, filter: { kind: 'text' as const } },
  {
    field: 'success',
    title: copy.value.result,
    width: 90,
    filter: {
      kind: 'select' as const,
      options: [
        { label: copy.value.success, value: true },
        { label: copy.value.failed, value: false },
      ],
    },
  },
  { field: 'failureCode', title: copy.value.failureCode, minWidth: 140, filter: { kind: 'text' as const } },
  { field: 'ipAddressHash', title: copy.value.ipHash, minWidth: 150, filter: { kind: 'text' as const } },
  { field: 'userAgentHash', title: copy.value.uaHash, minWidth: 160, filter: { kind: 'text' as const } },
  { field: 'traceId', title: copy.value.traceId, minWidth: 200, filter: { kind: 'text' as const } },
])

async function loadAudits(): Promise<void> {
  loading.value = true
  try {
    const result = await management.listLoginAudits({
      userNId: query.userNId.trim() || undefined,
      success: query.success === '' ? undefined : query.success === 'success',
      pageIndex: pageIndex.value,
      pageSize: pageSize.value,
    })
    rows.value = result.items
    total.value = result.total
  } catch (error) {
    reportManagementError(error, '加载登录审计失败')
  } finally {
    loading.value = false
  }
}

function search(): void {
  pageIndex.value = 1
  void loadAudits()
}

function resetQuery(): void {
  query.userNId = ''
  query.success = ''
  pageIndex.value = 1
  void loadAudits()
}

function onTableQuery(request: AppDataTableRequest): void {
  pageIndex.value = request.pageIndex
  pageSize.value = request.pageSize
}

function onTableQueryModeChange(mode: AppDataTableQueryMode): void {
  tableQueryMode.value = mode
  if (mode === 'header') {
    query.userNId = ''
    query.success = ''
  }
  pageIndex.value = 1
}

async function loadAuditsTable(request: AppDataTableRequest) {
  const filters = request.queryMode === 'top' ? { ...query, ...request.filters } : request.filters
  const success = filters.success
  const result = await management.listLoginAudits({
    keyword: String(filters.keyword ?? '').trim() || undefined,
    userNId: String(filters.userNId ?? '').trim() || undefined,
    loginNameSnapshot: String(filters.loginNameSnapshot ?? '').trim() || undefined,
    failureCode: String(filters.failureCode ?? '').trim() || undefined,
    ipAddressHash: String(filters.ipAddressHash ?? '').trim() || undefined,
    userAgentHash: String(filters.userAgentHash ?? '').trim() || undefined,
    traceId: String(filters.traceId ?? '').trim() || undefined,
    occurredFrom: Array.isArray(filters.occurredOn) ? String(filters.occurredOn[0] ?? '') || undefined : undefined,
    occurredTo: Array.isArray(filters.occurredOn) ? String(filters.occurredOn[1] ?? '') || undefined : undefined,
    success:
      success === '' || success === undefined ? undefined : success === true || success === 'true',
    pageIndex: request.pageIndex,
    pageSize: request.pageSize,
    sortField: request.sort?.field,
    sortOrder: request.sort?.order,
  })
  rows.value = result.items
  total.value = result.total
  return result
}

async function exportAudits(request: AppDataTableExportRequest): Promise<void> {
  if (management.exportLoginAudits === undefined) return
  const filters = request.queryMode === 'top' ? { ...query, ...request.filters } : request.filters
  const success = filters.success
  const blob = await management.exportLoginAudits({
    keyword: String(filters.keyword ?? '').trim() || undefined,
    userNId: String(filters.userNId ?? '').trim() || undefined,
    loginNameSnapshot: String(filters.loginNameSnapshot ?? '').trim() || undefined,
    failureCode: String(filters.failureCode ?? '').trim() || undefined,
    ipAddressHash: String(filters.ipAddressHash ?? '').trim() || undefined,
    userAgentHash: String(filters.userAgentHash ?? '').trim() || undefined,
    traceId: String(filters.traceId ?? '').trim() || undefined,
    occurredFrom: Array.isArray(filters.occurredOn) ? String(filters.occurredOn[0] ?? '') || undefined : undefined,
    occurredTo: Array.isArray(filters.occurredOn) ? String(filters.occurredOn[1] ?? '') || undefined : undefined,
    success:
      success === '' || success === undefined ? undefined : success === true || success === 'true',
    quantity: request.quantity,
    columns: request.columns,
    sortField: request.sort?.field,
    sortOrder: request.sort?.order,
  })
  downloadBlob(blob, `${request.filename}.xlsx`)
}

/** 摘要缩写展示(前 12 位),完整哈希仍可检索但页面上避免全量刷屏。 */
function shortHash(value: string): string {
  return value.length > 12 ? `${value.slice(0, 12)}…` : value
}

onMounted(() => {
  void loadAudits()
})
</script>

<template>
  <AppPage
    class="audits-page"
    data-testid="identity-audits-page"
    :title="copy.title"
    :description="copy.description"
  >
    <template #breadcrumb>
      <nav :aria-label="commonCopy.pagePath">{{ copy.breadcrumb }}</nav>
    </template>
    <template #heading-meta>
      <span class="audits-page__count">{{ total }} {{ copy.countSuffix }}</span>
    </template>

    <AppQueryPanel
      v-if="tableQueryMode === 'top'"
      class="audits-page__query-panel"
      :title="commonCopy.queryTitle"
      :show-actions="true"
      :submit-label="commonCopy.search"
      :reset-label="commonCopy.reset"
      :grid="true"
      @submit="search"
      @reset="resetQuery"
    >
        <el-input
          v-model="query.userNId"
          :placeholder="copy.userNId"
          :aria-label="copy.userNId"
          clearable
          class="audits-page__filter"
          @keyup.enter="search"
        />
        <el-select
          v-model="query.success"
          :placeholder="copy.result"
          :aria-label="copy.result"
          clearable
          class="audits-page__filter audits-page__filter--status"
        >
          <el-option :label="copy.success" value="success" />
          <el-option :label="copy.failed" value="failed" />
        </el-select>
    </AppQueryPanel>

    <AppDataTable
      table-key="identity-audits"
      route-key="identity-audits"
      row-key="traceId"
      :rows="rows"
      :total="total"
      :loading="loading"
      :columns="auditColumns"
      :page-size="pageSize"
      :loader="loadAuditsTable"
      :exporter="exportAudits"
      @query-mode-change="onTableQueryModeChange"
      @query-change="onTableQuery"
    >
      <template #cell-occurredOn="{ row }">{{ formatTime(row.occurredOn) }}</template>
      <template #cell-success="{ row }">
        <el-tag :type="row.success ? 'success' : 'danger'" effect="light">
          {{ row.success ? '成功' : '失败' }}
        </el-tag>
      </template>
      <template #cell-failureCode="{ row }">{{ row.failureCode ?? '—' }}</template>
      <template #cell-ipAddressHash="{ row }">
        <span class="audits-page__hash" :title="row.ipAddressHash">{{
          shortHash(row.ipAddressHash)
        }}</span>
      </template>
      <template #cell-userAgentHash="{ row }">
        <span class="audits-page__hash" :title="row.userAgentHash">{{
          shortHash(row.userAgentHash)
        }}</span>
      </template>
      <template #cell-traceId="{ row }">
        <span class="audits-page__hash" :title="row.traceId">{{ shortHash(row.traceId) }}</span>
      </template>
    </AppDataTable>

    <p class="audits-page__hint">{{ copy.hint }}</p>
  </AppPage>
</template>

<style scoped>
.audits-page {
  display: flex;
  flex-direction: column;
  gap: 0;
  overflow: hidden;
  background: var(--ip-color-bg-container);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-lg);
  min-width: 0;
}

.audits-page :deep(.app-page__header) {
  padding: 18px 20px 17px;
  border-bottom: 1px solid var(--ip-color-border);
}

.audits-page :deep(.app-page__body) {
  display: flex;
  flex-direction: column;
  min-width: 0;
  min-height: 0;
}

.audits-page :deep(.app-data-table) {
  flex: 1 1 auto;
  min-height: 0;
}

.audits-page :deep(.app-data-table__card) {
  display: flex;
  min-height: 0;
  flex: 1 1 auto;
  flex-direction: column;
}

.audits-page :deep(.app-query-panel) {
  gap: 0;
  padding: 14px 20px 16px;
  border-bottom: 1px solid var(--ip-color-border);
}

.audits-page :deep(.app-query-panel__header) {
  margin-bottom: var(--ip-space-3);
}

.audits-page :deep(.app-query-panel__body) {
  gap: 12px;
}

.audits-page__count {
  color: var(--ip-color-text-secondary);
  font-size: var(--ip-font-size-sm);
}

.audits-page__toolbar {
  display: flex;
  flex-wrap: wrap;
  gap: var(--ip-space-3);
  align-items: center;
}

.audits-page__filter {
  width: 200px;
}

.audits-page__filter--status {
  width: 120px;
}

.audits-page__pagination {
  justify-content: flex-end;
}

.audits-page__hash {
  font-family: var(--ip-font-mono);
  font-size: var(--ip-font-size-xs);
  color: var(--ip-color-text-secondary);
}

.audits-page__hint {
  margin: 0;
  font-size: var(--ip-font-size-xs);
  color: var(--ip-color-text-tertiary);
}
</style>
