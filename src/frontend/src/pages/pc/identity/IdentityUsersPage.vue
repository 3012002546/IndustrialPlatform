<script setup lang="ts">
/**
 * 用户管理页(TASK-ID-012/§16.3,§29A.5):列表分页过滤(含用户组/角色/已删除)、
 * 新建(服务端随机临时密码)/编辑、启用/禁用确认、分配角色、安全删除/恢复、
 * 独立重置密码权限;409 并发冲突提示重载。
 * 临时密码只经一次性弹窗展示,禁止持久化。操作按钮按 PermissionGate 控制(identity.user.*)。
 */
import { ElDropdown, ElDropdownItem, ElDropdownMenu, ElIcon, ElMessage, ElMessageBox } from 'element-plus'
import { ArrowDown, Plus } from '@element-plus/icons-vue'
import type { FormInstance, FormRules } from 'element-plus'
import { computed, nextTick, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue'

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
import { useAuthStore } from '@/stores/authStore'
import { readPageState, writePageState } from '@/workspace/pageState'
import type { UserUiScope } from '@/theme/types'

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
const authStore = useAuthStore()
const localization = useLocalizationStore()
const commonCopy = computed(() => localeMessages[localization.locale].common.action)
const copy = computed(() => localeMessages[localization.locale].identity.user)
const dialogCopy = computed(() => copy.value.copy)

function formatUserTime(value: string | null | undefined): string {
  return formatTime(value, {
    locale: localization.locale,
    timeZone: localization.preferences.timeZone,
  })
}

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
const advancedQueryOpen = ref(false)

const USER_ROW_SELECT = [
  'userNId',
  'loginName',
  'name',
  'email',
  'phone',
  'status',
  'tenantNId',
  'createdOn',
  'lastLoginOn',
  'mustChangePassword',
  'directRoleNIds',
  'groupRoleNIds',
  'effectiveRoleNIds',
  'effectiveRoleCount',
  'optimisticVersion',
  'concurrencyVersion',
  'isDeleted',
] as const

const IDENTITY_USERS_TAB_ID = 'identity-users'

function currentPageStateScope(): UserUiScope | null {
  const user = authStore.user
  return user === null ? null : { tenantId: user.tenantId, userId: user.userId }
}

function pageScrollElement(): HTMLElement | null {
  const main = document.querySelector<HTMLElement>('.ip-pc-main')
  if (main !== null) return main
  return document.scrollingElement instanceof HTMLElement ? document.scrollingElement : null
}

function readQueryValue(value: string | string[] | undefined): string {
  return Array.isArray(value) ? String(value[0] ?? '') : (value ?? '')
}

function restorePageState(): void {
  const scope = currentPageStateScope()
  if (scope === null) return
  const saved = readPageState(sessionStorage, scope, IDENTITY_USERS_TAB_ID)
  if (saved === null) return

  const savedQuery = saved.query ?? {}
  query.nId = readQueryValue(savedQuery.nId)
  query.loginName = readQueryValue(savedQuery.loginName)
  query.name = readQueryValue(savedQuery.name)
  query.status = readQueryValue(savedQuery.status)
  query.groupNId = readQueryValue(savedQuery.groupNId)
  query.roleNId = readQueryValue(savedQuery.roleNId)
  query.includeDeleted = readQueryValue(savedQuery.includeDeleted) === 'true'
  if (saved.pageIndex !== undefined) pageIndex.value = saved.pageIndex
  if (saved.pageSize !== undefined) pageSize.value = saved.pageSize

  if (saved.scrollTop !== undefined) {
    void nextTick(() => {
      const scroller = pageScrollElement()
      if (scroller !== null) scroller.scrollTop = saved.scrollTop ?? 0
    })
  }
}

function persistPageState(): void {
  const scope = currentPageStateScope()
  if (scope === null) return
  writePageState(sessionStorage, scope, IDENTITY_USERS_TAB_ID, {
    query: {
      nId: query.nId,
      loginName: query.loginName,
      name: query.name,
      status: query.status,
      groupNId: query.groupNId,
      roleNId: query.roleNId,
      includeDeleted: String(query.includeDeleted),
    },
    pageIndex: pageIndex.value,
    pageSize: pageSize.value,
    scrollTop: pageScrollElement()?.scrollTop ?? 0,
  })
}

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

function roleNames(roleNIds: readonly string[] | undefined): string {
  return (roleNIds ?? []).map(roleName).join('、') || '—'
}

/**
 * OData `$select` is intentionally a partial projection. Keep the page row
 * shape safe for the cell slots without pretending omitted fields were
 * returned by the server.
 */
function normalizeODataUser(item: UserSummaryDto): UserSummaryDto {
  const directRoleNIds = Array.isArray(item.directRoleNIds) ? item.directRoleNIds : []
  const groupRoleNIds = Array.isArray(item.groupRoleNIds) ? item.groupRoleNIds : []
  const effectiveRoleNIds = Array.isArray(item.effectiveRoleNIds) ? item.effectiveRoleNIds : []
  return {
    ...item,
    directRoleNIds,
    groupRoleNIds,
    effectiveRoleNIds,
    effectiveRoleCount: item.effectiveRoleCount ?? effectiveRoleNIds.length,
  }
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
    reportManagementError(error, copy.value.copy.loadFailed)
  } finally {
    loading.value = false
  }
}

