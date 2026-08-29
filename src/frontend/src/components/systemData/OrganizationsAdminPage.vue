<script setup lang="ts">
import { computed, reactive, ref } from 'vue'
import AppEmptyState from '@/components/base/AppEmptyState.vue'
import AppFormDrawer from '@/components/management/AppFormDrawer.vue'
import AppTreeTableLayout from '@/components/management/AppTreeTableLayout.vue'
import AppDataTable from '@/components/management/AppDataTable.vue'
import type { AppDataTableExportRequest } from '@/components/management/AppDataTable'
import { downloadBlob } from '@/components/management/download'
import SystemDataAdminFrame from './SystemDataAdminFrame.vue'
import PermissionGate from '@/permissions/PermissionGate.vue'
import { PERMISSIONS } from '@/permissions'
import type { OrganizationNodeDto, PositionDto } from '@/api/systemData/managementTypes'
import { useSystemDataManagementStore } from '@/stores/systemData/managementStore'
import { getSystemDataManagementApi } from '@/api/systemData/managementRegistry'

const props = withDefaults(
  defineProps<{ title?: string; description?: string; permission?: string }>(),
  {
    title: '行政组织与岗位',
    description:
      '维护根公司、子组织和岗位；先创建根公司，再选择可读的父组织；保存后树和岗位会刷新。',
    permission: PERMISSIONS.systemDataOrganizationView,
  },
)
const store = useSystemDataManagementStore()
const drawerOpen = ref(false)
const editingOrganization = ref(false)
const editingPosition = ref<PositionDto | null>(null)
const formError = ref('')
const form = reactive({
  nId: '',
  name: '',
  type: 'Company',
  parentNId: '',
  description: '',
  displayOrder: 0,
})
const moveTargetNId = ref('')
const moveReason = ref('')
const ORGANIZATION_COLUMNS = [
  { field: 'name', title: '组织', minWidth: 180 },
  { field: 'type', title: '类型', width: 100, filter: { kind: 'text' as const } },
  { field: 'status', title: '状态', width: 100, filter: { kind: 'text' as const } },
]
const POSITION_COLUMNS = [
  { field: 'name', title: '岗位', minWidth: 180 },
  { field: 'status', title: '状态', width: 100, filter: { kind: 'text' as const } },
]
async function exportOrganizations(request: AppDataTableExportRequest): Promise<void> {
  const api = getSystemDataManagementApi()
  if (api === null) return
  const blob = await api.exportOrganizations({
    search: typeof request.filters.name === 'string' ? request.filters.name : undefined,
    quantity: request.quantity,
    columns: request.columns,
    sortField: request.sort?.field,
    sortOrder: request.sort?.order,
  })
  downloadBlob(blob, `${request.filename}.xlsx`)
}

