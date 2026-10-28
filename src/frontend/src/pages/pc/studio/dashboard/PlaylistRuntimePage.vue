<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { Loading } from '@element-plus/icons-vue'
import AppPage from '@/components/base/AppPage.vue'
import DashboardRuntimeView from '@/components/studio/dashboard/DashboardRuntimeView.vue'
import DashboardScreenLinks from '@/components/studio/dashboard/DashboardScreenLinks.vue'
import { loadPlaylistScreen } from '@/api/dashboardScreen'
import { useAuthStore } from '@/stores/authStore'
import { getStudioApi, type DashboardRuntime, type Json } from '@/api/studio'
import { useStudioCopy } from '@/components/studio/shared/copy'
import { useDashboardText } from '@/components/studio/dashboard/dashboardCopy'
import {
  createDataSchedule,
  mergeRuntime,
  requiresFullRuntime,
} from '@/components/studio/dashboard/dataSources'
import {
  handoffDashboardRuntime,
  takeDashboardRuntime,
  parseQueryObject,
  rowKeyFor,
} from '@/components/studio/dashboard/navigation'
import { buildTabId } from '@/workspace'
import '@/components/studio/shared/studio.css'

const route = useRoute(),
  router = useRouter(),
  t = useStudioCopy(),
  dashboardT = useDashboardText()
const playlist = ref<Json | null>(null),
  currentRuntime = ref<DashboardRuntime | null>(null),
  currentIndex = ref(0),
  paused = ref(false),
  busy = ref(false),
  error = ref(''),
  generation = ref(0),
  preloaded = ref<{ index: number; runtime: DashboardRuntime } | null>(null),
  modalVisible = ref(false),
  modalBusy = ref(false),
  modalRuntime = ref<DashboardRuntime | null>(null),
  modalParameters = ref<Json>({})
