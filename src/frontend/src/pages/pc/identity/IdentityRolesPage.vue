<script setup lang="ts">
/**
 * 角色管理页(TASK-ID-012,§16.3):列表分页过滤、新建/编辑表单校验、
 * 分配权限(权限目录树);系统角色不可编辑/分配;409 并发冲突提示重载。
 */
import { ElDropdown, ElDropdownItem, ElDropdownMenu, ElMessage } from 'element-plus'
import type { FormInstance, FormRules } from 'element-plus'
import { computed, onMounted, reactive, ref } from 'vue'

import type { PermissionTreeNodeDto, RoleSummaryDto } from '@/api/identity/management'
import { getManagementApi } from '@/api/identity/managementRegistry'
import { PERMISSIONS, PermissionGate, usePermission } from '@/permissions'
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

const management = getManagementApi()
const { has } = usePermission()
const locale = usePlatformLocale()
const copy = computed(() => localeMessages[locale.value].identity.management.roles)
const commonCopy = computed(() => localeMessages[locale.value].identity.management.common)

type RoleAction = 'edit' | 'assign-permission'

const ROLE_ACTION_WIDTHS: Record<RoleAction, number> = {
  edit: 42,
  'assign-permission': 70,
}

const ROLE_ACTION_GAP = 4
const ROLE_MORE_WIDTH = 52
const ROLE_ACTION_HORIZONTAL_INSET = 16

function roleActionCandidates(): RoleAction[] {
  const actions: RoleAction[] = []
  if (has(PERMISSIONS.roleUpdate)) actions.push('edit')
  if (has(PERMISSIONS.roleAssignPermission)) actions.push('assign-permission')
  return actions
}

function directRoleActions(availableWidth?: number): RoleAction[] {
  const actions = roleActionCandidates()
  const width = Math.max(
    ROLE_MORE_WIDTH,
    Math.round(availableWidth ?? 180) - ROLE_ACTION_HORIZONTAL_INSET,
  )
  const totalWidth = actions.reduce(
    (total, action, index) =>
      total + ROLE_ACTION_WIDTHS[action] + (index === 0 ? 0 : ROLE_ACTION_GAP),
    0,
  )
  if (totalWidth <= width) return actions

  const direct: RoleAction[] = []
  let used = 0
  for (const action of actions) {
    const gap = direct.length === 0 ? 0 : ROLE_ACTION_GAP
    if (used + gap + ROLE_ACTION_WIDTHS[action] + ROLE_ACTION_GAP + ROLE_MORE_WIDTH > width) break
    direct.push(action)
    used += gap + ROLE_ACTION_WIDTHS[action]
  }
  return direct
}

function isDirectRoleAction(availableWidth: number | undefined, action: RoleAction): boolean {
  return directRoleActions(availableWidth).includes(action)
}

function hasMoreRoleActions(availableWidth?: number): boolean {
  const direct = directRoleActions(availableWidth)
  return roleActionCandidates().some((action) => !direct.includes(action))
}

function onRoleActionCommand(row: RoleSummaryDto, command: string): void {
  if (command === 'edit') openEdit(row)
  else if (command === 'assign-permission') openAssignPermissions(row)
}

// ---------------------------------------------------------------------------
// 列表与过滤
// ---------------------------------------------------------------------------

const loading = ref(false)
const rows = ref<RoleSummaryDto[]>([])
const total = ref(0)
const query = reactive({ nId: '', name: '' })
const pageIndex = ref(1)
const pageSize = ref(25)
const tableQueryMode = ref<AppDataTableQueryMode>('top')

const roleColumns = computed<readonly AppDataTableColumn[]>(() => [
  { field: 'name', title: copy.value.roleName, minWidth: 140, filter: { kind: 'text' as const } },
  { field: 'roleNId', title: copy.value.roleNId, minWidth: 180, filter: { kind: 'text' as const } },
  {
    field: 'description',
    title: copy.value.descriptionColumn,
    minWidth: 220,
    filter: { kind: 'text' as const },
  },
  {
    field: 'isSystem',
    title: copy.value.systemRole,
    width: 100,
    filter: {
      kind: 'select' as const,
      options: [
        { label: commonCopy.value.yes, value: true },
        { label: commonCopy.value.no, value: false },
      ],
    },
  },
  { field: 'permissionCount', title: copy.value.permissionCount, width: 90, filter: false },
])

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
    reportManagementError(error, copy.value.feedback.loadFailed)
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

