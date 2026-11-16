<script setup lang="ts">
import { computed, defineAsyncComponent, onBeforeUnmount, ref, watch } from 'vue'
import { getStudioApi, type DashboardRuntime, type Json } from '@/api/studio'
import type { TraceModalLink, TraceResourceTarget } from '@/api/studioTrace'
import DashboardRuntimeView from '@/components/studio/dashboard/DashboardRuntimeView.vue'
import { useTraceCopy } from './copy'
import { useTraceLinkCopy } from './linkCopy'

const props = defineProps<{ link: TraceModalLink }>()
const LowCode = defineAsyncComponent(() => import('@/pages/pc/studio/StudioRuntimePage.vue'))
const Report = defineAsyncComponent(() => import('@/pages/pc/studio/report/ReportRuntimePage.vue'))
const target = ref<Omit<TraceResourceTarget, 'parameters'> & { parameters: Json }>()
const dashboard = ref<DashboardRuntime>()
const error = ref('')
const t = useTraceCopy(),
  copy = useTraceLinkCopy()
let generation = 0
let controller: AbortController | undefined
let heartbeat: ReturnType<typeof setInterval> | undefined
let expiry: ReturnType<typeof setTimeout> | undefined
let checking = false
function release() {
  generation++
  controller?.abort()
  clearInterval(heartbeat)
  clearTimeout(expiry)
  target.value = undefined
  dashboard.value = undefined
  checking = false
}
const key = computed(() =>
  target.value ? target.value.resourceNId + '/' + target.value.revisionNId : '',
)
watch(
  () => props.link,
  async (link) => {
    release()
    error.value = ''
    const current = generation
    let endpoint: string
    try {
      const url = new URL(link.modalUrl, window.location.origin)
      const nId = url.searchParams.get('nId'),
        revision = url.searchParams.get('revisionNId'),
        ticket = url.searchParams.get('ticket')
      if (
        url.origin !== window.location.origin ||
        !url.pathname.endsWith('/screen/trace-resource') ||
        !nId ||
        !revision ||
        !ticket
      )
        throw new Error(copy('invalid'))
      endpoint = `/api/v1/studio/trace/${encodeURIComponent(nId)}/resource-content?revisionNId=${encodeURIComponent(revision)}&ticket=${encodeURIComponent(ticket)}`
      const remaining = Date.parse(link.expiresOn) - Date.now()
      if (!Number.isFinite(remaining) || remaining <= 0) throw new Error(copy('expired'))
      expiry = setTimeout(
        () => {
          release()
          error.value = copy('expired')
        },
        Math.min(remaining, 2147483647),
      )
    } catch (cause) {
      error.value = cause instanceof Error ? cause.message : copy('invalid')
      return
    }
    async function check() {
      if (checking || current !== generation) return
      checking = true
      controller = new AbortController()
      try {
        const next = await getStudioApi().client.get<NonNullable<typeof target.value>>(endpoint, {
          signal: controller.signal,
        })
        if (current !== generation) return
        if (!['LowCodePage', 'Report', 'ReportBundle', 'Dashboard'].includes(next.kind))
          throw new Error(copy('invalid'))
        if (!target.value) {
          if (next.kind === 'Dashboard') {
            const value = await getStudioApi().dashboardRuntime(
              next.resourceNId,
              next.revisionNId,
              next.parameters,
            )
            if (current !== generation) return
            dashboard.value = value
          }
          target.value = next
        }
      } catch (cause) {
        if (current === generation) {
          release()
          error.value = cause instanceof Error ? cause.message : copy('invalid')
        }
      } finally {
        if (current === generation) checking = false
      }
    }
    await check()
    if (current === generation) heartbeat = setInterval(() => void check(), 30000)
  },
  { immediate: true },
)
onBeforeUnmount(release)
</script>
<template>
  <section class="trace-resource-content">
    <el-alert v-if="error" :title="error" type="warning" :closable="false" />
    <template v-else-if="target">
      <LowCode
        v-if="target.kind === 'LowCodePage'"
        :key="key"
        :resource-n-id="target.resourceNId"
        :revision-n-id="target.revisionNId"
        :initial-parameters="target.parameters"
        embedded
      />
      <Report
        v-else-if="target.kind === 'Report' || target.kind === 'ReportBundle'"
        :key="key"
        :resource-n-id="target.resourceNId"
        :revision-n-id="target.revisionNId"
        :initial-parameters="target.parameters"
        embedded
      />
      <DashboardRuntimeView
        v-else-if="dashboard"
        :runtime="dashboard"
        :parameters="target.parameters"
      />
    </template>
    <p v-else role="status">{{ t('loading') }}</p>
  </section>
</template>
<style scoped>
.trace-resource-content {
  flex: 1;
  min-height: 0;
  overflow: auto;
}
</style>