function search(): void {
  pageIndex.value = 1
  persistPageState()
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
  persistPageState()
  void loadUsers()
}

function onTableQuery(request: AppDataTableRequest): void {
  pageIndex.value = request.pageIndex
  pageSize.value = request.pageSize
  persistPageState()
}

function onTableLoadError(error: unknown): void {
  reportManagementError(error, copy.value.copy.loadFailed)
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
  persistPageState()
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
    select: [...new Set([...(request.descriptor?.select ?? []), ...USER_ROW_SELECT])],
    pageIndex: 'pageIndex' in request ? request.pageIndex : 1,
    pageSize: 'pageSize' in request ? request.pageSize : 100,
  }
}

function hasLegacyOnlyTopConditions(): boolean {
  return query.groupNId.trim() !== '' || query.roleNId.trim() !== '' || query.includeDeleted
}

function buildLegacyTopQuery(request: AppDataTableRequest): Parameters<typeof management.listUsers>[0] {
  return {
    nId: query.nId.trim() || undefined,
    loginName: query.loginName.trim() || undefined,
    name: query.name.trim() || undefined,
    status: query.status || undefined,
    groupNId: query.groupNId.trim() || undefined,
    roleNId: query.roleNId.trim() || undefined,
    includeDeleted: query.includeDeleted || undefined,
    pageIndex: request.pageIndex,
    pageSize: request.pageSize,
    sortField: request.sort?.field,
    sortOrder: request.sort?.order,
  }
}

async function exportUsers(request: AppDataTableExportRequest): Promise<void> {
  if (
    !hasLegacyOnlyTopConditions() &&
    management.exportUsersOData !== undefined &&
    request.descriptor !== undefined
  ) {
    const blob = await management.exportUsersOData(
      buildUserQueryDescriptor(request),
      request.columns,
      request.quantity,
      document.documentElement.lang || 'zh-CN',
      localization.preferences.timeZone,
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
  if (request.queryMode === 'top' && hasLegacyOnlyTopConditions()) {
    const result = await management.listUsers(buildLegacyTopQuery(request))
    rows.value = result.items
    total.value = result.total
    return result
  }
  const result = await management.listUsersOData(buildUserQueryDescriptor(request))
  rows.value = result.items.map(normalizeODataUser)
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
const editing = ref<UserSummaryDto | null>(null)
const dialogTitle = computed(() => (editing.value === null ? copy.value.create : copy.value.edit))
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
  resetForm()
  dialogOpen.value = true
}

function openEdit(row: UserSummaryDto): void {
  editing.value = row
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
      ElMessage.success(copy.value.copy.createdSuccess)
      dialogOpen.value = false
      await loadUsers()
      // 服务端随机临时密码只出现一次,立即经一次性弹窗展示。
      showTemporaryPassword(
        result.temporaryPassword,
        copy.value.copy.createdDescription.replace('{loginName}', result.user.loginName),
      )
    } else {
      await management.updateUser(editing.value.userNId, {
        loginName: form.loginName.trim(),
        name: form.name.trim(),
        email: emptyToNull(form.email),
        phone: emptyToNull(form.phone),
        expectedOptimisticVersion: editing.value.optimisticVersion,
        expectedConcurrencyVersion: editing.value.concurrencyVersion,
      })
      ElMessage.success(copy.value.copy.updatedSuccess)
      dialogOpen.value = false
      await loadUsers()
    }
  } catch (error) {
    reportManagementError(error, copy.value.copy.saveFailed)
  } finally {
    dialogSaving.value = false
  }
}