function onTableQueryModeChange(mode: AppDataTableQueryMode): void {
  tableQueryMode.value = mode
  if (mode === 'header') {
    query.nId = ''
    query.name = ''
  }
  pageIndex.value = 1
}

function onTableQuery(request: AppDataTableRequest): void {
  pageIndex.value = request.pageIndex
  pageSize.value = request.pageSize
}

async function loadRolesTable(request: AppDataTableRequest) {
  const nId = request.queryMode === 'top' ? query.nId : request.filters.roleNId
  const name = request.queryMode === 'top' ? query.name : request.filters.name
  const filters = request.queryMode === 'top' ? { ...query, ...request.filters } : request.filters
  const result = await management.listRoles({
    keyword: String(filters.keyword ?? '').trim() || undefined,
    nId: String(nId ?? '').trim() || undefined,
    name: String(name ?? '').trim() || undefined,
    description: String(filters.description ?? '').trim() || undefined,
    isSystem:
      filters.isSystem === undefined || filters.isSystem === ''
        ? undefined
        : filters.isSystem === true || filters.isSystem === 'true',
    pageIndex: request.pageIndex,
    pageSize: request.pageSize,
    sortField: request.sort?.field,
    sortOrder: request.sort?.order,
  })
  rows.value = result.items
  total.value = result.total
  return result
}

async function exportRoles(request: AppDataTableExportRequest): Promise<void> {
  if (management.exportRoles === undefined) return
  const nId = request.queryMode === 'top' ? query.nId : request.filters.roleNId
  const name = request.queryMode === 'top' ? query.name : request.filters.name
  const filters = request.queryMode === 'top' ? { ...query, ...request.filters } : request.filters
  const blob = await management.exportRoles({
    nId: String(nId ?? '').trim() || undefined,
    name: String(name ?? '').trim() || undefined,
    keyword: String(filters.keyword ?? '').trim() || undefined,
    description: String(filters.description ?? '').trim() || undefined,
    isSystem:
      filters.isSystem === undefined || filters.isSystem === ''
        ? undefined
        : filters.isSystem === true || filters.isSystem === 'true',
    quantity: request.quantity,
    columns: request.columns,
    sortField: request.sort?.field,
    sortOrder: request.sort?.order,
  })
  downloadBlob(blob, `${request.filename}.xlsx`)
}

// ---------------------------------------------------------------------------
// 新建 / 编辑
// ---------------------------------------------------------------------------

const dialogOpen = ref(false)
const editing = ref<RoleSummaryDto | null>(null)
const dialogTitle = computed(() =>
  editing.value === null ? copy.value.createTitle : copy.value.editTitle,
)
const formRef = ref<FormInstance>()
const dialogSaving = ref(false)
const form = reactive({ nId: '', name: '', description: '' })

function openCreate(): void {
  editing.value = null
  form.nId = ''
  form.name = ''
  form.description = ''
  dialogOpen.value = true
}

function openEdit(row: RoleSummaryDto): void {
  editing.value = row
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
      ElMessage.success(copy.value.feedback.createSuccess)
    } else {
      await management.updateRole(editing.value.roleNId, {
        name: form.name.trim(),
        description: form.description.trim() || undefined,
        expectedOptimisticVersion: editing.value.optimisticVersion,
        expectedConcurrencyVersion: editing.value.concurrencyVersion,
      })
      ElMessage.success(copy.value.feedback.updateSuccess)
    }
    dialogOpen.value = false
    await loadRoles()
  } catch (error) {
    reportManagementError(error, copy.value.feedback.saveFailed)
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
    reportManagementError(error, copy.value.feedback.permissionLoadFailed)
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
    ElMessage.success(copy.value.feedback.permissionUpdated)
    permissionDialogOpen.value = false
    await loadRoles()
  } catch (error) {
    reportManagementError(error, copy.value.feedback.permissionSaveFailed)
  } finally {
    permissionSaving.value = false
  }
}

// ---------------------------------------------------------------------------
// 校验规则
// ---------------------------------------------------------------------------

const roleRules = computed<FormRules>(() => ({
  nId: [
    {
      pattern: /^[a-z][a-z0-9-]{2,63}$/,
      message: copy.value.feedback.businessIdRule,
      trigger: 'blur',
    },
  ],
  name: [
    { required: true, message: copy.value.feedback.nameRequired, trigger: 'blur' },
    { min: 2, max: 32, message: copy.value.feedback.nameLength, trigger: 'blur' },
  ],
}))

