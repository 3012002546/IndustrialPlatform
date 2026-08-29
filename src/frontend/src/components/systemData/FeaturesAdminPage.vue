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
import { useSystemDataManagementStore } from '@/stores/systemData/managementStore'
import { getSystemDataManagementApi } from '@/api/systemData/managementRegistry'
const props = withDefaults(
  defineProps<{ title?: string; description?: string; permission?: string }>(),
  {
    title: '功能开关',
    description: '维护租户功能覆盖；提交前确认影响资源和菜单，环境强制关闭时覆盖不会生效。',
    permission: PERMISSIONS.systemDataFeatureView,
  },
)
const store = useSystemDataManagementStore()
const selected = ref('')
const mode = ref('Inherit')
const reason = ref('')
const confirmed = ref(false)
const open = ref(false)
const FEATURE_COLUMNS = [
  { field: 'featureNId', title: 'FeatureNId', minWidth: 180, filter: { kind: 'text' as const } },
  { field: 'ownerModuleNId', title: '模块', minWidth: 140 },
  {
    field: 'effectiveEnabled',
    title: '默认/最终',
    minWidth: 150,
    filter: {
      kind: 'select' as const,
      options: [
        { label: '启用', value: true },
        { label: '关闭', value: false },
      ],
    },
  },
  { field: 'status', title: '状态', width: 110 },
]
async function exportFeatures(request: AppDataTableExportRequest): Promise<void> {
  const api = getSystemDataManagementApi()
  if (api === null) return
  const blob = await api.exportFeatures({
    search: typeof request.filters.featureNId === 'string' ? request.filters.featureNId : undefined,
    quantity: request.quantity,
    columns: request.columns,
    sortField: request.sort?.field,
    sortOrder: request.sort?.order,
  })
  downloadBlob(blob, `${request.filename}.xlsx`)
}
function edit(nId: string): void {
  selected.value = nId
  mode.value = 'Inherit'
  reason.value = ''
  confirmed.value = false
  open.value = true
}
async function submit(): Promise<void> {
  if (!selected.value || !confirmed.value) return
  await store.setFeatureOverride(selected.value, mode.value, reason.value.trim())
  if (!store.error) open.value = false
}
</script>
<template>
  <SystemDataAdminFrame
    kind="features"
    :title="props.title"
    :description="props.description"
    :permission="props.permission"
    ><h2>功能开关</h2>
    <AppEmptyState v-if="!store.features.length" title="暂无功能定义" />
    <AppDataTable
      v-else
      table-key="systemdata-features"
      route-key="systemdata-features"
      row-key="featureNId"
      :rows="store.features"
      :total="store.features.length"
      :columns="FEATURE_COLUMNS"
      :exporter="exportFeatures"
    >
      <template #cell-effectiveEnabled="{ row }">
        {{ row.defaultEnabled ? 'Enabled' : 'Disabled' }} /
        {{ row.effectiveEnabled ? 'Enabled' : 'Disabled' }}
      </template>
      <template #actions="{ row }"
        ><PermissionGate :permission-n-id="PERMISSIONS.systemDataFeatureManage"
          ><button type="button" @click="edit(row.featureNId)">覆盖</button></PermissionGate
        ></template
      >
    </AppDataTable></SystemDataAdminFrame
  ><AppFormDrawer v-model="open" :busy="store.loading" title="功能覆盖" @submit="submit"
    ><el-form label-width="120px"
      ><el-form-item label="覆盖模式"
        ><el-select v-model="mode"
          ><el-option label="Inherit" value="Inherit" /><el-option
            label="Enabled"
            value="Enabled" /><el-option
            label="Disabled"
            value="Disabled" /></el-select></el-form-item
      ><el-form-item label="Reason"><el-input v-model="reason" type="textarea" /></el-form-item
      ><el-form-item
        ><el-checkbox v-model="confirmed"
          >我已确认服务端影响资源/菜单统计后再提交</el-checkbox
        ></el-form-item
      ></el-form
    ></AppFormDrawer
  >
</template>