// ---------------------------------------------------------------------------
// 启用 / 禁用(状态确认)
// ---------------------------------------------------------------------------

async function toggleStatus(row: UserSummaryDto): Promise<void> {
  const isActive = row.status === 'Active'
  const action = isActive ? copy.value.disable : copy.value.enable
  try {
    await ElMessageBox.confirm(
      isActive
        ? copy.value.copy.statusDisableConfirm.replace('{loginName}', row.loginName)
        : copy.value.copy.statusEnableConfirm.replace('{loginName}', row.loginName),
      copy.value.copy.statusConfirmTitle.replace('{action}', action),
      { type: 'warning', confirmButtonText: action, cancelButtonText: commonCopy.value.cancel },
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
    ElMessage.success(copy.value.copy.statusUpdated.replace('{action}', action))
    await loadUsers()
  } catch (error) {
    reportManagementError(error, copy.value.copy.statusActionFailed.replace('{action}', action))
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
    ElMessage.success(copy.value.copy.rolesUpdated)
    rolesDialogOpen.value = false
    await loadUsers()
  } catch (error) {
    reportManagementError(error, copy.value.copy.rolesSaveFailed)
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
    ElMessage.success(copy.value.copy.passwordResetSuccess)
    passwordDialogOpen.value = false
    // 重置强制首次改密并撤销全部会话;临时密码只出现一次。
    showTemporaryPassword(
      result.temporaryPassword,
      copy.value.copy.passwordDescription.replace('{loginName}', target.loginName),
    )
  } catch (error) {
    reportManagementError(error, copy.value.copy.passwordResetFailed)
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
      copy.value.copy.deleteConfirm.replace('{loginName}', row.loginName),
      copy.value.copy.deleteTitle,
      {
        type: 'warning',
        confirmButtonText: copy.value.copy.deleteConfirmButton,
        cancelButtonText: commonCopy.value.cancel,
        inputPlaceholder: copy.value.copy.deleteReasonPlaceholder,
        inputValidator: () => true,
      },
    )
    await management.deleteUser(row.userNId, {
      reason: reason.trim().length > 0 ? reason.trim() : undefined,
      expectedOptimisticVersion: row.optimisticVersion,
      expectedConcurrencyVersion: row.concurrencyVersion,
    })
    ElMessage.success(copy.value.copy.deletedSuccess)
    await loadUsers()
  } catch (error) {
    if (error === 'cancel' || error === 'close') return // 用户取消
    reportManagementError(error, copy.value.copy.deleteFailed)
  }
}

async function restoreUser(row: UserSummaryDto): Promise<void> {
  try {
    const { value: reason } = await ElMessageBox.prompt(
      copy.value.copy.restoreConfirm.replace('{loginName}', row.loginName),
      copy.value.copy.restoreTitle,
      {
        type: 'warning',
        confirmButtonText: copy.value.copy.restoreConfirmButton,
        cancelButtonText: commonCopy.value.cancel,
        inputPlaceholder: copy.value.copy.restoreReasonPlaceholder,
        inputValidator: () => true,
      },
    )
    await management.restoreUser(row.userNId, {
      reason: reason.trim().length > 0 ? reason.trim() : undefined,
      expectedOptimisticVersion: row.optimisticVersion,
      expectedConcurrencyVersion: row.concurrencyVersion,
    })
    ElMessage.success(copy.value.copy.restoredSuccess)
    await loadUsers()
  } catch (error) {
    if (error === 'cancel' || error === 'close') return // 用户取消
    reportManagementError(error, copy.value.copy.restoreFailed)
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
      message: copy.value.copy.businessIdRule,
      trigger: 'blur',
    },
  ],
  loginName: [
    { required: true, message: copy.value.copy.loginRequired, trigger: 'blur' },
    { min: 3, max: 64, message: copy.value.copy.loginLength, trigger: 'blur' },
  ],
  name: [{ required: true, message: copy.value.copy.nameRequired, trigger: 'blur' }],
  email: [{ type: 'email', message: copy.value.copy.emailInvalid, trigger: 'blur' }],
}

onMounted(() => {
  restorePageState()
  void loadUsers()
  void loadAllRoles()
  void loadAllGroups()
})

