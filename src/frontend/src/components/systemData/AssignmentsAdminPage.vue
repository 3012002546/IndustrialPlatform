<script setup lang="ts">
import { ref } from 'vue'
import AppEmptyState from '@/components/base/AppEmptyState.vue'
import AppFormDrawer from '@/components/management/AppFormDrawer.vue'
import AppDataTable from '@/components/management/AppDataTable.vue'
import type { AppDataTableExportRequest } from '@/components/management/AppDataTable'
import { downloadBlob } from '@/components/management/download'
import SystemDataAdminFrame from './SystemDataAdminFrame.vue'
import PermissionGate from '@/permissions/PermissionGate.vue'
import { PERMISSIONS } from '@/permissions'
import { getManagementApi } from '@/api/identity/managementRegistry'
import type { UserSummaryDto } from '@/api/identity/management'
import { useSystemDataManagementStore } from '@/stores/systemData/managementStore'
import { getSystemDataManagementApi } from '@/api/systemData/managementRegistry'
const props = withDefaults(
  defineProps<{ title?: string; description?: string; permission?: string }>(),
  {
    title: '用户任职',
    description:
      '维护 Identity 用户的任职时间线；先搜索真实用户和岗位，再执行结束、取消或主任职切换。',
    permission: PERMISSIONS.systemDataAssignmentView,
  },
)
const store = useSystemDataManagementStore()
const drawerOpen = ref(false)
const query = ref('')
const users = ref<UserSummaryDto[]>([])
const selectedUser = ref<UserSummaryDto | null>(null)
const positionNId = ref('')
const primary = ref(false)
const from = ref('')
const to = ref('')
const unavailable = ref(false)
const ASSIGNMENT_COLUMNS = [
  { field: 'positionName', title: '岗位', minWidth: 160 },
  { field: 'state', title: '状态', width: 110 },
  {
    field: 'isPrimary',
    title: '主任职',
    width: 90,
    filter: {
      kind: 'select' as const,
      options: [
        { label: '是', value: true },
        { label: '否', value: false },
      ],
    },
  },
  { field: 'effectiveFrom', title: '生效区间', minWidth: 220 },
]
async function exportAssignments(request: AppDataTableExportRequest): Promise<void> {
  const api = getSystemDataManagementApi()
  const user = selectedUser.value
  if (api === null || user === null) return
  const blob = await api.exportAssignments(user.userNId, {
    quantity: request.quantity,
    columns: request.columns,
    sortField: request.sort?.field,
    sortOrder: request.sort?.order,
  })
  downloadBlob(blob, `${request.filename}.xlsx`)
}
async function searchUsers(): Promise<void> {
  unavailable.value = false
  try {
    const result = await getManagementApi().listUsers({
      name: query.value || undefined,
      loginName: query.value || undefined,
      status: 'Active',
      pageIndex: 1,
      pageSize: 10,
    })
    users.value = result.items
  } catch {
    unavailable.value = true
    users.value = []
  }
}
function choose(user: UserSummaryDto): void {
  selectedUser.value = user
  users.value = []
  void store.loadAssignments(user.userNId)
}
function openNew(): void {
  positionNId.value = ''
  primary.value = false
  from.value = ''
  to.value = ''
  drawerOpen.value = true
}
async function submit(): Promise<void> {
  if (!selectedUser.value || !positionNId.value) return
  await store.createAssignment(selectedUser.value.userNId, {
    positionNId: positionNId.value,
    isPrimary: primary.value,
    ...(from.value ? { effectiveFrom: from.value } : {}),
    ...(to.value ? { effectiveTo: to.value } : {}),
  })
  if (!store.error) drawerOpen.value = false
}
</script>
<template>
  <SystemDataAdminFrame
    kind="assignments"
    :title="props.title"
    :description="props.description"
    :permission="props.permission"
    ><template #toolbar
      ><PermissionGate :permission-n-id="PERMISSIONS.systemDataAssignmentManage"
        ><button type="button" @click="openNew">新建任职</button></PermissionGate
      ></template
    >
    <div class="systemdata-assignment-search">
      <label>Identity 用户搜索</label>
      <el-input
        v-model="query"
        aria-label="Identity 用户搜索"
        placeholder="姓名或登录名"
        @keyup.enter="searchUsers"
      /><button type="button" @click="searchUsers">搜索用户</button
      ><span v-if="unavailable" role="alert">用户目录暂不可用，禁止手输未知 UserNId 提交。</span>
      <ul v-if="users.length">
        <li v-for="user in users" :key="user.userNId">
          <button type="button" @click="choose(user)">
            {{ user.name }}（{{ user.loginName }}）
          </button>
        </li>
      </ul>
      <strong v-if="selectedUser"
        >已选择：{{ selectedUser.name }} / {{ selectedUser.userNId }}</strong
      >
    </div>
    <AppEmptyState
      v-if="!store.assignments.length"
      title="暂无任职记录"
      description="选择 Identity 用户后读取时间线。"
    />
    <AppDataTable
      v-else
      table-key="systemdata-assignments"
      route-key="systemdata-assignments"
      row-key="nId"
      :rows="store.assignments"
      :total="store.assignments.length"
      :columns="ASSIGNMENT_COLUMNS"
      :exporter="exportAssignments"
    >
      <template #cell-isPrimary="{ row }">{{ row.isPrimary ? '是' : '否' }}</template>
      <template #cell-effectiveFrom="{ row }"
        >{{ row.effectiveFrom }} – {{ row.effectiveTo ?? '至今' }}</template
      >
      <template #actions="{ row }">
        <PermissionGate :permission-n-id="PERMISSIONS.systemDataAssignmentManage"
          ><button v-if="row.state === 'Current'" type="button" @click="store.endAssignment(row.nId)">
          结束</button
        ><button
          v-if="row.state === 'Scheduled'"
          type="button"
          @click="store.cancelAssignment(row.nId, { reason: '管理员取消计划任职' })"
        >
          取消</button
        ><button
          v-if="!row.isPrimary && !['Cancelled', 'Ended'].includes(row.state)"
          type="button"
          @click="
            store.setPrimaryAssignment(row.userNId, {
              targetAssignmentNId: row.nId,
              effectiveOn: new Date().toISOString(),
              reason: '管理员切换主任职',
            })
          "
        >
          切换主任职
        </button></PermissionGate>
      </template>
    </AppDataTable></SystemDataAdminFrame
  >
  <AppFormDrawer v-model="drawerOpen" :busy="store.loading" title="新建任职" @submit="submit"
    ><el-form label-width="120px"
      ><el-form-item label="岗位"
        ><el-select v-model="positionNId" aria-label="任职岗位" placeholder="请选择岗位"
          ><el-option
            v-for="item in store.positions?.items ?? []"
            :key="item.nId"
            :label="item.name + '（' + item.nId + '）'"
            :value="item.nId" /></el-select></el-form-item
      ><el-form-item label="生效时间"
        ><el-input v-model="from" type="datetime-local" /></el-form-item
      ><el-form-item label="失效时间"><el-input v-model="to" type="datetime-local" /></el-form-item
      ><el-form-item label="主任职"
        ><el-checkbox v-model="primary">设为主任职</el-checkbox></el-form-item
      ></el-form
    ></AppFormDrawer
  >
</template>
