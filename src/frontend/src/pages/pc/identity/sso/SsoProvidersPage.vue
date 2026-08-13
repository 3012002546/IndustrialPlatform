<script setup lang="ts">
/**
 * 企业登录源管理页(TASK-ID-015,§26.8):登录源列表/新建/编辑/密钥引用(只写)/
 * 启用停用/连接测试(IdP 互通)/外部账号绑定与解绑。
 * 读操作 identity.sso.view,写 identity.sso.manage,测试 identity.sso.test。
 * 密钥只写配置节引用,摘要仅暴露 hasSecretReference,不回显键名。
 */
import { ElMessage, ElMessageBox } from 'element-plus'
import type { FormInstance, FormRules } from 'element-plus'
import { onMounted, reactive, ref } from 'vue'

import type { RoleSummaryDto } from '@/api/identity/management'
import { getManagementApi } from '@/api/identity/managementRegistry'

import {
  getSsoManagementApi,
  type ExternalAccountSummaryDto,
  type ProviderSummaryDto,
} from '@/api/identity/ssoManagement'
import { PERMISSIONS, PermissionGate } from '@/permissions'

import { formatTime, reportManagementError } from '../shared'

const ssoManagement = getSsoManagementApi()
const management = getManagementApi()

// ---------------------------------------------------------------------------
// 列表
// ---------------------------------------------------------------------------

const loading = ref(false)
const rows = ref<ProviderSummaryDto[]>([])

async function loadProviders(): Promise<void> {
  loading.value = true
  try {
    rows.value = await ssoManagement.listProviders()
  } catch (error) {
    reportManagementError(error, '加载登录源列表失败')
  } finally {
    loading.value = false
  }
}

// ---------------------------------------------------------------------------
// 新建 / 编辑
// ---------------------------------------------------------------------------

const PROTOCOL_OPTIONS = [
  { label: 'OIDC', value: 'Oidc' },
  { label: 'SAML 2.0', value: 'Saml2' },
]
const PROVISIONING_OPTIONS = [
  { label: '手动绑定', value: 'Manual' },
  { label: 'JIT 自动供给', value: 'JustInTime' },
]
const LOGOUT_OPTIONS = [
  { label: '本地注销', value: 'Local' },
  { label: '联邦注销', value: 'Federated' },
]

interface ProviderForm {
  name: string
  protocol: string
  authorityOrMetadataUrl: string
  clientIdOrEntityId: string
  callbackPath: string
  autoRedirect: boolean
  provisioningMode: string
  logoutMode: string
  allowedEmailDomains: string[]
  jitDefaultRoleNIds: string[]
}

const dialogOpen = ref(false)
const dialogTitle = ref('新建登录源')
const editing = ref<ProviderSummaryDto | null>(null)
const formRef = ref<FormInstance>()
const dialogSaving = ref(false)
const form = reactive<ProviderForm>({
  name: '',
  protocol: 'Oidc',
  authorityOrMetadataUrl: '',
  clientIdOrEntityId: '',
  callbackPath: '/identity/api/v1/sso/callback/oidc/',
  autoRedirect: false,
  provisioningMode: 'Manual',
  logoutMode: 'Local',
  allowedEmailDomains: [],
  jitDefaultRoleNIds: [],
})

const allRoles = ref<RoleSummaryDto[]>([])

async function loadAllRoles(): Promise<void> {
  try {
    const collected: RoleSummaryDto[] = []
    let page = 1
    let fetched: Awaited<ReturnType<typeof management.listRoles>>
    do {
      fetched = await management.listRoles({ pageIndex: page, pageSize: 100 })
      collected.push(...fetched.items)
      page += 1
    } while (collected.length < fetched.total)
    allRoles.value = collected
  } catch {
    // 角色选项加载失败不阻塞列表;JIT 默认角色选择时留空。
    allRoles.value = []
  }
}