onBeforeUnmount(() => {
  persistPageState()
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
      <nav :aria-label="dialogCopy.pagePath">{{ copy.breadcrumb }}</nav>
    </template>
    <template #heading-meta>
      <span class="users-page__count" data-testid="identity-users-total">
        {{ total }} {{ copy.userCountSuffix }}
      </span>
    </template>
    <template #actions>
      <PermissionGate :permission-n-id="PERMISSIONS.userCreate">
        <el-button
          type="primary"
          data-testid="identity-users-create"
          @click="openCreate"
        >
          <ElIcon class="users-page__create-icon" aria-hidden="true"><Plus /></ElIcon>
          {{ copy.create }}
        </el-button>
      </PermissionGate>
    </template>

    <AppQueryPanel
      data-testid="identity-users-query"
      :show-actions="true"
      :submit-label="commonCopy.search"
      :reset-label="commonCopy.reset"
      :grid="true"
      @submit="search"
      @reset="resetQuery"
    >
      <template #body-actions>
        <button
          type="button"
          class="users-page__more-conditions"
          data-testid="query-panel-toggle"
          :aria-expanded="advancedQueryOpen"
          aria-controls="identity-users-advanced-query"
          @click="advancedQueryOpen = !advancedQueryOpen"
        >
          {{ copy.moreConditions }}
          <ArrowDown aria-hidden="true" />
        </button>
      </template>
      <template v-if="tableQueryMode === 'top'">
        <label class="users-page__field users-page__field-login">
          <span>{{ copy.loginName }}</span>
          <el-input
            v-model="query.loginName"
            :placeholder="copy.loginName"
            :aria-label="copy.loginName"
            clearable
            class="users-page__filter users-page__filter--login"
            @keyup.enter="search"
          />
        </label>
        <label class="users-page__field users-page__field-name">
          <span>{{ copy.name }}</span>
          <el-input
            v-model="query.name"
            :placeholder="copy.name"
            :aria-label="copy.name"
            clearable
            class="users-page__filter users-page__filter--name"
            @keyup.enter="search"
          />
        </label>
        <label class="users-page__field users-page__field-status">
          <span>{{ copy.status }}</span>
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
        </label>
        <label class="users-page__field users-page__field-group">
          <span>{{ copy.group }}</span>
          <el-select
            v-model="query.groupNId"
            :placeholder="copy.group"
            :aria-label="copy.group"
            clearable
            filterable
            class="users-page__filter users-page__filter--group"
          >
            <el-option
              v-for="group in allGroups"
              :key="group.groupNId"
              :value="group.groupNId"
              :label="group.name"
            />
          </el-select>
        </label>
        <label class="users-page__field users-page__field-role">
          <span>{{ copy.role }}</span>
          <el-select
            v-model="query.roleNId"
            :placeholder="copy.role"
            :aria-label="copy.role"
            clearable
            filterable
            class="users-page__filter users-page__filter--role"
          >
            <el-option
              v-for="role in allRoles"
              :key="role.roleNId"
              :value="role.roleNId"
              :label="role.name"
            />
          </el-select>
        </label>
        <div
          v-if="advancedQueryOpen"
          id="identity-users-advanced-query"
          class="users-page__advanced-fields"
        >
          <label class="users-page__field users-page__field-business-id">
            <span>{{ copy.businessId }}</span>
            <el-input
              v-model="query.nId"
              :placeholder="copy.businessId"
              :aria-label="copy.businessId"
              clearable
              class="users-page__filter users-page__filter--business-id"
              @keyup.enter="search"
            />
          </label>
          <label class="users-page__include-deleted">
            <el-checkbox v-model="query.includeDeleted" :aria-label="copy.includeDeleted" @change="search">
              {{ copy.includeDeleted }}
            </el-checkbox>
          </label>
        </div>
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
      :toolbar-title="copy.userList"
      :toolbar-labels="true"
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
          :content="`${dialogCopy.directRoles}:${roleNames(row.directRoleNIds)} / ${dialogCopy.inheritedRoles}:${roleNames(row.groupRoleNIds)}`"
          placement="top"
          :show-after="300"
        >
          <span>{{ row.effectiveRoleCount ?? row.effectiveRoleNIds?.length ?? 0 }}</span>
        </el-tooltip>
      </template>
      <template #cell-lastLoginOn="{ row }">{{ formatUserTime(row.lastLoginOn) }}</template>
      <template #cell-createdOn="{ row }">{{ formatUserTime(row.createdOn) }}</template>
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
    <el-dialog v-model="detailOpen" :title="dialogCopy.dialogDetail" width="560px">
      <el-descriptions v-if="detailTarget" :column="1" border>
        <el-descriptions-item :label="copy.loginName">{{ detailTarget.loginName }}</el-descriptions-item>
        <el-descriptions-item :label="copy.name">{{ detailTarget.name }}</el-descriptions-item>
        <el-descriptions-item :label="copy.businessId">{{ detailTarget.userNId }}</el-descriptions-item>
        <el-descriptions-item :label="copy.status">
          {{ detailTarget.status === 'Active' ? copy.enabled : copy.disabled }}
        </el-descriptions-item>
        <el-descriptions-item :label="copy.mustChangePassword">
          {{ detailTarget.mustChangePassword ? dialogCopy.needsChange : dialogCopy.noChange }}
        </el-descriptions-item>
        <el-descriptions-item :label="copy.email">{{ detailTarget.email ?? '—' }}</el-descriptions-item>
        <el-descriptions-item :label="copy.phone">{{ detailTarget.phone ?? '—' }}</el-descriptions-item>
        <el-descriptions-item :label="copy.lastLoginOn">{{
          formatUserTime(detailTarget.lastLoginOn)
        }}</el-descriptions-item>
        <el-descriptions-item :label="copy.createdOn">{{
          formatUserTime(detailTarget.createdOn)
        }}</el-descriptions-item>
        <el-descriptions-item :label="dialogCopy.directRoles">{{
          roleNames(detailTarget.directRoleNIds)
        }}</el-descriptions-item>
        <el-descriptions-item :label="dialogCopy.inheritedRoles">{{
          roleNames(detailTarget.groupRoleNIds)
        }}</el-descriptions-item>
        <el-descriptions-item :label="dialogCopy.effectiveRoles">{{
          roleNames(detailTarget.effectiveRoleNIds)
        }}</el-descriptions-item>
      </el-descriptions>
      <template #footer>
        <el-button type="primary" @click="detailOpen = false">{{ dialogCopy.close }}</el-button>
      </template>
    </el-dialog>

    <!-- 新建 / 编辑(创建不再录入初始密码:服务端生成随机临时密码) -->
    <el-dialog v-model="dialogOpen" :title="dialogTitle" width="520px" @closed="resetForm">
      <el-form ref="formRef" :model="form" :rules="userRules" label-width="100px">
        <el-form-item v-if="editing === null" :label="copy.businessId" prop="nId">
          <el-input v-model="form.nId" :placeholder="dialogCopy.optionalAuto" />
        </el-form-item>
        <el-form-item :label="copy.loginName" prop="loginName">
          <el-input v-model="form.loginName" :placeholder="dialogCopy.loginPlaceholder" />
        </el-form-item>
        <el-form-item :label="copy.name" prop="name">
          <el-input v-model="form.name" :placeholder="dialogCopy.displayNamePlaceholder" />
        </el-form-item>
        <el-form-item :label="copy.email" prop="email">
          <el-input v-model="form.email" :placeholder="dialogCopy.optional" />
        </el-form-item>
        <el-form-item :label="copy.phone" prop="phone">
          <el-input v-model="form.phone" :placeholder="dialogCopy.optional" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogOpen = false">{{ commonCopy.cancel }}</el-button>
        <el-button type="primary" :loading="dialogSaving" @click="submitDialog">{{ commonCopy.save }}</el-button>
      </template>
    </el-dialog>

    <!-- 分配角色(直接角色,最终集) -->
    <el-dialog v-model="rolesDialogOpen" :title="dialogCopy.assignRole" width="520px">
      <p class="users-page__dialog-tip">
        {{
          dialogCopy.rolesDescription
            .replace('{loginName}', rolesTarget?.loginName ?? '')
            .replace('{count}', String(allRoles.length))
        }}
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
        <el-button @click="rolesDialogOpen = false">{{ commonCopy.cancel }}</el-button>
        <el-button type="primary" :loading="rolesSaving" @click="submitRoles">{{ commonCopy.save }}</el-button>
      </template>
    </el-dialog>

    <!-- 重置密码(独立权限,服务端随机临时密码) -->
    <el-dialog v-model="passwordDialogOpen" :title="dialogCopy.resetPassword" width="480px">
      <p class="users-page__dialog-tip">
        {{ dialogCopy.passwordDescription.replace('{loginName}', passwordTarget?.loginName ?? '') }}
      </p>
      <template #footer>
        <el-button @click="passwordDialogOpen = false">{{ commonCopy.cancel }}</el-button>
        <el-button type="primary" :loading="passwordSaving" @click="submitPassword"
          >{{ dialogCopy.confirmReset }}</el-button
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
  gap: 0;
  overflow: hidden;
  background: var(--ip-color-bg-container);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-lg);
  min-width: 0;
}

