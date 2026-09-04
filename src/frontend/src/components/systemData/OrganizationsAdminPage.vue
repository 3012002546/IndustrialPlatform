<script setup lang="ts">
import { computed, reactive, ref } from 'vue'
import { ElIcon, ElMessageBox } from 'element-plus'
import { Plus } from '@element-plus/icons-vue'
import AppErrorAlert from '@/components/base/AppErrorAlert.vue'
import AppEmptyState from '@/components/base/AppEmptyState.vue'
import AppFormDrawer from '@/components/management/AppFormDrawer.vue'
import AppTreeTableLayout from '@/components/management/AppTreeTableLayout.vue'
import AppDataTable from '@/components/management/AppDataTable.vue'
import type { AppDataTableExportRequest } from '@/components/management/AppDataTable'
import { downloadBlob } from '@/components/management/download'
import SystemDataAdminFrame from './SystemDataAdminFrame.vue'
import OrganizationMasterList from './OrganizationMasterList.vue'
import PermissionGate from '@/permissions/PermissionGate.vue'
import { PERMISSIONS } from '@/permissions'
import type { OrganizationNodeDto, PositionDto } from '@/api/systemData/managementTypes'
import { useSystemDataManagementStore } from '@/stores/systemData/managementStore'
import { getSystemDataManagementApi } from '@/api/systemData/managementRegistry'
import { localeMessages } from '@/localization/i18n'
import { interpolate, systemDataEnumLabel, systemDataPageCopy } from '@/localization/systemData'
import { useLocalizationStore } from '@/stores/localizationStore'

