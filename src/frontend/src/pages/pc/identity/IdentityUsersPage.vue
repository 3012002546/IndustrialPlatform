<script setup lang="ts">
/**
 * 用户管理页(TASK-ID-012/§16.3,§29A.5):列表分页过滤(含用户组/角色/已删除)、
 * 新建(服务端随机临时密码)/编辑、启用/禁用确认、分配角色、安全删除/恢复、
 * 独立重置密码权限;409 并发冲突提示重载。
 * 临时密码只经一次性弹窗展示,禁止持久化。操作按钮按 PermissionGate 控制(identity.user.*)。
 */
import { ElDropdown, ElDropdownItem, ElDropdownMenu, ElMessage, ElMessageBox } from 'element-plus'
import type { FormInstance, FormRules } from 'element-plus'
import { computed, onMounted, reactive, ref, watch } from 'vue'

import { getManagementApi } from '@/api/identity/managementRegistry'
import type { RoleSummaryDto, UserGroupSummaryDto, UserSummaryDto } from '@/api/identity/management'
import type {
  AppDataTableColumn,
  AppDataTableExportRequest,
  AppDataTableQueryMode,
  AppDataTableRequest,
} from '@/components/management/AppDataTable'
import type { QueryDescriptor } from '@/querying'
import AppPage from '@/components/base/AppPage.vue'
import AppQueryPanel from '@/components/management/AppQueryPanel.vue'
import { localeMessages } from '@/localization/i18n'
import { PERMISSIONS, PermissionGate, usePermission } from '@/permissions'
import AppDataTable from '@/components/management/AppDataTable.vue'
import { useLocalizationStore } from '@/stores/localizationStore'

import TemporaryPasswordDialog from './components/TemporaryPasswordDialog.vue'
import { formatTime, reportManagementError } from './shared'

interface UserForm {
  nId: string
  loginName: string
  name: string
  email: string
  phone: string
}

const management = getManagementApi()
const { has } = usePermission()
const localization = useLocalizationStore()
const commonCopy = computed(() => localeMessages[localization.locale].common.action)
const copy = computed(() => localeMessages[localization.locale].identity.user)

type UserAction =
  'detail' | 'edit' | 'status' | 'assign-role' | 'reset-password' | 'restore' | 'delete'

const USER_ACTION_WIDTHS: Record<UserAction, number> = {
  detail: 42,
  edit: 42,
  status: 42,
  'assign-role': 70,
  'reset-password': 70,
  restore: 42,
  delete: 42,
}

const USER_ACTION_GAP = 4
const USER_MORE_WIDTH = 52

function userActionCandidates(row: UserSummaryDto): UserAction[] {
  const actions: UserAction[] = ['detail']
  if (has(PERMISSIONS.userUpdate)) actions.push('edit')
  if (row.isDeleted) {
    if (has(PERMISSIONS.userRestore)) actions.push('restore')
  } else {
    if (has(PERMISSIONS.userStatus)) actions.push('status')
    if (has(PERMISSIONS.userAssignRole)) actions.push('assign-role')
    if (has(PERMISSIONS.userResetPassword)) actions.push('reset-password')
    if (has(PERMISSIONS.userDelete)) actions.push('delete')
  }
  return actions
}

function directUserActions(row: UserSummaryDto, availableWidth?: number): UserAction[] {
  const actions = userActionCandidates(row)
  const width = Math.max(120, Math.round(availableWidth ?? 220))
  const totalWidth = actions.reduce(
    (total, action, index) =>
      total + USER_ACTION_WIDTHS[action] + (index === 0 ? 0 : USER_ACTION_GAP),
    0,
  )
  if (totalWidth <= width) return actions

  const direct: UserAction[] = []
  let used = 0
  for (const [index, action] of actions.entries()) {
    const gap = direct.length === 0 ? 0 : USER_ACTION_GAP
    const hasOverflow = index < actions.length - 1
    const moreWidth = hasOverflow ? USER_ACTION_GAP + USER_MORE_WIDTH : 0
    if (used + gap + USER_ACTION_WIDTHS[action] + moreWidth > width) break
    direct.push(action)
    used += gap + USER_ACTION_WIDTHS[action]
  }
  return direct
}

function isDirectUserAction(
  row: UserSummaryDto,
  availableWidth: number | undefined,
  action: UserAction,
): boolean {
  return directUserActions(row, availableWidth).includes(action)
}

function hasMoreActions(row: UserSummaryDto, availableWidth?: number): boolean {
  const direct = directUserActions(row, availableWidth)
  return userActionCandidates(row).some((action) => !direct.includes(action))
}

function onRowActionCommand(row: UserSummaryDto, command: string): void {
  if (command === 'detail') openDetail(row)
  else if (command === 'edit') openEdit(row)
  else if (command === 'status') void toggleStatus(row)
  else if (command === 'assign-role') openAssignRoles(row)
  else if (command === 'reset-password') openResetPassword(row)
  else if (command === 'restore') void restoreUser(row)
  else if (command === 'delete') void deleteUser(row)
}