.users-page :deep(.app-page__header) {
  padding: 18px 20px 17px;
  border-bottom: 1px solid var(--ip-color-border);
}

.users-page :deep(.app-page__body) {
  display: flex;
  flex-direction: column;
  min-width: 0;
  min-height: 0;
}

.users-page :deep(.app-data-table) {
  flex: 1 1 auto;
  min-height: 0;
}

.users-page :deep(.app-data-table__card) {
  display: flex;
  min-height: 0;
  flex: 1 1 auto;
  flex-direction: column;
}

.users-page :deep(.app-query-panel) {
  gap: 0;
  padding: 14px 20px 16px;
  border-bottom: 1px solid var(--ip-color-border);
}

.users-page :deep(.app-query-panel__header) {
  justify-content: flex-end;
  margin-bottom: var(--ip-space-3);
}

.users-page :deep(.app-query-panel__body) {
  gap: 12px;
}

.users-page :deep(.app-query-panel__toggle) {
  padding: 0;
  border: 0;
  color: var(--ip-color-primary);
}

.users-page__more-conditions {
  display: inline-flex;
  align-items: center;
  gap: var(--ip-space-1);
  min-height: var(--ip-density-control-height);
  padding: 0;
  color: var(--ip-color-primary);
  background: transparent;
  border: 0;
  cursor: pointer;
  font-family: inherit;
  font-size: var(--ip-font-size-xs);
  line-height: 1.2;
}