const props = withDefaults(
  defineProps<{ title?: string; description?: string; permission?: string }>(),
  {
    title: '',
    description: '',
    permission: PERMISSIONS.systemDataOrganizationView,
  },
)
const store = useSystemDataManagementStore()
const localization = useLocalizationStore()
const copy = computed(() => systemDataPageCopy(localization.locale, 'organizations'))
const commonCopy = computed(() => localeMessages[localization.locale].systemData.copy)
const pageTitle = computed(() => props.title || copy.value.title)
const pageDescription = computed(() => props.description || copy.value.description)
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
const positionColumns = computed(() => [
  { field: 'name', title: copy.value.position, minWidth: 180 },
  { field: 'status', title: copy.value.status, width: 100, filter: { kind: 'text' as const } },
])
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
function newChildOrganization(): void {
  if (!store.selectedOrganizationNId) return
  resetForm()
  form.type = 'Department'
  form.parentNId = store.selectedOrganizationNId
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
function selectOrganization(nId: string | null): void {
  if (nId) void store.selectOrganization(nId)
  else store.clearOrganizationSelection()
}
async function refreshOrganizations(): Promise<void> {
  await store.load('organizations')
}
async function exportOrganizations(request: AppDataTableExportRequest): Promise<void> {
  const api = getSystemDataManagementApi()
  if (api === null) return
  const blob = await api.exportOrganizations({
    quantity: request.quantity,
    columns: request.columns,
    sortField: request.sort?.field,
    sortOrder: request.sort?.order,
  })
  downloadBlob(blob, `${request.filename}.xlsx`)
}
async function confirmAction(title: string, message: string): Promise<boolean> {
  try {
    await ElMessageBox.confirm(message, title, {
      type: 'warning',
      confirmButtonText: commonCopy.value.confirm,
      cancelButtonText: commonCopy.value.cancel,
    })
    return true
  } catch {
    return false
  }
}
async function toggleOrganizationStatus(): Promise<void> {
  const item = store.selectedOrganization
  if (!item) return
  const status = item.status === 'Active' ? 'Inactive' : 'Active'
  if (
    !(await confirmAction(
      copy.value.statusConfirmTitle,
      interpolate(copy.value.statusConfirmBody, {
        name: item.name,
        status: systemDataEnumLabel(localization.locale, status),
      }),
    ))
  )
    return
  await store.setOrganizationStatus(item.nId, {
    status,
    reason: moveReason.value.trim() || copy.value.organizationDetail,
  })
}
async function togglePositionStatus(item: PositionDto): Promise<void> {
  const status = item.status === 'Active' ? 'Inactive' : 'Active'
  if (
    !(await confirmAction(
      copy.value.statusConfirmTitle,
      interpolate(copy.value.statusConfirmBody, {
        name: item.name,
        status: systemDataEnumLabel(localization.locale, status),
      }),
    ))
  )
    return
  await store.setPositionStatus(item.nId, { status, reason: copy.value.organizationDetail })
}
async function confirmMove(): Promise<void> {
  const preview = store.movePreview
  if (!preview) return
  if (
    !(await confirmAction(
      copy.value.moveConfirmTitle,
      interpolate(copy.value.moveConfirmBody, {
        organizations: preview.subtreeOrganizationCount,
        positions: preview.subtreePositionCount,
      }),
    ))
  )
    return
  await store.moveOrganization(preview.nId, {
    previewOrganizationRevision: preview.organizationRevision,
    expectedOptimisticVersion: preview.expectedOptimisticVersion,
    expectedConcurrencyVersion: preview.expectedConcurrencyVersion,
    ...(moveTargetNId.value ? { targetParentOrganizationNId: moveTargetNId.value } : {}),
  })
}
async function submit(): Promise<void> {
  formError.value = ''
  if (!form.name.trim()) {
    formError.value = copy.value.invalidName
    return
  }
  if (!Number.isInteger(form.displayOrder) || form.displayOrder < 0) {
    formError.value = copy.value.invalidOrder
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
    formError.value = copy.value.invalidNid
    return
  } else if (form.type === 'position') {
    if (!store.selectedOrganizationNId) {
      formError.value = copy.value.selectOrganization
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
    formError.value = copy.value.missingParent
    return
  } else if (form.type === 'Company' && form.parentNId) {
    formError.value = copy.value.rootParent
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
    :title="pageTitle"
    :description="pageDescription"
    :permission="props.permission"
  >
    <template #toolbar>
      <PermissionGate :permission-n-id="PERMISSIONS.systemDataOrganizationCreate">
        <el-button
          type="primary"
          data-testid="systemdata-organizations-new"
          @click="newOrganization"
        >
          <ElIcon class="systemdata-page-action-icon" aria-hidden="true"><Plus /></ElIcon>
          {{ copy.newOrganization }}
        </el-button>
      </PermissionGate>
      <PermissionGate :permission-n-id="PERMISSIONS.systemDataOrganizationView">
        <el-button
          type="default"
          data-testid="systemdata-organizations-export"
          @click="
            exportOrganizations({
              filename: 'systemdata-organizations',
              quantity: 10000,
              columns: [],
              queryMode: 'top',
              filters: {},
            })
          "
        >
          {{ copy.export }}
        </el-button>
      </PermissionGate>
    </template>
    <AppTreeTableLayout
      class="systemdata-organizations-layout"
      :tree-label="copy.treeLabel"
      :content-label="copy.contentLabel"
    >
      <template #tree>
        <OrganizationMasterList
          :nodes="store.organizationTree"
          :selected-n-id="store.selectedOrganizationNId"
          :loading="store.organizationTreeLoading"
          @select="selectOrganization"
          @refresh="refreshOrganizations"
        />
      </template>
      <template #toolbar
        ><strong>{{ copy.organizationDetail }}</strong
        ><PermissionGate :permission-n-id="PERMISSIONS.systemDataOrganizationCreate"
          ><el-button
            v-if="store.selectedOrganizationNId"
            link
            type="primary"
            data-testid="systemdata-organizations-new-child"
            @click="newChildOrganization"
          >
            {{ copy.newOrganization }}
          </el-button></PermissionGate
        ><PermissionGate :permission-n-id="PERMISSIONS.systemDataPositionCreate"
          ><el-button
            v-if="store.selectedOrganizationNId"
            link
            type="primary"
            data-testid="systemdata-positions-new"
            @click="startPosition"
          >
            {{ copy.newPosition }}
          </el-button></PermissionGate
        ><PermissionGate :permission-n-id="PERMISSIONS.systemDataOrganizationUpdate"
          ><el-button
            v-if="store.selectedOrganization"
            link
            type="primary"
            @click="editOrganization"
          >
            {{ copy.editOrganization }}
          </el-button></PermissionGate
        ><PermissionGate :permission-n-id="PERMISSIONS.systemDataOrganizationStatus"
          ><el-button
            v-if="store.selectedOrganizationNId"
            link
            :type="store.selectedOrganization?.status === 'Active' ? 'danger' : 'success'"
            @click="toggleOrganizationStatus"
          >
            {{ store.selectedOrganization?.status === 'Active' ? copy.disable : copy.enable }}
          </el-button></PermissionGate
        ><el-select
          v-if="store.selectedOrganizationNId"
          v-model="moveTargetNId"
          :aria-label="copy.moveTarget"
          :placeholder="copy.moveTarget"
          clearable
          style="width: 220px"
          ><el-option
            v-for="item in organizations"
            :key="item.nId"
            :label="item.name + '（' + item.nId + '）'"
            :value="item.nId" /></el-select
        ><PermissionGate :permission-n-id="PERMISSIONS.systemDataOrganizationMove"
          ><el-button
            v-if="store.selectedOrganizationNId"
            type="default"
            @click="
              store.previewOrganizationMove(
                store.selectedOrganizationNId,
                moveTargetNId || undefined,
              )
            "
          >
            {{ copy.movePreview }}
          </el-button></PermissionGate
        ></template
      >
      <div v-if="store.organizationDetailLoading" class="systemdata-organization-detail-state" role="status" aria-live="polite">
        {{ commonCopy.loading }}
      </div>
      <AppErrorAlert
        v-else-if="store.organizationDetailError"
        :title="commonCopy.interfaceUnavailable"
        :message="store.organizationDetailError"
      >
        <el-button
          link
          type="primary"
          @click="store.selectedOrganizationNId && store.selectOrganization(store.selectedOrganizationNId)"
        >
          {{ commonCopy.retry }}
        </el-button>
      </AppErrorAlert>
      <template v-else>
        <p v-if="store.selectedOrganization" class="systemdata-organization-context">
          {{ store.selectedOrganization.name }} ·
          {{ systemDataEnumLabel(localization.locale, store.selectedOrganization.type) }} ·
          {{ systemDataEnumLabel(localization.locale, store.selectedOrganization.status) }}
        </p>
        <AppEmptyState v-if="!store.positions" :title="copy.selectOrganization" /><AppEmptyState
          v-else-if="store.positions.items.length === 0"
          :title="copy.noPositions"
        />
        <AppDataTable
          v-else
          table-key="systemdata-positions"
          route-key="systemdata-organizations"
          row-key="nId"
          :rows="store.positions.items"
          :total="store.positions.total"
          :columns="positionColumns"
          :exporter="exportPositions"
        >
        <template #cell-name="{ row }"
          >{{ row.name }}<small>{{ row.description }}</small></template
        >
        <template #cell-status="{ row }">{{
          systemDataEnumLabel(localization.locale, row.status)
        }}</template>
        <template #actions="{ row }">
          <PermissionGate :permission-n-id="PERMISSIONS.systemDataPositionUpdate"
            ><el-button link type="primary" @click="editPosition(row)">
              {{ copy.edit }}
            </el-button></PermissionGate
          ><PermissionGate :permission-n-id="PERMISSIONS.systemDataPositionStatus"
            ><el-button
              link
              :type="row.status === 'Active' ? 'danger' : 'success'"
              @click="togglePositionStatus(row)"
            >
              {{ row.status === 'Active' ? copy.disable : copy.enable }}
            </el-button></PermissionGate
          >
        </template>
        </AppDataTable>
      </template>
      <div v-if="store.movePreview" class="systemdata-move-preview" role="status">
        {{ copy.movePreview }}：{{ copy.organization }}
        {{ store.movePreview.subtreeOrganizationCount }} · {{ copy.position }}
        {{ store.movePreview.subtreePositionCount
        }}<PermissionGate :permission-n-id="PERMISSIONS.systemDataOrganizationMove"
          ><el-button type="primary" @click="confirmMove">
            {{ copy.confirmMove }}
          </el-button></PermissionGate
        >
      </div>
    </AppTreeTableLayout>
  </SystemDataAdminFrame>
  <AppFormDrawer
    v-model="drawerOpen"
    :busy="store.loading"
    :title="
      editingOrganization
        ? copy.editOrganizationTitle
        : editingPosition
          ? copy.editPositionTitle
          : form.type === 'position'
            ? copy.createPositionTitle
            : copy.createOrganizationTitle
    "
    @submit="submit"
    ><el-form :model="form" label-width="120px"
      ><p v-if="formError" role="alert">{{ formError }}</p>
      <el-form-item
        v-if="!editingOrganization && !editingPosition"
        :label="form.type === 'position' ? copy.positionNid : copy.nid"
        ><el-input
          v-model="form.nId"
          :aria-label="form.type === 'position' ? copy.positionNid : copy.nid"
          :placeholder="copy.nid" /></el-form-item
      ><el-form-item :label="copy.name"
        ><el-input v-model="form.name" :aria-label="copy.name" /></el-form-item
      ><template v-if="!editingOrganization && !editingPosition && form.type !== 'position'"
        ><el-form-item :label="copy.organizationType"
          ><el-select v-model="form.type" :aria-label="copy.organizationType"
            ><el-option :label="copy.company" value="Company" /><el-option
              :label="copy.department"
              value="Department" /><el-option :label="copy.section" value="Section" /><el-option
              :label="copy.team"
              value="Team" /></el-select></el-form-item
        ><el-form-item :label="copy.parent"
          ><el-select v-model="form.parentNId" :aria-label="copy.parent" clearable
            ><el-option :label="copy.rootCompany" value="" /><el-option
              v-for="item in organizations"
              :key="item.nId"
              :label="item.name + '（' + item.nId + '）'"
              :value="item.nId" /></el-select
          ><small>{{ copy.parentHint }}</small></el-form-item
        ></template
      ><el-form-item v-if="form.type === 'position'" :label="copy.descriptionField"
        ><el-input v-model="form.description" type="textarea" /></el-form-item
      ><el-form-item :label="copy.displayOrder"
        ><el-input-number
          v-model="form.displayOrder"
          :min="0"
          :step="1"
          :aria-label="copy.displayOrder" /></el-form-item></el-form
  ></AppFormDrawer>
</template>

<style scoped>
.systemdata-organizations-layout {
  min-height: 0;
}

.systemdata-organizations-layout :deep(.app-tree-table__tree) {
  width: min(460px, 36vw);
}

.systemdata-organizations-layout :deep(.app-data-table__card),
.systemdata-organizations-layout :deep(.app-data-table__surface) {
  min-width: 0;
}

.systemdata-organizations-layout :deep(.app-data-table__pagination) {
  max-width: 100%;
  flex-wrap: wrap;
  white-space: normal;
  row-gap: var(--ip-space-1);
}

.systemdata-organizations-layout :deep(.app-tree-table__toolbar) {
  align-items: center;
}

.systemdata-organizations-layout :deep(.app-tree-table__toolbar > strong) {
  margin-right: auto;
  color: var(--ip-color-text-primary);
  font-size: var(--ip-font-size-md);
}

.systemdata-organization-context {
  margin: 0;
  color: var(--ip-color-text-secondary);
  font-size: var(--ip-font-size-sm);
}
.systemdata-organization-detail-state {
  display: flex;
  min-height: 180px;
  align-items: center;
  justify-content: center;
  color: var(--ip-color-text-secondary);
  background: var(--ip-color-bg-muted);
  border: 1px dashed var(--ip-color-border);
  border-radius: var(--ip-radius-md);
}

.systemdata-move-preview {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--ip-space-2);
  padding: var(--ip-space-3);
  color: var(--ip-color-text-primary);
  background: var(--ip-color-bg-muted);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-md);
}

@media (max-width: 1200px) {
  .systemdata-organizations-layout {
    flex-direction: column;
  }

  .systemdata-organizations-layout :deep(.app-tree-table__tree) {
    width: auto;
    height: min(38vh, 320px);
    max-height: min(38vh, 320px);
    flex: 0 0 auto;
  }

  .systemdata-organizations-layout :deep(.app-tree-table__content) {
    min-height: 320px;
  }
}
</style>