async function exportPositions(request: AppDataTableExportRequest): Promise<void> {
  const api = getSystemDataManagementApi()
  if (api === null) return
  const blob = await api.exportPositions({
    organizationNId: store.selectedOrganizationNId ?? undefined,
    quantity: request.quantity,
    columns: request.columns,
    sortField: request.sort?.field,
    sortOrder: request.sort?.order,
  })
  downloadBlob(blob, `${request.filename}.xlsx`)
}
const organizations = computed(() => {
  const flatten = (nodes: readonly OrganizationNodeDto[]): OrganizationNodeDto[] =>
    nodes.flatMap((node) => [node, ...flatten(node.children)])
  return flatten(store.organizationTree)
})
function resetForm(): void {
  Object.assign(form, {
    nId: '',
    name: '',
    type: 'Company',
    parentNId: '',
    description: '',
    displayOrder: 0,
  })
  formError.value = ''
}
function newOrganization(): void {
  resetForm()
  editingOrganization.value = false
  editingPosition.value = null
  drawerOpen.value = true
}
function editOrganization(): void {
  const item = store.selectedOrganization
  if (!item) return
  Object.assign(form, {
    nId: item.nId,
    name: item.name,
    type: item.type,
    parentNId: item.parentOrganizationNId ?? '',
    displayOrder: item.displayOrder,
  })
  editingOrganization.value = true
  editingPosition.value = null
  drawerOpen.value = true
}
function startPosition(): void {
  resetForm()
  form.type = 'position'
  drawerOpen.value = true
}
function editPosition(item: PositionDto): void {
  Object.assign(form, {
    nId: item.nId,
    name: item.name,
    description: item.description,
    displayOrder: item.displayOrder,
  })
  editingPosition.value = item
  editingOrganization.value = false
  drawerOpen.value = true
}
function selectOrganizationTable(selected: unknown): void {
  const item = (selected as OrganizationNodeDto[])[0]
  if (item) store.selectOrganization(item.nId)
}
async function submit(): Promise<void> {
  formError.value = ''
  if (!form.name.trim()) {
    formError.value = form.type === 'position' ? '请输入岗位名称。' : '请输入组织名称。'
    return
  }
  if (!Number.isInteger(form.displayOrder) || form.displayOrder < 0) {
    formError.value = '显示顺序必须是大于等于 0 的整数。'
    return
  }
  if (editingOrganization.value) {
    const item = store.selectedOrganization
    if (!item) return
    await store.updateOrganization(item.nId, {
      name: form.name.trim(),
      displayOrder: form.displayOrder,
      expectedOptimisticVersion: item.optimisticVersion,
      expectedConcurrencyVersion: item.concurrencyVersion,
    })
  } else if (editingPosition.value)
    await store.updatePosition(editingPosition.value.nId, {
      name: form.name.trim(),
      description: form.description.trim(),
      displayOrder: form.displayOrder,
      expectedOptimisticVersion: editingPosition.value.optimisticVersion,
      expectedConcurrencyVersion: editingPosition.value.concurrencyVersion,
    })
  else if (!form.nId.trim()) {
    formError.value = form.type === 'position' ? '请输入岗位 NId。' : '请输入组织 NId。'
    return
  } else if (form.type === 'position') {
    if (!store.selectedOrganizationNId) {
      formError.value = '请先选择所属组织。'
      return
    }
    await store.createPosition({
      nId: form.nId.trim(),
      name: form.name.trim(),
      description: form.description.trim(),
      displayOrder: form.displayOrder,
      organizationNId: store.selectedOrganizationNId,
    })
  } else if (form.type !== 'Company' && !form.parentNId) {
    formError.value = '非根组织必须选择父组织；请先创建根公司。'
    return
  } else if (form.type === 'Company' && form.parentNId) {
    formError.value = '根公司不能选择父组织。'
    return
  } else
    await store.createOrganization({
      nId: form.nId.trim(),
      name: form.name.trim(),
      type: form.type,
      displayOrder: form.displayOrder,
      ...(form.parentNId ? { parentOrganizationNId: form.parentNId } : {}),
    })
  if (!store.error) drawerOpen.value = false
}
</script>

