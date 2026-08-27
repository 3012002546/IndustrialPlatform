<script setup lang="ts">
import { ref } from 'vue'
import AppEmptyState from '@/components/base/AppEmptyState.vue'
import AppFormDrawer from '@/components/management/AppFormDrawer.vue'
import SystemDataAdminFrame from './SystemDataAdminFrame.vue'
import { PERMISSIONS } from '@/permissions'
import { getManagementApi } from '@/api/identity/managementRegistry'
import type { UserSummaryDto } from '@/api/identity/management'
import { useSystemDataManagementStore } from '@/stores/systemData/managementStore'
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
    ><template #toolbar><button type="button" @click="openNew">新建任职</button></template>
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
    <table v-else>
      <thead>
        <tr>
          <th>岗位</th>
          <th>状态</th>
          <th>主任职</th>
          <th>生效区间</th>
          <th>操作</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="item in store.assignments" :key="item.nId">
          <td>{{ item.positionName }}</td>
          <td>{{ item.state }}</td>
          <td>{{ item.isPrimary ? '是' : '否' }}</td>
          <td>{{ item.effectiveFrom }} – {{ item.effectiveTo ?? '至今' }}</td>
          <td>
            <button
              v-if="item.state === 'Current'"
              type="button"
              @click="store.endAssignment(item.nId)"
            >
              结束</button
            ><button
              v-if="item.state === 'Scheduled'"
              type="button"
              @click="store.cancelAssignment(item.nId, { reason: '管理员取消计划任职' })"
            >
              取消</button
            ><button
              v-if="!item.isPrimary && !['Cancelled', 'Ended'].includes(item.state)"
              type="button"
              @click="
                store.setPrimaryAssignment(item.userNId, {
                  targetAssignmentNId: item.nId,
                  effectiveOn: new Date().toISOString(),
                  reason: '管理员切换主任职',
                })
              "
            >
              切换主任职
            </button>
          </td>
        </tr>
      </tbody>
    </table></SystemDataAdminFrame
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