onMounted(() => {
  void loadRoles()
})
</script>

<template>
  <AppPage
    class="roles-page"
    data-testid="identity-roles-page"
    :title="copy.title"
    :description="copy.description"
  >
    <template #breadcrumb>
      <nav :aria-label="commonCopy.pagePath">{{ copy.breadcrumb }}</nav>
    </template>
    <template #heading-meta>
      <span class="roles-page__count">{{ total }} {{ copy.countSuffix }}</span>
    </template>
    <template #actions>
      <PermissionGate :permission-n-id="PERMISSIONS.roleCreate">
        <el-button type="primary" data-testid="roles-create" @click="openCreate">
          {{ copy.create }}
        </el-button>
      </PermissionGate>
    </template>

    <AppQueryPanel
      v-if="tableQueryMode === 'top'"
      class="roles-page__query-panel"
      :title="commonCopy.queryTitle"
      :show-actions="true"
      :submit-label="commonCopy.search"
      :reset-label="commonCopy.reset"
      :grid="true"
      @submit="search"
      @reset="resetQuery"
    >
      <el-input
        v-model="query.nId"
        :placeholder="copy.roleNId"
        :aria-label="copy.roleNId"
        clearable
        class="roles-page__filter"
        @keyup.enter="search"
      />
      <el-input
        v-model="query.name"
        :placeholder="copy.roleName"
        :aria-label="copy.roleName"
        clearable
        class="roles-page__filter"
        @keyup.enter="search"
      />
    </AppQueryPanel>

    <AppDataTable
      table-key="identity-roles"
      route-key="identity-roles"
      row-key="roleNId"
      :rows="rows"
      :total="total"
      :loading="loading"
      :columns="roleColumns"
      :initial-page-index="pageIndex"
      :page-size="pageSize"
      :loader="loadRolesTable"
      :exporter="exportRoles"
      @query-mode-change="onTableQueryModeChange"
      @query-change="onTableQuery"
    >
      <template #cell-isSystem="{ row }">
        <el-tag v-if="row.isSystem" type="info" effect="plain">{{ copy.systemRole }}</el-tag>
        <span v-else>—</span>
      </template>
      <template #cell-permissionCount="{ row }">{{ row.permissionNIds.length }}</template>
      <template #actions="{ row, availableWidth }">
        <div class="roles-page__row-actions" :data-testid="`identity-role-actions-${row.roleNId}`">
          <PermissionGate
            v-if="isDirectRoleAction(availableWidth, 'edit')"
            :permission-n-id="PERMISSIONS.roleUpdate"
          >
            <el-button link type="primary" :disabled="row.isSystem" @click="openEdit(row)">{{
              copy.edit
            }}</el-button>
          </PermissionGate>
          <PermissionGate
            v-if="isDirectRoleAction(availableWidth, 'assign-permission')"
            :permission-n-id="PERMISSIONS.roleAssignPermission"
          >
            <el-button
              link
              type="primary"
              :disabled="row.isSystem"
              @click="openAssignPermissions(row)"
              >{{ copy.assignPermissions }}</el-button
            >
          </PermissionGate>
          <ElDropdown
            v-if="hasMoreRoleActions(availableWidth)"
            trigger="click"
            placement="bottom-end"
            :teleported="true"
            popper-class="roles-page__more-popper"
            @command="onRoleActionCommand(row, $event)"
          >
            <button
              type="button"
              class="roles-page__more-trigger"
              :data-testid="`identity-role-more-${row.roleNId}`"
              aria-haspopup="menu"
            >
              {{ commonCopy.more }}
            </button>
            <template #dropdown>
              <ElDropdownMenu :data-testid="`identity-role-more-menu-${row.roleNId}`" role="menu">
                <PermissionGate
                  v-if="!isDirectRoleAction(availableWidth, 'edit')"
                  :permission-n-id="PERMISSIONS.roleUpdate"
                >
                  <ElDropdownItem command="edit" :disabled="row.isSystem">{{
                    copy.edit
                  }}</ElDropdownItem>
                </PermissionGate>
                <PermissionGate
                  v-if="!isDirectRoleAction(availableWidth, 'assign-permission')"
                  :permission-n-id="PERMISSIONS.roleAssignPermission"
                >
                  <ElDropdownItem command="assign-permission" :disabled="row.isSystem">{{
                    copy.assignPermissions
                  }}</ElDropdownItem>
                </PermissionGate>
              </ElDropdownMenu>
            </template>
          </ElDropdown>
        </div>
      </template>
    </AppDataTable>

    <!-- 新建 / 编辑 -->
    <AppFormDrawer
      v-model="dialogOpen"
      :title="dialogTitle"
      :busy="dialogSaving"
      size="medium"
      @submit="submitDialog"
    >
      <el-form ref="formRef" :model="form" :rules="roleRules" label-width="90px">
        <el-form-item v-if="editing === null" :label="commonCopy.businessId" prop="nId">
          <el-input v-model="form.nId" :placeholder="commonCopy.optional" />
        </el-form-item>
        <el-form-item :label="copy.roleName" prop="name">
          <el-input v-model="form.name" :placeholder="copy.roleName" />
        </el-form-item>
        <el-form-item :label="commonCopy.description" prop="description">
          <el-input
            v-model="form.description"
            type="textarea"
            :rows="3"
            :placeholder="commonCopy.optional"
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogOpen = false">{{ commonCopy.cancel }}</el-button>
        <el-button type="primary" :loading="dialogSaving" @click="submitDialog">{{
          commonCopy.save
        }}</el-button>
      </template>
    </AppFormDrawer>

    <!-- 分配权限 -->
    <AppFormDrawer
      v-model="permissionDialogOpen"
      :title="copy.assignPermissions"
      :busy="permissionSaving"
      size="medium"
    >
      <p class="roles-page__dialog-tip">
        {{ copy.permissionDescription.replace('{name}', permissionTarget?.name ?? '') }}
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
        <el-button @click="permissionDialogOpen = false">{{ commonCopy.cancel }}</el-button>
        <el-button type="primary" :loading="permissionSaving" @click="submitPermissions">{{
          commonCopy.save
        }}</el-button>
      </template>
    </AppFormDrawer>
  </AppPage>
