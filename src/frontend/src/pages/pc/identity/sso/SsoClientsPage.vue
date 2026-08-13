<script setup lang="ts">
/**
 * 平台 SSO Client 管理页(TASK-ID-015,§26.7):Client 列表/新建/编辑/启用停用/
 * 端点(Redirect/PostLogoutRedirect/Origin)登记、启停与移除。
 * 读操作 identity.sso.view,写操作 identity.sso.manage。
 */
import { ElMessage, ElMessageBox } from 'element-plus'
import type { FormInstance, FormRules } from 'element-plus'
import { onMounted, reactive, ref } from 'vue'

import {
  getSsoManagementApi,
  type SsoClientSummaryDto,
  type SsoEndpointSummaryDto,
} from '@/api/identity/ssoManagement'
import { PERMISSIONS, PermissionGate } from '@/permissions'

import { formatTime, reportManagementError } from '../shared'

const ssoManagement = getSsoManagementApi()

// ---------------------------------------------------------------------------
// 列表
// ---------------------------------------------------------------------------

const loading = ref(false)
const rows = ref<SsoClientSummaryDto[]>([])

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
const dialogTitle = ref('新建 Client')
const editing = ref<SsoClientSummaryDto | null>(null)
const formRef = ref<FormInstance>()
const dialogSaving = ref(false)
const form = reactive<ClientForm>({ name: '', oauthClientId: '' })

function resetForm(): void {
  form.name = ''
  form.oauthClientId = ''
}

function openCreate(): void {
  editing.value = null
  dialogTitle.value = '新建 Client'
  resetForm()
  dialogOpen.value = true
}

function openEdit(row: SsoClientSummaryDto): void {
  editing.value = row
  dialogTitle.value = '编辑 Client'
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

const ENDPOINT_TYPE_OPTIONS = [
  { label: '回调 Redirect', value: 'Redirect' },
  { label: '登出回跳 PostLogoutRedirect', value: 'PostLogoutRedirect' },
  { label: '来源 Origin', value: 'Origin' },
]

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
  <section class="sso-clients-page">
    <div class="sso-clients-page__toolbar">
      <span class="sso-clients-page__tip">
        平台 SSO Client 用于校验回调与登出回跳地址,端点须显式登记。
      </span>
      <div class="sso-clients-page__spacer" />
      <PermissionGate :permission-n-id="PERMISSIONS.ssoManage">
        <el-button type="primary" plain data-testid="sso-client-create" @click="openCreate">
          新建 Client
        </el-button>
      </PermissionGate>
    </div>

    <el-table :data="rows" v-loading="loading" row-key="clientNId" border stripe>
      <el-table-column prop="name" label="名称" min-width="150" />
      <el-table-column prop="oauthClientId" label="OAuth ClientId" min-width="180" />
      <el-table-column label="端点数" width="90" align="center">
        <template #default="{ row }">{{ row.endpoints.length }}</template>
      </el-table-column>
      <el-table-column label="状态" width="80" align="center">
        <template #default="{ row }">
          <el-tag :type="row.enabled ? 'success' : 'danger'" effect="light">
            {{ row.enabled ? '启用' : '停用' }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column label="创建时间" width="170">
        <template #default="{ row }">{{ formatTime(row.createdOn) }}</template>
      </el-table-column>
      <el-table-column label="操作" width="220" fixed="right">
        <template #default="{ row }">
          <PermissionGate :permission-n-id="PERMISSIONS.ssoManage">
            <el-button link type="primary" @click="openEdit(row)">编辑</el-button>
            <el-button link type="primary" @click="openEndpoints(row)">端点</el-button>
            <el-button link :type="row.enabled ? 'danger' : 'success'" @click="toggleEnabled(row)">
              {{ row.enabled ? '停用' : '启用' }}
            </el-button>
          </PermissionGate>
        </template>
      </el-table-column>
    </el-table>

    <!-- 新建 / 编辑 -->
    <el-dialog v-model="dialogOpen" :title="dialogTitle" width="480px" @closed="resetForm">
      <el-form ref="formRef" :model="form" :rules="clientRules" label-width="120px">
        <el-form-item label="名称" prop="name">
          <el-input v-model="form.name" placeholder="Client 显示名称" />
        </el-form-item>
        <el-form-item label="OAuth ClientId" prop="oauthClientId">
          <el-input v-model="form.oauthClientId" placeholder="第三方系统持有" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogOpen = false">取消</el-button>
        <el-button type="primary" :loading="dialogSaving" @click="submitDialog">保存</el-button>
      </template>
    </el-dialog>

    <!-- 端点管理 -->
    <el-drawer
      v-model="endpointsDrawerOpen"
      :title="`端点管理 · ${endpointsClient?.name ?? ''}`"
      size="600px"
    >
      <el-form
        ref="endpointFormRef"
        :model="endpointForm"
        :rules="endpointRules"
        label-width="110px"
      >
        <el-form-item label="类型" prop="type">
          <el-select v-model="endpointForm.type" class="sso-clients-page__full">
            <el-option
              v-for="option in ENDPOINT_TYPE_OPTIONS"
              :key="option.value"
              :label="option.label"
              :value="option.value"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="地址" prop="uri">
          <el-input v-model="endpointForm.uri" placeholder="https://…" />
        </el-form-item>
        <el-form-item label="业务标识">
          <el-input v-model="endpointForm.nId" placeholder="可选,默认自动生成" />
        </el-form-item>
        <el-form-item>
          <PermissionGate :permission-n-id="PERMISSIONS.ssoManage">
            <el-button type="primary" :loading="endpointSaving" @click="submitEndpoint">
              登记端点
            </el-button>
          </PermissionGate>
        </el-form-item>
      </el-form>

      <el-table
        :data="endpointsClient?.endpoints ?? []"
        row-key="endpointNId"
        border
        stripe
        size="small"
      >
        <el-table-column label="类型" width="180">
          <template #default="{ row }">
            <el-tag effect="light">
              {{
                row.type === 'Redirect'
                  ? '回调'
                  : row.type === 'PostLogoutRedirect'
                    ? '登出回跳'
                    : row.type === 'Origin'
                      ? '来源'
                      : row.type
              }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="uri" label="地址" min-width="220" show-overflow-tooltip />
        <el-table-column label="状态" width="70" align="center">
          <template #default="{ row }">
            <el-tag :type="row.enabled ? 'success' : 'info'" effect="light">
              {{ row.enabled ? '启用' : '停用' }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="操作" width="140" align="center">
          <template #default="{ row }">
            <PermissionGate :permission-n-id="PERMISSIONS.ssoManage">
              <el-button
                link
                :type="row.enabled ? 'danger' : 'success'"
                @click="toggleEndpointEnabled(row)"
              >
                {{ row.enabled ? '停用' : '启用' }}
              </el-button>
              <el-button link type="danger" @click="removeEndpoint(row)"> 移除 </el-button>
            </PermissionGate>
          </template>
        </el-table-column>
      </el-table>
    </el-drawer>
  </section>
</template>

<style scoped>
.sso-clients-page {
  display: flex;
  flex-direction: column;
  gap: var(--ip-space-4);
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
