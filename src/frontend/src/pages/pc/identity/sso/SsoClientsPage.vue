<script setup lang="ts">
/**
 * 平台 SSO Client 管理页(TASK-ID-015,§26.7):Client 列表/新建/编辑/启用停用/
 * 端点(Redirect/PostLogoutRedirect/Origin)登记、启停与移除。
 * 读操作 identity.sso.view,写操作 identity.sso.manage。
 */
import { ElMessage, ElMessageBox } from 'element-plus'
import type { FormInstance, FormRules } from 'element-plus'
import { computed, onMounted, reactive, ref } from 'vue'

import {
  getSsoManagementApi,
  type SsoClientSummaryDto,
  type SsoEndpointSummaryDto,
} from '@/api/identity/ssoManagement'
import { PERMISSIONS, PermissionGate } from '@/permissions'
import AppPage from '@/components/base/AppPage.vue'
import AppDataTable from '@/components/management/AppDataTable.vue'
import AppFormDrawer from '@/components/management/AppFormDrawer.vue'
import type { AppDataTableExportRequest } from '@/components/management/AppDataTable'
import { localeMessages } from '@/localization/i18n'
import { usePlatformLocale } from '@/localization/localeContext'

import { downloadBlob, formatTime, reportManagementError } from '../shared'

const ssoManagement = getSsoManagementApi()
const locale = usePlatformLocale()
const copy = computed(() => localeMessages[locale.value].identity.management.ssoClients)
const commonCopy = computed(() => localeMessages[locale.value].identity.management.common)

// ---------------------------------------------------------------------------
// 列表
// ---------------------------------------------------------------------------

const loading = ref(false)
const rows = ref<SsoClientSummaryDto[]>([])

const clientColumns = computed(() => [
  { field: 'name', title: copy.value.name, minWidth: 150, filter: { kind: 'text' as const } },
  { field: 'oauthClientId', title: copy.value.clientId, minWidth: 180 },
  { field: 'endpointCount', title: copy.value.endpointCount, width: 90 },
  { field: 'enabled', title: copy.value.status, width: 80 },
  { field: 'createdOn', title: copy.value.createdOn, width: 170, sortable: true },
])

const endpointColumns = computed(() => [
  { field: 'type', title: copy.value.type, width: 180 },
  { field: 'uri', title: copy.value.uri, minWidth: 220 },
  { field: 'enabled', title: copy.value.status, width: 70 },
])

async function exportClients(request: AppDataTableExportRequest): Promise<void> {
  const filters = request.filters
  const blob = await ssoManagement.exportClients({
    name: typeof filters.name === 'string' ? filters.name : undefined,
    quantity: request.quantity,
    columns: request.columns,
    sortField: request.sort?.field,
    sortOrder: request.sort?.order,
  })
  downloadBlob(blob, `${request.filename}.xlsx`)
}

async function exportEndpoints(request: AppDataTableExportRequest): Promise<void> {
  const client = endpointsClient.value
  if (client === null) return
  const blob = await ssoManagement.exportClientEndpoints(client.clientNId, {
    quantity: request.quantity,
    columns: request.columns,
  })
  downloadBlob(blob, `${request.filename}.xlsx`)
}

async function loadClients(): Promise<void> {
  loading.value = true
  try {
    rows.value = await ssoManagement.listClients()
  } catch (error) {
    reportManagementError(error, '加载 Client 列表失败')
  } finally {
    loading.value = false
  }
}

// ---------------------------------------------------------------------------
// 新建 / 编辑
// ---------------------------------------------------------------------------

interface ClientForm {
  name: string
  oauthClientId: string
}

const dialogOpen = ref(false)
const editing = ref<SsoClientSummaryDto | null>(null)
const dialogTitle = computed(() =>
  editing.value === null ? copy.value.createTitle : copy.value.editTitle,
)
const formRef = ref<FormInstance>()
const dialogSaving = ref(false)
const form = reactive<ClientForm>({ name: '', oauthClientId: '' })

function resetForm(): void {
  form.name = ''
  form.oauthClientId = ''
}

function openCreate(): void {
  editing.value = null
  resetForm()
  dialogOpen.value = true
}

function openEdit(row: SsoClientSummaryDto): void {
  editing.value = row
  resetForm()
  form.name = row.name
  form.oauthClientId = row.oauthClientId
  dialogOpen.value = true
}

