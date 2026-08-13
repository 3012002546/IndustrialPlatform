<script setup lang="ts">
/**
 * 角色管理页(TASK-ID-012,§16.3):列表分页过滤、新建/编辑表单校验、
 * 分配权限(权限目录树);系统角色不可编辑/分配;409 并发冲突提示重载。
 */
import { ElMessage } from 'element-plus'
import type { FormInstance, FormRules } from 'element-plus'
import { onMounted, reactive, ref } from 'vue'

import type { PermissionTreeNodeDto, RoleSummaryDto } from '@/api/identity/management'
import { getManagementApi } from '@/api/identity/managementRegistry'
import { PERMISSIONS, PermissionGate } from '@/permissions'

import { reportManagementError } from './shared'

const management = getManagementApi()

// ---------------------------------------------------------------------------
// 列表与过滤
// ---------------------------------------------------------------------------

const loading = ref(false)
const rows = ref<RoleSummaryDto[]>([])
const total = ref(0)
const query = reactive({ nId: '', name: '' })
const pageIndex = ref(1)
const pageSize = ref(20)

async function loadRoles(): Promise<void> {
  loading.value = true
  try {
    const result = await management.listRoles({
      nId: query.nId.trim() || undefined,
      name: query.name.trim() || undefined,
      pageIndex: pageIndex.value,
      pageSize: pageSize.value,
    })
    rows.value = result.items
    total.value = result.total
  } catch (error) {
    reportManagementError(error, '加载角色列表失败')
  } finally {
    loading.value = false
  }
}

function search(): void {
  pageIndex.value = 1
  void loadRoles()
}

function resetQuery(): void {
  query.nId = ''
  query.name = ''
  pageIndex.value = 1
  void loadRoles()
}

// ---------------------------------------------------------------------------
// 新建 / 编辑
// ---------------------------------------------------------------------------

const dialogOpen = ref(false)
const dialogTitle = ref('新建角色')
const editing = ref<RoleSummaryDto | null>(null)
const formRef = ref<FormInstance>()
const dialogSaving = ref(false)
const form = reactive({ nId: '', name: '', description: '' })

function openCreate(): void {
  editing.value = null
  dialogTitle.value = '新建角色'
  form.nId = ''
  form.name = ''
  form.description = ''
  dialogOpen.value = true
}

function openEdit(row: RoleSummaryDto): void {
  editing.value = row
  dialogTitle.value = '编辑角色'
  form.nId = row.roleNId
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
      await management.createRole({
        nId: form.nId.trim() || undefined,
        name: form.name.trim(),
        description: form.description.trim() || undefined,
      })
      ElMessage.success('角色创建成功')
    } else {
      await management.updateRole(editing.value.roleNId, {
        name: form.name.trim(),
        description: form.description.trim() || undefined,
        expectedOptimisticVersion: editing.value.optimisticVersion,
        expectedConcurrencyVersion: editing.value.concurrencyVersion,
      })
      ElMessage.success('角色已更新')
    }
    dialogOpen.value = false
    await loadRoles()
  } catch (error) {
    reportManagementError(error, '保存角色失败')
  } finally {
    dialogSaving.value = false
  }
}

// ---------------------------------------------------------------------------
// 分配权限(权限目录树)
// ---------------------------------------------------------------------------

const permissionDialogOpen = ref(false)
const permissionTarget = ref<RoleSummaryDto | null>(null)
/** el-tree 数据形态(node-key=permissionNId)。 */
interface TreeData {
  id: string
  label: string
  children?: TreeData[]
}
const permissionTree = ref<TreeData[]>([])
const checkedPermissionNIds = ref<string[]>([])
const permissionTreeRef = ref()
const permissionSaving = ref(false)
const treeLoading = ref(false)

/** 权限树 → el-tree data(node-key=permissionNId)。 */
function toTreeData(nodes: PermissionTreeNodeDto[]): TreeData[] {
  return nodes.map((node) => ({
    id: node.permissionNId,
    label: node.name,
    ...(node.children.length > 0 ? { children: toTreeData(node.children) } : {}),
  }))
}

async function loadPermissionTree(): Promise<void> {
  treeLoading.value = true
  try {
    permissionTree.value = toTreeData(await management.getPermissionTree())
  } catch (error) {
    reportManagementError(error, '加载权限目录失败')
    permissionTree.value = []
  } finally {
    treeLoading.value = false
  }
}

function openAssignPermissions(row: RoleSummaryDto): void {
  permissionTarget.value = row
  checkedPermissionNIds.value = []
  permissionDialogOpen.value = true
  // 树数据就绪后回填已授权项并展开。
  void loadPermissionTree().then(() => {
    checkedPermissionNIds.value = [...row.permissionNIds]
    const tree = permissionTreeRef.value
    if (tree !== undefined) tree.setCheckedKeys(checkedPermissionNIds.value)
  })
}

async function submitPermissions(): Promise<void> {
  const target = permissionTarget.value
  if (target === null) return
  const tree = permissionTreeRef.value
  const checked = tree === undefined ? [] : (tree.getCheckedKeys() as string[])
  const halfChecked = tree === undefined ? [] : (tree.getHalfCheckedKeys() as string[])
  // 半选(父节点)一并提交,保证权限意图完整。
  const permissionNIds = [...new Set([...checked, ...halfChecked])]
  permissionSaving.value = true
  try {
    await management.assignRolePermissions(target.roleNId, {
      permissionNIds,
      expectedOptimisticVersion: target.optimisticVersion,
      expectedConcurrencyVersion: target.concurrencyVersion,
    })
    ElMessage.success('权限已更新')
    permissionDialogOpen.value = false
    await loadRoles()
  } catch (error) {
    reportManagementError(error, '保存权限分配失败')
  } finally {
    permissionSaving.value = false
  }
}