let timer: ReturnType<typeof setTimeout> | undefined
let refreshTimer: ReturnType<typeof setTimeout> | undefined
const dataSchedule = createDataSchedule()
const failedItems = new Set<number>()
let playlistLoadVersion = 0
let navigating = false
const items = computed(() => (playlist.value?.items as Json[]) ?? [])
const currentItem = computed(() => items.value[currentIndex.value] ?? null)
const playlistParameters = ref<Json>({})
const runtimeParameters = ref<Json>({})
const parameterDefinitions = computed(() => (playlist.value?.document?.parameters as Json[]) ?? [])
const screenMode = computed(() => route.meta.playlistScreen === true)
const screenLinksVisible = ref(false)
const playerRoot = ref<HTMLElement>()
const publicNavigation = ref<Json[]>([])
let expiresTimer: ReturnType<typeof setTimeout> | undefined
const publicControllers = new Set<AbortController>()
let warming: { index: number; promise: Promise<DashboardRuntime> } | undefined
const canShare = computed(
  () => !screenMode.value && useAuthStore().hasPermission('studio.dashboard.publish'),
)
const displayedRuntime = computed(() =>
  currentRuntime.value && screenMode.value
    ? {
        ...currentRuntime.value,
        document: {
          ...currentRuntime.value.document,
          canvas: { ...currentRuntime.value.document.canvas, fit: 'Contain' },
        },
      }
    : currentRuntime.value,
)
async function fullscreen() {
  if (document.fullscreenElement) await document.exitFullscreen?.()
  else await playerRoot.value?.requestFullscreen?.()
}
function keyboard(event: KeyboardEvent) {
  if (
    busy.value ||
    (event.target as HTMLElement)?.closest('input,textarea,select,button,[role="combobox"]')
  )
    return
  if (event.key === 'ArrowLeft' || event.key === 'ArrowRight') {
    event.preventDefault()
    void advance(event.key === 'ArrowLeft' ? -1 : 1)
  } else if (event.code === 'Space') {
    event.preventDefault()
    togglePause()
  }
}
function hashState() {
  const params = new URLSearchParams(route.hash.slice(1))
  let text = params.get('navigation') ?? '[]'
  if (text.startsWith('%')) text = decodeURIComponent(text)
  const steps: unknown = JSON.parse(text)
  if (!Array.isArray(steps) || steps.length > 8)
    throw new Error(dashboardT.value('playlistNavigationInvalid'))
  return { key: params.get('key') ?? '', item: params.get('item'), steps: steps as Json[] }
}
function storePosition() {
  if (!screenMode.value) return
  const params = new URLSearchParams(route.hash.slice(1))
  params.set('item', String(currentItem.value?.id ?? ''))
  if (publicNavigation.value.length)
    params.set('navigation', JSON.stringify(publicNavigation.value))
  else params.delete('navigation')
  void router.replace({ hash: '#' + params.toString() }).then(() => {
    if (playlist.value) document.title = String(playlist.value.resource.name)
  })
}
function expireScreen() {
  generation.value++
  currentRuntime.value = null
  playlist.value = null
  preloaded.value = null
  paused.value = true
  busy.value = false
  stopTimer()
  stopRefreshTimer()
  error.value = dashboardT.value('screenUnavailable')
}
function armExpiry(value: Json) {
  clearTimeout(expiresTimer)
  if (!screenMode.value || !value.expiresOn) return
  const delay = Date.parse(value.expiresOn) - Date.now()
  if (delay <= 0) {
    expireScreen()
    return
  }
  expiresTimer = setTimeout(() => armExpiry(value), Math.min(delay, 2147483647))
}
async function publicRuntime(item?: Json, ids?: string[], known?: string): Promise<Json> {
  const key = hashState().key
  if (key.length < 64 || key.length > 1024)
    throw Object.assign(new Error(dashboardT.value('screenUnavailable')), { status: 404 })
  const controller = new AbortController()
  publicControllers.add(controller)
  try {
    const value = await loadPlaylistScreen(
      String(route.params.screenId),
      key,
      controller.signal,
      item ? String(item.id) : undefined,
      ids,
      known,
      item ? publicNavigation.value : undefined,
    )
    armExpiry(value)
    return value
  } finally {
    publicControllers.delete(controller)
  }
}
function fetchItem(item: Json, ids?: string[], known?: string): Promise<DashboardRuntime> {
  return screenMode.value
    ? (publicRuntime(item, ids, known) as Promise<DashboardRuntime>)
    : getStudioApi().dashboardRuntime(
        String(item.dashboardNId),
        String(item.revisionNId),
        itemParameters(item),
        ids,
      )
}
function adoptManifest(value: DashboardRuntime, item: Json) {
  const manifest = (value as Json).playlist
  if (!screenMode.value || !manifest) return
  if (manifest.revisionNId !== playlist.value?.revisionNId) preloaded.value = null
  playlist.value = manifest
  const index = items.value.findIndex((candidate) => candidate.id === item.id)
  if (index >= 0) currentIndex.value = index
  document.title = String(manifest.resource.name)
}
function itemParameters(item: Json): Json {
  if (!Array.isArray(item.parameterMap)) return (item.parameterMap as Json) ?? {}
  const result: Json = {}
  for (const mapping of item.parameterMap as Json[]) {
    const source = mapping.source as Json | undefined
    if (!source) continue
    result[String(mapping.target)] =
      source.from === 'Constant'
        ? source.value
        : source.from === 'PageParam'
          ? playlistParameters.value[String((source.path as unknown[])?.[0] ?? '')]
          : undefined
  }
  return result
}
function stopTimer() {
  if (timer) {
    clearTimeout(timer)
    timer = undefined
  }
}
function stopRefreshTimer() {
  if (refreshTimer) {
    clearTimeout(refreshTimer)
    refreshTimer = undefined
  }
}
function scheduleRefresh() {
  stopRefreshTimer()
  if (!currentRuntime.value || !currentItem.value) return
  if (
    !screenMode.value &&
    !((currentRuntime.value.document.instances as Json[]) ?? []).some(
      (source) => source.sourceType !== 'Constant' && Number(source.refreshSeconds) > 0,
    )
  )
    return
  refreshTimer = setTimeout(
    () => void refreshCurrentItem(true),
    screenMode.value
      ? Math.min(30000, dataSchedule.delay(currentRuntime.value.document))
      : dataSchedule.delay(currentRuntime.value.document),
  )
}
function nextIndex(index: number) {
  if (index + 1 < items.value.length) return index + 1
  return playlist.value?.loop === true ? 0 : -1
}
function schedule() {
  stopTimer()
  if (paused.value || items.value.length < 2 || !currentItem.value || publicNavigation.value.length)
    return
  const seconds = Math.max(3, Number(currentItem.value.staySeconds ?? 10))
  timer = setTimeout(() => void advance(1), seconds * 1000)
}
async function preload(index: number, token: number) {
  if (Number(playlist.value?.preloadCount ?? 1) === 0) return
  if (
    index < 0 ||
    index >= items.value.length ||
    index === currentIndex.value ||
    preloaded.value?.index === index ||
    warming?.index === index
  )
    return
  const item = items.value[index]
  if (!item) return
  try {
    const task = fetchItem(item)
    warming = { index, promise: task }
    const value = await task
    if (token === generation.value) preloaded.value = { index, runtime: value }
  } catch {
    /* current item still controls failure policy */
  } finally {
    if (warming?.index === index) warming = undefined
  }
}
async function mergeItemResponse(
  item: Json,
  previous: DashboardRuntime,
  incoming: DashboardRuntime,
  partial: boolean,
) {
  // A new target revision already returns a full runtime. Only changed parameters
  // on the same revision require missing instances to be read again.
  if (screenMode.value && partial && requiresFullRuntime(previous, incoming))
    incoming = await fetchItem(item)
  return { runtime: mergeRuntime(previous, incoming), receivedIds: Object.keys(incoming.instances) }
}
async function loadItem(index: number, resetNavigation = true, loaded?: DashboardRuntime) {
  stopTimer()
  stopRefreshTimer()
  if (index < 0 || index >= items.value.length) {
    paused.value = true
    stopTimer()
    return
  }
  const token = ++generation.value
  const item = items.value[index]
  if (!item) return
  if (screenMode.value && resetNavigation) publicNavigation.value = []
  currentIndex.value = index
  busy.value = true
  error.value = ''
  try {
    let value: DashboardRuntime | undefined
    if (loaded) value = loaded
    else if (preloaded.value?.index === index) {
      value = preloaded.value.runtime
      preloaded.value = null
      if (screenMode.value) {
        const checked = await fetchItem(item, [], value.revisionNId)
        value = (await mergeItemResponse(item, value, checked, true)).runtime
      }
    } else if (warming?.index === index) {
      value = await warming.promise
    } else value = await fetchItem(item)
    if (token !== generation.value) return
    adoptManifest(value, item)
    currentRuntime.value = value
    runtimeParameters.value = itemParameters(item)
    dataSchedule.reset()
    dataSchedule.mark(Object.keys(value.instances))
    failedItems.clear()
    schedule()
    scheduleRefresh()
    const following = nextIndex(index)
    if (following >= 0 && !publicNavigation.value.length) void preload(following, token)
    storePosition()
  } catch (e) {
    if (token !== generation.value) return
    currentRuntime.value = null
    error.value = e instanceof Error ? e.message : t.value('error')
    failedItems.add(index)
    const following = nextIndex(index)
    if (
      playlist.value?.onFailure === 'Skip' &&
      following >= 0 &&
      !failedItems.has(following) &&
      !publicNavigation.value.length
    )
      await loadItem(following)
    else {
      paused.value = true
      stopTimer()
    }
  } finally {
    if (token === generation.value) busy.value = false
  }
}
async function refreshCurrentItem(scheduled: unknown = false) {
  const token = generation.value
  const index = currentIndex.value
  const item = currentItem.value
  if (!item || !currentRuntime.value) return
  const ids = scheduled === true ? dataSchedule.due(currentRuntime.value.document) : undefined
  if (ids && !ids.length && !screenMode.value) {
    scheduleRefresh()
    return
  }
  try {
    const previous = currentRuntime.value
    let value = await fetchItem(item, ids, previous.revisionNId)
    if (token !== generation.value || index !== currentIndex.value) return
    const merged = await mergeItemResponse(item, previous, value, ids !== undefined)
    value = merged.runtime
    if (token !== generation.value || index !== currentIndex.value) return
    const changed =
      screenMode.value && (value as Json).playlist?.revisionNId !== playlist.value?.revisionNId
    adoptManifest(value, item)
    currentRuntime.value = value
    dataSchedule.mark(merged.receivedIds)
    if (changed) {
      schedule()
      scheduleRefresh()
      storePosition()
    }
    error.value = ''
  } catch (e) {
    if (token !== generation.value || index !== currentIndex.value) return
    if (screenMode.value && [401, 403, 404].includes(Number((e as Json).status))) {
      expireScreen()
      return
    }
    error.value = e instanceof Error ? e.message : t.value('error')
    currentRuntime.value = { ...currentRuntime.value, transport: 'Disconnected' }
    if (ids) dataSchedule.mark(ids)
  } finally {
    if (token === generation.value && index === currentIndex.value) scheduleRefresh()
  }
}
async function advance(delta: number) {
  if (busy.value) return
  const next = nextIndex(currentIndex.value)
  if (delta < 0) {
    const previous =
      currentIndex.value > 0
        ? currentIndex.value - 1
        : playlist.value?.loop === true
          ? Math.max(0, items.value.length - 1)
          : 0
    await loadItem(previous)
    return
  }
  if (next >= 0) await loadItem(next)
  else {
    paused.value = true
    stopTimer()
  }
}
async function selectItem(value: unknown) {
  const index = Number(value)
  if (
    busy.value ||
    !Number.isInteger(index) ||
    index < 0 ||
    index >= items.value.length ||
    (index === currentIndex.value && !publicNavigation.value.length)
  )
    return
  await loadItem(index)
}
function togglePause() {
  failedItems.clear()
  paused.value = !paused.value
  if (paused.value) stopTimer()
  else schedule()
}
async function backToItem() {
  publicNavigation.value = publicNavigation.value.slice(0, -1)
  await loadItem(currentIndex.value, false)
}
async function navigate(component: Json, row?: Json) {
  if (!currentRuntime.value || !currentItem.value || busy.value || navigating) return
  const rule = ((currentRuntime.value.document.navigation as Json[]) ?? []).find(
    (item) => item.componentId === component.id && item.event === (row ? 'RowClick' : 'Click'),
  )
  if (!rule) return
  if (screenMode.value) {
    if (busy.value || publicNavigation.value.length >= 8) return
    publicNavigation.value = [
      ...publicNavigation.value,
      { ruleId: rule.id, ...(row ? { rowKey: rowKeyFor(component, row) } : {}) },
    ]
    preloaded.value = null
    await loadItem(currentIndex.value, false)
    return
  }
  navigating = true
  try {
    const resolved = await getStudioApi().dashboardResolve(String(currentItem.value.dashboardNId), {
      revisionNId: currentRuntime.value.revisionNId,
      ruleId: rule.id,
      rowKey: row ? rowKeyFor(component, row) : undefined,
      parameters: itemParameters(currentItem.value),
      filters: parseQueryObject(route.query.filters),
      viewport: parseQueryObject(route.query.viewport),
      includeRuntime: true,
    })
    if (rule.target?.kind !== 'Dashboard') {
      ElMessage.info(dashboardT.value('embeddedNavigationUnavailable'))
      return
    }
    const navigationState = (resolved.navigationState as Json | undefined) ?? {}
    const targetParameters = (resolved.parameters as Json) ?? {}
    const targetPath = `/pc/studio/dashboards/${encodeURIComponent(String(resolved.targetResourceNId))}/run`
    if (String(rule.openMode ?? resolved.openMode ?? 'Current') === 'Modal') {
      modalBusy.value = true
      modalRuntime.value = null
      modalParameters.value = targetParameters
      try {
        modalRuntime.value =
          (resolved.targetRuntime as DashboardRuntime | undefined) ??
          (await getStudioApi().dashboardRuntime(
            String(resolved.targetResourceNId),
            String(resolved.targetRevisionNId),
            targetParameters,
          ))
        modalVisible.value = true
      } finally {
        modalBusy.value = false
      }
      return
    }
    const target = {
      path: targetPath,
      query: {
        revisionNId: String(resolved.targetRevisionNId),
        ...Object.fromEntries(
          Object.entries(targetParameters).map(([key, value]) => [key, String(value ?? '')]),
        ),
        filters: JSON.stringify(navigationState.filters ?? {}),
        viewport: JSON.stringify(navigationState.viewport ?? {}),
        sourcePlaylistNId: String(route.params.nId),
        sourceTabId: buildTabId(String(route.name ?? ''), route.params, route.query),
        sourcePlaylistRevisionNId: String(playlist.value?.revisionNId ?? ''),
        sourcePlaylistParameters: JSON.stringify(playlistParameters.value),
        sourceFilters: JSON.stringify(
          navigationState.filters ?? parseQueryObject(route.query.filters),
        ),
        sourceViewport: JSON.stringify(
          navigationState.viewport ?? parseQueryObject(route.query.viewport),
        ),
      },
    }
    const fullPath = router.resolve(target).fullPath
    if (resolved.targetRuntime)
      handoffDashboardRuntime(fullPath, resolved.targetRuntime as DashboardRuntime)
    try {
      if (await router.push(target)) takeDashboardRuntime(fullPath)
    } catch (e) {
      takeDashboardRuntime(fullPath)
      throw e
    }
  } catch (e) {
    error.value = e instanceof Error ? e.message : t.value('error')
  } finally {
    navigating = false
  }
}
async function load() {
  stopTimer()
  stopRefreshTimer()
  generation.value++
  const loadVersion = ++playlistLoadVersion
  preloaded.value = null
  failedItems.clear()
  const firstLoad = playlist.value === null
  busy.value = true
  error.value = ''
  try {
    const state = screenMode.value ? hashState() : undefined
    if (state) publicNavigation.value = state.steps
    const resumed = state?.item
      ? ((await publicRuntime({ id: state.item })) as DashboardRuntime)
      : undefined
    const result = screenMode.value
      ? ((resumed as Json | undefined)?.playlist ?? (await publicRuntime()))
      : await getStudioApi().playlistRuntime(
          String(route.params.nId),
          typeof route.query.revisionNId === 'string' ? route.query.revisionNId : undefined,
        )
    if (loadVersion !== playlistLoadVersion) return
    playlist.value = result
    if (firstLoad)
      for (const parameter of parameterDefinitions.value) {
        const name = String(parameter.name)
        if (playlistParameters.value[name] === undefined)
          playlistParameters.value[name] = route.query[name] ?? parameter.defaultValue ?? ''
      }
    currentIndex.value = 0
    if (screenMode.value) {
      if (state?.item) {
        const index = items.value.findIndex((item) => item.id === state.item)
        if (index < 0) throw new Error(dashboardT.value('screenUnavailable'))
        currentIndex.value = index
      }
      document.title = String(result.resource.name)
    }
    if (items.value.length) await loadItem(currentIndex.value, false, resumed)
  } catch (e) {
    if (loadVersion !== playlistLoadVersion) return
    currentRuntime.value = null
    error.value = e instanceof Error ? e.message : t.value('error')
  } finally {
    if (loadVersion === playlistLoadVersion) busy.value = false
  }
}
onMounted(() => {
  void load()
  if (screenMode.value) {
    playerRoot.value?.focus({ preventScroll: true })
  }
})
onBeforeUnmount(() => {
  playlistLoadVersion++
  stopTimer()
  stopRefreshTimer()
  generation.value++
  clearTimeout(expiresTimer)
  for (const controller of publicControllers) controller.abort()
})
</script>