<template>
  <SystemDataAdminFrame
    kind="organizations"
    :title="props.title"
    :description="props.description"
    :permission="props.permission"
  >
    <AppTreeTableLayout tree-label="组织森林" content-label="岗位表">
      <template #tree>
        <AppDataTable
          table-key="systemdata-organizations"
          route-key="systemdata-organizations"
          row-key="nId"
          mode="tree"
          selection="single"
          :rows="store.organizationTree"
          :total="organizations.length"
          :columns="ORGANIZATION_COLUMNS"
          :exporter="exportOrganizations"
          @selection-change="selectOrganizationTable"
        >
          <template #toolbar-actions
            ><PermissionGate :permission-n-id="PERMISSIONS.systemDataOrganizationCreate"
              ><button type="button" data-testid="systemdata-organizations-new" @click="newOrganization">
                新建组织
              </button></PermissionGate
            ><PermissionGate :permission-n-id="PERMISSIONS.systemDataPositionCreate"
              ><button
                v-if="store.selectedOrganizationNId"
                type="button"
                data-testid="systemdata-positions-new"
                @click="startPosition"
              >
                新建岗位
              </button></PermissionGate></template
          >
          <template #cell-name="{ row }">
            <button type="button" @click="store.selectOrganization(row.nId)">{{ row.name }}</button>
          </template>
        </AppDataTable>
      </template>
      <template #toolbar
        ><strong>组织详情与岗位</strong
        ><PermissionGate :permission-n-id="PERMISSIONS.systemDataOrganizationUpdate"
          ><button v-if="store.selectedOrganization" type="button" @click="editOrganization">
            编辑组织
          </button></PermissionGate
        ><PermissionGate :permission-n-id="PERMISSIONS.systemDataOrganizationStatus"
          ><button
          v-if="store.selectedOrganizationNId"
          type="button"
          @click="
            store.setOrganizationStatus(store.selectedOrganizationNId, {
              status: store.selectedOrganization?.status === 'Active' ? 'Inactive' : 'Active',
              reason: moveReason || '管理员调整组织状态',
            })
          "
        >
          {{ store.selectedOrganization?.status === 'Active' ? '停用组织' : '启用组织' }}</button></PermissionGate
        ><el-select
          v-if="store.selectedOrganizationNId"
          v-model="moveTargetNId"
          aria-label="目标父组织"
          placeholder="移动到父组织"
          clearable
          style="width: 220px"
          ><el-option
            v-for="item in organizations"
            :key="item.nId"
            :label="item.name + '（' + item.nId + '）'"
            :value="item.nId" /></el-select
        ><PermissionGate :permission-n-id="PERMISSIONS.systemDataOrganizationMove"
          ><button
          v-if="store.selectedOrganizationNId"
          type="button"
          @click="
            store.previewOrganizationMove(store.selectedOrganizationNId, moveTargetNId || undefined)
          "
        >
          预览移动
        </button></PermissionGate></template
      >
      <p v-if="store.selectedOrganization">
        {{ store.selectedOrganization.name }} · {{ store.selectedOrganization.type }} ·
        {{ store.selectedOrganization.status }}
      </p>
      <AppEmptyState v-if="!store.positions" title="请选择组织读取岗位" /><AppEmptyState
        v-else-if="store.positions.items.length === 0"
        title="暂无岗位"
      />
      <AppDataTable
        v-else
        table-key="systemdata-positions"
        route-key="systemdata-organizations"
        row-key="nId"
        :rows="store.positions.items"
        :total="store.positions.total"
        :columns="POSITION_COLUMNS"
        :exporter="exportPositions"
      >
        <template #cell-name="{ row }"
          >{{ row.name }}<small>{{ row.description }}</small></template
        >
        <template #actions="{ row }">
          <PermissionGate :permission-n-id="PERMISSIONS.systemDataPositionUpdate"
            ><button type="button" @click="editPosition(row)">编辑</button></PermissionGate
          ><PermissionGate :permission-n-id="PERMISSIONS.systemDataPositionStatus"
            ><button
            type="button"
            @click="
              store.setPositionStatus(row.nId, {
                status: row.status === 'Active' ? 'Inactive' : 'Active',
                reason: '岗位状态调整',
              })
            "
          >
            {{ row.status === 'Active' ? '停用' : '启用' }}
          </button></PermissionGate>
        </template>
      </AppDataTable>
      <div v-if="store.movePreview" role="status">
        移动预览：组织 {{ store.movePreview.subtreeOrganizationCount }} · 岗位
        {{ store.movePreview.subtreePositionCount
        }}<PermissionGate :permission-n-id="PERMISSIONS.systemDataOrganizationMove"
          ><button
          type="button"
          @click="
            store.moveOrganization(store.movePreview.nId, {
              previewOrganizationRevision: store.movePreview.organizationRevision,
              expectedOptimisticVersion: store.movePreview.expectedOptimisticVersion,
              expectedConcurrencyVersion: store.movePreview.expectedConcurrencyVersion,
              ...(moveTargetNId ? { targetParentOrganizationNId: moveTargetNId } : {}),
            })
          "
        >
          确认移动
        </button></PermissionGate>
      </div>
    </AppTreeTableLayout>
  </SystemDataAdminFrame>
  <AppFormDrawer
    v-model="drawerOpen"
    :busy="store.loading"
    :title="
      editingOrganization
        ? '编辑组织'
        : editingPosition
          ? '编辑岗位'
          : form.type === 'position'
            ? '新建岗位'
            : '新建组织'
    "
    @submit="submit"
    ><el-form :model="form" label-width="120px"
      ><p v-if="formError" role="alert">{{ formError }}</p>
      <el-form-item
        v-if="!editingOrganization && !editingPosition"
        :label="form.type === 'position' ? '岗位 NId' : '组织 NId'"
        ><el-input
          v-model="form.nId"
          :aria-label="form.type === 'position' ? '岗位 NId' : '组织 NId'"
          placeholder="租户内稳定业务标识" /></el-form-item
      ><el-form-item label="名称"
        ><el-input
          v-model="form.name"
          :aria-label="form.type === 'position' ? '岗位名称' : '组织名称'" /></el-form-item
      ><template v-if="!editingOrganization && !editingPosition && form.type !== 'position'"
        ><el-form-item label="组织类型"
          ><el-select v-model="form.type" aria-label="组织类型"
            ><el-option label="Company" value="Company" /><el-option
              label="Department"
              value="Department" /><el-option label="Section" value="Section" /><el-option
              label="Team"
              value="Team" /></el-select></el-form-item
        ><el-form-item label="父组织"
          ><el-select v-model="form.parentNId" aria-label="父组织" clearable
            ><el-option label="根公司（无父组织）" value="" /><el-option
              v-for="item in organizations"
              :key="item.nId"
              :label="item.name + '（' + item.nId + '）'"
              :value="item.nId" /></el-select
          ><small>先创建根公司，子组织使用可读名称选择父组织。</small></el-form-item
        ></template
      ><el-form-item v-if="form.type === 'position'" label="描述"
        ><el-input v-model="form.description" type="textarea" /></el-form-item
      ><el-form-item :label="form.type === 'position' ? '岗位显示顺序' : '组织显示顺序'"
        ><el-input-number
          v-model="form.displayOrder"
          :min="0"
          :step="1"
          :aria-label="
            form.type === 'position' ? '岗位显示顺序' : '组织显示顺序'
          " /></el-form-item></el-form
  ></AppFormDrawer>
</template>