async function submitDialog(): Promise<void> {
  if (formRef.value === undefined) return
  const valid = await formRef.value.validate().catch(() => false)
  if (!valid) return
  dialogSaving.value = true
  try {
    if (editing.value === null) {
      await ssoManagement.createClient({
        name: form.name.trim(),
        oauthClientId: form.oauthClientId.trim() || undefined,
      })
      ElMessage.success('Client 创建成功')
    } else {
      await ssoManagement.updateClient(editing.value.clientNId, {
        name: form.name.trim(),
        oauthClientId: form.oauthClientId.trim() || undefined,
        expectedOptimisticVersion: editing.value.optimisticVersion,
        expectedConcurrencyVersion: editing.value.concurrencyVersion,
      })
      ElMessage.success('Client 已更新')
    }
    dialogOpen.value = false
    await loadClients()
  } catch (error) {
    reportManagementError(error, '保存 Client 失败')
  } finally {
    dialogSaving.value = false
  }
}

// ---------------------------------------------------------------------------
// 启用 / 停用
// ---------------------------------------------------------------------------

async function toggleEnabled(row: SsoClientSummaryDto): Promise<void> {
  const enabling = !row.enabled
  try {
    await ElMessageBox.confirm(
      enabling
        ? `确定启用 Client「${row.name}」?`
        : `确定停用 Client「${row.name}」?停用后其回调校验立即失效。`,
      `${enabling ? '启用' : '停用'}确认`,
      { type: 'warning', confirmButtonText: enabling ? '启用' : '停用', cancelButtonText: '取消' },
    )
  } catch {
    return // 用户取消
  }
  try {
    await ssoManagement.setClientEnabled(row.clientNId, {
      enabled: enabling,
      expectedOptimisticVersion: row.optimisticVersion,
      expectedConcurrencyVersion: row.concurrencyVersion,
    })
    ElMessage.success(`Client 已${enabling ? '启用' : '停用'}`)
    await loadClients()
  } catch (error) {
    reportManagementError(error, `Client ${enabling ? '启用' : '停用'}失败`)
  }
}

// ---------------------------------------------------------------------------
// 端点管理
// ---------------------------------------------------------------------------

const endpointTypeOptions = computed(() => [
  { label: copy.value.redirect, value: 'Redirect' },
  { label: copy.value.postLogoutRedirect, value: 'PostLogoutRedirect' },
  { label: copy.value.origin, value: 'Origin' },
])

const endpointsDrawerOpen = ref(false)
const endpointsClient = ref<SsoClientSummaryDto | null>(null)
const endpointFormRef = ref<FormInstance>()
const endpointSaving = ref(false)
const endpointForm = reactive({ nId: '', type: 'Redirect', uri: '' })

function openEndpoints(row: SsoClientSummaryDto): void {
  endpointsClient.value = row
  endpointForm.nId = ''
  endpointForm.type = 'Redirect'
  endpointForm.uri = ''
  endpointsDrawerOpen.value = true
}

async function submitEndpoint(): Promise<void> {
  const client = endpointsClient.value
  if (client === null || endpointFormRef.value === undefined) return
  const valid = await endpointFormRef.value.validate().catch(() => false)
  if (!valid) return
  endpointSaving.value = true
  try {
    const updated = await ssoManagement.addClientEndpoint(client.clientNId, {
      nId: endpointForm.nId.trim() || undefined,
      type: endpointForm.type,
      uri: endpointForm.uri.trim(),
      expectedOptimisticVersion: client.optimisticVersion,
      expectedConcurrencyVersion: client.concurrencyVersion,
    })
    ElMessage.success('端点已登记')
    endpointsClient.value = updated
    endpointForm.nId = ''
    endpointForm.type = 'Redirect'
    endpointForm.uri = ''
    await loadClients()
  } catch (error) {
    reportManagementError(error, '登记端点失败')
  } finally {
    endpointSaving.value = false
  }
}

async function toggleEndpointEnabled(endpoint: SsoEndpointSummaryDto): Promise<void> {
  const client = endpointsClient.value
  if (client === null) return
  try {
    const updated = await ssoManagement.setClientEndpointEnabled(
      client.clientNId,
      endpoint.endpointNId,
      {
        enabled: !endpoint.enabled,
        expectedOptimisticVersion: client.optimisticVersion,
        expectedConcurrencyVersion: client.concurrencyVersion,
      },
    )
    ElMessage.success(`端点已${endpoint.enabled ? '停用' : '启用'}`)
    endpointsClient.value = updated
    await loadClients()
  } catch (error) {
    reportManagementError(error, '更新端点状态失败')
  }
}

