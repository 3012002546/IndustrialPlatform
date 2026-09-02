<script setup lang="ts">
/**
 * 用户组管理页(TASK-ID-021,§29A.5/§29A.7):查询/创建/编辑/启用禁用/
 * 成员管理/角色管理/安全删除与恢复。
 * 所有写请求回传行内双版本(乐观并发);409/组异常(ID_CONCURRENCY_CONFLICT、
 * ID_GROUP_NOT_FOUND、ID_GROUP_DISABLED、ID_GROUP_ROLE_INVALID)提示并刷新列表。
 * 操作按钮按 PermissionGate 控制(identity.user-group.*);服务端执行权威授权。
 */
import { ElMessage, ElMessageBox } from 'element-plus'
import type { FormInstance, FormRules } from 'element-plus'
import { computed, onMounted, reactive, ref } from 'vue'

import { ApiError } from '@/api/errors'
import { getManagementApi } from '@/api/identity/managementRegistry'
import type {
  RoleSummaryDto,
  UserGroupDetailDto,
  UserGroupSummaryDto,
  UserSummaryDto,
} from '@/api/identity/management'
import { PERMISSIONS, PermissionGate } from '@/permissions'
import AppPage from '@/components/base/AppPage.vue'
import AppDataTable from '@/components/management/AppDataTable.vue'
import AppFormDrawer from '@/components/management/AppFormDrawer.vue'
import AppQueryPanel from '@/components/management/AppQueryPanel.vue'
import type {
  AppDataTableColumn,
  AppDataTableExportRequest,
  AppDataTableQueryMode,
  AppDataTableRequest,
} from '@/components/management/AppDataTable'

import { downloadBlob, reportManagementError } from './shared'
import { localeMessages } from '@/localization/i18n'
import { usePlatformLocale } from '@/localization/localeContext'

/** 组相关异常码:展示后端文案并刷新列表(不整页重载)。 */
const RELOAD_ERROR_CODES = new Set([
  'ID_CONCURRENCY_CONFLICT',
  'ID_GROUP_NOT_FOUND',
  'ID_GROUP_DISABLED',
  'ID_GROUP_ROLE_INVALID',
])

interface GroupForm {
  nId: string
  name: string
  description: string
}

const management = getManagementApi()
const locale = usePlatformLocale()
const copy = computed(() => localeMessages[locale.value].identity.management.userGroups)
const commonCopy = computed(() => localeMessages[locale.value].identity.management.common)

// ---------------------------------------------------------------------------
// 列表与过滤
// ---------------------------------------------------------------------------

const loading = ref(false)
const rows = ref<UserGroupSummaryDto[]>([])
const total = ref(0)
const query = reactive({ name: '', status: '', includeDeleted: false })
const pageIndex = ref(1)
const pageSize = ref(25)
const tableQueryMode = ref<AppDataTableQueryMode>('top')

const groupColumns = computed<readonly AppDataTableColumn[]>(() => [
  { field: 'name', title: copy.value.name, minWidth: 150, filter: { kind: 'text' as const } },
  { field: 'groupNId', title: copy.value.groupNId, minWidth: 180, filter: { kind: 'text' as const } },
  { field: 'description', title: copy.value.descriptionColumn, minWidth: 200, filter: { kind: 'text' as const } },
  {
    field: 'status',
    title: commonCopy.value.status,
    width: 90,
    filter: {
      kind: 'select' as const,
      options: [
        { label: copy.value.enable, value: 'Active' },
        { label: copy.value.disable, value: 'Disabled' },
      ],
    },
  },
  { field: 'memberCount', title: copy.value.memberCount, width: 90, filter: false },
  { field: 'roleCount', title: copy.value.roleCount, width: 90, filter: false },
])

async function loadGroups(): Promise<void> {
  loading.value = true
  try {
    const result = await management.listUserGroups({
      name: query.name.trim() || undefined,
      status: query.status || undefined,
      includeDeleted: query.includeDeleted || undefined,
      pageIndex: pageIndex.value,
      pageSize: pageSize.value,
    })
    rows.value = result.items
    total.value = result.total
  } catch (error) {
    reportManagementError(error, '加载用户组列表失败')
  } finally {
    loading.value = false
  }
}

function search(): void {
  pageIndex.value = 1
  void loadGroups()
}

function resetQuery(): void {
  query.name = ''
  query.status = ''
  query.includeDeleted = false
  pageIndex.value = 1
  void loadGroups()
}

