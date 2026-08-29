<script setup lang="ts">
import { reactive, watch } from 'vue'
import SystemDataAdminFrame from './SystemDataAdminFrame.vue'
import PermissionGate from '@/permissions/PermissionGate.vue'
import { PERMISSIONS } from '@/permissions'
import { useSystemDataManagementStore } from '@/stores/systemData/managementStore'
const props = withDefaults(
  defineProps<{ title?: string; description?: string; permission?: string }>(),
  {
    title: '租户主题策略',
    description:
      '维护主题允许集合和默认值；保存后重新读取策略，集合或默认值不合法时按错误提示修正。',
    permission: PERMISSIONS.systemDataThemePolicyView,
  },
)
const store = useSystemDataManagementStore()
const draft = reactive({
  allowedPalettes: [] as string[],
  allowedModes: [] as string[],
  allowedPcDensities: [] as string[],
  defaultPalette: '',
  defaultMode: '',
  defaultPcDensity: '',
})
function sync(): void {
  if (store.themePolicy)
    Object.assign(draft, {
      allowedPalettes: [...store.themePolicy.allowedPalettes],
      allowedModes: [...store.themePolicy.allowedModes],
      allowedPcDensities: [...store.themePolicy.allowedPcDensities],
      defaultPalette: store.themePolicy.defaultPalette,
      defaultMode: store.themePolicy.defaultMode,
      defaultPcDensity: store.themePolicy.defaultPcDensity,
    })
}
async function save(): Promise<void> {
  if (
    !store.themePolicy ||
    !draft.allowedPalettes.length ||
    !draft.allowedModes.length ||
    !draft.allowedPcDensities.length
  )
    return
  Object.assign(store.themePolicy, {
    allowedPalettes: [...draft.allowedPalettes],
    allowedModes: [...draft.allowedModes],
    allowedPcDensities: [...draft.allowedPcDensities],
    defaultPalette: draft.defaultPalette,
    defaultMode: draft.defaultMode,
    defaultPcDensity: draft.defaultPcDensity,
  })
  await store.updateThemeDefaults()
}
watch(() => store.themePolicy, sync, { immediate: true })
</script>
<template>
  <SystemDataAdminFrame
    kind="themes"
    :title="props.title"
    :description="props.description"
    :permission="props.permission"
    ><template #toolbar
      ><button type="button" @click="sync">重新读取策略</button
      ><PermissionGate :permission-n-id="PERMISSIONS.systemDataThemePolicyManage"
        ><button type="button" :disabled="store.loading" @click="save">
          保存策略并重新获取
        </button></PermissionGate></template
    >
    <div class="systemdata-theme-editor">
      <h2>允许配色</h2>
      <el-form label-width="120px"
        ><el-form-item label="允许配色"
          ><el-checkbox-group v-model="draft.allowedPalettes"
            ><el-checkbox
              v-for="value in ['industrial-cyan', 'technology-blue', 'neutral-gray']"
              :key="value"
              :label="value" /></el-checkbox-group></el-form-item
        ><el-form-item label="允许模式"
          ><el-checkbox-group v-model="draft.allowedModes"
            ><el-checkbox
              v-for="value in ['light', 'dark', 'system']"
              :key="value"
              :label="value" /></el-checkbox-group></el-form-item
        ><el-form-item label="PC 密度"
          ><el-checkbox-group v-model="draft.allowedPcDensities"
            ><el-checkbox
              v-for="value in ['comfortable', 'compact']"
              :key="value"
              :label="value" /></el-checkbox-group></el-form-item
        ><el-form-item label="默认配色"
          ><el-select v-model="draft.defaultPalette"
            ><el-option
              v-for="value in draft.allowedPalettes"
              :key="value"
              :label="value"
              :value="value" /></el-select></el-form-item
        ><el-form-item label="默认模式"
          ><el-select v-model="draft.defaultMode"
            ><el-option
              v-for="value in draft.allowedModes"
              :key="value"
              :label="value"
              :value="value" /></el-select></el-form-item
        ><el-form-item label="默认密度"
          ><el-select v-model="draft.defaultPcDensity"
            ><el-option
              v-for="value in draft.allowedPcDensities"
              :key="value"
              :label="value"
              :value="value" /></el-select></el-form-item
      ></el-form>
      <p>
        代表性主题预览：{{ draft.defaultPalette }} / {{ draft.defaultMode }} /
        {{ draft.defaultPcDensity }}
      </p>
    </div></SystemDataAdminFrame
  >
</template>