async function removeEndpoint(endpoint: SsoEndpointSummaryDto): Promise<void> {
  const client = endpointsClient.value
  if (client === null) return
  try {
    await ElMessageBox.confirm(
      `确定移除端点「${endpoint.type} ${endpoint.uri}」?`,
      '移除端点确认',
      { type: 'warning', confirmButtonText: '移除', cancelButtonText: '取消' },
    )
  } catch {
    return
  }
  try {
    await ssoManagement.removeClientEndpoint(
      client.clientNId,
      endpoint.endpointNId,
      client.optimisticVersion,
      client.concurrencyVersion,
    )
    ElMessage.success('端点已移除')
    await loadClients()
    // 重新读取当前 Client 详情(移除端点可能推进版本)。
    const fresh = await ssoManagement.getClient(client.clientNId)
    endpointsClient.value = fresh
  } catch (error) {
    reportManagementError(error, '移除端点失败')
  }
}

// ---------------------------------------------------------------------------
// 校验规则
// ---------------------------------------------------------------------------

const clientRules: FormRules = {
  name: [{ required: true, message: '请输入 Client 名称', trigger: 'blur' }],
  oauthClientId: [
    {
      pattern: /^[a-zA-Z0-9._-]{2,128}$/,
      message: 'OAuth ClientId 仅含字母/数字/._-,长度 2-128',
      trigger: 'blur',
    },
  ],
}

const endpointRules: FormRules = {
  type: [{ required: true, message: '请选择端点类型', trigger: 'change' }],
  uri: [
    { required: true, message: '请输入端点地址', trigger: 'blur' },
    {
      pattern: /^https?:\/\/.+$/,
      message: '端点须为合法 http(s) URL',
      trigger: 'blur',
    },
  ],
}

onMounted(() => {
  void loadClients()
})
</script>