// ---------------------------------------------------------------------------
// 列表与过滤
// ---------------------------------------------------------------------------

const loading = ref(false)
const rows = ref<UserSummaryDto[]>([])
const total = ref(0)
const query = reactive({
  nId: '',
  loginName: '',
  name: '',
  status: '',
  groupNId: '',
  roleNId: '',
  includeDeleted: false,
})
const pageIndex = ref(1)
const pageSize = ref(25)
const tableQueryMode = ref<AppDataTableQueryMode>('top')

/** 全部可用角色/用户组选项(供角色/用户组过滤与角色来源展示)。 */
const allRoles = ref<RoleSummaryDto[]>([])
const allGroups = ref<UserGroupSummaryDto[]>([])

const userColumns = computed<readonly AppDataTableColumn[]>(() => [
  {
    field: 'loginName',
    title: copy.value.loginName,
    minWidth: 130,
    sortable: true,
    filter: { kind: 'text' as const },
  },
  { field: 'name', title: copy.value.name, minWidth: 110, filter: { kind: 'text' as const } },
  {
    field: 'status',
    title: copy.value.status,
    width: 90,
    filter: {
      kind: 'select' as const,
      options: [
        { label: copy.value.enabled, value: 'Active' },
        { label: copy.value.disabled, value: 'Disabled' },
      ],
    },
  },
  {
    field: 'mustChangePassword',
    title: copy.value.mustChangePassword,
    width: 80,
    filter: {
      kind: 'select' as const,
      options: [
        { label: copy.value.mustChangePassword, value: true },
        { label: copy.value.noChangePassword, value: false },
      ],
    },
  },
  { field: 'email', title: copy.value.email, minWidth: 170, filter: { kind: 'text' as const } },
  { field: 'phone', title: copy.value.phone, minWidth: 120, filter: { kind: 'text' as const } },
  { field: 'effectiveRoleCount', title: copy.value.effectiveRoles, width: 100, filter: false },
  {
    field: 'lastLoginOn',
    title: copy.value.lastLoginOn,
    width: 240,
    minWidth: 240,
    sortable: true,
    filter: { kind: 'date-range' as const },
  },
  {
    field: 'createdOn',
    title: copy.value.createdOn,
    width: 240,
    minWidth: 240,
    sortable: true,
    filter: { kind: 'date-range' as const },
  },
])

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
    // 角色选项加载失败不阻塞列表;分配时再次尝试并提示。
    allRoles.value = []
  }
}

async function loadAllGroups(): Promise<void> {
  try {
    const collected: UserGroupSummaryDto[] = []
    let page = 1
    let fetched: Awaited<ReturnType<typeof management.listUserGroups>>
    do {
      fetched = await management.listUserGroups({ pageIndex: page, pageSize: 100 })
      collected.push(...fetched.items)
      page += 1
    } while (collected.length < fetched.total)
    allGroups.value = collected
  } catch {
    // 用户组选项加载失败(可能无 user-group.view 权限)不阻塞列表。
    allGroups.value = []
  }
}

function roleName(roleNId: string): string {
  return allRoles.value.find((role) => role.roleNId === roleNId)?.name ?? roleNId
}

function roleNames(roleNIds: readonly string[]): string {
  return roleNIds.map(roleName).join('、') || '—'
}

async function loadUsers(): Promise<void> {
  loading.value = true
  try {
    const result = await management.listUsers({
      nId: query.nId.trim() || undefined,
      loginName: query.loginName.trim() || undefined,
      name: query.name.trim() || undefined,
      status: query.status || undefined,
      groupNId: query.groupNId || undefined,
      roleNId: query.roleNId || undefined,
      includeDeleted: query.includeDeleted || undefined,
      pageIndex: pageIndex.value,
      pageSize: pageSize.value,
    })
    rows.value = result.items
    total.value = result.total
  } catch (error) {
    reportManagementError(error, '加载用户列表失败')
  } finally {
    loading.value = false
  }
}

function search(): void {
  pageIndex.value = 1
  void loadUsers()
}

function resetQuery(): void {
  query.nId = ''
  query.loginName = ''
  query.name = ''
  query.status = ''
  query.groupNId = ''
  query.roleNId = ''
  query.includeDeleted = false
  pageIndex.value = 1
  void loadUsers()
}

function onTableQuery(request: AppDataTableRequest): void {
  pageIndex.value = request.pageIndex
  pageSize.value = request.pageSize
}

function onTableLoadError(error: unknown): void {
  reportManagementError(error, '加载用户列表失败')
}

function onTableQueryModeChange(mode: AppDataTableQueryMode): void {
  tableQueryMode.value = mode
  if (mode === 'header') {
    query.nId = ''
    query.loginName = ''
    query.name = ''
    query.status = ''
    query.groupNId = ''
    query.roleNId = ''
    query.includeDeleted = false
  }
  pageIndex.value = 1
}