<template>
  <div
    ref="playerRoot"
    class="playlist-host"
    :class="{ 'playlist-screen': screenMode }"
    :tabindex="screenMode ? 0 : undefined"
    @keydown="keyboard"
  >
    <component
      :is="screenMode ? 'main' : AppPage"
      :title="playlist?.resource?.name ?? dashboardT('playlistRun')"
      :class="screenMode ? 'playlist-stage' : 'studio-page'"
      ><template v-if="!screenMode" #actions
        ><el-button @click="router.push('/pc/studio/playlists')">{{ dashboardT('back') }}</el-button
        ><el-button :disabled="busy" @click="load">{{ dashboardT('refresh') }}</el-button
        ><el-button v-if="canShare && playlist" @click="screenLinksVisible = true">{{
          dashboardT('screenLink')
        }}</el-button>
      </template>
      <div :class="{ 'screen-control-dock': screenMode }">
        <div class="playlist-controls" :class="{ 'screen-controls': screenMode }">
          <span v-if="screenMode"
            >{{ playlist?.resource.name || dashboardT('playlistScreen') }} ·
            {{ items.length ? currentIndex + 1 : 0 }} / {{ items.length }}</span
          >
          <el-button
            v-if="screenMode && publicNavigation.length"
            :disabled="busy"
            @click="backToItem"
            >{{ dashboardT('back') }}</el-button
          >
          <el-button :disabled="!items.length || busy" @click="advance(-1)">{{
            dashboardT('previous')
          }}</el-button
          ><el-button :disabled="!items.length || busy" @click="togglePause">{{
            paused ? dashboardT('continue') : dashboardT('pause')
          }}</el-button
          ><el-button :disabled="!items.length || busy" @click="advance(1)">{{
            dashboardT('next')
          }}</el-button>
          <el-select
            :model-value="currentIndex"
            :disabled="!items.length || busy"
            :teleported="!screenMode"
            :aria-label="dashboardT('switchDashboard')"
            @change="selectItem"
          >
            <el-option
              v-for="(item, index) in items"
              :key="String(item.id ?? index)"
              :value="index"
              :label="`${index + 1} · ${item.label || dashboardT('dashboard')}`"
            />
          </el-select>
          <el-button
            v-if="screenMode"
            :disabled="busy"
            @click="publicNavigation.length ? loadItem(currentIndex, false) : load()"
            >{{ dashboardT('refresh') }}</el-button
          >
          <el-button @click="fullscreen">{{ dashboardT('openFullscreen') }}</el-button>
        </div>
      </div>
      <el-alert v-if="error" :title="error" type="error" :closable="false" />
      <div v-if="!screenMode && items.length" class="playlist-runtime-bar">
        <strong>{{ currentIndex + 1 }} / {{ items.length }}</strong
        ><span>{{ currentItem?.label || currentRuntime?.resource.name }}</span
        ><el-progress
          :percentage="Math.round(((currentIndex + 1) / items.length) * 100)"
          :show-text="false"
        />
      </div>
      <div
        v-if="busy"
        class="playlist-loading"
        :class="{ 'is-initial': !currentRuntime }"
        role="status"
        aria-live="polite"
      >
        <el-icon class="is-loading"><Loading /></el-icon>
        <span>{{ dashboardT('loading') }}</span>
      </div>
      <DashboardRuntimeView
        v-if="displayedRuntime"
        :inert="busy ? '' : undefined"
        :aria-busy="busy"
        :runtime="displayedRuntime"
        :parameters="screenMode ? ((currentRuntime as Json)?.parameters ?? {}) : runtimeParameters"
        @navigate="navigate"
      /><el-empty
        v-else-if="!busy && !error"
        :description="dashboardT('noPlaylistItems')"
      /><el-dialog
        v-model="modalVisible"
        :title="dashboardT('dashboardDetail')"
        width="90%"
        destroy-on-close
      >
        <el-skeleton v-if="modalBusy" :rows="8" animated />
        <DashboardRuntimeView
          v-else-if="modalRuntime"
          :runtime="modalRuntime"
          :parameters="modalParameters"
        /> </el-dialog
      ><DashboardScreenLinks
        v-if="!screenMode && playlist"
        v-model="screenLinksVisible"
        :n-id="String(route.params.nId)"
        :revision-n-id="String(playlist.revisionNId)"
        :parameters="playlistParameters"
        resource-kind="Playlist"
      />
    </component>
  </div>
