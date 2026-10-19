<script setup lang="ts">
import { onMounted, onBeforeUnmount, ref } from 'vue'
import { getSchedulerApi, type HealthSummary } from '@/api/systemData/schedulerApi'
import { useAuthStore } from '@/stores/authStore'
import { useLocalizationStore } from '@/stores/localizationStore'
import { PERMISSIONS } from '@/permissions'
const auth = useAuthStore(),
  locale = useLocalizationStore()
const summary = ref<HealthSummary | null>(null),
  failed = ref(false),
  busy = ref(false),
  refreshed = ref<number | null>(null),
  now = ref(Date.now())
let timer: ReturnType<typeof setInterval> | null = null,
  alive = true
const text = (zh: string, en: string): string => (locale.locale === 'zh-CN' ? zh : en)
async function refresh(): Promise<void> {
  if (busy.value || !auth.hasPermission(PERMISSIONS.platformHealthView)) return
  busy.value = true
  try {
    const data = await getSchedulerApi().health()
    if (alive) {
      summary.value = data
      refreshed.value = Date.now()
      failed.value = false
    }
  } catch {
    if (alive) {
      failed.value = true
      summary.value = null
    }
  } finally {
    if (alive) busy.value = false
  }
}
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
  <section
    v-if="auth.hasPermission(PERMISSIONS.platformHealthView)"
    class="health-summary"
    :aria-label="text('平台健康摘要', 'Platform health summary')"
  >
    <div class="heading">
      <strong>{{ text('平台健康', 'Platform health') }}</strong
      ><el-button link :loading="busy" @click="refresh">{{ text('刷新', 'Refresh') }}</el-button>
    </div>
    <p v-if="failed" role="alert">
      {{ text('摘要不可用，请重试。', 'Summary unavailable. Retry.') }}
    </p>
    <p v-else-if="summary">
      {{ refreshed && now - refreshed > 90000 ? 'Unknown' : summary.state }} ·
      {{ text('健康', 'Healthy') }} {{ summary.healthyCount }} · {{ text('降级', 'Degraded') }}
      {{ summary.degradedCount }} · {{ text('不可用', 'Unavailable') }}
      {{ summary.unavailableCount }} · {{ text('未知', 'Unknown') }} {{ summary.unknownCount }} ·
      {{ text('停用', 'Disabled') }} {{ summary.disabledCount
      }}<small
        >{{ text('来源摘要时间', 'Summary generated') }}
        {{ new Date(summary.generatedOn).toLocaleString(locale.locale) }}</small
      >
    </p>
    <p v-else>{{ text('等待健康摘要', 'Waiting for health summary') }}</p>
  </section>
</template>
<style scoped>
.health-summary {
  border: 1px solid var(--el-border-color);
  border-radius: var(--el-border-radius-base);
  padding: var(--ip-space-4);
  margin-block: var(--ip-space-4);
}
.heading {
  display: flex;
  justify-content: space-between;
  align-items: center;
}
small {
  display: block;
  color: var(--el-text-color-secondary);
  margin-top: var(--ip-space-2);
}
</style>