function onTableQuery(request: AppDataTableRequest): void {
  pageIndex.value = request.pageIndex
  pageSize.value = request.pageSize
}

function onTableQueryModeChange(mode: AppDataTableQueryMode): void {
  tableQueryMode.value = mode
  if (mode === 'header') {
    query.name = ''
    query.status = ''
    query.includeDeleted = false
  }
  pageIndex.value = 1
}

async function loadGroupsTable(request: AppDataTableRequest) {
  const filters = request.queryMode === 'top' ? { ...query, ...request.filters } : request.filters
  const result = await management.listUserGroups({
    nId: String(filters.groupNId ?? '').trim() || undefined,
    name: String(filters.name ?? '').trim() || undefined,
    description: String(filters.description ?? '').trim() || undefined,
    keyword: String(filters.keyword ?? '').trim() || undefined,
    status: String(filters.status ?? '') || undefined,
    includeDeleted: filters.includeDeleted === true || filters.includeDeleted === 'true',
    pageIndex: request.pageIndex,
    pageSize: request.pageSize,
    sortField: request.sort?.field,
    sortOrder: request.sort?.order,
  })
  rows.value = result.items
  total.value = result.total
  return result
}

async function exportGroups(request: AppDataTableExportRequest): Promise<void> {
  if (management.exportUserGroups === undefined) return
  const filters = request.queryMode === 'top' ? { ...query, ...request.filters } : request.filters
  const blob = await management.exportUserGroups({
    nId: String(filters.groupNId ?? '').trim() || undefined,
    name: String(filters.name ?? '').trim() || undefined,
    description: String(filters.description ?? '').trim() || undefined,
    keyword: String(filters.keyword ?? '').trim() || undefined,
    status: String(filters.status ?? '') || undefined,
    includeDeleted: filters.includeDeleted === true || filters.includeDeleted === 'true',
    quantity: request.quantity,
    columns: request.columns,
    sortField: request.sort?.field,
    sortOrder: request.sort?.order,
  })
  downloadBlob(blob, `${request.filename}.xlsx`)
}

/** 组异常统一呈现:组相关错误码展示文案并刷新列表;其余走通用错误处理。 */
function reportGroupError(error: unknown, fallback: string): void {
  const code = error instanceof ApiError ? error.details.code : undefined
  if (code !== undefined && RELOAD_ERROR_CODES.has(code)) {
    const message =
      error instanceof ApiError && error.details.message.length > 0
        ? error.details.message
        : fallback
    ElMessage.error(message)
    void loadGroups()
    return
  }
  reportManagementError(error, fallback)
}

// ---------------------------------------------------------------------------
// 候选用户 / 角色选项(成员管理、角色管理、创建初始集)
// ---------------------------------------------------------------------------

const allUsers = ref<UserSummaryDto[]>([])
const allRoles = ref<RoleSummaryDto[]>([])

async function loadAllUsers(): Promise<void> {
  try {
    const collected: UserSummaryDto[] = []
    let page = 1
    let fetched: Awaited<ReturnType<typeof management.listUsers>>
    do {
      fetched = await management.listUsers({ pageIndex: page, pageSize: 100 })
      collected.push(...fetched.items)
      page += 1
    } while (collected.length < fetched.total)
    allUsers.value = collected
  } catch {
    allUsers.value = []
  }
}

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
    allRoles.value = []
  }
}

function userName(userNId: string): string {
  const user = allUsers.value.find((item) => item.userNId === userNId)
  return user === undefined ? userNId : `${user.name}(${user.loginName})`
}

// ---------------------------------------------------------------------------
// 新建 / 编辑
// ---------------------------------------------------------------------------

const dialogOpen = ref(false)
const editing = ref<UserGroupSummaryDto | null>(null)
const dialogTitle = computed(() =>
  editing.value === null ? copy.value.createTitle : copy.value.editTitle,
)
const formRef = ref<FormInstance>()
const dialogSaving = ref(false)
const form = reactive<GroupForm>({ nId: '', name: '', description: '' })
/** 创建时可选原子提交的初始成员/角色。 */
const createMemberUserNIds = ref<string[]>([])
const createRoleNIds = ref<string[]>([])

function resetForm(): void {
  form.nId = ''
  form.name = ''
  form.description = ''
  createMemberUserNIds.value = []
  createRoleNIds.value = []
}

function openCreate(): void {
  editing.value = null
  resetForm()
  dialogOpen.value = true
}

function openEdit(row: UserGroupSummaryDto): void {
  editing.value = row
  resetForm()
  form.name = row.name
  form.description = row.description ?? ''
  dialogOpen.value = true
}