function buildUserQueryDescriptor(
  request: AppDataTableRequest | AppDataTableExportRequest,
): QueryDescriptor {
  const filters = [...(request.descriptor?.filters ?? [])]
  if (request.queryMode === 'top') {
    const topFilters: Array<[string, unknown]> = [
      ['userNId', query.nId.trim()],
      ['loginName', query.loginName.trim()],
      ['name', query.name.trim()],
      ['status', query.status],
    ]
    topFilters.forEach(([field, value]) => {
      if (value !== '') {
        filters.push({ field, operator: field === 'status' ? 'eq' : 'contains', value })
      }
    })
  }
  return {
    filters,
    orderBy: request.descriptor?.orderBy ?? [],
    select: request.descriptor?.select ?? userColumns.value.map((column) => column.field),
    pageIndex: 'pageIndex' in request ? request.pageIndex : 1,
    pageSize: 'pageSize' in request ? request.pageSize : 100,
  }
}

async function exportUsers(request: AppDataTableExportRequest): Promise<void> {
  if (management.exportUsersOData !== undefined && request.descriptor !== undefined) {
    const blob = await management.exportUsersOData(
      buildUserQueryDescriptor(request),
      request.columns,
      request.quantity,
      document.documentElement.lang || 'zh-CN',
      Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC',
    )
    const url = URL.createObjectURL(blob)
    const anchor = document.createElement('a')
    anchor.href = url
    anchor.download = `${request.filename}.xlsx`
    anchor.click()
    URL.revokeObjectURL(url)
    return
  }
  if (management.exportUsers === undefined) return
  const filters = request.queryMode === 'top' ? { ...query, ...request.filters } : request.filters
  const blob = await management.exportUsers({
    keyword: String(filters.keyword ?? '').trim() || undefined,
    nId: String(filters.nId ?? '').trim() || undefined,
    loginName: String(filters.loginName ?? '').trim() || undefined,
    name: String(filters.name ?? '').trim() || undefined,
    status: String(filters.status ?? '') || undefined,
    groupNId: String(filters.groupNId ?? '') || undefined,
    roleNId: String(filters.roleNId ?? '') || undefined,
    email: String(filters.email ?? '').trim() || undefined,
    phone: String(filters.phone ?? '').trim() || undefined,
    mustChangePassword:
      filters.mustChangePassword === undefined || filters.mustChangePassword === ''
        ? undefined
        : filters.mustChangePassword === true || filters.mustChangePassword === 'true',
    lastLoginFrom: Array.isArray(filters.lastLoginOn)
      ? String(filters.lastLoginOn[0] ?? '') || undefined
      : undefined,
    lastLoginTo: Array.isArray(filters.lastLoginOn)
      ? String(filters.lastLoginOn[1] ?? '') || undefined
      : undefined,
    createdFrom: Array.isArray(filters.createdOn)
      ? String(filters.createdOn[0] ?? '') || undefined
      : undefined,
    createdTo: Array.isArray(filters.createdOn)
      ? String(filters.createdOn[1] ?? '') || undefined
      : undefined,
    includeDeleted: filters.includeDeleted === true || filters.includeDeleted === 'true',
    quantity: request.quantity,
    columns: request.columns,
    sortField: request.sort?.field,
    sortOrder: request.sort?.order,
  })
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = `${request.filename}.xlsx`
  anchor.click()
  URL.revokeObjectURL(url)
}

async function loadUsersTable(request: AppDataTableRequest) {
  const result = await management.listUsersOData(buildUserQueryDescriptor(request))
  rows.value = result.items
  total.value = result.total
  return result
}

// ---------------------------------------------------------------------------
// 详情查看(含角色来源)
// ---------------------------------------------------------------------------

const detailOpen = ref(false)
const detailTarget = ref<UserSummaryDto | null>(null)

function openDetail(row: UserSummaryDto): void {
  detailTarget.value = row
  detailOpen.value = true
}

// ---------------------------------------------------------------------------
// 新建 / 编辑
// ---------------------------------------------------------------------------

const dialogOpen = ref(false)
const dialogTitle = ref('新建用户')
const editing = ref<UserSummaryDto | null>(null)
const formRef = ref<FormInstance>()
const dialogSaving = ref(false)
const form = reactive<UserForm>({
  nId: '',
  loginName: '',
  name: '',
  email: '',
  phone: '',
})

function resetForm(): void {
  form.nId = ''
  form.loginName = ''
  form.name = ''
  form.email = ''
  form.phone = ''
}

function openCreate(): void {
  editing.value = null
  dialogTitle.value = '新建用户'
  resetForm()
  dialogOpen.value = true
}

function openEdit(row: UserSummaryDto): void {
  editing.value = row
  dialogTitle.value = '编辑用户'
  resetForm()
  form.loginName = row.loginName
  form.name = row.name
  form.email = row.email ?? ''
  form.phone = row.phone ?? ''
  dialogOpen.value = true
}

function emptyToNull(value: string): string | null {
  return value.trim().length === 0 ? null : value.trim()
}