function resetForm(): void {
  form.name = ''
  form.protocol = 'Oidc'
  form.authorityOrMetadataUrl = ''
  form.clientIdOrEntityId = ''
  form.callbackPath = '/identity/api/v1/sso/callback/oidc/'
  form.autoRedirect = false
  form.provisioningMode = 'Manual'
  form.logoutMode = 'Local'
  form.allowedEmailDomains = []
  form.jitDefaultRoleNIds = []
}

function openCreate(): void {
  editing.value = null
  dialogTitle.value = '新建登录源'
  resetForm()
  dialogOpen.value = true
}

function openEdit(row: ProviderSummaryDto): void {
  editing.value = row
  dialogTitle.value = '编辑登录源'
  resetForm()
  form.name = row.name
  form.protocol = row.protocol
  form.authorityOrMetadataUrl = row.authorityOrMetadataUrl
  form.clientIdOrEntityId = row.clientIdOrEntityId
  form.callbackPath = row.callbackPath
  form.autoRedirect = row.autoRedirect
  form.provisioningMode = row.provisioningMode
  form.logoutMode = row.logoutMode
  form.allowedEmailDomains = [...row.allowedEmailDomains]
  form.jitDefaultRoleNIds = [...row.jitDefaultRoleNIds]
  dialogOpen.value = true
}

function emptyToUndefined(value: string): string | undefined {
  return value.trim().length === 0 ? undefined : value.trim()
}

async function submitDialog(): Promise<void> {
  if (formRef.value === undefined) return
  const valid = await formRef.value.validate().catch(() => false)
  if (!valid) return
  dialogSaving.value = true
  try {
    if (editing.value === null) {
      await ssoManagement.createProvider({
        name: form.name.trim(),
        protocol: form.protocol,
        authorityOrMetadataUrl: emptyToUndefined(form.authorityOrMetadataUrl),
        clientIdOrEntityId: emptyToUndefined(form.clientIdOrEntityId),
        callbackPath: emptyToUndefined(form.callbackPath),
        autoRedirect: form.autoRedirect,
        provisioningMode: form.provisioningMode,
        logoutMode: form.logoutMode,
        allowedEmailDomains: form.allowedEmailDomains,
        jitDefaultRoleNIds: form.jitDefaultRoleNIds,
      })
      ElMessage.success('登录源创建成功')
    } else {
      await ssoManagement.updateProvider(editing.value.providerNId, {
        name: form.name.trim(),
        protocol: form.protocol,
        authorityOrMetadataUrl: emptyToUndefined(form.authorityOrMetadataUrl),
        clientIdOrEntityId: emptyToUndefined(form.clientIdOrEntityId),
        callbackPath: emptyToUndefined(form.callbackPath),
        autoRedirect: form.autoRedirect,
        provisioningMode: form.provisioningMode,
        logoutMode: form.logoutMode,
        allowedEmailDomains: form.allowedEmailDomains,
        jitDefaultRoleNIds: form.jitDefaultRoleNIds,
        expectedOptimisticVersion: editing.value.optimisticVersion,
        expectedConcurrencyVersion: editing.value.concurrencyVersion,
      })
      ElMessage.success('登录源已更新')
    }
    dialogOpen.value = false
    await loadProviders()
  } catch (error) {
    reportManagementError(error, '保存登录源失败')
  } finally {
    dialogSaving.value = false
  }
}

// ---------------------------------------------------------------------------
// 密钥引用(只写)
// ---------------------------------------------------------------------------

const secretDialogOpen = ref(false)
const secretTarget = ref<ProviderSummaryDto | null>(null)
const secretFormRef = ref<FormInstance>()
const secretSaving = ref(false)
const secretForm = reactive({ reference: '' })

function openSecret(row: ProviderSummaryDto): void {
  secretTarget.value = row
  secretForm.reference = ''
  secretDialogOpen.value = true
}

