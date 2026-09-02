<script setup lang="ts">
/**
 * 企业登录源管理页(TASK-ID-015,§26.8):登录源列表/新建/编辑/密钥引用(只写)/
 * 启用停用/连接测试(IdP 互通)/外部账号绑定与解绑。
 * 读操作 identity.sso.view,写 identity.sso.manage,测试 identity.sso.test。
 * 密钥只写配置节引用,摘要仅暴露 hasSecretReference,不回显键名。
 */
import { ElMessage, ElMessageBox } from 'element-plus'
import type { FormInstance, FormRules } from 'element-plus'
import { computed, onMounted, reactive, ref } from 'vue'

import type { RoleSummaryDto } from '@/api/identity/management'
import { getManagementApi } from '@/api/identity/managementRegistry'

import {
  getSsoManagementApi,
  type ExternalAccountSummaryDto,
  type ProviderSummaryDto,
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
const management = getManagementApi()
const locale = usePlatformLocale()
const copy = computed(() => localeMessages[locale.value].identity.management.ssoProviders)
const commonCopy = computed(() => localeMessages[locale.value].identity.management.common)

// ---------------------------------------------------------------------------
// 列表
// ---------------------------------------------------------------------------

const loading = ref(false)
const rows = ref<ProviderSummaryDto[]>([])

const providerColumns = computed(() => [
  { field: 'name', title: copy.value.name, minWidth: 140, filter: { kind: 'text' as const } },
  { field: 'protocol', title: copy.value.protocol, width: 90 },
  { field: 'authorityOrMetadataUrl', title: copy.value.authority, minWidth: 200 },
  { field: 'clientIdOrEntityId', title: copy.value.clientId, minWidth: 150 },
  { field: 'hasSecretReference', title: copy.value.secret, width: 80 },
  { field: 'autoRedirect', title: copy.value.autoRedirect, width: 90 },
  { field: 'enabled', title: copy.value.status, width: 80 },
  { field: 'createdOn', title: copy.value.createdOn, width: 170 },
])

const accountColumns = computed(() => [
  { field: 'userLoginName', title: copy.value.bindUser, minWidth: 120 },
  { field: 'userName', title: commonCopy.value.name, minWidth: 100 },
  { field: 'externalName', title: copy.value.externalName, minWidth: 100 },
  { field: 'externalEmail', title: copy.value.externalEmail, minWidth: 140 },
  { field: 'lastLoginOn', title: copy.value.lastLoginOn, width: 170 },
])

async function exportProviders(request: AppDataTableExportRequest): Promise<void> {
  const filters = request.filters
  const blob = await ssoManagement.exportProviders({
    name: typeof filters.name === 'string' ? filters.name : undefined,
    quantity: request.quantity,
    columns: request.columns,
    sortField: request.sort?.field,
    sortOrder: request.sort?.order,
  })
  downloadBlob(blob, `${request.filename}.xlsx`)
}

async function exportAccounts(request: AppDataTableExportRequest): Promise<void> {
  const provider = accountsProvider.value
  if (provider === null) return
  const blob = await ssoManagement.exportAccounts(provider.providerNId, {
    quantity: request.quantity,
    columns: request.columns,
    sortField: request.sort?.field,
    sortOrder: request.sort?.order,
  })
  downloadBlob(blob, `${request.filename}.xlsx`)
}

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
const provisioningOptions = computed(() => [
  { label: copy.value.manualProvisioning, value: 'Manual' },
  { label: copy.value.jitProvisioning, value: 'JustInTime' },
])
const logoutOptions = computed(() => [
  { label: copy.value.localLogout, value: 'Local' },
  { label: copy.value.federatedLogout, value: 'Federated' },
])

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
const editing = ref<ProviderSummaryDto | null>(null)
const dialogTitle = computed(() =>
  editing.value === null ? copy.value.createTitle : copy.value.editTitle,
)
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
  resetForm()
  dialogOpen.value = true
}

