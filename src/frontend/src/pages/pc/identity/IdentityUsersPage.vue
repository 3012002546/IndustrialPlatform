<script setup lang="ts">
/**
 * 用户管理页(TASK-ID-012,§16.3):列表分页过滤、新建/编辑表单校验、
 * 启用/禁用确认、分配角色、重置密码;409 并发冲突提示重载。
 * 操作按钮按 PermissionGate 控制(identity.user.*)。
 */
import { ElMessage, ElMessageBox } from 'element-plus'
import type { FormInstance, FormItemRule, FormRules } from 'element-plus'
import { onMounted, reactive, ref } from 'vue'

import { getManagementApi } from '@/api/identity/managementRegistry'
import type { RoleSummaryDto, UserSummaryDto } from '@/api/identity/management'
import { PERMISSIONS, PermissionGate } from '@/permissions'

import { formatTime, reportManagementError } from './shared'

/** 与后端 PasswordPolicy 对齐(§6.4):≥12 且含大小写/数字/特殊字符。 */
const SPECIAL_CHARS = '!@#$%^&*()-_=+[]{}|;:,.<>?/'

interface UserForm {
  nId: string
  loginName: string
  name: string
  initialPassword: string
  email: string
  phone: string
}

const management = getManagementApi()

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
})
const pageIndex = ref(1)
const pageSize = ref(20)