async function submitDialog(): Promise<void> {
  if (formRef.value === undefined) return
  const valid = await formRef.value.validate().catch(() => false)
  if (!valid) return
  dialogSaving.value = true
  try {
    if (editing.value === null) {
      const result = await management.createUser({
        nId: form.nId.trim() || undefined,
        loginName: form.loginName.trim(),
        name: form.name.trim(),
        email: emptyToNull(form.email),
        phone: emptyToNull(form.phone),
      })
      ElMessage.success('用户创建成功')
      dialogOpen.value = false
      await loadUsers()
      // 服务端随机临时密码只出现一次,立即经一次性弹窗展示。
      showTemporaryPassword(result.temporaryPassword, `用户「${result.user.loginName}」创建成功`)
    } else {
      await management.updateUser(editing.value.userNId, {
        loginName: form.loginName.trim(),
        name: form.name.trim(),
        email: emptyToNull(form.email),
        phone: emptyToNull(form.phone),
        expectedOptimisticVersion: editing.value.optimisticVersion,
        expectedConcurrencyVersion: editing.value.concurrencyVersion,
      })
      ElMessage.success('用户已更新')
      dialogOpen.value = false
      await loadUsers()
    }
  } catch (error) {
    reportManagementError(error, '保存用户失败')
  } finally {
    dialogSaving.value = false
  }
}

// ---------------------------------------------------------------------------
// 启用 / 禁用(状态确认)
// ---------------------------------------------------------------------------

async function toggleStatus(row: UserSummaryDto): Promise<void> {
  const isActive = row.status === 'Active'
  const action = isActive ? '禁用' : '启用'
  try {
    await ElMessageBox.confirm(
      isActive
        ? `确定禁用账号「${row.loginName}」?禁用后该用户将无法登录。`
        : `确定启用账号「${row.loginName}」?启用后可正常登录。`,
      `${action}确认`,
      { type: 'warning', confirmButtonText: `${action}`, cancelButtonText: '取消' },
    )
  } catch {
    return // 用户取消
  }
  try {
    await management.setUserStatus(row.userNId, {
      enabled: !isActive,
      expectedOptimisticVersion: row.optimisticVersion,
      expectedConcurrencyVersion: row.concurrencyVersion,
    })
    ElMessage.success(`账号已${action}`)
    await loadUsers()
  } catch (error) {
    reportManagementError(error, `${action}账号失败`)
  }
}

// ---------------------------------------------------------------------------
// 分配角色
// ---------------------------------------------------------------------------

const rolesDialogOpen = ref(false)
const rolesTarget = ref<UserSummaryDto | null>(null)
const selectedRoleNIds = ref<string[]>([])
const rolesSaving = ref(false)

function openAssignRoles(row: UserSummaryDto): void {
  rolesTarget.value = row
  selectedRoleNIds.value = [...row.directRoleNIds]
  rolesDialogOpen.value = true
}

async function submitRoles(): Promise<void> {
  const target = rolesTarget.value
  if (target === null) return
  rolesSaving.value = true
  try {
    await management.assignUserRoles(target.userNId, {
      roleNIds: selectedRoleNIds.value,
      expectedOptimisticVersion: target.optimisticVersion,
      expectedConcurrencyVersion: target.concurrencyVersion,
    })
    ElMessage.success('角色已更新')
    rolesDialogOpen.value = false
    await loadUsers()
  } catch (error) {
    reportManagementError(error, '保存角色分配失败')
  } finally {
    rolesSaving.value = false
  }
}

// ---------------------------------------------------------------------------
// 重置密码(服务端随机临时密码,独立权限)
// ---------------------------------------------------------------------------

const passwordDialogOpen = ref(false)
const passwordTarget = ref<UserSummaryDto | null>(null)
const passwordSaving = ref(false)

function openResetPassword(row: UserSummaryDto): void {
  passwordTarget.value = row
  passwordDialogOpen.value = true
}

async function submitPassword(): Promise<void> {
  const target = passwordTarget.value
  if (target === null) return
  passwordSaving.value = true
  try {
    const result = await management.resetPassword(target.userNId)
    ElMessage.success('密码已重置')
    passwordDialogOpen.value = false
    // 重置强制首次改密并撤销全部会话;临时密码只出现一次。
    showTemporaryPassword(result.temporaryPassword, `用户「${target.loginName}」的密码已重置`)
  } catch (error) {
    reportManagementError(error, '重置密码失败')
  } finally {
    passwordSaving.value = false
  }
}

// ---------------------------------------------------------------------------
// 安全删除 / 恢复(§29A.3)
// ---------------------------------------------------------------------------