// ---------------------------------------------------------------------------
// 校验规则
// ---------------------------------------------------------------------------

const roleRules: FormRules = {
  nId: [
    {
      pattern: /^[a-z][a-z0-9-]{2,63}$/,
      message: '业务标识须以小写字母开头,仅含小写字母/数字/连字符',
      trigger: 'blur',
    },
  ],
  name: [
    { required: true, message: '请输入角色名称', trigger: 'blur' },
    { min: 2, max: 32, message: '角色名称长度 2-32 个字符', trigger: 'blur' },
  ],
}

onMounted(() => {
  void loadRoles()
})
</script>

<template>
  <section class="roles-page">
    <div class="roles-page__toolbar">
      <el-input
        v-model="query.nId"
        placeholder="业务标识"
        clearable
        class="roles-page__filter"
        @keyup.enter="search"
      />
      <el-input
        v-model="query.name"
        placeholder="角色名称"
        clearable
        class="roles-page__filter"
        @keyup.enter="search"
      />
      <el-button type="primary" @click="search">查询</el-button>
      <el-button @click="resetQuery">重置</el-button>
      <div class="roles-page__spacer" />
      <PermissionGate :permission-n-id="PERMISSIONS.roleCreate">
        <el-button type="primary" plain @click="openCreate">新建角色</el-button>
      </PermissionGate>
    </div>

    <el-table :data="rows" v-loading="loading" row-key="roleNId" border stripe>
      <el-table-column prop="name" label="角色名称" min-width="140" />
      <el-table-column prop="roleNId" label="业务标识" min-width="180" />
      <el-table-column prop="description" label="描述" min-width="220" show-overflow-tooltip />
      <el-table-column label="系统角色" width="100" align="center">
        <template #default="{ row }">
          <el-tag v-if="row.isSystem" type="info" effect="plain">系统</el-tag>
          <span v-else>—</span>
        </template>
      </el-table-column>
      <el-table-column label="权限数" width="90" align="center">
        <template #default="{ row }">{{ row.permissionNIds.length }}</template>
      </el-table-column>
      <el-table-column label="操作" width="170" fixed="right">
        <template #default="{ row }">
          <PermissionGate :permission-n-id="PERMISSIONS.roleUpdate">
            <el-button link type="primary" :disabled="row.isSystem" @click="openEdit(row)"
              >编辑</el-button
            >
          </PermissionGate>
          <PermissionGate :permission-n-id="PERMISSIONS.roleAssignPermission">
            <el-button
              link
              type="primary"
              :disabled="row.isSystem"
              @click="openAssignPermissions(row)"
              >分配权限</el-button
            >
          </PermissionGate>
        </template>
      </el-table-column>
    </el-table>

    <el-pagination
      class="roles-page__pagination"
      layout="total, sizes, prev, pager, next, jumper"
      :total="total"
      :page-size="pageSize"
      :page-sizes="[10, 20, 50, 100]"
      :current-page="pageIndex"
      @current-change="
        (page: number) => {
          pageIndex = page
          void loadRoles()
        }
      "
      @size-change="
        (size: number) => {
          pageSize = size
          pageIndex = 1
          void loadRoles()
        }
      "
    />

    <!-- 新建 / 编辑 -->
    <el-dialog v-model="dialogOpen" :title="dialogTitle" width="480px">
      <el-form ref="formRef" :model="form" :rules="roleRules" label-width="90px">
        <el-form-item v-if="editing === null" label="业务标识" prop="nId">
          <el-input v-model="form.nId" placeholder="可选,默认自动生成" />
        </el-form-item>
        <el-form-item label="角色名称" prop="name">
          <el-input v-model="form.name" placeholder="如:仓库管理员" />
        </el-form-item>
        <el-form-item label="描述" prop="description">
          <el-input v-model="form.description" type="textarea" :rows="3" placeholder="可选" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogOpen = false">取消</el-button>
        <el-button type="primary" :loading="dialogSaving" @click="submitDialog">保存</el-button>
      </template>
    </el-dialog>

    <!-- 分配权限 -->
    <el-dialog v-model="permissionDialogOpen" title="分配权限" width="520px">
      <p class="roles-page__dialog-tip">
        为「{{ permissionTarget?.name ?? '' }}」勾选权限:叶子为操作,父节点为页面(自动级联)。
      </p>
      <div class="roles-page__tree" v-loading="treeLoading">
        <el-tree
          ref="permissionTreeRef"
          :data="permissionTree"
          show-checkbox
          node-key="id"
          :default-expand-all="true"
          :props="{ label: 'label', children: 'children' }"
        />
      </div>
      <template #footer>
        <el-button @click="permissionDialogOpen = false">取消</el-button>
        <el-button type="primary" :loading="permissionSaving" @click="submitPermissions"
          >保存</el-button
        >
      </template>
    </el-dialog>
  </section>
</template>

<style scoped>
.roles-page {
  display: flex;
  flex-direction: column;
  gap: var(--ip-space-4);
}

.roles-page__toolbar {
  display: flex;
  flex-wrap: wrap;
  gap: var(--ip-space-3);
  align-items: center;
}

.roles-page__filter {
  width: 200px;
}

.roles-page__spacer {
  flex: 1;
}

.roles-page__pagination {
  justify-content: flex-end;
}

.roles-page__dialog-tip {
  margin: 0 0 var(--ip-space-3);
  font-size: var(--ip-font-size-sm);
  color: var(--ip-color-text-secondary);
}

.roles-page__tree {
  max-height: 420px;
  overflow: auto;
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-md);
  padding: var(--ip-space-3);
}
</style>