async function submitDialog(): Promise<void> {
  if (formRef.value === undefined) return
  const valid = await formRef.value.validate().catch(() => false)
  if (!valid) return
  dialogSaving.value = true
  try {
    if (editing.value === null) {
      await management.createUserGroup({
        nId: form.nId.trim() || undefined,
        name: form.name.trim(),
        description: form.description.trim() || undefined,
        memberUserNIds:
          createMemberUserNIds.value.length > 0 ? [...createMemberUserNIds.value] : undefined,
        roleNIds: createRoleNIds.value.length > 0 ? [...createRoleNIds.value] : undefined,
      })
      ElMessage.success('用户组创建成功')
    } else {
      await management.updateUserGroup(editing.value.groupNId, {
        name: form.name.trim(),
        description: form.description.trim() || undefined,
        expectedOptimisticVersion: editing.value.optimisticVersion,
        expectedConcurrencyVersion: editing.value.concurrencyVersion,
      })
      ElMessage.success('用户组已更新')
    }
    dialogOpen.value = false
    await loadGroups()
  } catch (error) {
    reportGroupError(error, '保存用户组失败')
  } finally {
    dialogSaving.value = false
  }
}

// ---------------------------------------------------------------------------
// 启用 / 禁用
// ---------------------------------------------------------------------------

async function toggleStatus(row: UserGroupSummaryDto): Promise<void> {
  const isActive = row.status === 'Active'
  const action = isActive ? '禁用' : '启用'
  try {
    await ElMessageBox.confirm(
      isActive
        ? `确定禁用用户组「${row.name}」?禁用后该组不再贡献任何角色,成员与角色配置保留。`
        : `确定启用用户组「${row.name}」?启用后组角色重新生效。`,
      `${action}确认`,
      { type: 'warning', confirmButtonText: `${action}`, cancelButtonText: '取消' },
    )
  } catch {
    return // 用户取消
  }
  try {
    await management.setUserGroupStatus(row.groupNId, {
      enabled: !isActive,
      expectedOptimisticVersion: row.optimisticVersion,
      expectedConcurrencyVersion: row.concurrencyVersion,
    })
    ElMessage.success(`用户组已${action}`)
    await loadGroups()
  } catch (error) {
    reportGroupError(error, `${action}用户组失败`)
  }
}

// ---------------------------------------------------------------------------
// 成员管理(最终成员集)
// ---------------------------------------------------------------------------

const membersDialogOpen = ref(false)
const membersTarget = ref<UserGroupSummaryDto | null>(null)
const membersDetail = ref<UserGroupDetailDto | null>(null)
const membersLoading = ref(false)
const selectedMemberUserNIds = ref<string[]>([])
const membersSaving = ref(false)

async function openManageMembers(row: UserGroupSummaryDto): Promise<void> {
  membersTarget.value = row
  membersDetail.value = null
  selectedMemberUserNIds.value = []
  membersDialogOpen.value = true
  membersLoading.value = true
  try {
    const detail = await management.getUserGroup(row.groupNId)
    membersDetail.value = detail
    selectedMemberUserNIds.value = [...detail.memberUserNIds]
  } catch (error) {
    membersDialogOpen.value = false
    reportGroupError(error, '加载用户组成员失败')
  } finally {
    membersLoading.value = false
  }
}

async function submitMembers(): Promise<void> {
  const target = membersTarget.value
  const detail = membersDetail.value
  if (target === null || detail === null) return
  membersSaving.value = true
  try {
    await management.setUserGroupMembers(target.groupNId, {
      memberUserNIds: selectedMemberUserNIds.value,
      expectedOptimisticVersion: detail.optimisticVersion,
      expectedConcurrencyVersion: detail.concurrencyVersion,
    })
    ElMessage.success('成员已更新')
    membersDialogOpen.value = false
    await loadGroups()
  } catch (error) {
    reportGroupError(error, '保存成员失败')
  } finally {
    membersSaving.value = false
  }
}

// ---------------------------------------------------------------------------
// 角色管理(最终角色集)
// ---------------------------------------------------------------------------

const rolesDialogOpen = ref(false)
const rolesTarget = ref<UserGroupSummaryDto | null>(null)
const rolesDetail = ref<UserGroupDetailDto | null>(null)
const rolesLoading = ref(false)
const selectedRoleNIds = ref<string[]>([])
const rolesSaving = ref(false)

