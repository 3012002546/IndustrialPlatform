<script setup lang="ts">
import { computed, ref } from 'vue'
import { ElIcon, ElMessageBox } from 'element-plus'
import { Plus } from '@element-plus/icons-vue'
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
import { localeMessages } from '@/localization/i18n'
import { interpolate, systemDataEnumLabel, systemDataPageCopy } from '@/localization/systemData'
import { useLocalizationStore } from '@/stores/localizationStore'
import AppQueryPanel from '@/components/management/AppQueryPanel.vue'
const props = withDefaults(
  defineProps<{ title?: string; description?: string; permission?: string }>(),
  {
    title: '',
    description: '',
    permission: PERMISSIONS.systemDataAssignmentView,
  },
)
const store = useSystemDataManagementStore()
const localization = useLocalizationStore()
const copy = computed(() => systemDataPageCopy(localization.locale, 'assignments'))
const commonCopy = computed(() => localeMessages[localization.locale].systemData.copy)
const pageTitle = computed(() => props.title || copy.value.title)
const pageDescription = computed(() => props.description || copy.value.description)
const drawerOpen = ref(false)
const query = ref('')
const users = ref<UserSummaryDto[]>([])
const selectedUser = ref<UserSummaryDto | null>(null)
const positionNId = ref('')
const primary = ref(false)
const from = ref('')
const to = ref('')
const unavailable = ref(false)
const assignmentColumns = computed(() => [
  { field: 'positionName', title: copy.value.position, minWidth: 160 },
  { field: 'state', title: copy.value.state, width: 110 },
  {
    field: 'isPrimary',
    title: copy.value.primary,
    width: 90,
    filter: {
      kind: 'select' as const,
      options: [
        { label: copy.value.yes, value: true },
        { label: copy.value.no, value: false },
      ],
    },
  },
  { field: 'effectiveFrom', title: copy.value.interval, minWidth: 220 },
])
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
function resetUserSearch(): void {
  query.value = ''
  users.value = []
  unavailable.value = false
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
async function confirmAssignmentAction(action: string, body: string): Promise<boolean> {
  try {
    await ElMessageBox.confirm(body, copy.value.confirmTitle, {
      type: 'warning',
      confirmButtonText: action,
      cancelButtonText: commonCopy.value.cancel,
    })
    return true
  } catch {
    return false
  }
}
async function endAssignment(nId: string, name: string): Promise<void> {
  if (await confirmAssignmentAction(copy.value.end, interpolate(copy.value.endedBody, { name })))
    await store.endAssignment(nId)
}
async function cancelAssignment(nId: string, name: string): Promise<void> {
  if (
    await confirmAssignmentAction(
      copy.value.cancelAssignment,
      interpolate(copy.value.cancelledBody, { name }),
    )
  )
    await store.cancelAssignment(nId, { reason: copy.value.cancelledBody })
}
async function switchPrimary(nId: string, name: string): Promise<void> {
  if (
    await confirmAssignmentAction(
      copy.value.switchPrimary,
      interpolate(copy.value.confirmBody, { action: copy.value.switchPrimary, name }),
    )
  )
    await store.setPrimaryAssignment(store.assignmentUserNId, {
      targetAssignmentNId: nId,
      effectiveOn: new Date().toISOString(),
      reason: copy.value.switchPrimary,
    })
}
</script>
<template>
  <SystemDataAdminFrame
    kind="assignments"
    :title="pageTitle"
    :description="pageDescription"
    :permission="props.permission"
    ><template #toolbar
      ><PermissionGate :permission-n-id="PERMISSIONS.systemDataAssignmentManage"
        ><el-button type="primary" @click="openNew">
          <ElIcon class="systemdata-page-action-icon" aria-hidden="true"><Plus /></ElIcon>
          {{ copy.newAssignment }}
        </el-button></PermissionGate
      ></template
    >
    <div class="systemdata-assignment-query">
      <AppQueryPanel
        :title="copy.userSearch"
        :submit-label="copy.searchUser"
        :reset-label="commonCopy.reset"
        grid
        show-actions
        @submit="searchUsers"
        @reset="resetUserSearch"
      >
        <div class="systemdata-assignment-query__field">
          <el-input
            v-model="query"
            :aria-label="copy.userSearch"
            :placeholder="copy.userSearchPlaceholder"
            @keyup.enter="searchUsers"
          />
        </div>
        <span v-if="unavailable" class="systemdata-assignment-query__error" role="alert">
          {{ copy.directoryUnavailable }}
        </span>
        <ul v-if="users.length" class="systemdata-assignment-query__results">
          <li v-for="user in users" :key="user.userNId">
            <el-button link type="primary" @click="choose(user)">
              {{ user.name }} ({{ user.loginName }})
            </el-button>
          </li>
        </ul>
        <strong v-if="selectedUser" class="systemdata-assignment-query__selected">{{
          interpolate(copy.selectedUser, { name: selectedUser.name, id: selectedUser.userNId })
        }}</strong>
      </AppQueryPanel>
    </div>
    <AppEmptyState
      v-if="!store.assignments.length"
      :title="copy.noAssignments"
      :description="copy.chooseUser"
    />
    <AppDataTable
      v-else
      table-key="systemdata-assignments"
      route-key="systemdata-assignments"
      row-key="nId"
      :rows="store.assignments"
      :total="store.assignments.length"
      :columns="assignmentColumns"
      :exporter="exportAssignments"
    >
      <template #cell-isPrimary="{ row }">{{ row.isPrimary ? copy.yes : copy.no }}</template>
      <template #cell-state="{ row }">{{
        systemDataEnumLabel(localization.locale, row.state)
      }}</template>
      <template #cell-effectiveFrom="{ row }"
        >{{ row.effectiveFrom }} – {{ row.effectiveTo ?? '—' }}</template
      >
      <template #actions="{ row }">
        <PermissionGate :permission-n-id="PERMISSIONS.systemDataAssignmentManage"
          ><el-button
            v-if="row.state === 'Current'"
            link
            type="danger"
            @click="endAssignment(row.nId, row.positionName)"
          >
            {{ copy.end }}</el-button
          ><el-button
            v-if="row.state === 'Scheduled'"
            link
            type="warning"
            @click="cancelAssignment(row.nId, row.positionName)"
          >
            {{ copy.cancelAssignment }}</el-button
          ><el-button
            v-if="!row.isPrimary && !['Cancelled', 'Ended'].includes(row.state)"
            link
            type="success"
            @click="switchPrimary(row.nId, row.positionName)"
          >
            {{ copy.switchPrimary }}
          </el-button></PermissionGate
        >
      </template>
    </AppDataTable></SystemDataAdminFrame
  >
  <AppFormDrawer
    v-model="drawerOpen"
    :busy="store.loading"
    :title="copy.createTitle"
    @submit="submit"
    ><el-form label-width="120px"
      ><el-form-item :label="copy.position"
        ><el-select
          v-model="positionNId"
          :aria-label="copy.position"
          :placeholder="copy.selectPosition"
          ><el-option
            v-for="item in store.positions?.items ?? []"
            :key="item.nId"
            :label="item.name + ' (' + item.nId + ')'"
            :value="item.nId" /></el-select></el-form-item
      ><el-form-item :label="copy.effectiveFrom"
        ><el-input v-model="from" type="datetime-local" /></el-form-item
      ><el-form-item :label="copy.effectiveTo"
        ><el-input v-model="to" type="datetime-local" /></el-form-item
      ><el-form-item :label="copy.primary"
        ><el-checkbox v-model="primary">{{ copy.setPrimary }}</el-checkbox></el-form-item
      ></el-form
    ></AppFormDrawer
  >