</template>

<style scoped>
.playlist-host {
  position: relative;
}
.playlist-loading {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  color: var(--el-text-color-secondary);
}
.playlist-loading.is-initial {
  min-height: 360px;
}
.playlist-loading:not(.is-initial) {
  position: absolute;
  z-index: 10;
  bottom: 88px;
  left: 50%;
  transform: translateX(-50%);
  padding: 10px 16px;
  border-radius: 8px;
  background: var(--el-bg-color-overlay);
  box-shadow: 0 4px 16px #0003;
  pointer-events: none;
}
.playlist-screen .playlist-loading {
  color: #a4c4d8;
}
.playlist-screen .playlist-loading.is-initial {
  min-height: 0;
  height: 100%;
}
.playlist-screen .playlist-loading:not(.is-initial) {
  background: #0d2034e6;
}
.playlist-controls {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 10px;
  margin-bottom: 12px;
}
.playlist-controls .el-select {
  width: 240px;
}
.playlist-screen {
  position: fixed;
  inset: 0;
  width: 100vw;
  height: 100dvh;
  background: #030a12;
  color: #e1edf8;
  overflow: hidden;
}
.playlist-stage {
  height: 100%;
}
.playlist-screen :deep(.dashboard-runtime-host) {
  box-sizing: border-box;
  width: 100%;
  height: 100%;
  min-height: 0;
  border: 0;
  border-radius: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  overflow: hidden;
}
.playlist-screen :deep(.dashboard-runtime-frame) {
  margin: 0;
  flex-shrink: 0;
}
.screen-control-dock {
  position: absolute;
  bottom: 0;
  left: 50%;
  transform: translateX(-50%);
  z-index: 20;
  width: max-content;
  max-width: calc(100% - 24px);
  padding: 24px 0 max(12px, env(safe-area-inset-bottom));
}
.screen-controls {
  --el-color-white: #e1edf8;
  --el-text-color-regular: #e1edf8;
  --el-fill-color-blank: #182d40;
  --el-border-color: #42627d;
  --el-color-primary: #7ed8ec;
  justify-content: center;
  gap: 8px;
  margin: 0;
  padding: 10px 12px;
  border: 1px solid #42627d80;
  border-radius: 12px;
  background: #0d2034e6;
  box-shadow: 0 6px 24px #0006;
  backdrop-filter: blur(12px);
  transition: opacity 0.2s;
  opacity: 0;
  pointer-events: none;
}
.screen-controls :deep(.el-button + .el-button) {
  margin-left: 0;
}
.screen-control-dock:hover .screen-controls,
.screen-control-dock:has(:focus-visible) .screen-controls {
  opacity: 1;
  pointer-events: auto;
}
@media (hover: none) {
  .screen-controls {
    opacity: 1;
    pointer-events: auto;
  }
}
.playlist-screen :deep(.el-alert) {
  position: absolute;
  top: 80px;
  left: 10%;
  width: 80%;
  z-index: 10;
}
.playlist-runtime-bar {
  display: grid;
  grid-template-columns: auto 1fr 240px;
  align-items: center;
  gap: 14px;
  margin-bottom: 12px;
  padding: 10px 14px;
  border: 1px solid var(--el-border-color);
  background: var(--el-bg-color);
}
@media (max-width: 700px) {
  .playlist-runtime-bar {
    grid-template-columns: 1fr;
  }
}
</style>