async function deleteUser(row: UserSummaryDto): Promise<void> {
  try {
    const { value: reason } = await ElMessageBox.prompt(
      `确定删除用户「${row.loginName}」?删除为不可恢复的墓碑删除:登录标识永久保留不复用,该用户全部会话将失效。`,
      '删除用户',
      {
        type: 'warning',
        confirmButtonText: '删除',
        cancelButtonText: '取消',
        inputPlaceholder: '删除原因(可选)',
        inputValidator: () => true,
      },
    )
    await management.deleteUser(row.userNId, {
      reason: reason.trim().length > 0 ? reason.trim() : undefined,
      expectedOptimisticVersion: row.optimisticVersion,
      expectedConcurrencyVersion: row.concurrencyVersion,
    })
    ElMessage.success('用户已删除')
    await loadUsers()
  } catch (error) {
    if (error === 'cancel' || error === 'close') return // 用户取消
    reportManagementError(error, '删除用户失败')
  }
}

async function restoreUser(row: UserSummaryDto): Promise<void> {
  try {
    const { value: reason } = await ElMessageBox.prompt(
      `确定恢复用户「${row.loginName}」?仅已删除(墓碑)用户可恢复;恢复后状态为禁用,需重新分配授权、重置密码并启用。`,
      '恢复用户',
      {
        type: 'warning',
        confirmButtonText: '恢复',
        cancelButtonText: '取消',
        inputPlaceholder: '恢复原因(可选)',
        inputValidator: () => true,
      },
    )
    await management.restoreUser(row.userNId, {
      reason: reason.trim().length > 0 ? reason.trim() : undefined,
      expectedOptimisticVersion: row.optimisticVersion,
      expectedConcurrencyVersion: row.concurrencyVersion,
    })
    ElMessage.success('用户已恢复')
    await loadUsers()
  } catch (error) {
    if (error === 'cancel' || error === 'close') return // 用户取消
    reportManagementError(error, '恢复用户失败')
  }
}

// ---------------------------------------------------------------------------
// 一次性临时密码弹窗
// ---------------------------------------------------------------------------

const tempDialogOpen = ref(false)
const tempPassword = ref('')
const tempDescription = ref('')

function showTemporaryPassword(password: string, description: string): void {
  tempPassword.value = password
  tempDescription.value = description
  tempDialogOpen.value = true
}

// 弹窗关闭后立即清空页面内存中的临时密码,禁止滞留。
watch(tempDialogOpen, (open) => {
  if (!open) {
    tempPassword.value = ''
    tempDescription.value = ''
  }
})

// ---------------------------------------------------------------------------
// 表单校验规则
// ---------------------------------------------------------------------------

const userRules: FormRules = {
  nId: [
    {
      pattern: /^[a-z][a-z0-9-]{2,63}$/,
      message: '业务标识须以小写字母开头,仅含小写字母/数字/连字符',
      trigger: 'blur',
    },
  ],
  loginName: [
    { required: true, message: '请输入登录名', trigger: 'blur' },
    { min: 3, max: 64, message: '登录名长度 3-64 个字符', trigger: 'blur' },
  ],
  name: [{ required: true, message: '请输入姓名', trigger: 'blur' }],
  email: [{ type: 'email', message: '邮箱格式不正确', trigger: 'blur' }],
}

onMounted(() => {
  void loadUsers()
  void loadAllRoles()
  void loadAllGroups()
})
</script>

