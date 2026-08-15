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
import { onMounted, reactive, ref } from 'vue'

import { ApiError } from '@/api/errors'
import { getManagementApi } from '@/api/identity/managementRegistry'
import type {
  RoleSummaryDto,
  UserGroupDetailDto,
  UserGroupSummaryDto,
  UserSummaryDto,
} from '@/api/identity/management'
import { PERMISSIONS, PermissionGate } from '@/permissions'

import { reportManagementError } from './shared'

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

// ---------------------------------------------------------------------------
// 列表与过滤
// ---------------------------------------------------------------------------

const loading = ref(false)
const rows = ref<UserGroupSummaryDto[]>([])
const total = ref(0)
const query = reactive({ name: '', status: '', includeDeleted: false })
const pageIndex = ref(1)
const pageSize = ref(20)

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
const dialogTitle = ref('新建用户组')
const editing = ref<UserGroupSummaryDto | null>(null)
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
  dialogTitle.value = '新建用户组'
  resetForm()
  dialogOpen.value = true
}

function openEdit(row: UserGroupSummaryDto): void {
  editing.value = row
  dialogTitle.value = '编辑用户组'
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
  <section class="groups-page">
    <div class="groups-page__toolbar">
      <el-input
        v-model="query.name"
        placeholder="用户组名称"
        clearable
        class="groups-page__filter"
        data-testid="user-groups-search"
        @keyup.enter="search"
      />
      <el-select
        v-model="query.status"
        placeholder="状态"
        clearable
        class="groups-page__filter groups-page__filter--status"
      >
        <el-option label="启用" value="Active" />
        <el-option label="禁用" value="Disabled" />
      </el-select>
      <el-checkbox v-model="query.includeDeleted" @change="search">包含已删除</el-checkbox>
      <el-button type="primary" @click="search">查询</el-button>
      <el-button @click="resetQuery">重置</el-button>
      <div class="groups-page__spacer" />
      <PermissionGate :permission-n-id="PERMISSIONS.userGroupCreate">
        <el-button type="primary" plain @click="openCreate">新建用户组</el-button>
      </PermissionGate>
    </div>

    <el-table :data="rows" v-loading="loading" row-key="groupNId" border stripe>
      <el-table-column prop="name" label="用户组名称" min-width="150" />
      <el-table-column prop="groupNId" label="业务标识" min-width="180" />
      <el-table-column prop="description" label="描述" min-width="200" show-overflow-tooltip />
      <el-table-column label="状态" width="90" align="center">
        <template #default="{ row }">
          <el-tag :type="row.status === 'Active' ? 'success' : 'danger'" effect="light">
            {{ row.status === 'Active' ? '启用' : '禁用' }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column label="成员数" width="90" align="center">
        <template #default="{ row }">{{ row.memberCount }}</template>
      </el-table-column>
      <el-table-column label="角色数" width="90" align="center">
        <template #default="{ row }">{{ row.roleCount }}</template>
      </el-table-column>
      <el-table-column label="操作" width="360" fixed="right">
        <template #default="{ row }">
          <PermissionGate :permission-n-id="PERMISSIONS.userGroupUpdate">
            <el-button link type="primary" @click="openEdit(row)">编辑</el-button>
          </PermissionGate>
          <PermissionGate :permission-n-id="PERMISSIONS.userGroupStatus">
            <el-button
              link
              :type="row.status === 'Active' ? 'danger' : 'success'"
              @click="toggleStatus(row)"
            >
              {{ row.status === 'Active' ? '禁用' : '启用' }}
            </el-button>
          </PermissionGate>
          <PermissionGate :permission-n-id="PERMISSIONS.userGroupAssignMember">
            <el-button link type="primary" @click="openManageMembers(row)">成员</el-button>
          </PermissionGate>
          <PermissionGate :permission-n-id="PERMISSIONS.userGroupAssignRole">
            <el-button link type="primary" @click="openManageRoles(row)">角色</el-button>
          </PermissionGate>
          <PermissionGate :permission-n-id="PERMISSIONS.userGroupDelete">
            <el-button
              v-if="!row.isDeleted"
              link
              type="danger"
              @click="deleteGroup(row)"
            >
              删除
            </el-button>
          </PermissionGate>
          <PermissionGate :permission-n-id="PERMISSIONS.userGroupRestore">
            <el-button v-if="row.isDeleted" link type="success" @click="restoreGroup(row)">
              恢复
            </el-button>
          </PermissionGate>
        </template>
      </el-table-column>
    </el-table>

    <el-pagination
      class="groups-page__pagination"
      layout="total, sizes, prev, pager, next, jumper"
      :total="total"
      :page-size="pageSize"
      :page-sizes="[10, 20, 50, 100]"
      :current-page="pageIndex"
      @current-change="
        (page: number) => {
          pageIndex = page
          void loadGroups()
        }
      "
      @size-change="
        (size: number) => {
          pageSize = size
          pageIndex = 1
          void loadGroups()
        }
      "
    />

    <!-- 新建 / 编辑 -->
    <el-dialog v-model="dialogOpen" :title="dialogTitle" width="560px" @closed="resetForm">
      <el-form ref="formRef" :model="form" :rules="groupRules" label-width="100px">
        <el-form-item v-if="editing === null" label="业务标识" prop="nId">
          <el-input v-model="form.nId" placeholder="可选,默认自动生成" />
        </el-form-item>
        <el-form-item label="名称" prop="name">
          <el-input v-model="form.name" placeholder="如:仓库作业组" />
        </el-form-item>
        <el-form-item label="描述" prop="description">
          <el-input v-model="form.description" type="textarea" :rows="3" placeholder="可选" />
        </el-form-item>
        <template v-if="editing === null">
          <el-form-item label="初始成员">
            <el-select
              v-model="createMemberUserNIds"
              multiple
              filterable
              clearable
              class="groups-page__select"
              placeholder="可选,创建后可在成员管理中调整"
            >
              <el-option
                v-for="user in allUsers"
                :key="user.userNId"
                :value="user.userNId"
                :label="userName(user.userNId)"
              />
            </el-select>
          </el-form-item>
          <el-form-item label="初始角色">
            <el-select
              v-model="createRoleNIds"
              multiple
              filterable
              clearable
              class="groups-page__select"
              placeholder="可选,创建后可在角色管理中调整"
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
        <el-button @click="dialogOpen = false">取消</el-button>
        <el-button type="primary" :loading="dialogSaving" @click="submitDialog">保存</el-button>
      </template>
    </el-dialog>

    <!-- 成员管理(最终成员集) -->
    <el-dialog v-model="membersDialogOpen" title="成员管理" width="560px">
      <div v-loading="membersLoading">
        <p class="groups-page__dialog-tip">
          设置「{{ membersTarget?.name ?? '' }}」的成员:勾选为最终成员集,未勾选的现有成员将被移出。
        </p>
        <el-select
          v-model="selectedMemberUserNIds"
          multiple
          filterable
          clearable
          class="groups-page__select"
          placeholder="选择用户(共 {{ allUsers.length }} 个可用用户)"
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
        <el-button @click="membersDialogOpen = false">取消</el-button>
        <el-button type="primary" :loading="membersSaving" @click="submitMembers">保存</el-button>
      </template>
    </el-dialog>

    <!-- 角色管理(最终角色集) -->
    <el-dialog v-model="rolesDialogOpen" title="角色管理" width="560px">
      <div v-loading="rolesLoading">
        <p class="groups-page__dialog-tip">
          设置「{{ rolesTarget?.name ?? '' }}」的角色:勾选为最终角色集,未勾选的现有角色将被移除;组角色即时贡献给全部成员。
        </p>
        <el-select
          v-model="selectedRoleNIds"
          multiple
          filterable
          clearable
          class="groups-page__select"
          placeholder="选择角色(共 {{ allRoles.length }} 个可用角色)"
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
        <el-button @click="rolesDialogOpen = false">取消</el-button>
        <el-button type="primary" :loading="rolesSaving" @click="submitRoles">保存</el-button>
      </template>
    </el-dialog>
  </section>
</template>

<style scoped>
.groups-page {
  display: flex;
  flex-direction: column;
  gap: var(--ip-space-4);
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
