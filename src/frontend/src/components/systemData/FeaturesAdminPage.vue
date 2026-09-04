<script setup lang="ts">
import { ref } from 'vue'
import { computed } from 'vue'
import { ElMessageBox } from 'element-plus'
import AppEmptyState from '@/components/base/AppEmptyState.vue'
import AppFormDrawer from '@/components/management/AppFormDrawer.vue'
import AppDataTable from '@/components/management/AppDataTable.vue'
import type {
  AppDataTableColumn,
  AppDataTableExportRequest,
} from '@/components/management/AppDataTable'
import { downloadBlob } from '@/components/management/download'
import SystemDataAdminFrame from './SystemDataAdminFrame.vue'
import PermissionGate from '@/permissions/PermissionGate.vue'
import { PERMISSIONS } from '@/permissions'
import { useSystemDataManagementStore } from '@/stores/systemData/managementStore'
import { getSystemDataManagementApi } from '@/api/systemData/managementRegistry'
import { localeMessages } from '@/localization/i18n'
import { interpolate, systemDataEnumLabel, systemDataPageCopy } from '@/localization/systemData'
import { useLocalizationStore } from '@/stores/localizationStore'
import type { NavigationNodeDto } from '@/api/systemData/managementTypes'
const props = withDefaults(
  defineProps<{ title?: string; description?: string; permission?: string }>(),
  {
    title: '',
    description: '',
    permission: PERMISSIONS.systemDataFeatureView,
  },
)
const store = useSystemDataManagementStore()
const localization = useLocalizationStore()
const copy = computed(() => systemDataPageCopy(localization.locale, 'features'))
const commonCopy = computed(() => localeMessages[localization.locale].systemData.copy)
const pageTitle = computed(() => props.title || copy.value.title)
const pageDescription = computed(() => props.description || copy.value.description)
const selected = ref('')
const mode = ref('Inherit')
const reason = ref('')
const confirmed = ref(false)
const open = ref(false)
const featureRows = computed(() => {
  const affectedMenus = new Map<string, string[]>()
  const visit = (items: readonly NavigationNodeDto[]): void => {
    items.forEach((item) => {
      if (item.featureNId) {
        affectedMenus.set(item.featureNId, [
          ...(affectedMenus.get(item.featureNId) ?? []),
          item.label,
        ])
      }
      visit(item.children ?? [])
    })
  }
  visit(store.navigationDraft?.nodes ?? [])
  return store.features.map((feature) => ({
    ...feature,
    affectedMenus: affectedMenus.get(feature.featureNId)?.join('、') ?? '—',
  }))
})
const featureColumns = computed<readonly AppDataTableColumn[]>(() => [
  {
    field: 'name',
    title: copy.value.name || copy.value.feature,
    minWidth: 180,
    filter: { kind: 'text' as const },
  },
  { field: 'description', title: copy.value.description || copy.value.use, minWidth: 220 },
  { field: 'featureNId', title: copy.value.code || copy.value.module, minWidth: 180 },
  {
    field: 'defaultEnabled',
    title: copy.value.platformDefault || copy.value.effective,
    width: 130,
    filter: false,
  },
  {
    field: 'effectiveEnabled',
    title: copy.value.currentEffective || copy.value.effective,
    minWidth: 150,
    filter: {
      kind: 'select' as const,
      options: [
        { label: commonCopy.value.enabled, value: true },
        { label: commonCopy.value.disabled, value: false },
      ],
    },
  },
  { field: 'affectedMenus', title: copy.value.affectedMenus || copy.value.impact, minWidth: 180 },
  { field: 'status', title: copy.value.status, width: 110 },
])
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
  try {
    await ElMessageBox.confirm(
      interpolate(copy.value.confirmBody, { feature: selected.value }),
      copy.value.confirmTitle,
      {
        type: 'warning',
        confirmButtonText: commonCopy.value.confirm,
        cancelButtonText: commonCopy.value.cancel,
      },
    )
  } catch {
    return
  }
  await store.setFeatureOverride(selected.value, mode.value, reason.value.trim())
  if (!store.error) open.value = false
}
</script>
<template>
  <SystemDataAdminFrame
    kind="features"
    :title="pageTitle"
    :description="pageDescription"
    :permission="props.permission"
  >
    <AppEmptyState v-if="!store.features.length" :title="copy.noFeatures" />
    <AppDataTable
      v-else
      table-key="systemdata-features"
      route-key="systemdata-features"
      row-key="featureNId"
      :rows="featureRows"
      :total="featureRows.length"
      :columns="featureColumns"
      :exporter="exportFeatures"
    >
      <template #cell-name="{ row }"
        ><strong>{{ row.name }}</strong></template
      >
      <template #cell-description="{ row }">{{ row.description || '—' }}</template>
      <template #cell-defaultEnabled="{ row }">
        {{ row.defaultEnabled ? commonCopy.enabled : commonCopy.disabled }}
      </template>
      <template #cell-effectiveEnabled="{ row }">
        {{ row.effectiveEnabled ? commonCopy.enabled : commonCopy.disabled }}
      </template>
      <template #cell-status="{ row }">{{
        systemDataEnumLabel(localization.locale, row.status)
      }}</template>
      <template #actions="{ row }"
        ><PermissionGate :permission-n-id="PERMISSIONS.systemDataFeatureManage"
          ><el-button link type="primary" @click="edit(row.featureNId)">
            {{ copy.override }}
          </el-button></PermissionGate
        ></template
      >
    </AppDataTable></SystemDataAdminFrame
  ><AppFormDrawer v-model="open" :busy="store.loading" :title="copy.overrideTitle" @submit="submit"
    ><el-form label-width="120px"
      ><el-form-item :label="copy.overrideMode"
        ><el-select v-model="mode" :aria-label="copy.overrideMode"
          ><el-option
            :label="copy.inherit || systemDataEnumLabel(localization.locale, 'Inherit')"
            value="Inherit" /><el-option
            :label="systemDataEnumLabel(localization.locale, 'Enabled')"
            value="Enabled" /><el-option
            :label="systemDataEnumLabel(localization.locale, 'Disabled')"
            value="Disabled" /></el-select></el-form-item
      ><el-form-item :label="copy.reason"
        ><el-input v-model="reason" type="textarea" /></el-form-item
      ><el-form-item
        ><el-checkbox v-model="confirmed">{{ copy.confirmImpact }}</el-checkbox></el-form-item
      ></el-form
    ></AppFormDrawer
  >
</template>