function openEdit(row: ProviderSummaryDto): void {
  editing.value = row
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
  <AppPage
    class="sso-providers-page"
    data-testid="identity-sso-providers-page"
    :title="copy.title"
    :description="copy.description"
  >
    <template #breadcrumb>
      <nav :aria-label="commonCopy.pagePath">{{ copy.breadcrumb }}</nav>
    </template>
    <template #heading-meta>
      <span class="sso-providers-page__count">{{ rows.length }} {{ copy.countSuffix }}</span>
    </template>
    <template #actions>
      <PermissionGate :permission-n-id="PERMISSIONS.ssoManage">
        <el-button type="primary" data-testid="sso-provider-create" @click="openCreate">
          {{ copy.create }}
        </el-button>
      </PermissionGate>
    </template>

    <AppDataTable
      table-key="identity-sso-providers"
      route-key="identity-sso-providers"
      row-key="providerNId"
      :rows="rows"
      :total="rows.length"
      :loading="loading"
      :columns="providerColumns"
      :exporter="exportProviders"
    >
      <template #cell-protocol="{ row }">
        {{
          row.protocol === 'Oidc' ? 'OIDC' : row.protocol === 'Saml2' ? 'SAML 2.0' : row.protocol
        }}
      </template>
      <template #cell-hasSecretReference="{ row }">
        <el-tag :type="row.hasSecretReference ? 'success' : 'info'" effect="light">
          {{ row.hasSecretReference ? copy.configured : copy.notConfigured }}
        </el-tag>
      </template>
      <template #cell-autoRedirect="{ row }">
        <el-tag :type="row.autoRedirect ? 'warning' : 'info'" effect="light">
          {{ row.autoRedirect ? commonCopy.yes : commonCopy.no }}
        </el-tag>
      </template>
      <template #cell-enabled="{ row }">
        <el-tag :type="row.enabled ? 'success' : 'danger'" effect="light">
          {{ row.enabled ? copy.enabled : copy.disabled }}
        </el-tag>
      </template>
      <template #cell-createdOn="{ row }">{{ formatTime(row.createdOn) }}</template>
      <template #actions="{ row }">
        <PermissionGate :permission-n-id="PERMISSIONS.ssoManage">
          <el-button link type="primary" @click="openEdit(row)">{{ copy.edit }}</el-button>
          <el-button link type="warning" @click="openSecret(row)">{{ copy.secretAction }}</el-button>
          <el-button link :type="row.enabled ? 'danger' : 'success'" @click="toggleEnabled(row)">
            {{ row.enabled ? copy.disabled : copy.enabled }}
          </el-button>
        </PermissionGate>
        <PermissionGate :permission-n-id="PERMISSIONS.ssoManage">
          <el-button link type="primary" @click="openAccounts(row)">{{ copy.accounts }}</el-button>
        </PermissionGate>
        <PermissionGate :permission-n-id="PERMISSIONS.ssoTest">
          <el-button link type="primary" :disabled="testing" @click="testProvider(row)">
            {{ copy.test }}
          </el-button>
        </PermissionGate>
      </template>
    </AppDataTable>

    <!-- 新建 / 编辑 -->
    <AppFormDrawer
      v-model="dialogOpen"
      :title="dialogTitle"
      :busy="dialogSaving"
      size="wide"
      @cancel="resetForm"
      @submit="submitDialog"
    >
      <el-form ref="formRef" :model="form" :rules="providerRules" label-width="130px">
        <el-form-item :label="copy.name" prop="name">
          <el-input v-model="form.name" :placeholder="copy.name" />
        </el-form-item>
        <el-form-item :label="copy.protocol" prop="protocol">
          <el-select v-model="form.protocol" class="sso-providers-page__full">
            <el-option
              v-for="option in PROTOCOL_OPTIONS"
              :key="option.value"
              :label="option.label"
              :value="option.value"
            />
          </el-select>
        </el-form-item>
        <el-form-item :label="copy.authority" prop="authorityOrMetadataUrl">
          <el-input v-model="form.authorityOrMetadataUrl" placeholder="https://…" />
        </el-form-item>
        <el-form-item :label="copy.clientId" prop="clientIdOrEntityId">
          <el-input
            v-model="form.clientIdOrEntityId"
            :placeholder="copy.clientIdPlaceholder"
          />
        </el-form-item>
        <el-form-item :label="copy.callbackPath" prop="callbackPath">
          <el-input
            v-model="form.callbackPath"
            :placeholder="copy.callbackPlaceholder"
          />
        </el-form-item>
        <el-form-item :label="copy.autoRedirect">
          <el-switch v-model="form.autoRedirect" />
          <span class="sso-providers-page__switch-tip">{{ copy.autoRedirectHint }}</span>
        </el-form-item>
        <el-form-item :label="copy.provisioningMode">
          <el-select v-model="form.provisioningMode" class="sso-providers-page__full">
            <el-option
              v-for="option in provisioningOptions"
              :key="option.value"
              :label="option.label"
              :value="option.value"
            />
          </el-select>
        </el-form-item>
        <el-form-item :label="copy.logoutMode">
          <el-select v-model="form.logoutMode" class="sso-providers-page__full">
            <el-option
              v-for="option in logoutOptions"
              :key="option.value"
              :label="option.label"
              :value="option.value"
            />
          </el-select>
        </el-form-item>
        <el-form-item :label="copy.allowedEmailDomains">
          <el-select
            v-model="form.allowedEmailDomains"
            multiple
            filterable
            allow-create
            default-first-option
            :placeholder="copy.allowedEmailDomainsPlaceholder"
            class="sso-providers-page__full"
          />
        </el-form-item>
        <el-form-item v-if="form.provisioningMode === 'JustInTime'" :label="copy.defaultRole">
          <el-select
            v-model="form.jitDefaultRoleNIds"
            multiple
            filterable
            clearable
            :placeholder="copy.defaultRolePlaceholder"
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
        <el-button @click="dialogOpen = false">{{ commonCopy.cancel }}</el-button>
        <el-button type="primary" :loading="dialogSaving" @click="submitDialog">{{ commonCopy.save }}</el-button>
      </template>
    </AppFormDrawer>

    <!-- 密钥引用(只写) -->
    <AppFormDrawer
      v-model="secretDialogOpen"
      :title="copy.secretTitle"
      :busy="secretSaving"
      size="medium"
      @submit="submitSecret"
    >
      <p class="sso-providers-page__dialog-tip">{{ copy.secretDescription }}</p>
      <el-form ref="secretFormRef" :model="secretForm" :rules="secretRules" label-width="90px">
        <el-form-item :label="copy.secretReference" prop="reference">
          <el-input v-model="secretForm.reference" :placeholder="copy.secretReferencePlaceholder" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="secretDialogOpen = false">{{ commonCopy.cancel }}</el-button>
        <el-button type="primary" :loading="secretSaving" @click="submitSecret">{{ commonCopy.save }}</el-button>
      </template>
    </AppFormDrawer>

    <!-- 绑定账号 -->
    <AppFormDrawer
      v-model="accountsDrawerOpen"
      :title="copy.accountTitle"
      size="wide"
      :allow-mode-switch="false"
    >
      <el-form ref="bindFormRef" :model="bindForm" :rules="bindRules" label-width="110px">
        <el-form-item :label="copy.bindUser" prop="userNId">
          <el-input v-model="bindForm.userNId" :placeholder="copy.userNIdPlaceholder" />
        </el-form-item>
        <el-form-item :label="copy.externalSubject" prop="externalSubject">
          <el-input v-model="bindForm.externalSubject" :placeholder="copy.externalSubjectPlaceholder" />
        </el-form-item>
        <el-form-item :label="copy.externalName">
          <el-input v-model="bindForm.externalName" :placeholder="commonCopy.optional" />
        </el-form-item>
        <el-form-item :label="copy.externalEmail">
          <el-input v-model="bindForm.externalEmail" :placeholder="commonCopy.optional" />
        </el-form-item>
        <el-form-item>
          <PermissionGate :permission-n-id="PERMISSIONS.ssoManage">
            <el-button type="primary" :loading="bindSaving" @click="submitBind">{{ copy.bind }}</el-button>
          </PermissionGate>
        </el-form-item>
      </el-form>

      <h3 class="sso-providers-page__section-title">{{ copy.accounts }}</h3>
      <AppDataTable
        table-key="identity-sso-provider-accounts"
        route-key="identity-sso-provider-accounts"
        row-key="accountNId"
        :rows="accounts"
        :total="accounts.length"
        :loading="accountsLoading"
        :columns="accountColumns"
        :exporter="exportAccounts"
      >
        <template #cell-lastLoginOn="{ row }">{{ formatTime(row.lastLoginOn) }}</template>
        <template #actions="{ row }">
          <PermissionGate :permission-n-id="PERMISSIONS.ssoManage">
            <el-button link type="danger" @click="unbindAccount(row)">{{ copy.unbind }}</el-button>
          </PermissionGate>
        </template>
      </AppDataTable>
      <template #footer>
        <el-button @click="accountsDrawerOpen = false">{{ commonCopy.cancel }}</el-button>
      </template>
    </AppFormDrawer>
  </AppPage>
</template>

<style scoped>
.sso-providers-page {
  display: flex;
  flex-direction: column;
  gap: 0;
  overflow: hidden;
  background: var(--ip-color-bg-container);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-lg);
  min-width: 0;
}

.sso-providers-page :deep(.app-page__header) {
  padding: 18px 20px 17px;
  border-bottom: 1px solid var(--ip-color-border);
}

.sso-providers-page :deep(.app-page__body) {
  display: flex;
  flex-direction: column;
  min-width: 0;
  min-height: 0;
}

.sso-providers-page :deep(.app-data-table) {
  flex: 1 1 auto;
  min-height: 0;
}

.sso-providers-page :deep(.app-data-table__card) {
  display: flex;
  min-height: 0;
  flex: 1 1 auto;
  flex-direction: column;
}

.sso-providers-page__count {
  color: var(--ip-color-text-secondary);
  font-size: var(--ip-font-size-sm);
}

.sso-providers-page__section-title {
  margin: var(--ip-space-5) 0 var(--ip-space-3);
  font-size: var(--ip-font-size-md);
  color: var(--ip-color-text-primary);
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
