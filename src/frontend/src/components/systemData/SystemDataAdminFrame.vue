<script setup lang="ts">
import { computed, onMounted } from 'vue'

import AppErrorAlert from '@/components/base/AppErrorAlert.vue'
import AppLoadingState from '@/components/base/AppLoadingState.vue'
import AppPage from '@/components/base/AppPage.vue'
import AppPermissionState from '@/components/base/AppPermissionState.vue'
import AppQueryPanel from '@/components/management/AppQueryPanel.vue'
import { useAuthStore } from '@/stores/authStore'
import {
  type SystemDataAdminKind,
  useSystemDataManagementStore,
} from '@/stores/systemData/managementStore'

const props = defineProps<{
  kind: SystemDataAdminKind
  title: string
  description: string
  permission?: string
}>()
const authStore = useAuthStore()
const store = useSystemDataManagementStore()
const hasPermission = computed(() => !props.permission || authStore.hasPermission(props.permission))
const statusMessage = computed(() =>
  store.error?.includes('409') || store.error?.includes('冲突')
    ? '并发版本冲突：请重新读取后再提交。'
    : (store.error ?? ''),
)

onMounted(() => {
  if (hasPermission.value) void store.load(props.kind)
})
</script>

<template>
  <AppPage :title="title" :description="description">
    <div data-testid="systemdata-admin-page" class="systemdata-admin-page">
      <AppPermissionState v-if="!hasPermission" />
      <template v-else>
        <AppQueryPanel
          title="查询与操作"
          show-actions
          @submit="store.retry(kind)"
          @reset="store.retry(kind)"
        >
          <template #actions>
            <div class="systemdata-admin-toolbar"><slot name="toolbar" /></div>
          </template>
          <div class="systemdata-admin-toolbar">
            <button type="button" @click="store.retry(kind)">刷新</button>
          </div>
        </AppQueryPanel>
        <div class="systemdata-status-strip" data-testid="systemdata-status-strip">
          <span v-if="store.loading" role="status">正在保存或读取，请勿重复提交。</span
          ><span v-else>{{ description }}</span>
        </div>
        <AppLoadingState v-if="store.loading" />
        <AppErrorAlert
          v-else-if="store.error"
          title="管理接口不可用"
          :message="statusMessage"
          :trace-id="store.traceId || ''"
          ><button type="button" @click="store.retry(kind)">重试</button></AppErrorAlert
        >
        <div v-else class="systemdata-admin-content"><slot :store="store" /></div>
      </template>
    </div>
  </AppPage>
</template>

<style scoped>
.systemdata-admin-page {
  display: flex;
  flex-direction: column;
  gap: var(--ip-space-4);
  min-height: 100%;
}
.systemdata-admin-toolbar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--ip-space-2);
}
.systemdata-admin-content {
  min-width: 0;
}
.systemdata-status-strip {
  color: var(--ip-color-text-secondary);
}
</style>