<template>
  <AppPage
    class="users-page"
    data-testid="identity-users-page"
    :title="copy.title"
    :description="copy.description"
  >
    <template #breadcrumb>
      <nav aria-label="页面路径">{{ copy.breadcrumb }}</nav>
    </template>
    <template #meta>
      <span data-testid="identity-users-total">{{ total }} {{ copy.userCountSuffix }}</span>
    </template>
    <template #actions>
      <PermissionGate :permission-n-id="PERMISSIONS.userCreate">
        <el-button
          type="primary"
          data-testid="identity-users-create"
          @click="openCreate"
        >
          {{ copy.create }}
        </el-button>
      </PermissionGate>
    </template>

    <AppQueryPanel
      data-testid="identity-users-query"
      :title="copy.queryTitle"
      :show-actions="true"
      :submit-label="commonCopy.search"
      :reset-label="commonCopy.reset"
      :grid="true"
      @submit="search"
      @reset="resetQuery"
    >
      <template v-if="tableQueryMode === 'top'">
        <el-input
          v-model="query.nId"
          :placeholder="copy.businessId"
          :aria-label="copy.businessId"
          clearable
          class="users-page__filter"
          @keyup.enter="search"
        />
        <el-input
          v-model="query.loginName"
          :placeholder="copy.loginName"
          :aria-label="copy.loginName"
          clearable
          class="users-page__filter"
          @keyup.enter="search"
        />
        <el-input
          v-model="query.name"
          :placeholder="copy.name"
          :aria-label="copy.name"
          clearable
          class="users-page__filter"
          @keyup.enter="search"
        />
        <el-select
          v-model="query.status"
          :placeholder="copy.status"
          :aria-label="copy.status"
          clearable
          class="users-page__filter users-page__filter--status"
        >
          <el-option :label="copy.enabled" value="Active" />
          <el-option :label="copy.disabled" value="Disabled" />
        </el-select>
        <el-select
          v-model="query.groupNId"
          :placeholder="copy.group"
          :aria-label="copy.group"
          clearable
          filterable
          class="users-page__filter"
        >
          <el-option
            v-for="group in allGroups"
            :key="group.groupNId"
            :value="group.groupNId"
            :label="group.name"
          />
        </el-select>
        <el-select
          v-model="query.roleNId"
          :placeholder="copy.role"
          :aria-label="copy.role"
          clearable
          filterable
          class="users-page__filter"
        >
          <el-option
            v-for="role in allRoles"
            :key="role.roleNId"
            :value="role.roleNId"
            :label="role.name"
          />
        </el-select>
        <el-checkbox v-model="query.includeDeleted" :aria-label="copy.includeDeleted" @change="search">
          {{ copy.includeDeleted }}
        </el-checkbox>
      </template>
      <p v-else class="users-page__query-mode-hint" role="status">
        {{ copy.queryTitle }} · {{ copy.tableActions }}
      </p>
    </AppQueryPanel>

    <AppDataTable
      table-key="identity-users"
      :rows="rows"
      :total="total"
      :loading="loading"
      :columns="userColumns"
      :page-size="pageSize"
      :loader="loadUsersTable"
      :exporter="exportUsers"
      @query-mode-change="onTableQueryModeChange"
      @query-change="onTableQuery"
      @load-error="onTableLoadError"
    >
      <template #cell-status="{ row }">
        <el-tag :type="row.status === 'Active' ? 'success' : 'danger'" effect="light">
          {{ row.status === 'Active' ? copy.enabled : copy.disabled }}
        </el-tag>
      </template>
      <template #cell-mustChangePassword="{ row }">
        <el-tag v-if="row.mustChangePassword" type="warning" effect="plain">
          {{ copy.mustChangePassword }}
        </el-tag>
        <span v-else>{{ copy.noChangePassword }}</span>
      </template>
      <template #cell-effectiveRoleCount="{ row }">
        <el-tooltip
          :content="`直接:${roleNames(row.directRoleNIds)} / 组继承:${roleNames(row.groupRoleNIds)}`"
          placement="top"
          :show-after="300"
        >
          <span>{{ row.effectiveRoleNIds.length }}</span>
        </el-tooltip>
      </template>
      <template #cell-lastLoginOn="{ row }">{{ formatTime(row.lastLoginOn) }}</template>
      <template #cell-createdOn="{ row }">{{ formatTime(row.createdOn) }}</template>
      <template #actions="{ row, availableWidth }">
        <div class="users-page__row-actions" :data-testid="`identity-user-actions-${row.userNId}`">
          <el-button
            v-if="isDirectUserAction(row, availableWidth, 'detail')"
            link
            type="primary"
            @click="openDetail(row)"
            >{{ copy.detail }}</el-button
          >
          <PermissionGate
            v-if="isDirectUserAction(row, availableWidth, 'edit')"
            :permission-n-id="PERMISSIONS.userUpdate"
          >
            <el-button link type="primary" @click="openEdit(row)">{{ copy.edit }}</el-button>
          </PermissionGate>
          <PermissionGate
            v-if="isDirectUserAction(row, availableWidth, 'status')"
            :permission-n-id="PERMISSIONS.userStatus"
          >
            <el-button
              link
              :type="row.status === 'Active' ? 'danger' : 'success'"
              @click="toggleStatus(row)"
            >
              {{ row.status === 'Active' ? copy.disable : copy.enable }}
            </el-button>
          </PermissionGate>
          <PermissionGate
            v-if="isDirectUserAction(row, availableWidth, 'assign-role')"
            :permission-n-id="PERMISSIONS.userAssignRole"
          >
            <el-button link type="primary" @click="openAssignRoles(row)">{{ copy.assignRole }}</el-button>
          </PermissionGate>
          <PermissionGate
            v-if="isDirectUserAction(row, availableWidth, 'reset-password')"
            :permission-n-id="PERMISSIONS.userResetPassword"
          >
            <el-button link type="warning" @click="openResetPassword(row)">{{ copy.resetPassword }}</el-button>
          </PermissionGate>
          <PermissionGate
            v-if="isDirectUserAction(row, availableWidth, 'restore')"
            :permission-n-id="PERMISSIONS.userRestore"
          >
            <el-button link type="success" @click="restoreUser(row)">{{ copy.restore }}</el-button>
          </PermissionGate>
          <PermissionGate
            v-if="isDirectUserAction(row, availableWidth, 'delete')"
            :permission-n-id="PERMISSIONS.userDelete"
          >
            <el-button link type="danger" @click="deleteUser(row)">{{ copy.delete }}</el-button>
          </PermissionGate>
          <ElDropdown
            v-if="hasMoreActions(row, availableWidth)"
            trigger="click"
            placement="bottom-end"
            :teleported="true"
            popper-class="users-page__more-popper"
            @command="onRowActionCommand(row, $event)"
          >
            <button
              type="button"
              class="users-page__more-trigger"
              :data-testid="`identity-user-more-${row.userNId}`"
              aria-haspopup="menu"
            >
              {{ copy.more }}
            </button>
            <template #dropdown>
              <ElDropdownMenu :data-testid="`identity-user-more-menu-${row.userNId}`" role="menu">
                <ElDropdownItem
                  v-if="!isDirectUserAction(row, availableWidth, 'detail')"
                  command="detail"
                  :data-testid="`identity-user-action-detail-${row.userNId}`"
                  >{{ copy.detail }}</ElDropdownItem
                >
                <PermissionGate
                  v-if="!isDirectUserAction(row, availableWidth, 'edit')"
                  :permission-n-id="PERMISSIONS.userUpdate"
                >
                  <ElDropdownItem
                    command="edit"
                    :data-testid="`identity-user-action-edit-${row.userNId}`"
                    >{{ copy.edit }}</ElDropdownItem
                  >
                </PermissionGate>
                <PermissionGate
                  v-if="!isDirectUserAction(row, availableWidth, 'status')"
                  :permission-n-id="PERMISSIONS.userStatus"
                >
                  <ElDropdownItem
                    command="status"
                    :class="row.status === 'Active' ? 'is-danger' : 'is-success'"
                    :data-testid="`identity-user-action-status-${row.userNId}`"
                  >
                    {{ row.status === 'Active' ? copy.disable : copy.enable }}
                  </ElDropdownItem>
                </PermissionGate>
                <PermissionGate
                  v-if="!isDirectUserAction(row, availableWidth, 'assign-role')"
                  :permission-n-id="PERMISSIONS.userAssignRole"
                >
                  <ElDropdownItem
                    command="assign-role"
                    :data-testid="`identity-user-action-assign-role-${row.userNId}`"
                    >{{ copy.assignRole }}</ElDropdownItem
                  >
                </PermissionGate>
                <PermissionGate
                  v-if="!isDirectUserAction(row, availableWidth, 'reset-password')"
                  :permission-n-id="PERMISSIONS.userResetPassword"
                >
                  <ElDropdownItem
                    command="reset-password"
                    :data-testid="`identity-user-action-reset-password-${row.userNId}`"
                    >{{ copy.resetPassword }}</ElDropdownItem
                  >
                </PermissionGate>
                <template v-if="row.isDeleted">
                  <PermissionGate
                    v-if="!isDirectUserAction(row, availableWidth, 'restore')"
                    :permission-n-id="PERMISSIONS.userRestore"
                  >
                    <ElDropdownItem
                      command="restore"
                      :data-testid="`identity-user-action-restore-${row.userNId}`"
                      >{{ copy.restore }}</ElDropdownItem
                    >
                  </PermissionGate>
                </template>
                <template v-else>
                  <PermissionGate
                    v-if="!isDirectUserAction(row, availableWidth, 'delete')"
                    :permission-n-id="PERMISSIONS.userDelete"
                  >
                    <ElDropdownItem
                      command="delete"
                      class="is-danger"
                      :data-testid="`identity-user-action-delete-${row.userNId}`"
                      >{{ copy.delete }}</ElDropdownItem
                    >
                  </PermissionGate>
                </template>
              </ElDropdownMenu>
            </template>
          </ElDropdown>
        </div>
      </template>
    </AppDataTable>

    <!-- 详情(含角色来源) -->
    <el-dialog v-model="detailOpen" title="用户详情" width="560px">
      <el-descriptions v-if="detailTarget" :column="1" border>
        <el-descriptions-item label="登录名">{{ detailTarget.loginName }}</el-descriptions-item>
        <el-descriptions-item label="姓名">{{ detailTarget.name }}</el-descriptions-item>
        <el-descriptions-item label="业务标识">{{ detailTarget.userNId }}</el-descriptions-item>
        <el-descriptions-item label="状态">
          {{ detailTarget.status === 'Active' ? '启用' : '禁用' }}
        </el-descriptions-item>
        <el-descriptions-item label="首次登录改密">
          {{ detailTarget.mustChangePassword ? '需要' : '不需要' }}
        </el-descriptions-item>
        <el-descriptions-item label="邮箱">{{ detailTarget.email ?? '—' }}</el-descriptions-item>
        <el-descriptions-item label="手机号">{{ detailTarget.phone ?? '—' }}</el-descriptions-item>
        <el-descriptions-item label="最近登录">{{
          formatTime(detailTarget.lastLoginOn)
        }}</el-descriptions-item>
        <el-descriptions-item label="创建时间">{{
          formatTime(detailTarget.createdOn)
        }}</el-descriptions-item>
        <el-descriptions-item label="直接角色">{{
          roleNames(detailTarget.directRoleNIds)
        }}</el-descriptions-item>
        <el-descriptions-item label="组继承角色">{{
          roleNames(detailTarget.groupRoleNIds)
        }}</el-descriptions-item>
        <el-descriptions-item label="有效角色">{{
          roleNames(detailTarget.effectiveRoleNIds)
        }}</el-descriptions-item>
      </el-descriptions>
      <template #footer>
        <el-button type="primary" @click="detailOpen = false">关闭</el-button>
      </template>
    </el-dialog>

    <!-- 新建 / 编辑(创建不再录入初始密码:服务端生成随机临时密码) -->
    <el-dialog v-model="dialogOpen" :title="dialogTitle" width="520px" @closed="resetForm">
      <el-form ref="formRef" :model="form" :rules="userRules" label-width="100px">
        <el-form-item v-if="editing === null" label="业务标识" prop="nId">
          <el-input v-model="form.nId" placeholder="可选,默认自动生成" />
        </el-form-item>
        <el-form-item label="登录名" prop="loginName">
          <el-input v-model="form.loginName" placeholder="登录用户名" />
        </el-form-item>
        <el-form-item label="姓名" prop="name">
          <el-input v-model="form.name" placeholder="显示姓名" />
        </el-form-item>
        <el-form-item label="邮箱" prop="email">
          <el-input v-model="form.email" placeholder="可选" />
        </el-form-item>
        <el-form-item label="手机号" prop="phone">
          <el-input v-model="form.phone" placeholder="可选" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogOpen = false">取消</el-button>
        <el-button type="primary" :loading="dialogSaving" @click="submitDialog">保存</el-button>
      </template>
    </el-dialog>

    <!-- 分配角色(直接角色,最终集) -->
    <el-dialog v-model="rolesDialogOpen" title="分配角色" width="520px">
      <p class="users-page__dialog-tip">
        为 {{ rolesTarget?.loginName ?? '' }} 分配直接角色:共 {{ allRoles.length }} 个可用角色。
        用户组继承的角色在用户组页维护。
      </p>
      <el-select
        v-model="selectedRoleNIds"
        multiple
        filterable
        clearable
        class="users-page__role-select"
      >
        <el-option
          v-for="role in allRoles"
          :key="role.roleNId"
          :value="role.roleNId"
          :label="role.name"
        />
      </el-select>
      <template #footer>
        <el-button @click="rolesDialogOpen = false">取消</el-button>
        <el-button type="primary" :loading="rolesSaving" @click="submitRoles">保存</el-button>
      </template>
    </el-dialog>

    <!-- 重置密码(独立权限,服务端随机临时密码) -->
    <el-dialog v-model="passwordDialogOpen" title="重置密码" width="480px">
      <p class="users-page__dialog-tip">
        重置「{{ passwordTarget?.loginName ?? '' }}」的登录密码。服务端将生成一次性临时密码,
        重置后旧密码立即失效、全部会话被撤销,该用户下次登录须修改密码。
      </p>
      <template #footer>
        <el-button @click="passwordDialogOpen = false">取消</el-button>
        <el-button type="primary" :loading="passwordSaving" @click="submitPassword"
          >确认重置</el-button
        >
      </template>
    </el-dialog>

    <!-- 一次性临时密码(只展示一次,关闭即清空,禁止持久化) -->
    <TemporaryPasswordDialog
      v-model="tempDialogOpen"
      :password="tempPassword"
      :description="tempDescription"
    />
  </AppPage>