async function submitSecret(): Promise<void> {
  const target = secretTarget.value
  if (target === null || secretFormRef.value === undefined) return
  const valid = await secretFormRef.value.validate().catch(() => false)
  if (!valid) return
  secretSaving.value = true
  try {
    await ssoManagement.updateProviderSecret(target.providerNId, {
      secretOrCertificateReference: emptyToUndefined(secretForm.reference),
      expectedOptimisticVersion: target.optimisticVersion,
      expectedConcurrencyVersion: target.concurrencyVersion,
    })
    ElMessage.success('密钥引用已更新(只写,不回显)')
    secretDialogOpen.value = false
    await loadProviders()
  } catch (error) {
    reportManagementError(error, '保存密钥引用失败')
  } finally {
    secretSaving.value = false
  }
}

// ---------------------------------------------------------------------------
// 启用 / 停用
// ---------------------------------------------------------------------------

async function toggleEnabled(row: ProviderSummaryDto): Promise<void> {
  const enabling = !row.enabled
  try {
    await ElMessageBox.confirm(
      enabling
        ? `确定启用登录源「${row.name}」?启用后用户可在登录页选择该源。`
        : `确定停用登录源「${row.name}」?停用后该源立即不可用。`,
      `${enabling ? '启用' : '停用'}确认`,
      { type: 'warning', confirmButtonText: enabling ? '启用' : '停用', cancelButtonText: '取消' },
    )
  } catch {
    return // 用户取消
  }
  try {
    await ssoManagement.setProviderEnabled(row.providerNId, {
      enabled: enabling,
      expectedOptimisticVersion: row.optimisticVersion,
      expectedConcurrencyVersion: row.concurrencyVersion,
    })
    ElMessage.success(`登录源已${enabling ? '启用' : '停用'}`)
    await loadProviders()
  } catch (error) {
    reportManagementError(error, `登录源${enabling ? '启用' : '停用'}失败`)
  }
}

// ---------------------------------------------------------------------------
// 连接测试(IdP 互通,§26.8)
// ---------------------------------------------------------------------------

const testing = ref(false)

async function testProvider(row: ProviderSummaryDto): Promise<void> {
  if (testing.value) return
  testing.value = true
  try {
    const result = await ssoManagement.testProvider(row.providerNId)
    void ElMessageBox.alert(result.message, result.reachable ? '连接测试成功' : '连接测试失败', {
      type: result.reachable ? 'success' : 'error',
      confirmButtonText: '确定',
    })
  } catch (error) {
    reportManagementError(error, '连接测试失败')
  } finally {
    testing.value = false
  }
}

// ---------------------------------------------------------------------------
// 外部账号(绑定 / 解绑)
// ---------------------------------------------------------------------------

const accountsDrawerOpen = ref(false)
const accountsProvider = ref<ProviderSummaryDto | null>(null)
const accountsLoading = ref(false)
const accounts = ref<ExternalAccountSummaryDto[]>([])
const bindFormRef = ref<FormInstance>()
const bindSaving = ref(false)
const bindForm = reactive({ userNId: '', externalSubject: '', externalName: '', externalEmail: '' })

async function loadAccounts(): Promise<void> {
  const provider = accountsProvider.value
  if (provider === null) return
  accountsLoading.value = true
  try {
    accounts.value = await ssoManagement.listAccounts(provider.providerNId)
  } catch (error) {
    reportManagementError(error, '加载绑定账号失败')
  } finally {
    accountsLoading.value = false
  }
}

function openAccounts(row: ProviderSummaryDto): void {
  accountsProvider.value = row
  accounts.value = []
  bindForm.userNId = ''
  bindForm.externalSubject = ''
  bindForm.externalName = ''
  bindForm.externalEmail = ''
  accountsDrawerOpen.value = true
  void loadAccounts()
}

