<script setup lang="ts">
/**
 * 登录审计页(TASK-ID-012,§16.3):分页过滤查看登录审计。
 * 安全约束(§20):仅展示 IP/UserAgent 哈希摘要与登录名快照,不含原始 IP/UA。
 */
import { onMounted, reactive, ref } from 'vue'

import type { LoginAuditItemDto } from '@/api/identity/management'
import { getManagementApi } from '@/api/identity/managementRegistry'

import { formatTime, reportManagementError } from './shared'

const management = getManagementApi()

const loading = ref(false)
const rows = ref<LoginAuditItemDto[]>([])
const total = ref(0)
const query = reactive({ userNId: '', success: '' })
const pageIndex = ref(1)
const pageSize = ref(20)

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

/** 摘要缩写展示(前 12 位),完整哈希仍可检索但页面上避免全量刷屏。 */
function shortHash(value: string): string {
  return value.length > 12 ? `${value.slice(0, 12)}…` : value
}

onMounted(() => {
  void loadAudits()
})
</script>

<template>
  <section class="audits-page">
    <div class="audits-page__toolbar">
      <el-input
        v-model="query.userNId"
        placeholder="用户业务标识"
        clearable
        class="audits-page__filter"
        @keyup.enter="search"
      />
      <el-select
        v-model="query.success"
        placeholder="结果"
        clearable
        class="audits-page__filter audits-page__filter--status"
      >
        <el-option label="成功" value="success" />
        <el-option label="失败" value="failed" />
      </el-select>
      <el-button type="primary" @click="search">查询</el-button>
      <el-button @click="resetQuery">重置</el-button>
    </div>

    <el-table :data="rows" v-loading="loading" row-key="traceId" border stripe>
      <el-table-column label="时间" width="175">
        <template #default="{ row }">{{ formatTime(row.occurredOn) }}</template>
      </el-table-column>
      <el-table-column prop="loginNameSnapshot" label="登录名" min-width="140" />
      <el-table-column label="结果" width="90" align="center">
        <template #default="{ row }">
          <el-tag :type="row.success ? 'success' : 'danger'" effect="light">
            {{ row.success ? '成功' : '失败' }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column label="失败原因" min-width="140">
        <template #default="{ row }">{{ row.failureCode ?? '—' }}</template>
      </el-table-column>
      <el-table-column label="IP(哈希)" min-width="150">
        <template #default="{ row }">
          <span class="audits-page__hash" :title="row.ipAddressHash">{{
            shortHash(row.ipAddressHash)
          }}</span>
        </template>
      </el-table-column>
      <el-table-column label="UA(哈希)" min-width="160">
        <template #default="{ row }">
          <span class="audits-page__hash" :title="row.userAgentHash">{{
            shortHash(row.userAgentHash)
          }}</span>
        </template>
      </el-table-column>
      <el-table-column label="TraceId" min-width="200">
        <template #default="{ row }">
          <span class="audits-page__hash" :title="row.traceId">{{ shortHash(row.traceId) }}</span>
        </template>
      </el-table-column>
    </el-table>

    <el-pagination
      class="audits-page__pagination"
      layout="total, sizes, prev, pager, next, jumper"
      :total="total"
      :page-size="pageSize"
      :page-sizes="[10, 20, 50, 100]"
      :current-page="pageIndex"
      @current-change="
        (page: number) => {
          pageIndex = page
          void loadAudits()
        }
      "
      @size-change="
        (size: number) => {
          pageSize = size
          pageIndex = 1
          void loadAudits()
        }
      "
    />

    <p class="audits-page__hint">出于安全考虑,仅展示 IP / User-Agent 哈希摘要,不存储原始值。</p>
  </section>
</template>

<style scoped>
.audits-page {
  display: flex;
  flex-direction: column;
  gap: var(--ip-space-4);
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