</template>

<style scoped>
.users-page {
  display: flex;
  flex-direction: column;
  gap: var(--ip-space-4);
}

.users-page__filter {
  width: 160px;
}

.users-page__filter--status {
  width: 110px;
}

.users-page__query-mode-hint {
  margin: 0;
  color: var(--ip-color-text-secondary);
  font-size: var(--ip-font-size-sm);
}

.users-page__pagination {
  justify-content: flex-end;
}

.users-page__dialog-tip {
  margin: 0 0 var(--ip-space-3);
  font-size: var(--ip-font-size-sm);
  color: var(--ip-color-text-secondary);
}

.users-page__role-select {
  width: 100%;
}

.users-page__row-actions {
  position: relative;
  display: inline-flex;
  align-items: center;
  gap: var(--ip-space-1);
  white-space: nowrap;
}

.users-page__more-trigger {
  min-height: var(--ip-density-control-height);
  padding: 0 var(--ip-space-1);
  color: var(--ip-color-primary);
  background: transparent;
  border: 0;
  cursor: pointer;
  font: inherit;
}

.users-page__more-trigger:focus-visible {
  outline: 2px solid var(--ip-color-primary);
  outline-offset: 2px;
}

:global(.users-page__more-popper) {
  min-width: 148px;
  padding: var(--ip-space-1);
  background: var(--ip-color-bg-container);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-md);
  box-shadow: var(--ip-shadow-md);
}

:global(.users-page__more-popper .el-dropdown-menu) {
  padding: 0;
  background: transparent;
}

:global(.users-page__more-popper .el-dropdown-menu__item) {
  min-height: var(--ip-density-control-height);
  color: var(--ip-color-text-primary);
  border-radius: var(--ip-radius-sm);
}

:global(.users-page__more-popper .el-dropdown-menu__item.is-danger) {
  color: var(--ip-color-danger);
}

:global(.users-page__more-popper .el-dropdown-menu__item.is-success) {
  color: var(--ip-color-success);
}
</style>
