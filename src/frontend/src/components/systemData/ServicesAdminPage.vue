<script setup lang="ts">
import { computed, reactive, ref } from 'vue'
import AppEmptyState from '@/components/base/AppEmptyState.vue'
import AppFormDrawer from '@/components/management/AppFormDrawer.vue'
import SystemDataAdminFrame from './SystemDataAdminFrame.vue'
import { PERMISSIONS } from '@/permissions'
import type { ServiceCatalogDto } from '@/api/systemData/managementTypes'
import { useSystemDataManagementStore } from '@/stores/systemData/managementStore'
const props = withDefaults(
  defineProps<{ title?: string; description?: string; permission?: string }>(),
  {
    title: '服务目录',
    description:
      '维护服务目录和状态；External 入口必须是 HTTPS，Platform 路径和健康地址由服务端维护。',
    permission: PERMISSIONS.systemDataServiceCatalogView,
  },
)
const store = useSystemDataManagementStore()
const open = ref(false)
const editing = ref('')
const form = reactive({ name: '', entryPoint: 'https://', owner: '' })
const groups = computed(() => ({
  Platform: store.services.filter((item) => item.kind === 'Platform'),
  External: store.services.filter((item) => item.kind !== 'Platform'),
}))
function edit(item: ServiceCatalogDto): void {
  editing.value = item.serviceNId
  Object.assign(form, {
    name: item.name,
    entryPoint: item.entryPoint,
    owner: item.ownerOrganizationNId ?? '',
  })
  open.value = true
}
function create(): void {
  editing.value = ''
  Object.assign(form, { name: '', entryPoint: 'https://', owner: '' })
  open.value = true
}
async function submit(): Promise<void> {
  if (!form.name.trim() || !form.entryPoint.startsWith('https://')) return
  if (editing.value)
    await store.updateService(editing.value, {
      name: form.name.trim(),
      entryPoint: form.entryPoint.trim(),
      ...(form.owner ? { ownerOrganizationNId: form.owner } : {}),
    })
  else await store.createService(form.name.trim(), form.entryPoint.trim(), form.owner || undefined)
  if (!store.error) open.value = false
}
</script>
<template>
  <SystemDataAdminFrame
    kind="services"
    :title="props.title"
    :description="props.description"
    :permission="props.permission"
    ><template #toolbar><button type="button" @click="create">新建 External</button></template>
    <div v-for="(items, group) in groups" :key="group">
      <h2>{{ group }}</h2>
      <AppEmptyState v-if="!items.length" :title="group + ' 服务为空'" />
      <table v-else>
        <thead>
          <tr>
            <th>名称</th>
            <th>入口</th>
            <th>健康声明</th>
            <th>状态</th>
            <th>操作</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="item in items" :key="item.serviceNId">
            <td>{{ item.name }}</td>
            <td>{{ item.entryPoint }}</td>
            <td>{{ item.healthPath ?? '由 PlatformHealth 提供' }}</td>
            <td>{{ item.status }}</td>
            <td>
              <button v-if="item.kind === 'External'" type="button" @click="edit(item)">编辑</button
              ><button
                type="button"
                @click="
                  store.setServiceStatus(item.serviceNId, {
                    status: item.status === 'Active' ? 'Inactive' : 'Active',
                  })
                "
              >
                {{ item.status === 'Active' ? '停用' : '启用' }}
              </button>
            </td>
          </tr>
        </tbody>
      </table>
    </div></SystemDataAdminFrame
  ><AppFormDrawer
    v-model="open"
    :busy="store.loading"
    :title="editing ? '编辑 External 服务' : '新建 External 服务'"
    @submit="submit"
    ><el-form label-width="120px"
      ><el-form-item label="名称"
        ><el-input v-model="form.name" aria-label="服务名称" /></el-form-item
      ><el-form-item label="External HTTPS 入口"
        ><el-input v-model="form.entryPoint" aria-label="External HTTPS 入口" /></el-form-item
      ><el-form-item label="所有者组织"
        ><el-select v-model="form.owner" clearable
          ><el-option
            v-for="item in store.organizationTree"
            :key="item.nId"
            :label="item.name + '（' + item.nId + '）'"
            :value="item.nId" /></el-select
      ></el-form-item>
      <p>
        External 入口只接受 HTTPS；Platform 的 GatewayPathPrefix/HealthPath 由服务端只读维护。
      </p></el-form
    ></AppFormDrawer
  >
</template>
