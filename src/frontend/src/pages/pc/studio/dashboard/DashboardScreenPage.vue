<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import type { Json } from '@/api/studio'
import { loadDashboardScreen } from '@/api/dashboardScreen'
import DashboardRuntimeView from '@/components/studio/dashboard/DashboardRuntimeView.vue'
import { useLocalizationStore } from '@/stores/localizationStore'
import {
  createDataSchedule,
  mergeRuntime,
  requiresFullRuntime,
} from '@/components/studio/dashboard/dataSources'
import { rowKeyFor } from '@/components/studio/dashboard/navigation'
import { createScreenPreloads } from '@/components/studio/dashboard/screenPreloads'
const route = useRoute()
const router = useRouter()
const locale = useLocalizationStore()
const tr = (zh: string, en: string) => (locale.locale === 'en-US' ? en : zh)
const runtime = ref<Awaited<ReturnType<typeof loadDashboardScreen>> | null>(null)
const error = ref('')
const busy = ref(false)
const controls = ref(true)
const root = ref<HTMLElement>()
const screenRuntime = computed(() =>
  runtime.value
    ? {
        ...runtime.value,
        document: {
          ...runtime.value.document,
          canvas: { ...runtime.value.document.canvas, fit: 'Contain' },
        },
      }
    : null,
)
let timer: ReturnType<typeof setTimeout> | undefined
let hideTimer: ReturnType<typeof setTimeout> | undefined
let expiresTimer: ReturnType<typeof setTimeout> | undefined
let controller: AbortController | undefined
let generation = 0
let warmTimer: ReturnType<typeof setTimeout> | undefined
let warmCount = 0
let pending: ReturnType<typeof preloads.take>
let scope = ''
let changing = false
let receivedOn = 0
const dataSchedule = createDataSchedule()
const preloads = createScreenPreloads((steps, signal) =>
  loadDashboardScreen(
    String(route.params.screenId),
    new URLSearchParams(route.hash.slice(1)).get('key') || '',
    signal,
    undefined,
    undefined,
    steps,
  ),
)
const navigationState = computed<{ steps: Json[]; error?: string }>(() => {
  try {
    const text = new URLSearchParams(route.hash.slice(1)).get('navigation')
    if (!text) return { steps: [] }
    if (text.length > 8192) throw new Error()
    const steps: unknown = JSON.parse(text)
    if (
      !Array.isArray(steps) ||
      steps.length > 8 ||
      steps.some((step) => !step || typeof step !== 'object' || typeof step.ruleId !== 'string')
    )
      throw new Error()
    return { steps }
  } catch {
    return { steps: [], error: tr('大屏明细路径无效。', 'Invalid screen navigation path.') }
  }
})
const navigation = computed(() => navigationState.value.steps)
function changeNavigation(steps: Json[]) {
  if (changing) return
  if (runtime.value && runtime.value.transport !== 'Disconnected')
    preloads.put(navigation.value, runtime.value, receivedOn)
  pending = preloads.take(steps)
  changing = true
  busy.value = true
  const hash = new URLSearchParams(route.hash.slice(1))
  if (steps.length) hash.set('navigation', JSON.stringify(steps))
  else hash.delete('navigation')
  void router.push({ path: route.path, hash: `#${hash}` })
}
function preload(candidates: { component: Json; row?: Json }[]) {
  clearTimeout(warmTimer)
  // A single shallow batch per displayed page; never recurse into preloaded screens.
  warmTimer = setTimeout(() => {
    if (
      !runtime.value ||
      busy.value ||
      runtime.value.transport === 'Disconnected' ||
      navigation.value.length >= 8
    )
      return
    for (const { component, row } of candidates) {
      if (warmCount >= 8) break
      const rule = ((runtime.value.document.navigation as Json[]) ?? []).find(
        (item) => item.componentId === component.id && item.event === (row ? 'RowClick' : 'Click'),
      )
      if (!rule) continue
      warmCount++
      preloads.warm([
        ...navigation.value,
        { ruleId: rule.id, ...(row ? { rowKey: rowKeyFor(component, row) } : {}) },
      ])
    }
  }, 250)
}
function navigate(component: Json, row?: Json) {
  if (changing || !runtime.value) return
  const rule = ((runtime.value.document.navigation as Json[]) ?? []).find(
    (item) => item.componentId === component.id && item.event === (row ? 'RowClick' : 'Click'),
  )
  if (!rule) return
  if (navigation.value.length >= 8) {
    error.value = tr(
      '明细最多支持8级，请先返回。',
      'Return before opening more than eight detail levels.',
    )
    showControls()
    return
  }
  changeNavigation([
    ...navigation.value,
    { ruleId: rule.id, ...(row ? { rowKey: rowKeyFor(component, row) } : {}) },
  ])
}
function showControls() {
  controls.value = true
  clearTimeout(hideTimer)
  hideTimer = setTimeout(() => {
    controls.value = false
  }, 3000)
}
function armExpiry(value: NonNullable<typeof runtime.value>) {
  clearTimeout(expiresTimer)
  if (value.expiresOn == null) return
  const remaining = Date.parse(value.expiresOn) - Date.now()
  if (remaining <= 0)
    throw Object.assign(new Error(tr('大屏链接已过期。', 'The screen link has expired.')), {
      status: 404,
    })
  // Longer lifetimes are checked by normal refreshes; never overflow a browser timer.
  if (remaining < 2147483647)
    expiresTimer = setTimeout(() => {
      generation++
      controller?.abort()
      clearTimeout(timer)
      clearTimeout(warmTimer)
      preloads.clear()
      runtime.value = null
      busy.value = false
      error.value = tr('大屏链接已过期。', 'The screen link has expired.')
    }, remaining)
}
async function load(scheduled = false, adopted?: ReturnType<typeof preloads.take>) {
  const current = ++generation
  clearTimeout(timer)
  controller?.abort()
  clearTimeout(warmTimer)
  preloads.stop()
  if (!adopted) preloads.clear()
  warmCount = 0
  controller = adopted?.promise && !adopted.value ? adopted.controller! : new AbortController()
  busy.value = true
  const requestController = controller
  const timeout = setTimeout(() => requestController.abort(), 30000)
  try {
    if (navigationState.value.error)
      throw Object.assign(new Error(navigationState.value.error), { status: 400 })
    const key = new URLSearchParams(route.hash.slice(1)).get('key') || ''
    if (!/^(?:[0-9a-f]{64}|v1\.[A-Za-z0-9_-]{61,1021})$/.test(key))
      throw new Error(
        tr(
          '大屏链接缺少有效密钥，请使用完整分享链接。',
          'Use the complete screen link including its key.',
        ),
      )
    const ids = adopted?.value
      ? []
      : scheduled && runtime.value
        ? dataSchedule.due(runtime.value.document)
        : undefined
    const previous = runtime.value
    const read = (instanceIds?: string[]) =>
      loadDashboardScreen(
        String(route.params.screenId),
        key,
        requestController.signal,
        instanceIds,
        previous?.revisionNId,
        navigation.value,
      )
    let fullRead = ids === undefined
    let value = await (adopted?.promise && !adopted.value ? adopted.promise : read(ids))
    if (current !== generation) return
    if (!fullRead && previous && requiresFullRuntime(previous, value)) {
      value = await read()
      fullRead = true
    }
    if (current !== generation) return
    armExpiry(value)
    runtime.value = mergeRuntime(runtime.value, value)
    if (fullRead || !receivedOn || previous?.revisionNId !== value.revisionNId)
      receivedOn = Date.now()
    dataSchedule.mark(Object.keys(value.instances ?? {}))
    error.value = ''
    document.title = value.resource.name
  } catch (e) {
    if (current !== generation) return
    const status = (e as { status?: number })?.status
    if (runtime.value && (status === undefined || status >= 500)) {
      runtime.value = { ...runtime.value, transport: 'Disconnected' }
      dataSchedule.mark(dataSchedule.due(runtime.value.document))
    } else {
      clearTimeout(expiresTimer)
      runtime.value = null
      preloads.clear()
    }
    error.value =
      e instanceof Error && e.name !== 'AbortError'
        ? e.message
        : tr('大屏连接超时，正在重试。', 'Connection timed out. Retrying.')
  } finally {
    clearTimeout(timeout)
    if (current === generation) {
      busy.value = false
      timer = setTimeout(
        () => void load(true),
        runtime.value ? dataSchedule.delay(runtime.value.document) : 30000,
      )
    }
  }
}
async function fullscreen() {
  try {
    if (document.fullscreenElement) await document.exitFullscreen()
    else await root.value?.requestFullscreen()
  } catch {
    error.value = tr('浏览器未允许全屏，请按 F11。', 'Use F11 to enter fullscreen.')
  }
}
watch(
  () => route.fullPath,
  () => {
    changing = false
    const nextScope = route.path + new URLSearchParams(route.hash.slice(1)).get('key')
    if (nextScope !== scope) {
      preloads.clear()
      pending = undefined
      scope = nextScope
    }
    const adopted = navigationState.value.error
      ? undefined
      : (pending ?? preloads.take(navigation.value))
    pending = undefined
    runtime.value = adopted?.value ?? null
    receivedOn = adopted?.received ?? 0
    clearTimeout(expiresTimer)
    if (runtime.value) {
      document.title = runtime.value.resource.name
      try {
        armExpiry(runtime.value)
      } catch {
        runtime.value = null
        preloads.clear()
      }
    }
    dataSchedule.reset()
    if (runtime.value) dataSchedule.mark(Object.keys(runtime.value.instances ?? {}), receivedOn)
    void load(false, adopted)
    showControls()
  },
  { immediate: true },
)
onBeforeUnmount(() => {
  generation++
  clearTimeout(timer)
  clearTimeout(hideTimer)
  clearTimeout(expiresTimer)
  controller?.abort()
  clearTimeout(warmTimer)
  preloads.clear()
})
</script>
<template>
  <main ref="root" class="dashboard-screen" @pointermove="showControls" @keydown="showControls">
    <DashboardRuntimeView
      v-if="screenRuntime"
      :key="screenRuntime.revisionNId"
      :runtime="screenRuntime"
      :parameters="runtime?.parameters ?? {}"
      class="screen-canvas"
      preload-navigation
      @navigate="navigate"
      @preload="preload"
    />
    <div v-else class="screen-message" role="status">
      <h1>{{ tr('看板大屏', 'Dashboard screen') }}</h1>
      <p>{{ error || tr('正在加载看板…', 'Loading dashboard…') }}</p>
      <button :disabled="busy" @click="load(false)">{{ tr('重试', 'Retry') }}</button>
    </div>
    <aside
      class="screen-controls"
      :class="{ hidden: !controls }"
      @focusin="showControls"
      @pointerenter="showControls"
    >
      <span>{{ runtime?.resource.name || tr('独立大屏', 'Standalone screen') }}</span>
      <span v-if="runtime && busy" role="status">{{ tr('正在更新…', 'Updating…') }}</span>
      <button
        v-if="navigation.length"
        :disabled="busy && !runtime"
        @click="changeNavigation(navigation.slice(0, -1))"
      >
        {{ tr('返回', 'Back') }}
      </button>
      <span v-if="runtime && error" role="alert">{{ error }}</span>
      <button :disabled="busy" @click="load(false)">{{ tr('刷新', 'Refresh') }}</button>
      <button @click="fullscreen">{{ tr('全屏 / 退出', 'Toggle fullscreen') }}</button>
    </aside>
  </main>