</template>

<style scoped>
.systemdata-assignment-query {
  container-type: inline-size;
}

.systemdata-assignment-query :deep(.app-query-panel) {
  gap: 0;
  padding: 14px 20px 16px;
  background: var(--ip-color-bg-container);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-lg);
}

.systemdata-assignment-query :deep(.app-query-panel__header) {
  justify-content: space-between;
  margin-bottom: var(--ip-space-3);
}

.systemdata-assignment-query :deep(.app-query-panel__body) {
  gap: 12px;
}

.systemdata-assignment-query__field {
  flex: 0 1 280px;
  min-width: min(280px, 100%);
}

.systemdata-assignment-query__field :deep(.el-input) {
  width: 100%;
}

.systemdata-assignment-query__error,
.systemdata-assignment-query__selected,
.systemdata-assignment-query__results {
  flex: 1 0 100%;
}

.systemdata-assignment-query__error {
  color: var(--ip-color-danger);
  font-size: var(--ip-font-size-sm);
}

.systemdata-assignment-query__selected {
  color: var(--ip-color-text-secondary);
  font-size: var(--ip-font-size-sm);
}

.systemdata-assignment-query__results {
  display: flex;
  flex-wrap: wrap;
  gap: var(--ip-space-2);
  margin: 0;
  padding: 0;
  list-style: none;
}

.systemdata-assignment-query__results li {
  padding: 4px 8px;
  background: var(--ip-color-bg-muted);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-sm);
}

@container (max-width: 720px) {
  .systemdata-assignment-query :deep(.app-query-panel__body--grid) {
    align-items: stretch;
  }

  .systemdata-assignment-query__field {
    flex-basis: 100%;
  }
}
</style>