</template>

<style scoped>
.roles-page {
  display: flex;
  flex-direction: column;
  gap: 0;
  overflow: hidden;
  background: var(--ip-color-bg-container);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-lg);
  min-width: 0;
}

.roles-page :deep(.app-page__header) {
  padding: 18px 20px 17px;
  border-bottom: 1px solid var(--ip-color-border);
}

.roles-page :deep(.app-page__body) {
  display: flex;
  flex-direction: column;
  min-width: 0;
  min-height: 0;
}

.roles-page :deep(.app-data-table) {
  flex: 1 1 auto;
  min-height: 0;
}

.roles-page :deep(.app-data-table__card) {
  display: flex;
  min-height: 0;
  flex: 1 1 auto;
  flex-direction: column;
}

.roles-page :deep(.app-query-panel) {
  gap: 0;
  padding: 14px 20px 16px;
  border-bottom: 1px solid var(--ip-color-border);
}

.roles-page :deep(.app-query-panel__header) {
  margin-bottom: var(--ip-space-3);
}

.roles-page :deep(.app-query-panel__body) {
  gap: 12px;
}

.roles-page__count {
  color: var(--ip-color-text-secondary);
  font-size: var(--ip-font-size-sm);
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

.roles-page__row-actions {
  position: relative;
  display: inline-flex;
  align-items: center;
  gap: var(--ip-space-1);
  white-space: nowrap;
}

.roles-page__more-trigger {
  min-height: var(--ip-density-control-height);
  padding: 0 var(--ip-space-1);
  color: var(--ip-color-primary);
  background: transparent;
  border: 0;
  cursor: pointer;
  font: inherit;
}

.roles-page__more-trigger:focus-visible {
  outline: 2px solid var(--ip-color-primary);
  outline-offset: 2px;
}

:global(.roles-page__more-popper) {
  min-width: 148px;
  padding: var(--ip-space-1);
  background: var(--ip-color-bg-container);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-md);
  box-shadow: var(--ip-shadow-md);
}

:global(.roles-page__more-popper .el-dropdown-menu) {
  padding: 0;
  background: transparent;
}

:global(.roles-page__more-popper .el-dropdown-menu__item) {
  min-height: var(--ip-density-control-height);
  color: var(--ip-color-text-primary);
  border-radius: var(--ip-radius-sm);
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
