<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import AppPage from '@/components/base/AppPage.vue'
import AppQueryPanel from '@/components/management/AppQueryPanel.vue'
import DashboardRuntimeView from '@/components/studio/dashboard/DashboardRuntimeView.vue'
import DashboardScreenLinks from '@/components/studio/dashboard/DashboardScreenLinks.vue'
import { PermissionGate } from '@/permissions'
import { useLocalizationStore } from '@/stores/localizationStore'
import { getStudioApi, type DashboardRuntime, type Json } from '@/api/studio'
import { useStudioCopy } from '@/components/studio/shared/copy'
import { useDashboardText } from '@/components/studio/dashboard/dashboardCopy'
import { createDataSchedule, mergeRuntime } from '@/components/studio/dashboard/dataSources'
import {
  handoffDashboardRuntime,
  takeDashboardRuntime,
  parseQueryObject,
  rowKeyFor,
} from '@/components/studio/dashboard/navigation'
import { useWorkspaceTabsStore } from '@/stores/workspaceTabsStore'
import { buildTabId } from '@/workspace'
import { ROUTE_NAMES } from '@/router/routeNames'
import '@/components/studio/shared/studio.css'

const route = useRoute(),
  router = useRouter(),
  t = useStudioCopy(),
  dashboardT = useDashboardText()