async function openManageRoles(row: UserGroupSummaryDto): Promise<void> {
  rolesTarget.value = row
  rolesDetail.value = null
  selectedRoleNIds.value = []
  rolesDialogOpen.value = true
  rolesLoading.value = true
  try {
    const detail = await management.getUserGroup(row.groupNId)
    rolesDetail.value = detail
    selectedRoleNIds.value = [...detail.roleNIds]
  } catch (error) {
    rolesDialogOpen.value = false
    reportGroupError(error, '加载用户组角色失败')
  } finally {
    rolesLoading.value = false
  }
}

async function submitRoles(): Promise<void> {
  const target = rolesTarget.value
  const detail = rolesDetail.value
  if (target === null || detail === null) return
  rolesSaving.value = true
  try {
    await management.setUserGroupRoles(target.groupNId, {
      roleNIds: selectedRoleNIds.value,
      expectedOptimisticVersion: detail.optimisticVersion,
      expectedConcurrencyVersion: detail.concurrencyVersion,
    })
    ElMessage.success('角色已更新')
    rolesDialogOpen.value = false
    await loadGroups()
  } catch (error) {
    reportGroupError(error, '保存角色失败')
  } finally {
    rolesSaving.value = false
  }
}

// ---------------------------------------------------------------------------
// 安全删除 / 恢复(§29A.5)
// ---------------------------------------------------------------------------

async function deleteGroup(row: UserGroupSummaryDto): Promise<void> {
  try {
    const { value: reason } = await ElMessageBox.prompt(
      `确定删除用户组「${row.name}」?删除为墓碑删除:有效成员与组角色关系将被解除,成员将失去该组继承的角色。`,
      '删除用户组',
      {
        type: 'warning',
        confirmButtonText: '删除',
        cancelButtonText: '取消',
        inputPlaceholder: '删除原因(可选)',
        inputValidator: () => true,
      },
    )
    await management.deleteUserGroup(row.groupNId, {
      reason: reason.trim().length > 0 ? reason.trim() : undefined,
      expectedOptimisticVersion: row.optimisticVersion,
      expectedConcurrencyVersion: row.concurrencyVersion,
    })
    ElMessage.success('用户组已删除')
    await loadGroups()
  } catch (error) {
    if (error === 'cancel' || error === 'close') return // 用户取消
    reportGroupError(error, '删除用户组失败')
  }
}

async function restoreGroup(row: UserGroupSummaryDto): Promise<void> {
  try {
    const { value: reason } = await ElMessageBox.prompt(
      `确定恢复用户组「${row.name}」?仅已删除(墓碑)用户组可恢复;恢复后状态为禁用,不自动恢复成员/角色关系。`,
      '恢复用户组',
      {
        type: 'warning',
        confirmButtonText: '恢复',
        cancelButtonText: '取消',
        inputPlaceholder: '恢复原因(可选)',
        inputValidator: () => true,
      },
    )
    await management.restoreUserGroup(row.groupNId, {
      reason: reason.trim().length > 0 ? reason.trim() : undefined,
      expectedOptimisticVersion: row.optimisticVersion,
      expectedConcurrencyVersion: row.concurrencyVersion,
    })
    ElMessage.success('用户组已恢复')
    await loadGroups()
  } catch (error) {
    if (error === 'cancel' || error === 'close') return // 用户取消
    reportGroupError(error, '恢复用户组失败')
  }
}

// ---------------------------------------------------------------------------
// 校验规则
// ---------------------------------------------------------------------------

const groupRules: FormRules = {
  nId: [
    {
      pattern: /^[a-z][a-z0-9-]{2,63}$/,
      message: '业务标识须以小写字母开头,仅含小写字母/数字/连字符',
      trigger: 'blur',
    },
  ],
  name: [
    { required: true, message: '请输入用户组名称', trigger: 'blur' },
    { min: 2, max: 32, message: '用户组名称长度 2-32 个字符', trigger: 'blur' },
  ],
}

onMounted(() => {
  void loadGroups()
  void loadAllUsers()
  void loadAllRoles()
})
</script>