<template>
  <AppPage
    class="sso-clients-page"
    data-testid="identity-sso-clients-page"
    :title="copy.title"
    :description="copy.description"
  >
    <template #breadcrumb>
      <nav :aria-label="commonCopy.pagePath">{{ copy.breadcrumb }}</nav>
    </template>
    <template #heading-meta>
      <span class="sso-clients-page__count">{{ rows.length }} {{ copy.countSuffix }}</span>
    </template>
    <template #actions>
      <PermissionGate :permission-n-id="PERMISSIONS.ssoManage">
        <el-button type="primary" data-testid="sso-client-create" @click="openCreate">
          {{ copy.create }}
        </el-button>
      </PermissionGate>
    </template>

    <AppDataTable
      table-key="identity-sso-clients"
      route-key="identity-sso-clients"
      row-key="clientNId"
      :rows="rows"
      :total="rows.length"
      :loading="loading"
      :columns="clientColumns"
      :exporter="exportClients"
    >
      <template #cell-endpointCount="{ row }">{{ row.endpoints.length }}</template>
      <template #cell-enabled="{ row }">
        <el-tag :type="row.enabled ? 'success' : 'danger'" effect="light">
          {{ row.enabled ? copy.enabled : copy.disabled }}
        </el-tag>
      </template>
      <template #cell-createdOn="{ row }">{{ formatTime(row.createdOn) }}</template>
      <template #actions="{ row }">
        <PermissionGate :permission-n-id="PERMISSIONS.ssoManage">
          <el-button link type="primary" @click="openEdit(row)">{{ copy.edit }}</el-button>
          <el-button link type="primary" @click="openEndpoints(row)">{{ copy.endpoints }}</el-button>
          <el-button link :type="row.enabled ? 'danger' : 'success'" @click="toggleEnabled(row)">
            {{ row.enabled ? copy.disabled : copy.enabled }}
          </el-button>
        </PermissionGate>
      </template>
    </AppDataTable>

    <!-- 新建 / 编辑 -->
    <AppFormDrawer
      v-model="dialogOpen"
      :title="dialogTitle"
      :busy="dialogSaving"
      size="medium"
      @cancel="resetForm"
      @submit="submitDialog"
    >
      <el-form ref="formRef" :model="form" :rules="clientRules" label-width="120px">
        <el-form-item :label="copy.name" prop="name">
          <el-input v-model="form.name" :placeholder="copy.name" />
        </el-form-item>
        <el-form-item label="OAuth ClientId" prop="oauthClientId">
          <el-input v-model="form.oauthClientId" :placeholder="copy.clientId" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogOpen = false">{{ commonCopy.cancel }}</el-button>
        <el-button type="primary" :loading="dialogSaving" @click="submitDialog">{{ commonCopy.save }}</el-button>
      </template>
    </AppFormDrawer>

    <!-- 端点管理 -->
    <AppFormDrawer
      v-model="endpointsDrawerOpen"
      :title="copy.endpointTitle"
      size="wide"
      :allow-mode-switch="false"
    >
      <el-form
        ref="endpointFormRef"
        :model="endpointForm"
        :rules="endpointRules"
        label-width="110px"
      >
        <el-form-item :label="copy.type" prop="type">
          <el-select v-model="endpointForm.type" class="sso-clients-page__full">
            <el-option
              v-for="option in endpointTypeOptions"
              :key="option.value"
              :label="option.label"
              :value="option.value"
            />
          </el-select>
        </el-form-item>
        <el-form-item :label="copy.uri" prop="uri">
          <el-input v-model="endpointForm.uri" placeholder="https://…" />
        </el-form-item>
        <el-form-item :label="commonCopy.businessId">
          <el-input v-model="endpointForm.nId" :placeholder="commonCopy.optional" />
        </el-form-item>
        <el-form-item>
          <PermissionGate :permission-n-id="PERMISSIONS.ssoManage">
            <el-button type="primary" :loading="endpointSaving" @click="submitEndpoint">
              {{ copy.register }}
            </el-button>
          </PermissionGate>
        </el-form-item>
      </el-form>

      <h3 class="sso-clients-page__section-title">{{ copy.endpoints }}</h3>
      <AppDataTable
        table-key="identity-sso-client-endpoints"
        route-key="identity-sso-client-endpoints"
        row-key="endpointNId"
        :rows="endpointsClient?.endpoints ?? []"
        :total="endpointsClient?.endpoints.length ?? 0"
        :columns="endpointColumns"
        :exporter="exportEndpoints"
      >
        <template #cell-type="{ row }">
          <el-tag effect="light">
            {{
              row.type === 'Redirect'
                ? copy.redirect
                : row.type === 'PostLogoutRedirect'
                  ? copy.postLogoutRedirect
                  : row.type === 'Origin'
                    ? copy.origin
                    : row.type
            }}
          </el-tag>
        </template>
        <template #cell-enabled="{ row }">
          <el-tag :type="row.enabled ? 'success' : 'info'" effect="light">
            {{ row.enabled ? copy.enabled : copy.disabled }}
          </el-tag>
        </template>
        <template #actions="{ row }">
          <PermissionGate :permission-n-id="PERMISSIONS.ssoManage">
            <el-button
              link
              :type="row.enabled ? 'danger' : 'success'"
              @click="toggleEndpointEnabled(row)"
            >
              {{ row.enabled ? copy.disabled : copy.enabled }}
            </el-button>
            <el-button link type="danger" @click="removeEndpoint(row)">{{ copy.remove }}</el-button>
          </PermissionGate>
        </template>
      </AppDataTable>
      <template #footer>
        <el-button @click="endpointsDrawerOpen = false">{{ commonCopy.cancel }}</el-button>
      </template>
    </AppFormDrawer>
  </AppPage>
</template>

<style scoped>
.sso-clients-page {
  display: flex;
  flex-direction: column;
  gap: 0;
  overflow: hidden;
  background: var(--ip-color-bg-container);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-lg);
  min-width: 0;
}

.sso-clients-page :deep(.app-page__header) {
  padding: 18px 20px 17px;
  border-bottom: 1px solid var(--ip-color-border);
}

.sso-clients-page :deep(.app-page__body) {
  display: flex;
  flex-direction: column;
  min-width: 0;
  min-height: 0;
}

.sso-clients-page :deep(.app-data-table) {
  flex: 1 1 auto;
  min-height: 0;
}

.sso-clients-page :deep(.app-data-table__card) {
  display: flex;
  min-height: 0;
  flex: 1 1 auto;
  flex-direction: column;
}

.sso-clients-page__count {
  color: var(--ip-color-text-secondary);
  font-size: var(--ip-font-size-sm);
}

.sso-clients-page__section-title {
  margin: var(--ip-space-5) 0 var(--ip-space-3);
  font-size: var(--ip-font-size-md);
  color: var(--ip-color-text-primary);
}

.sso-clients-page__toolbar {
  display: flex;
  flex-wrap: wrap;
  gap: var(--ip-space-3);
  align-items: center;
}

.sso-clients-page__tip {
  font-size: var(--ip-font-size-sm);
  color: var(--ip-color-text-secondary);
}

.sso-clients-page__spacer {
  flex: 1;
}

.sso-clients-page__full {
  width: 100%;
}
</style>