async function submitBind(): Promise<void> {
  const provider = accountsProvider.value
  if (provider === null || bindFormRef.value === undefined) return
  const valid = await bindFormRef.value.validate().catch(() => false)
  if (!valid) return
  bindSaving.value = true
  try {
    await ssoManagement.bindAccount(provider.providerNId, {
      userNId: bindForm.userNId.trim(),
      externalSubject: bindForm.externalSubject.trim(),
      externalName: emptyToUndefined(bindForm.externalName),
      externalEmail: emptyToUndefined(bindForm.externalEmail),
    })
    ElMessage.success('外部账号已绑定')
    bindForm.userNId = ''
    bindForm.externalSubject = ''
    bindForm.externalName = ''
    bindForm.externalEmail = ''
    await loadAccounts()
  } catch (error) {
    reportManagementError(error, '绑定外部账号失败')
  } finally {
    bindSaving.value = false
  }
}

async function unbindAccount(row: ExternalAccountSummaryDto): Promise<void> {
  const provider = accountsProvider.value
  if (provider === null) return
  try {
    await ElMessageBox.confirm(
      `确定解绑「${row.userLoginName}」与该登录源的绑定?解绑后该用户需重新经 IdP 登录或重新绑定。`,
      '解绑确认',
      { type: 'warning', confirmButtonText: '解绑', cancelButtonText: '取消' },
    )
  } catch {
    return
  }
  try {
    await ssoManagement.unbindAccount(provider.providerNId, row.userNId)
    ElMessage.success('外部账号已解绑')
    await loadAccounts()
  } catch (error) {
    reportManagementError(error, '解绑外部账号失败')
  }
}

// ---------------------------------------------------------------------------
// 表单校验规则
// ---------------------------------------------------------------------------

const providerRules: FormRules = {
  name: [{ required: true, message: '请输入登录源名称', trigger: 'blur' }],
  authorityOrMetadataUrl: [
    {
      pattern: /^https?:\/\/.+$/,
      message: '授权/元数据地址须为合法 http(s) URL',
      trigger: 'blur',
    },
  ],
}

const secretRules: FormRules = {
  reference: [
    {
      required: true,
      message: '请输入密钥引用(配置节键名,不含明文)',
      trigger: 'blur',
    },
  ],
}

const bindRules: FormRules = {
  userNId: [{ required: true, message: '请输入平台用户业务标识', trigger: 'blur' }],
  externalSubject: [{ required: true, message: '请输入 IdP 主体标识', trigger: 'blur' }],
  externalEmail: [{ type: 'email', message: '邮箱格式不正确', trigger: 'blur' }],
}

onMounted(() => {
  void loadProviders()
  void loadAllRoles()
})
</script>