<template>
  <AppPage
    class="groups-page"
    data-testid="identity-user-groups-page"
    :title="copy.title"
    :description="copy.description"
  >
    <template #breadcrumb>
      <nav :aria-label="commonCopy.pagePath">{{ copy.breadcrumb }}</nav>
    </template>
    <template #heading-meta>
      <span class="groups-page__count">{{ total }} {{ copy.countSuffix }}</span>
    </template>
    <template #actions>
      <PermissionGate :permission-n-id="PERMISSIONS.userGroupCreate">
        <el-button type="primary" data-testid="user-groups-create" @click="openCreate">
          {{ copy.create }}
        </el-button>
      </PermissionGate>
    </template>

    <AppQueryPanel
      v-if="tableQueryMode === 'top'"
      class="groups-page__query-panel"
      :title="commonCopy.queryTitle"
      :show-actions="true"
      :submit-label="commonCopy.search"
      :reset-label="commonCopy.reset"
      :grid="true"
      @submit="search"
      @reset="resetQuery"
    >
        <el-input
          v-model="query.name"
          :placeholder="copy.name"
          :aria-label="copy.name"
          clearable
          class="groups-page__filter"
          data-testid="user-groups-search"
          @keyup.enter="search"
        />
        <el-select
          v-model="query.status"
          :placeholder="commonCopy.status"
          :aria-label="commonCopy.status"
          clearable
          class="groups-page__filter groups-page__filter--status"
        >
          <el-option :label="copy.enable" value="Active" />
          <el-option :label="copy.disable" value="Disabled" />
        </el-select>
        <el-checkbox v-model="query.includeDeleted" @change="search">{{ copy.includeDeleted }}</el-checkbox>
    </AppQueryPanel>

    <AppDataTable
      table-key="identity-user-groups"
      route-key="identity-user-groups"
      row-key="groupNId"
      :rows="rows"
      :total="total"
      :loading="loading"
      :columns="groupColumns"
      :page-size="pageSize"
      :loader="loadGroupsTable"
      :exporter="exportGroups"
      @query-mode-change="onTableQueryModeChange"
      @query-change="onTableQuery"
    >
      <template #cell-status="{ row }">
        <el-tag :type="row.status === 'Active' ? 'success' : 'danger'" effect="light">
          {{ row.status === 'Active' ? copy.enable : copy.disable }}
        </el-tag>
      </template>
      <template #actions="{ row }">
        <PermissionGate :permission-n-id="PERMISSIONS.userGroupUpdate">
          <el-button link type="primary" @click="openEdit(row)">{{ copy.edit }}</el-button>
        </PermissionGate>
        <PermissionGate :permission-n-id="PERMISSIONS.userGroupStatus">
          <el-button
            link
            :type="row.status === 'Active' ? 'danger' : 'success'"
            @click="toggleStatus(row)"
          >
            {{ row.status === 'Active' ? copy.disable : copy.enable }}
          </el-button>
        </PermissionGate>
        <PermissionGate :permission-n-id="PERMISSIONS.userGroupAssignMember">
          <el-button link type="primary" @click="openManageMembers(row)">{{ copy.members }}</el-button>
        </PermissionGate>
        <PermissionGate :permission-n-id="PERMISSIONS.userGroupAssignRole">
          <el-button link type="primary" @click="openManageRoles(row)">{{ copy.roles }}</el-button>
        </PermissionGate>
        <PermissionGate :permission-n-id="PERMISSIONS.userGroupDelete">
          <el-button v-if="!row.isDeleted" link type="danger" @click="deleteGroup(row)">
            {{ copy.delete }}
          </el-button>
        </PermissionGate>
        <PermissionGate :permission-n-id="PERMISSIONS.userGroupRestore">
          <el-button v-if="row.isDeleted" link type="success" @click="restoreGroup(row)">
            {{ copy.restore }}
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
      <el-form ref="formRef" :model="form" :rules="groupRules" label-width="100px">
        <el-form-item v-if="editing === null" :label="commonCopy.businessId" prop="nId">
          <el-input v-model="form.nId" :placeholder="commonCopy.optional" />
        </el-form-item>
        <el-form-item :label="commonCopy.name" prop="name">
          <el-input v-model="form.name" :placeholder="copy.name" />
        </el-form-item>
        <el-form-item :label="commonCopy.description" prop="description">
          <el-input v-model="form.description" type="textarea" :rows="3" :placeholder="commonCopy.optional" />
        </el-form-item>
        <template v-if="editing === null">
          <el-form-item :label="copy.initialMembers">
            <el-select
              v-model="createMemberUserNIds"
              multiple
              filterable
              clearable
              class="groups-page__select"
              :placeholder="copy.selectUser"
            >
              <el-option
                v-for="user in allUsers"
                :key="user.userNId"
                :value="user.userNId"
                :label="userName(user.userNId)"
              />
            </el-select>
          </el-form-item>
          <el-form-item :label="copy.initialRoles">
            <el-select
              v-model="createRoleNIds"
              multiple
              filterable
              clearable
              class="groups-page__select"
              :placeholder="copy.selectRole"
            >
              <el-option
                v-for="role in allRoles"
                :key="role.roleNId"
                :value="role.roleNId"
                :label="role.name"
              />
            </el-select>
          </el-form-item>
        </template>
      </el-form>
      <template #footer>
        <el-button @click="dialogOpen = false">{{ commonCopy.cancel }}</el-button>
        <el-button type="primary" :loading="dialogSaving" @click="submitDialog">{{ commonCopy.save }}</el-button>
      </template>
    </AppFormDrawer>

    <!-- 成员管理(最终成员集) -->
    <AppFormDrawer v-model="membersDialogOpen" :title="copy.memberTitle" :busy="membersSaving">
      <div v-loading="membersLoading">
        <p class="groups-page__dialog-tip">
          {{ copy.memberDescription.replace('{name}', membersTarget?.name ?? '') }}
        </p>
        <el-select
          v-model="selectedMemberUserNIds"
          multiple
          filterable
          clearable
          class="groups-page__select"
          :placeholder="copy.selectUser"
        >
          <el-option
            v-for="user in allUsers"
            :key="user.userNId"
            :value="user.userNId"
            :label="userName(user.userNId)"
          />
        </el-select>
      </div>
      <template #footer>
        <el-button @click="membersDialogOpen = false">{{ commonCopy.cancel }}</el-button>
        <el-button type="primary" :loading="membersSaving" @click="submitMembers">{{ commonCopy.save }}</el-button>
      </template>
    </AppFormDrawer>

    <!-- 角色管理(最终角色集) -->
    <AppFormDrawer v-model="rolesDialogOpen" :title="copy.roleTitle" :busy="rolesSaving">
      <div v-loading="rolesLoading">
        <p class="groups-page__dialog-tip">
          {{ copy.roleDescription.replace('{name}', rolesTarget?.name ?? '') }}
        </p>
        <el-select
          v-model="selectedRoleNIds"
          multiple
          filterable
          clearable
          class="groups-page__select"
          :placeholder="copy.selectRole"
        >
          <el-option
            v-for="role in allRoles"
            :key="role.roleNId"
            :value="role.roleNId"
            :label="role.name"
          />
        </el-select>
      </div>
      <template #footer>
        <el-button @click="rolesDialogOpen = false">{{ commonCopy.cancel }}</el-button>
        <el-button type="primary" :loading="rolesSaving" @click="submitRoles">{{ commonCopy.save }}</el-button>
      </template>
    </AppFormDrawer>
  </AppPage>