.users-page__more-conditions > svg,
.users-page :deep(.el-button > svg) {
  flex: 0 0 auto;
  width: 14px;
  height: 14px;
}

.users-page :deep(.users-page__create-icon) {
  display: inline-flex;
  flex: 0 0 14px;
  width: 14px;
  height: 14px;
}

.users-page :deep(.users-page__create-icon > svg) {
  width: 14px;
  height: 14px;
}

.users-page__count {
  display: inline-flex;
  min-height: 22px;
  align-items: center;
  box-sizing: border-box;
  padding: 0 var(--ip-space-2);
  color: var(--ip-color-text-secondary);
  background: var(--ip-color-bg-muted);
  border: 1px solid var(--ip-color-border);
  border-radius: 999px;
  font-size: var(--ip-font-size-xs);
  font-weight: 500;
  line-height: 1;
  white-space: nowrap;
}

.users-page__more-conditions:focus-visible {
  outline: 2px solid var(--ip-focus-ring-color);
  outline-offset: 2px;
}

.users-page__field {
  display: flex;
  flex: 0 0 auto;
  flex-direction: column;
  gap: 6px;
  color: var(--ip-color-text-secondary);
  font-size: var(--ip-font-size-sm);
  font-weight: 500;
}

.users-page__field-login,
.users-page__field-business-id { width: 154px; }
.users-page__field-name { width: 128px; }
.users-page__field-status { width: 116px; }
.users-page__field-group,
.users-page__field-role { width: 138px; }

@media (min-width: 960px) and (max-width: 1280px) {
  .users-page :deep(.app-query-panel__body--grid) {
    flex-wrap: nowrap;
    gap: 8px;
  }

  .users-page__field-login,
  .users-page__field-business-id { width: 130px; }
  .users-page__field-name { width: 110px; }
  .users-page__field-status { width: 100px; }
  .users-page__field-group,
  .users-page__field-role { width: 120px; }

  .users-page :deep(.app-query-panel__body-actions) {
    flex-wrap: nowrap;
    gap: 6px;
  }

  .users-page__more-conditions {
    white-space: nowrap;
  }
}

.users-page__field :deep(.el-input),
.users-page__field :deep(.el-select) { width: 100%; }

.users-page__include-deleted {
  display: inline-flex;
  align-items: center;
  min-height: var(--ip-density-control-height);
}

.users-page__advanced-fields {
  display: contents;
}

.users-page__filter {
  width: 100%;
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
