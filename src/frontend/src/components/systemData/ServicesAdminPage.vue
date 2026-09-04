<script setup lang="ts">
import { computed, reactive, ref } from 'vue'
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
import type { ServiceCatalogDto } from '@/api/systemData/managementTypes'
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
    permission: PERMISSIONS.systemDataServiceCatalogView,
  },
)
const store = useSystemDataManagementStore()
const localization = useLocalizationStore()
const copy = computed(() => systemDataPageCopy(localization.locale, 'services'))
const commonCopy = computed(() => localeMessages[localization.locale].systemData.copy)
const pageTitle = computed(() => props.title || copy.value.title)
const pageDescription = computed(() => props.description || copy.value.description)
const open = ref(false)
const editing = ref('')
const formError = ref('')
const form = reactive({ name: '', entryPoint: 'https://', owner: '' })
const serviceColumns = computed(() => [
  { field: 'name', title: copy.value.name, minWidth: 160, filter: { kind: 'text' as const } },
  {
    field: 'entryPoint',
    title: copy.value.entryPoint,
    minWidth: 220,
    filter: { kind: 'text' as const },
  },
  {
    field: 'healthPath',
    title: copy.value.healthPath,
    minWidth: 180,
    filter: { kind: 'text' as const },
  },
  { field: 'status', title: copy.value.status, width: 100, filter: { kind: 'text' as const } },
])
const groups = computed(() => ({
  Platform: store.services.filter((item) => item.kind === 'Platform'),
  External: store.services.filter((item) => item.kind !== 'Platform'),
}))
async function exportServices(
  group: 'Platform' | 'External',
  request: AppDataTableExportRequest,
): Promise<void> {
  const api = getSystemDataManagementApi()
  if (api === null) return
  const blob = await api.exportServices({
    kind: group,
    search: typeof request.filters.name === 'string' ? request.filters.name : undefined,
    quantity: request.quantity,
    columns: request.columns,
    sortField: request.sort?.field,
    sortOrder: request.sort?.order,
  })
  downloadBlob(blob, `${request.filename}.xlsx`)
}
function edit(item: ServiceCatalogDto): void {
  editing.value = item.serviceNId
  formError.value = ''
  Object.assign(form, {
    name: item.name,
    entryPoint: item.entryPoint,
    owner: item.ownerOrganizationNId ?? '',
  })
  open.value = true
}
function create(): void {
  editing.value = ''
  formError.value = ''
  Object.assign(form, { name: '', entryPoint: 'https://', owner: '' })
  open.value = true
}
async function submit(): Promise<void> {
  formError.value = ''
  const name = form.name.trim()
  const entryPoint = form.entryPoint.trim()
  if (!name) {
    formError.value = copy.value.invalidName
    return
  }
  if (!entryPoint.startsWith('https://')) {
    formError.value = copy.value.invalidHttps
    return
  }
  if (editing.value)
    await store.updateService(editing.value, {
      name,
      entryPoint,
      ...(form.owner ? { ownerOrganizationNId: form.owner } : {}),
    })
  else await store.createService(name, entryPoint, form.owner || undefined)
  if (!store.error) open.value = false
}
async function toggleStatus(item: ServiceCatalogDto): Promise<void> {
  const status = item.status === 'Active' ? 'Inactive' : 'Active'
  try {
    await ElMessageBox.confirm(
      interpolate(copy.value.statusConfirmBody, {
        name: item.name,
        status: systemDataEnumLabel(localization.locale, status),
      }),
      copy.value.statusConfirmTitle,
      {
        type: 'warning',
        confirmButtonText: commonCopy.value.confirm,
        cancelButtonText: commonCopy.value.cancel,
      },
    )
  } catch {
    return
  }
  await store.setServiceStatus(item.serviceNId, { status })
}
</script>
<template>
  <SystemDataAdminFrame
    kind="services"
    :title="pageTitle"
    :description="pageDescription"
    :permission="props.permission"
    ><template #toolbar
      ><PermissionGate :permission-n-id="PERMISSIONS.systemDataServiceCatalogManage"
        ><el-button type="primary" @click="create">
          <ElIcon class="systemdata-page-action-icon" aria-hidden="true"><Plus /></ElIcon>
          {{ copy.createExternal }}
        </el-button></PermissionGate
      ></template
    >
    <div v-for="(items, group) in groups" :key="group" class="systemdata-service-group">
      <h2>{{ group === 'Platform' ? copy.platform : copy.external }}</h2>
      <AppEmptyState v-if="!items.length" :title="copy.emptyGroup" />
      <AppDataTable
        v-else
        :table-key="`systemdata-services-${group}`"
        route-key="systemdata-services"
        row-key="serviceNId"
        :rows="items"
        :total="items.length"
        :columns="serviceColumns"
        :exporter="(request) => exportServices(group, request)"
      >
        <template #cell-healthPath="{ row }">{{ row.healthPath ?? copy.platformHealth }}</template>
        <template #cell-status="{ row }">{{
          systemDataEnumLabel(localization.locale, row.status)
        }}</template>
        <template #actions="{ row }">
          <PermissionGate :permission-n-id="PERMISSIONS.systemDataServiceCatalogManage"
            ><el-button v-if="row.kind === 'External'" link type="primary" @click="edit(row)">
              {{ commonCopy.edit }}</el-button
            ><el-button
              link
              :type="row.status === 'Active' ? 'danger' : 'success'"
              @click="toggleStatus(row)"
            >
              {{ row.status === 'Active' ? commonCopy.disabled : commonCopy.enabled }}
            </el-button></PermissionGate
          >
        </template>
      </AppDataTable>
    </div></SystemDataAdminFrame
  ><AppFormDrawer
    v-model="open"
    :busy="store.loading"
    :title="editing ? copy.editExternal : copy.createExternal"
    @submit="submit"
    ><el-form label-width="120px"
      ><p v-if="formError" role="alert">{{ formError }}</p>
      <el-form-item :label="copy.name"
        ><el-input v-model="form.name" :aria-label="copy.name" /></el-form-item
      ><el-form-item :label="copy.entryPoint"
        ><el-input v-model="form.entryPoint" :aria-label="copy.entryPoint" /></el-form-item
      ><el-form-item :label="copy.owner"
        ><el-select v-model="form.owner" clearable
          ><el-option
            v-for="item in store.organizationTree"
            :key="item.nId"
            :label="item.name + ' (' + item.nId + ')'"
            :value="item.nId" /></el-select
      ></el-form-item>
      <p>
        {{ copy.httpsHint }}
      </p></el-form
    ></AppFormDrawer
  >
</template>

<style scoped>
.systemdata-service-group {
  display: flex;
  min-width: 0;
  flex-direction: column;
  gap: var(--ip-space-3);
}

.systemdata-service-group h2 {
  margin: 0;
  color: var(--ip-color-text-primary);
  font-size: var(--ip-font-size-lg);
  line-height: var(--ip-line-height-tight);
}
</style>