</template>
<style scoped>
.dashboard-screen {
  position: fixed;
  inset: 0;
  width: 100vw;
  height: 100dvh;
  overflow: hidden;
  background: #030a12;
  color: #e1edf8;
}
.screen-canvas {
  height: 100%;
  min-height: 0;
  border: 0;
  border-radius: 0;
  overflow: hidden;
  display: flex;
  align-items: center;
  justify-content: center;
}
.screen-canvas :deep(.dashboard-runtime-frame) {
  flex-shrink: 0;
  margin: 0;
}
.screen-controls {
  position: absolute;
  right: 20px;
  top: 16px;
  display: flex;
  gap: 14px;
  align-items: center;
  background: #0d2034e6;
  padding: 10px 14px;
  border-radius: 8px;
  transition: opacity 0.2s;
  z-index: 10;
}
.screen-controls.hidden {
  opacity: 0;
  pointer-events: none;
}
.screen-controls:focus-within {
  opacity: 1;
  pointer-events: auto;
}
button {
  padding: 7px 12px;
  border: 1px solid #426989;
  border-radius: 4px;
  background: #102f4b;
  color: #e1edf8;
  cursor: pointer;
}
button:disabled {
  opacity: 0.5;
  cursor: wait;
}
.screen-message {
  display: grid;
  justify-items: center;
  align-content: center;
  height: 100%;
  padding: 24px;
  text-align: center;
}
</style>