const runtime = ref<DashboardRuntime | null>(null)
const screenLinksVisible = ref(false)
const locale = useLocalizationStore()
const screenLabel = computed(() => (locale.locale === 'en-US' ? 'Screen links' : '大屏链接'))
const parameterDocument = ref<Json | null>(null)
const error = ref('')
const busy = ref(false)
let navigating = false
const parameters = ref<Json>({})
const modalVisible = ref(false)
const modalBusy = ref(false)
const modalRuntime = ref<DashboardRuntime | null>(null)
const modalParameters = ref<Json>({})
const definitions = computed(() => {
  const values = runtime.value?.document.parameters ?? parameterDocument.value?.parameters
  return (Array.isArray(values) ? (values as Json[]) : []).filter(
    (parameter) => parameter.source !== 'TrustedContext',
  )
})
let generation = 0
let refreshTimer: ReturnType<typeof setTimeout> | undefined
const dataSchedule = createDataSchedule()
function stopRefreshTimer() {
  if (refreshTimer) {
    clearTimeout(refreshTimer)
    refreshTimer = undefined
  }
}
function queryParameters(values: Json): Record<string, string> {
  return {
    ...Object.fromEntries(Object.entries(values).map(([key, value]) => [key, String(value ?? '')])),
    pageParameters: JSON.stringify(values),
  }
}
function scheduleRefresh() {
  stopRefreshTimer()
  if (!runtime.value) return
  const dynamic = ((runtime.value.document.instances as Json[]) ?? []).some(
    (source) => source.sourceType !== 'Constant' && Number(source.refreshSeconds) > 0,
  )
  if (!dynamic) return
  refreshTimer = setTimeout(() => {
    const ids = dataSchedule.due(runtime.value!.document)
    if (ids.length) void load(ids)
    else scheduleRefresh()
  }, dataSchedule.delay(runtime.value.document))
}
function seedParameters() {
  for (const parameter of definitions.value) {
    const name = String(parameter.name)
    if (parameters.value[name] === undefined)
      parameters.value[name] = route.query[name] ?? parameter.defaultValue ?? ''
  }
}
function seedRouteParameters() {
  for (const [key, value] of Object.entries(parseQueryObject(route.query.pageParameters)))
    if (parameters.value[key] === undefined) parameters.value[key] = value
  const reserved = new Set([
    'pageParameters',
    'sourcePlaylistParameters',
    'revisionNId',
    'sourceNId',
    'sourceRevisionNId',
    'sourcePlaylistNId',
    'sourcePlaylistRevisionNId',
    'sourceParameters',
    'sourceFilters',
    'sourceViewport',
    'sourceTabId',
    'filters',
    'viewport',
  ])
  for (const [key, value] of Object.entries(route.query)) {
    if (reserved.has(key) || parameters.value[key] !== undefined) continue
    parameters.value[key] = Array.isArray(value) ? (value[0] ?? '') : (value ?? '')
  }
}
async function loadParameterDefinitions(current: number) {
  const definition = await getStudioApi().dashboardDefinition(
    String(route.params.nId),
    typeof route.query.revisionNId === 'string' ? route.query.revisionNId : undefined,
  )
  if (current !== generation) return
  parameterDocument.value = definition.document
  seedParameters()
}
function missingRequired() {
  return definitions.value.some(
    (parameter) =>
      parameter.required &&
      (parameters.value[String(parameter.name)] == null ||
        String(parameters.value[String(parameter.name)]).trim() === ''),
  )
}
async function load(instanceIds?: unknown, initial = false) {
  const selectedIds = Array.isArray(instanceIds) ? (instanceIds as string[]) : undefined
  const current = ++generation
  stopRefreshTimer()
  busy.value = true
  error.value = ''
  seedRouteParameters()
  try {
    const value =
      (initial ? takeDashboardRuntime(route.fullPath) : undefined) ??
      (await getStudioApi().dashboardRuntime(
        String(route.params.nId),
        selectedIds
          ? runtime.value?.revisionNId
          : typeof route.query.revisionNId === 'string'
            ? route.query.revisionNId
            : undefined,
        parameters.value,
        selectedIds,
      ))
    if (current !== generation) return
    runtime.value = mergeRuntime(runtime.value, value)
    dataSchedule.mark(Object.keys(value.instances))
    parameterDocument.value = value.document
    seedParameters()
  } catch (e) {
    if (current === generation) {
      error.value = e instanceof Error ? e.message : t.value('error')
      if (runtime.value) runtime.value = { ...runtime.value, transport: 'Disconnected' }
      if (selectedIds) dataSchedule.mark(selectedIds)
      else {
        try {
          await loadParameterDefinitions(current)
        } catch {
          // Preserve the original runtime error when metadata is unavailable.
        }
      }
    }
  } finally {
    if (current === generation) {
      busy.value = false
      if (runtime.value) scheduleRefresh()
    }
  }
}
async function submit() {
  if (missingRequired()) {
    error.value = dashboardT.value('requiredParameter')
    return
  }
  await load()
}
function reset() {
  parameters.value = {}
  seedParameters()
  void load()
}
async function navigate(component: Json, row?: Json) {
  if (!runtime.value || navigating) return
  const rule = ((runtime.value.document.navigation as Json[]) ?? []).find(
    (item) => item.componentId === component.id && item.event === (row ? 'RowClick' : 'Click'),
  )
  if (!rule) return
  navigating = true
  try {
    const resolved = await getStudioApi().dashboardResolve(String(route.params.nId), {
      revisionNId: runtime.value.revisionNId,
      ruleId: rule.id,
      rowKey: row ? rowKeyFor(component, row) : undefined,
      parameters: parameters.value,
      filters: parseQueryObject(route.query.filters),
      viewport: parseQueryObject(route.query.viewport),
      includeRuntime: true,
    })
    const targetKind = String(resolved.targetResourceNId ? rule.target?.kind : '')
    const navigationState = (resolved.navigationState as Json | undefined) ?? {}
    const sourceContext = {
      sourceTabId: buildTabId(String(route.name ?? ''), route.params, route.query),
      sourceNId: String(route.params.nId),
      sourceRevisionNId: runtime.value.revisionNId,
      sourceParameters: JSON.stringify(parameters.value),
      sourceFilters: JSON.stringify(
        navigationState.filters ?? parseQueryObject(route.query.filters),
      ),
      sourceViewport: JSON.stringify(
        navigationState.viewport ?? parseQueryObject(route.query.viewport),
      ),
    }
    if (targetKind === 'Dashboard') {
      const targetPath = `/pc/studio/dashboards/${encodeURIComponent(String(resolved.targetResourceNId))}/run`
      const targetQuery = {
        revisionNId: String(resolved.targetRevisionNId),
        ...queryParameters((resolved.parameters as Json) ?? {}),
        filters: JSON.stringify(navigationState.filters ?? {}),
        viewport: JSON.stringify(navigationState.viewport ?? {}),
        ...sourceContext,
      }
      if (String(rule.openMode ?? resolved.openMode ?? 'Current') === 'Modal') {
        modalBusy.value = true
        modalParameters.value = (resolved.parameters as Json) ?? {}
        try {
          modalRuntime.value =
            (resolved.targetRuntime as DashboardRuntime | undefined) ??
            (await getStudioApi().dashboardRuntime(
              String(resolved.targetResourceNId),
              String(resolved.targetRevisionNId),
              modalParameters.value,
            ))
          modalVisible.value = true
        } finally {
          modalBusy.value = false
        }
      } else {
        const target = { path: targetPath, query: targetQuery }
        const fullPath = router.resolve(target).fullPath
        if (resolved.targetRuntime)
          handoffDashboardRuntime(fullPath, resolved.targetRuntime as DashboardRuntime)
        try {
          if (await router.push(target)) takeDashboardRuntime(fullPath)
        } catch (e) {
          takeDashboardRuntime(fullPath)
          throw e
        }
      }
      return
    }
    if (targetKind === 'LowCodePage') {
      await router.push({
        path: `/pc/studio/low-code/${encodeURIComponent(String(resolved.targetResourceNId))}/run`,
        query: {
          revisionNId: String(resolved.targetRevisionNId),
          ...queryParameters((resolved.parameters as Json) ?? {}),
          ...sourceContext,
        },
      })
      return
    }
    ElMessage.info(dashboardT.value('embeddedNavigationUnavailable'))
  } catch (e) {
    error.value = e instanceof Error ? e.message : t.value('error')
  } finally {
    navigating = false
  }
}
function goBack() {
  const sourceNId = String(route.query.sourceNId ?? '')
  const sourcePlaylistNId = String(route.query.sourcePlaylistNId ?? '')
  const sourceId = sourceNId || sourcePlaylistNId
  if (sourceId) {
    const name = sourceNId ? ROUTE_NAMES.studioDashboardRun : ROUTE_NAMES.studioPlaylistRun
    const candidates = useWorkspaceTabsStore().businessTabs.filter(
      (tab) => tab.route.name === name && tab.route.params.nId === sourceId,
    )
    const sourceTabId = route.query.sourceTabId
    const existing =
      candidates.find((tab) => tab.id === sourceTabId) ??
      (!sourceTabId && candidates.length === 1 ? candidates[0] : undefined)
    if (existing) {
      void router.push(existing.route)
      return
    }
    const query: Record<string, string> = {
      ...queryParameters(
        parseQueryObject(
          sourceNId ? route.query.sourceParameters : route.query.sourcePlaylistParameters,
        ),
      ),
    }
    const revision = sourceNId
      ? route.query.sourceRevisionNId
      : route.query.sourcePlaylistRevisionNId
    if (revision) query.revisionNId = String(revision)
    if (route.query.sourceFilters) query.filters = String(route.query.sourceFilters)
    if (route.query.sourceViewport) query.viewport = String(route.query.sourceViewport)
    void router.push({
      name,
      params: { nId: sourceId },
      query,
    })
    return
  }
  void router.push('/pc/studio/dashboards')
}
watch(
  () => route.fullPath,
  () => {
    parameters.value = {}
    runtime.value = null
    parameterDocument.value = null
    void load(undefined, true)
  },
)
onMounted(() => load(undefined, true))
onBeforeUnmount(() => {
  stopRefreshTimer()
  generation++
})
</script>

