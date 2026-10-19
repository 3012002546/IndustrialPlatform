<script setup lang="ts">
import { computed, onMounted, onBeforeUnmount, ref } from 'vue'
import AppPage from '@/components/base/AppPage.vue'
import AppDataTable from '@/components/management/AppDataTable.vue'
import {
  getSchedulerApi,
  type HealthSummary,
  type HealthCheck,
} from '@/api/systemData/schedulerApi'
import { useLocalizationStore } from '@/stores/localizationStore'
const locale = useLocalizationStore()
const summary = ref<HealthSummary | null>(null),
  selected = ref<string | null>(null),
  loading = ref(false),
  error = ref(''),
  now = ref(Date.now())
let timer: ReturnType<typeof setInterval> | null = null,
  alive = true
const text = (zh: string, en: string): string => (locale.locale === 'zh-CN' ? zh : en)
const services = computed(() => summary.value?.services ?? [])
const selectedService = computed(() =>
  services.value.find((item) => item.serviceNId === selected.value),
)
const serviceColumns = computed(() => [
  { field: 'name', title: text('服务', 'Service'), minWidth: 180 },
  { field: 'state', title: text('健康状态', 'Health state'), width: 150 },
  { field: 'lastObservedOn', title: text('最早来源时间', 'Oldest source time'), minWidth: 210 },
  { field: 'configuredCount', title: text('配置项', 'Configured'), width: 100 },
  { field: 'enabledCount', title: text('启用项', 'Enabled'), width: 100 },
])
const columns = computed(() => [
  { field: 'name', title: text('能力', 'Capability'), minWidth: 170 },
  { field: 'criticality', title: text('关键性', 'Criticality'), width: 130 },
  { field: 'effectiveState', title: text('有效状态', 'Effective state'), width: 140 },
  { field: 'checkedOn', title: text('来源时间', 'Source time'), minWidth: 200 },
  { field: 'reasonSummary', title: text('原因', 'Reason'), minWidth: 220 },
  { field: 'traceId', title: 'TraceId', minWidth: 200 },
])
const effectiveState = (check: HealthCheck): string =>
  check.reportedState === 'Disabled'
    ? 'Disabled'
    : !check.checkedOn ||
        !check.receivedOn ||
        now.value - Math.min(Date.parse(check.checkedOn), Date.parse(check.receivedOn)) > 90000
      ? 'Unknown'
      : check.effectiveState
async function refresh(): Promise<void> {
  if (loading.value) return
  loading.value = true
  try {
    const data = await getSchedulerApi().health(true)
    if (alive) {
      summary.value = data
      error.value = ''
      if (!data.services.some((service) => service.serviceNId === selected.value))
        selected.value = data.services[0]?.serviceNId ?? null
    }
  } catch (cause) {
    if (alive) {
      summary.value = null
      selected.value = null
      error.value =
        cause instanceof Error
          ? cause.message
          : text('健康摘要不可用。', 'Health summary unavailable.')
    }
  } finally {
    if (alive) loading.value = false
  }
}
const time = (value: string | null): string =>
  value ? new Date(value).toLocaleString(locale.locale) : '—'
onMounted(() => {
  void refresh()
  timer = setInterval(() => {
    now.value = Date.now()
    if (document.visibilityState === 'visible') void refresh()
  }, 30000)
})
onBeforeUnmount(() => {
  alive = false
  if (timer) clearInterval(timer)
})
</script>
<template>
  <AppPage
    class="pf07-page"
    :title="text('平台健康', 'Platform health')"
    :description="
      text(
        '真实来源时间、能力降级及未知状态；刷新只读取最新摘要。',
        'Source timestamps, capability degradation and unknown states. Refresh reads the latest summary.',
      )
    "
    ><template #actions
      ><el-button :loading="loading" @click="refresh">{{
        text('刷新', 'Refresh')
      }}</el-button></template
    >
    <p v-if="error" role="alert">{{ error }}</p>
    <p v-if="summary">
      {{ summary.state }} · {{ text('生成时间', 'Generated') }} {{ time(summary.generatedOn) }}
    </p>
    <AppDataTable
      table-key="platform-health-services"
      route-key="pc-platform-health"
      row-key="serviceNId"
      selection="single"
      :rows="services"
      :columns="serviceColumns"
      :selected-row-key="selected"
      :loading="loading"
      @row-click="(row) => (selected = row.serviceNId)"
      @selection-change="(selectedRows) => (selected = selectedRows[0]?.serviceNId ?? null)"
      ><template #cell-lastObservedOn="{ row }">{{
        time(row.lastObservedOn)
      }}</template></AppDataTable
    >
    <h3>{{ selectedService?.name ?? text('请选择服务', 'Select a service') }}</h3>
    <AppDataTable
      table-key="platform-health-checks"
      route-key="pc-platform-health"
      row-key="capabilityKey"
      :rows="selectedService?.checks ?? []"
      :columns="columns"
      ><template #cell-effectiveState="{ row }">{{ effectiveState(row) }}</template
      ><template #cell-checkedOn="{ row }">{{ time(row.checkedOn) }}</template></AppDataTable
    ></AppPage
  >
</template>
<style scoped>
.pf07-page :deep(.vxe-table) {
  --vxe-ui-layout-background-color: var(--el-bg-color);
  --vxe-ui-table-header-background-color: var(--el-fill-color-light);
  --vxe-ui-table-footer-background-color: var(--el-bg-color);
  --vxe-ui-font-color: var(--el-text-color-primary);
  --vxe-ui-table-header-font-color: var(--el-text-color-primary);
  --vxe-ui-table-border-color: var(--el-border-color);
  --vxe-ui-table-row-hover-background-color: var(--el-fill-color-light);
  --vxe-ui-table-row-radio-checked-background-color: var(--el-color-primary-light-9);
  --vxe-ui-table-row-hover-radio-checked-background-color: var(--el-color-primary-light-8);
}

[role='alert'] {
  color: var(--el-color-danger);
}
</style>