async function loadUsers(): Promise<void> {
  loading.value = true
  try {
    const result = await management.listUsers({
      nId: query.nId.trim() || undefined,
      loginName: query.loginName.trim() || undefined,
      name: query.name.trim() || undefined,
      status: query.status || undefined,
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
  pageIndex.value = 1
  void loadUsers()
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
  initialPassword: '',
  email: '',
  phone: '',
})

function resetForm(): void {
  form.nId = ''
  form.loginName = ''
  form.name = ''
  form.initialPassword = ''
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
      await management.createUser({
        nId: form.nId.trim() || undefined,
        loginName: form.loginName.trim(),
        name: form.name.trim(),
        initialPassword: form.initialPassword,
        email: emptyToNull(form.email),
        phone: emptyToNull(form.phone),
      })
      ElMessage.success('用户创建成功')
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
    }
    dialogOpen.value = false
    await loadUsers()
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
const allRoles = ref<RoleSummaryDto[]>([])
const rolesSaving = ref(false)

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

function openAssignRoles(row: UserSummaryDto): void {
  rolesTarget.value = row
  selectedRoleNIds.value = [...row.roleNIds]
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
// 重置密码
// ---------------------------------------------------------------------------

const passwordDialogOpen = ref(false)
const passwordTarget = ref<UserSummaryDto | null>(null)
const passwordFormRef = ref<FormInstance>()
const passwordSaving = ref(false)
const passwordForm = reactive({ newPassword: '', confirmPassword: '' })

function openResetPassword(row: UserSummaryDto): void {
  passwordTarget.value = row
  passwordForm.newPassword = ''
  passwordForm.confirmPassword = ''
  passwordDialogOpen.value = true
}

async function submitPassword(): Promise<void> {
  const target = passwordTarget.value
  if (target === null || passwordFormRef.value === undefined) return
  const valid = await passwordFormRef.value.validate().catch(() => false)
  if (!valid) return
  passwordSaving.value = true
  try {
    await management.resetPassword(target.userNId, { newPassword: passwordForm.newPassword })
    ElMessage.success('密码已重置')
    passwordDialogOpen.value = false
  } catch (error) {
    reportManagementError(error, '重置密码失败')
  } finally {
    passwordSaving.value = false
  }
}

// ---------------------------------------------------------------------------
// 表单校验规则
// ---------------------------------------------------------------------------

function passwordValidator(
  _rule: FormItemRule,
  value: string,
  callback: (error?: Error) => void,
): void {
  if (value.length === 0) {
    callback(new Error('请输入密码'))
    return
  }
  if (value.length < 12) {
    callback(new Error('密码长度不能少于 12 个字符'))
    return
  }
  if (value.length > 128) {
    callback(new Error('密码长度不能超过 128 个字符'))
    return
  }
  if (!/[A-Z]/.test(value)) {
    callback(new Error('密码必须包含大写字母'))
    return
  }
  if (!/[a-z]/.test(value)) {
    callback(new Error('密码必须包含小写字母'))
    return
  }
  if (!/[0-9]/.test(value)) {
    callback(new Error('密码必须包含数字'))
    return
  }
  if (!SPECIAL_CHARS.split('').some((ch) => value.includes(ch))) {
    callback(new Error('密码必须包含特殊字符'))
    return
  }
  callback()
}

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
  initialPassword: [{ validator: passwordValidator, trigger: 'blur' }],
  email: [{ type: 'email', message: '邮箱格式不正确', trigger: 'blur' }],
}

const passwordRules: FormRules = {
  newPassword: [{ validator: passwordValidator, trigger: 'blur' }],
  confirmPassword: [
    {
      validator: (_rule: FormItemRule, value: string, callback: (error?: Error) => void) => {
        if (value !== passwordForm.newPassword) {
          callback(new Error('两次输入的密码不一致'))
          return
        }
        callback()
      },
      trigger: 'blur',
    },
  ],
}

onMounted(() => {
  void loadUsers()
  void loadAllRoles()
})
</script>

<template>
  <section class="users-page">
    <div class="users-page__toolbar">
      <el-input
        v-model="query.nId"
        placeholder="业务标识"
        clearable
        class="users-page__filter"
        @keyup.enter="search"
      />
      <el-input
        v-model="query.loginName"
        placeholder="登录名"
        clearable
        class="users-page__filter"
        @keyup.enter="search"
      />
      <el-input
        v-model="query.name"
        placeholder="姓名"
        clearable
        class="users-page__filter"
        @keyup.enter="search"
      />
      <el-select
        v-model="query.status"
        placeholder="状态"
        clearable
        class="users-page__filter users-page__filter--status"
      >
        <el-option label="启用" value="Active" />
        <el-option label="禁用" value="Disabled" />
      </el-select>
      <el-button type="primary" @click="search">查询</el-button>
      <el-button @click="resetQuery">重置</el-button>
      <div class="users-page__spacer" />
      <PermissionGate :permission-n-id="PERMISSIONS.userCreate">
        <el-button type="primary" plain @click="openCreate">新建用户</el-button>
      </PermissionGate>
    </div>

    <el-table :data="rows" v-loading="loading" row-key="userNId" border stripe>
      <el-table-column prop="loginName" label="登录名" min-width="140" />
      <el-table-column prop="name" label="姓名" min-width="120" />
      <el-table-column label="状态" width="90" align="center">
        <template #default="{ row }">
          <el-tag :type="row.status === 'Active' ? 'success' : 'danger'" effect="light">
            {{ row.status === 'Active' ? '启用' : '禁用' }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column prop="email" label="邮箱" min-width="180" show-overflow-tooltip />
      <el-table-column prop="phone" label="手机号" min-width="130" />
      <el-table-column label="角色" width="80" align="center">
        <template #default="{ row }">{{ row.roleNIds.length }}</template>
      </el-table-column>
      <el-table-column label="最近登录" width="170">
        <template #default="{ row }">{{ formatTime(row.lastLoginOn) }}</template>
      </el-table-column>
      <el-table-column label="创建时间" width="170">
        <template #default="{ row }">{{ formatTime(row.createdOn) }}</template>
      </el-table-column>
      <el-table-column label="操作" width="230" fixed="right">
        <template #default="{ row }">
          <PermissionGate :permission-n-id="PERMISSIONS.userUpdate">
            <el-button link type="primary" @click="openEdit(row)">编辑</el-button>
          </PermissionGate>
          <PermissionGate :permission-n-id="PERMISSIONS.userStatus">
            <el-button
              link
              :type="row.status === 'Active' ? 'danger' : 'success'"
              @click="toggleStatus(row)"
            >
              {{ row.status === 'Active' ? '禁用' : '启用' }}
            </el-button>
          </PermissionGate>
          <PermissionGate :permission-n-id="PERMISSIONS.userAssignRole">
            <el-button link type="primary" @click="openAssignRoles(row)">分配角色</el-button>
          </PermissionGate>
          <PermissionGate :permission-n-id="PERMISSIONS.userUpdate">
            <el-button link type="warning" @click="openResetPassword(row)">重置密码</el-button>
          </PermissionGate>
        </template>
      </el-table-column>
    </el-table>

    <el-pagination
      class="users-page__pagination"
      layout="total, sizes, prev, pager, next, jumper"
      :total="total"
      :page-size="pageSize"
      :page-sizes="[10, 20, 50, 100]"
      :current-page="pageIndex"
      @current-change="
        (page: number) => {
          pageIndex = page
          void loadUsers()
        }
      "
      @size-change="
        (size: number) => {
          pageSize = size
          pageIndex = 1
          void loadUsers()
        }
      "
    />

    <!-- 新建 / 编辑 -->
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
        <el-form-item v-if="editing === null" label="初始密码" prop="initialPassword">
          <el-input
            v-model="form.initialPassword"
            type="password"
            show-password
            placeholder="≥12 位,含大小写/数字/特殊字符"
          />
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

    <!-- 分配角色 -->
    <el-dialog v-model="rolesDialogOpen" title="分配角色" width="520px">
      <p class="users-page__dialog-tip">
        为 {{ rolesTarget?.loginName ?? '' }} 分配角色:共 {{ allRoles.length }} 个可用角色。
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

    <!-- 重置密码 -->
    <el-dialog v-model="passwordDialogOpen" title="重置密码" width="480px">
      <p class="users-page__dialog-tip">
        重置「{{ passwordTarget?.loginName ?? '' }}」的登录密码,重置后旧密码立即失效。
      </p>
      <el-form
        ref="passwordFormRef"
        :model="passwordForm"
        :rules="passwordRules"
        label-width="90px"
      >
        <el-form-item label="新密码" prop="newPassword">
          <el-input v-model="passwordForm.newPassword" type="password" show-password />
        </el-form-item>
        <el-form-item label="确认密码" prop="confirmPassword">
          <el-input v-model="passwordForm.confirmPassword" type="password" show-password />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="passwordDialogOpen = false">取消</el-button>
        <el-button type="primary" :loading="passwordSaving" @click="submitPassword"
          >确认重置</el-button
        >
      </template>
    </el-dialog>
  </section>
</template>

<style scoped>
.users-page {
  display: flex;
  flex-direction: column;
  gap: var(--ip-space-4);
}

.users-page__toolbar {
  display: flex;
  flex-wrap: wrap;
  gap: var(--ip-space-3);
  align-items: center;
}

.users-page__filter {
  width: 180px;
}

.users-page__filter--status {
  width: 120px;
}

.users-page__spacer {
  flex: 1;
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
</style>