<template>
  <AppPage :title="runtime?.resource.name ?? dashboardT('dashboardRun')" class="studio-page"
    ><template #actions
      ><el-button @click="goBack">{{ dashboardT('back') }}</el-button
      ><el-button :disabled="busy" @click="load">{{ dashboardT('refresh') }}</el-button>
      <PermissionGate permission-n-id="studio.dashboard.publish"
        ><el-button :disabled="!runtime || busy" @click="screenLinksVisible = true">{{
          screenLabel
        }}</el-button></PermissionGate
      ></template
    ><el-alert v-if="error" :title="error" type="error" :closable="false" /><AppQueryPanel
      v-if="definitions.length"
      show-actions
      @submit="submit"
      @reset="reset"
      ><div class="studio-form-grid">
        <el-form-item
          v-for="parameter in definitions"
          :key="String(parameter.name)"
          :label="String(parameter.label || parameter.name)"
          :required="parameter.required"
          ><el-switch
            v-if="parameter.type === 'Boolean'"
            v-model="parameters[String(parameter.name)]" /><el-input-number
            v-else-if="['Int32', 'Decimal'].includes(String(parameter.type))"
            v-model="parameters[String(parameter.name)]" /><el-input
            v-else
            v-model="parameters[String(parameter.name)]"
        /></el-form-item></div></AppQueryPanel
    ><el-alert
      v-if="missingRequired()"
      :title="dashboardT('requiredParameterBeforeRun')"
      type="info"
      :closable="false"
    /><el-skeleton v-if="busy && !runtime" :rows="8" animated /><DashboardRuntimeView
      v-if="runtime"
      :runtime="runtime"
      :parameters="parameters"
      @navigate="navigate"
    /><el-empty
      v-else-if="!busy && !error"
      :description="dashboardT('noRuntimeResult')"
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
      />
    </el-dialog>
    <DashboardScreenLinks
      v-if="runtime"
      v-model="screenLinksVisible"
      :n-id="String(route.params.nId)"
      :revision-n-id="runtime.revisionNId"
      :parameters="parameters"
    />
  </AppPage>
</template>
