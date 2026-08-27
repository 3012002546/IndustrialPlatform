<script setup lang="ts">
import { ref } from 'vue'
import AppEmptyState from '@/components/base/AppEmptyState.vue'
import AppFormDrawer from '@/components/management/AppFormDrawer.vue'
import SystemDataAdminFrame from './SystemDataAdminFrame.vue'
import { PERMISSIONS } from '@/permissions'
import { useSystemDataManagementStore } from '@/stores/systemData/managementStore'
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
    <table v-else>
      <thead>
        <tr>
          <th>FeatureNId</th>
          <th>模块</th>
          <th>默认/最终</th>
          <th>状态</th>
          <th>操作</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="feature in store.features" :key="feature.featureNId">
          <td>{{ feature.featureNId }}</td>
          <td>{{ feature.ownerModuleNId }}</td>
          <td>
            {{ feature.defaultEnabled ? 'Enabled' : 'Disabled' }} /
            {{ feature.effectiveEnabled ? 'Enabled' : 'Disabled' }}
          </td>
          <td>{{ feature.status }}</td>
          <td><button type="button" @click="edit(feature.featureNId)">覆盖</button></td>
        </tr>
      </tbody>
    </table></SystemDataAdminFrame
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