<template>
  <section class="sso-providers-page">
    <div class="sso-providers-page__toolbar">
      <span class="sso-providers-page__tip">
        企业登录源配置。密钥只写配置节引用,不在界面回显明文。
      </span>
      <div class="sso-providers-page__spacer" />
      <PermissionGate :permission-n-id="PERMISSIONS.ssoManage">
        <el-button type="primary" plain data-testid="sso-provider-create" @click="openCreate">
          新建登录源
        </el-button>
      </PermissionGate>
    </div>

    <el-table :data="rows" v-loading="loading" row-key="providerNId" border stripe>
      <el-table-column prop="name" label="名称" min-width="140" />
      <el-table-column label="协议" width="90" align="center">
        <template #default="{ row }">
          {{
            row.protocol === 'Oidc' ? 'OIDC' : row.protocol === 'Saml2' ? 'SAML 2.0' : row.protocol
          }}
        </template>
      </el-table-column>
      <el-table-column
        prop="authorityOrMetadataUrl"
        label="授权/元数据地址"
        min-width="200"
        show-overflow-tooltip
      />
      <el-table-column prop="clientIdOrEntityId" label="ClientId/EntityId" min-width="150" />
      <el-table-column label="密钥" width="80" align="center">
        <template #default="{ row }">
          <el-tag :type="row.hasSecretReference ? 'success' : 'info'" effect="light">
            {{ row.hasSecretReference ? '已配置' : '未配置' }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column label="自动跳转" width="90" align="center">
        <template #default="{ row }">
          <el-tag :type="row.autoRedirect ? 'warning' : 'info'" effect="light">
            {{ row.autoRedirect ? '是' : '否' }}
          </el-tag>
        </template>
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
      <el-table-column label="操作" width="280" fixed="right">
        <template #default="{ row }">
          <PermissionGate :permission-n-id="PERMISSIONS.ssoManage">
            <el-button link type="primary" @click="openEdit(row)">编辑</el-button>
            <el-button link type="warning" @click="openSecret(row)">密钥</el-button>
            <el-button link :type="row.enabled ? 'danger' : 'success'" @click="toggleEnabled(row)">
              {{ row.enabled ? '停用' : '启用' }}
            </el-button>
          </PermissionGate>
          <PermissionGate :permission-n-id="PERMISSIONS.ssoManage">
            <el-button link type="primary" @click="openAccounts(row)">绑定账号</el-button>
          </PermissionGate>
          <PermissionGate :permission-n-id="PERMISSIONS.ssoTest">
            <el-button link type="primary" :disabled="testing" @click="testProvider(row)">
              连接测试
            </el-button>
          </PermissionGate>
        </template>
      </el-table-column>
    </el-table>

    <!-- 新建 / 编辑 -->
    <el-dialog v-model="dialogOpen" :title="dialogTitle" width="560px" @closed="resetForm">
      <el-form ref="formRef" :model="form" :rules="providerRules" label-width="130px">
        <el-form-item label="名称" prop="name">
          <el-input v-model="form.name" placeholder="登录源显示名称" />
        </el-form-item>
        <el-form-item label="协议" prop="protocol">
          <el-select v-model="form.protocol" class="sso-providers-page__full">
            <el-option
              v-for="option in PROTOCOL_OPTIONS"
              :key="option.value"
              :label="option.label"
              :value="option.value"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="授权/元数据地址" prop="authorityOrMetadataUrl">
          <el-input v-model="form.authorityOrMetadataUrl" placeholder="https://…" />
        </el-form-item>
        <el-form-item label="ClientId/EntityId" prop="clientIdOrEntityId">
          <el-input
            v-model="form.clientIdOrEntityId"
            placeholder="OIDC ClientId 或 SAML EntityId"
          />
        </el-form-item>
        <el-form-item label="回调路径" prop="callbackPath">
          <el-input
            v-model="form.callbackPath"
            placeholder="默认 /identity/api/v1/sso/callback/…"
          />
        </el-form-item>
        <el-form-item label="自动跳转">
          <el-switch v-model="form.autoRedirect" />
          <span class="sso-providers-page__switch-tip">唯一启用源时登录页直接跳转 IdP</span>
        </el-form-item>
        <el-form-item label="供给模式">
          <el-select v-model="form.provisioningMode" class="sso-providers-page__full">
            <el-option
              v-for="option in PROVISIONING_OPTIONS"
              :key="option.value"
              :label="option.label"
              :value="option.value"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="注销模式">
          <el-select v-model="form.logoutMode" class="sso-providers-page__full">
            <el-option
              v-for="option in LOGOUT_OPTIONS"
              :key="option.value"
              :label="option.label"
              :value="option.value"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="允许邮箱域">
          <el-select
            v-model="form.allowedEmailDomains"
            multiple
            filterable
            allow-create
            default-first-option
            placeholder="JIT 允许的邮箱域,回车添加"
            class="sso-providers-page__full"
          />
        </el-form-item>
        <el-form-item v-if="form.provisioningMode === 'JustInTime'" label="JIT 默认角色">
          <el-select
            v-model="form.jitDefaultRoleNIds"
            multiple
            filterable
            clearable
            placeholder="JIT 新用户默认角色"
            class="sso-providers-page__full"
          >
            <el-option
              v-for="role in allRoles"
              :key="role.roleNId"
              :value="role.roleNId"
              :label="role.name"
            />
          </el-select>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogOpen = false">取消</el-button>
        <el-button type="primary" :loading="dialogSaving" @click="submitDialog">保存</el-button>
      </template>
    </el-dialog>

    <!-- 密钥引用(只写) -->
    <el-dialog v-model="secretDialogOpen" title="更新密钥引用" width="480px">
      <p class="sso-providers-page__dialog-tip">
        为「{{ secretTarget?.name ?? '' }}」配置密钥引用:仅填写配置节键名(如
        <code>Identity:Sso:Secrets:my-oidc</code>),服务端按引用读取密钥,明文与键名均不回显。
      </p>
      <el-form ref="secretFormRef" :model="secretForm" :rules="secretRules" label-width="90px">
        <el-form-item label="密钥引用" prop="reference">
          <el-input v-model="secretForm.reference" placeholder="配置节键名,留空清除" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="secretDialogOpen = false">取消</el-button>
        <el-button type="primary" :loading="secretSaving" @click="submitSecret">保存</el-button>
      </template>
    </el-dialog>

    <!-- 绑定账号 -->
    <el-drawer
      v-model="accountsDrawerOpen"
      :title="`绑定账号 · ${accountsProvider?.name ?? ''}`"
      size="560px"
    >
      <el-form ref="bindFormRef" :model="bindForm" :rules="bindRules" label-width="110px">
        <el-form-item label="平台用户标识" prop="userNId">
          <el-input v-model="bindForm.userNId" placeholder="用户业务标识(NId)" />
        </el-form-item>
        <el-form-item label="IdP 主体标识" prop="externalSubject">
          <el-input v-model="bindForm.externalSubject" placeholder="external subject,不回显" />
        </el-form-item>
        <el-form-item label="外部姓名">
          <el-input v-model="bindForm.externalName" placeholder="可选" />
        </el-form-item>
        <el-form-item label="外部邮箱">
          <el-input v-model="bindForm.externalEmail" placeholder="可选" />
        </el-form-item>
        <el-form-item>
          <PermissionGate :permission-n-id="PERMISSIONS.ssoManage">
            <el-button type="primary" :loading="bindSaving" @click="submitBind">绑定</el-button>
          </PermissionGate>
        </el-form-item>
      </el-form>

      <el-table :data="accounts" v-loading="accountsLoading" row-key="accountNId" border stripe>
        <el-table-column prop="userLoginName" label="平台登录名" min-width="120" />
        <el-table-column prop="userName" label="姓名" min-width="100" />
        <el-table-column prop="externalName" label="外部姓名" min-width="100" />
        <el-table-column
          prop="externalEmail"
          label="外部邮箱"
          min-width="140"
          show-overflow-tooltip
        />
        <el-table-column label="最近登录" width="170">
          <template #default="{ row }">{{ formatTime(row.lastLoginOn) }}</template>
        </el-table-column>
        <el-table-column label="操作" width="80" align="center">
          <template #default="{ row }">
            <PermissionGate :permission-n-id="PERMISSIONS.ssoManage">
              <el-button link type="danger" @click="unbindAccount(row)">解绑</el-button>
            </PermissionGate>
          </template>
        </el-table-column>
      </el-table>
    </el-drawer>
  </section>
</template>

<style scoped>
.sso-providers-page {
  display: flex;
  flex-direction: column;
  gap: var(--ip-space-4);
}

.sso-providers-page__toolbar {
  display: flex;
  flex-wrap: wrap;
  gap: var(--ip-space-3);
  align-items: center;
}

.sso-providers-page__tip {
  font-size: var(--ip-font-size-sm);
  color: var(--ip-color-text-secondary);
}

.sso-providers-page__spacer {
  flex: 1;
}

.sso-providers-page__full {
  width: 100%;
}

.sso-providers-page__switch-tip {
  margin-left: var(--ip-space-2);
  font-size: var(--ip-font-size-xs);
  color: var(--ip-color-text-secondary);
}

.sso-providers-page__dialog-tip {
  margin: 0 0 var(--ip-space-3);
  font-size: var(--ip-font-size-sm);
  line-height: var(--ip-line-height-normal);
  color: var(--ip-color-text-secondary);
}
</style>