</template>

<style scoped>
.groups-page {
  display: flex;
  flex-direction: column;
  gap: 0;
  overflow: hidden;
  background: var(--ip-color-bg-container);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-lg);
  min-width: 0;
}

.groups-page :deep(.app-page__header) {
  padding: 18px 20px 17px;
  border-bottom: 1px solid var(--ip-color-border);
}

.groups-page :deep(.app-page__body) {
  display: flex;
  flex-direction: column;
  min-width: 0;
  min-height: 0;
}

.groups-page :deep(.app-data-table) {
  flex: 1 1 auto;
  min-height: 0;
}

.groups-page :deep(.app-data-table__card) {
  display: flex;
  min-height: 0;
  flex: 1 1 auto;
  flex-direction: column;
}

.groups-page :deep(.app-query-panel) {
  gap: 0;
  padding: 14px 20px 16px;
  border-bottom: 1px solid var(--ip-color-border);
}

.groups-page :deep(.app-query-panel__header) {
  margin-bottom: var(--ip-space-3);
}

.groups-page :deep(.app-query-panel__body) {
  gap: 12px;
}

.groups-page__count {
  color: var(--ip-color-text-secondary);
  font-size: var(--ip-font-size-sm);
}

.groups-page__toolbar {
  display: flex;
  flex-wrap: wrap;
  gap: var(--ip-space-3);
  align-items: center;
}

.groups-page__filter {
  width: 200px;
}

.groups-page__filter--status {
  width: 120px;
}

.groups-page__spacer {
  flex: 1;
}

.groups-page__pagination {
  justify-content: flex-end;
}

.groups-page__dialog-tip {
  margin: 0 0 var(--ip-space-3);
  font-size: var(--ip-font-size-sm);
  color: var(--ip-color-text-secondary);
}

.groups-page__select {
  width: 100%;
}
</style>
