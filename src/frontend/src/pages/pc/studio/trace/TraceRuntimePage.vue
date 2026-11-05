<script setup lang="ts">
import { computed, onBeforeUnmount, onDeactivated, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  getTraceApi,
  type TraceContract,
  type TraceStartRow,
  type TraceExecutableModel,
} from '@/api/studioTrace'
import { useAuthStore } from '@/stores/authStore'
import { PermissionGate } from '@/permissions'
import AppPage from '@/components/base/AppPage.vue'
import GraphWorkbench from '@/components/studio/graph/GraphWorkbench.vue'
import TraceStartPicker from '@/components/studio/trace/TraceStartPicker.vue'
import TraceLinkModal from '@/components/studio/trace/TraceLinkModal.vue'
import TraceDetailSection from '@/components/studio/trace/TraceDetailSection.vue'
import { projectTraceResult } from '@/components/studio/trace/adapter'
import { useTraceRuntime } from '@/components/studio/trace/useTraceRuntime'
import { useTraceCopy } from '@/components/studio/trace/copy'
import { useTracePresentation } from '@/components/studio/trace/presentation'
import type { TraceDirection, TraceLayoutDirection } from '@/components/studio/trace/model'
defineOptions({ name: 'TraceRuntimePage' })
const route = useRoute(),
  router = useRouter(),
  auth = useAuthStore(),
  api = getTraceApi(),
  t = useTraceCopy(),
  text = useTracePresentation()
const models = ref<TraceExecutableModel[]>([]),
  modelsBusy = ref(false),
  modelsError = ref('')
let modelsGeneration = 0
let modelsController: AbortController | undefined
const contract = ref<TraceContract>(),
  pickerOpen = ref(false),
  advancedOpen = ref(false),
  direction = ref<TraceDirection>('Both'),
  layoutDirection = ref<TraceLayoutDirection>('LeftToRight'),
  activeDetail = ref('overview'),
  relationFilter = ref<string[]>([]),
  maxDepth = ref(12),
  loadError = ref(''),
  loading = ref(false),
  contextKey = ref(crypto.randomUUID())
// A retained page must not consume another workspace tab's query parameters.
const pagePath = route.path
const executionQuery = ref(route.query)
watch(
  () => route.query,
  (query) => {
    if (route.path === pagePath) executionQuery.value = query
  },
  { flush: 'sync' },
)
const nId = computed(() =>
    typeof executionQuery.value.model === 'string' ? executionQuery.value.model : '',
  ),
  runtime = useTraceRuntime(api, () => ({
    nId: nId.value,
    revisionNId: contract.value?.revisionNId ?? '',
  }))
const {
  starts,
  selectedKeys,
  result,
  detail,
  modal,
  selection,
  busy,
  detailBusy,
  error,
  detailError,
} = runtime
let contractGeneration = 0
let controller: AbortController | undefined
let disposed = false
async function loadModels() {
  const current = ++modelsGeneration
  modelsController?.abort()
  modelsController = new AbortController()
  modelsBusy.value = true
  modelsError.value = ''
  try {
    const value = await api.models(modelsController.signal)
    if (current !== modelsGeneration || disposed) return
    models.value = value.items
    if (nId.value && !value.items.some((item) => item.nId === nId.value)) {
      contractGeneration++
      controller?.abort()
      runtime.invalidate()
      contract.value = undefined
      pickerOpen.value = false
      loading.value = false
      loadError.value = t('noModels')
    }
  } catch (e) {
    if (current === modelsGeneration && !disposed) {
      models.value = []
      contractGeneration++
      controller?.abort()
      contract.value = undefined
      runtime.invalidate()
      loading.value = false
      modelsError.value = e instanceof Error ? e.message : String(e)
    }
  } finally {
    if (current === modelsGeneration && !disposed) modelsBusy.value = false
  }
}
function chooseModel(value: string) {
  void router.replace({ query: value ? { model: value } : {} })
}
async function refreshModels() {
  await loadModels()
}
function identityLabel(key: string) {
  const labels: Record<string, string> = {
    tenantNId: t('tenant'),
    sourceSystem: t('sourceSystem'),
    factory: t('factory'),
    businessKey: t('businessKey'),
  }
  return labels[key] ?? key
}
function evidenceLabel(key: string) {
  const labels: Record<string, string> = {
    recordKey: t('recordKey'),
    occurredOn: t('occurredOn'),
    quantity: t('quantity'),
    unit: t('unit'),
  }
  return labels[key] ?? key
}
function nodeLabel(key: string) {
  return result.value?.nodes.find((node) => node.key === key)?.identity.businessKey ?? key
}
const graph = computed(() =>
  result.value
    ? projectTraceResult(
        result.value,
        {
          contextKey: `${contextKey.value}:${layoutDirection.value}`,
          definition: {
            kind: 'published',
            resourceNId: nId.value,
            revisionNId: result.value.revisionNId,
          },
        },
        layoutDirection.value,
      )
    : null,
)
const selectedNode = computed(() =>
  selection.value[0]?.kind === 'node'
    ? result.value?.nodes.find((n) => n.key === selection.value[0]?.id)
    : undefined,
)
const selectedEdge = computed(() =>
  selection.value[0]?.kind === 'edge'
    ? result.value?.edges.find((e) => e.key === selection.value[0]?.id)
    : undefined,
)
const relationIds = () => [...relationFilter.value]
async function load() {
  const current = ++contractGeneration
  controller?.abort()
  controller = new AbortController()
  runtime.invalidate()
  contract.value = undefined
  pickerOpen.value = false
  contextKey.value = crypto.randomUUID()
  direction.value = 'Both'
  relationFilter.value = []
  error.value = ''
  loadError.value = ''
  if (!nId.value || disposed) {
    loading.value = false
    return
  }
  loading.value = true
  try {
    const value = await api.contract(
      nId.value,
      typeof executionQuery.value.revisionNId === 'string'
        ? executionQuery.value.revisionNId
        : undefined,
      controller.signal,
    )
    if (current !== contractGeneration || disposed) return
    contract.value = value
    layoutDirection.value = value.presentation?.layoutDirection ?? 'LeftToRight'
    maxDepth.value = value.maxDepth
    relationFilter.value = []
  } catch (e) {
    if (current === contractGeneration && !disposed)
      loadError.value = e instanceof Error ? e.message : String(e)
  } finally {
    if (current === contractGeneration && !disposed) loading.value = false
  }
}
function confirmStarts(rows: TraceStartRow[]) {
  runtime.invalidateQuery()
  starts.value = rows
  selectedKeys.value = rows.map((row) => row.startKey)
  pickerOpen.value = false
}
function denied(message: string) {
  pickerOpen.value = false
  runtime.invalidate()
  error.value = message
}
watch(
  [nId, () => executionQuery.value.revisionNId, () => JSON.stringify(auth.user)],
  () => {
    void load()
  },
  { immediate: true, flush: 'sync' },
)
watch(
  () => JSON.stringify(auth.user),
  () => {
    models.value = []
    void loadModels()
  },
  { immediate: true },
)
watch([direction, relationFilter, maxDepth, selectedKeys], () => runtime.invalidateQuery())
watch(
  () => selection.value[0]?.id,
  () => {
    activeDetail.value = 'overview'
  },
)
onDeactivated(() => {
  pickerOpen.value = false
})
onBeforeUnmount(() => {
  disposed = true
  contractGeneration++
  controller?.abort()
  modelsGeneration++
  modelsController?.abort()
})
</script>
<template>
  <AppPage class="trace-run-page">
    <div class="trace-model-bar">
      <label
        >{{ t('model')
        }}<el-select
          :model-value="models.some((model) => model.nId === nId) ? nId : ''"
          filterable
          clearable
          :aria-label="t('model')"
          :placeholder="modelsBusy ? t('loading') : t('chooseModel')"
          :loading="modelsBusy"
          @update:model-value="chooseModel"
        >
          <el-option
            v-for="model in models"
            :key="model.nId"
            :value="model.nId"
            :label="model.name"
          /> </el-select
      ></label>
      <PermissionGate permission-n-id="studio.trace.run">
        <label class="trace-direction"
          >{{ t('direction')
          }}<el-select v-model="direction" :disabled="!contract" :aria-label="t('direction')">
            <el-option
              v-for="v in ['Upstream', 'Downstream', 'Both'] as const"
              :key="v"
              :value="v"
              :label="t(v)"
            /> </el-select
        ></label>
        <el-button :disabled="!contract || busy" @click="pickerOpen = true">{{
          t('chooseGraph')
        }}</el-button>
        <el-button
          type="primary"
          :disabled="!contract || !selectedKeys.length || busy"
          :loading="busy"
          @click="runtime.query(direction, relationIds(), maxDepth)"
          >{{ t('loadGraph') }}</el-button
        >
        <el-button v-if="busy" @click="runtime.cancel">{{ t('cancel') }}</el-button>
        <span v-if="selectedKeys.length" class="trace-selection-summary"
          >{{ t('selected') }}: {{ selectedKeys.length }}</span
        >
        <el-button
          v-if="contract"
          link
          :aria-expanded="advancedOpen"
          @click="advancedOpen = !advancedOpen"
          >{{ t('advanced') }}</el-button
        >
      </PermissionGate>
      <el-button link :loading="modelsBusy" @click="refreshModels">{{
        t('refreshModels')
      }}</el-button>
      <label v-if="contract" class="trace-direction trace-layout-direction"
        >{{ t('graphLayout') }}
        <el-select v-model="layoutDirection" :aria-label="t('graphLayout')">
          <el-option
            v-for="value in ['LeftToRight', 'TopToBottom'] as const"
            :key="value"
            :value="value"
            :label="t(value)"
          />
        </el-select>
      </label>
    </div>
    <PermissionGate v-if="contract && advancedOpen" permission-n-id="studio.trace.run">
      <div class="trace-query-controls">
        <el-tag>{{ t('currentVersion') }} {{ contract.revisionNo }}</el-tag>
        <label
          >{{ t('relation')
          }}<el-select
            v-model="relationFilter"
            multiple
            collapse-tags
            collapse-tags-tooltip
            clearable
            :aria-label="t('relation')"
          >
            <el-option
              v-for="relation in contract.relations"
              :key="relation.id"
              :value="relation.id"
              :label="relation.name + ' · ' + text.option(relation.kind)"
            /> </el-select
        ></label>
        <label
          >{{ t('depth')
          }}<el-input-number
            v-model="maxDepth"
            :min="1"
            :max="contract.maxDepth"
            :aria-label="t('depth')"
        /></label>
      </div>
    </PermissionGate>
    <el-alert v-if="modelsError" :title="modelsError" type="error" :closable="false" />
    <el-alert
      v-if="loadError || error"
      :title="loadError || error"
      type="error"
      :closable="false"
    />
    <el-button v-if="loadError" @click="load">{{ t('retry') }}</el-button>
    <div v-if="loading || busy" role="status">{{ t('loading') }}</div>
    <PermissionGate permission-n-id="studio.trace.run">
      <TraceStartPicker
        v-if="pickerOpen && contract"
        :key="contract.revisionNId"
        :resource-n-id="nId"
        :contract="contract"
        @close="pickerOpen = false"
        @confirm="confirmStarts"
        @denied="denied"
      />
    </PermissionGate>
    <template v-if="result && graph"
      ><el-alert
        :type="result.status === 'Partial' ? 'warning' : 'success'"
        :title="t(result.status === 'Partial' ? 'partial' : 'complete')"
        :closable="false"
        ><ul v-if="result.partialReasons.length">
          <li v-for="reason in result.partialReasons" :key="reason">{{ reason }}</li>
        </ul></el-alert
      >
      <div class="trace-result-workspace" :class="{ 'has-selection': selection.length }">
        <GraphWorkbench
          :view="graph"
          mode="run"
          read-only
          :selection="selection"
          @selection-change="runtime.select"
          @error="error = $event"
        />
        <aside v-if="selection.length" class="trace-details">
          <PermissionGate permission-n-id="studio.trace.run"
            ><el-button
              v-if="selectedNode"
              :disabled="busy"
              @click="runtime.expand(direction, relationIds())"
              >{{ t('expand') }}</el-button
            ></PermissionGate
          >
          <p v-if="!selection.length">{{ t('noSelection') }}</p>
          <el-alert v-if="detailError" :title="detailError" type="error" :closable="false" />
          <p v-if="detailBusy">{{ t('loading') }}</p>
          <el-tabs v-if="selectedNode" v-model="activeDetail" class="trace-detail-tabs">
            <el-tab-pane name="overview" :label="t('overview')">
              <template v-if="selectedNode"
                ><h3>{{ t('identity') }}</h3>
                <dl>
                  <template v-for="(value, key) in selectedNode.identity" :key="key"
                    ><dt>{{ identityLabel(key) }}</dt>
                    <dd>{{ value }}</dd></template
                  >
                </dl>
                <el-tag v-if="selectedNode.boundary" type="warning">{{ t('partial') }}</el-tag>
                <p v-for="reason in selectedNode.boundaryReasons ?? []" :key="reason">
                  {{ reason }}
                </p></template
              >
              <template v-if="detail"
                ><h3>{{ t('detail') }}</h3>
                <dl>
                  <template v-for="(field, i) in detail.fields" :key="i"
                    ><dt>{{ field.label }}</dt>
                    <dd>{{ field.value }}</dd></template
                  >
                </dl>
                <el-button
                  v-for="link in detail.links"
                  :key="link.id"
                  @click="runtime.openLink(link.id)"
                  >{{ link.label }}</el-button
                ><small>{{ detail.observedOn || t('unknown') }}</small></template
              >
            </el-tab-pane>
            <el-tab-pane
              v-for="section in detail?.sections ?? []"
              :key="section.id"
              :name="`section:${section.id}`"
              :label="section.label"
            >
              <TraceDetailSection
                v-if="activeDetail === `section:${section.id}`"
                :key="`${selectedNode.key}:${contract?.revisionNId}:${section.id}`"
                :section="section"
                :table-key="`trace-section:${nId}:${contract?.revisionNId}:${section.id}`"
                :load="runtime.loadSection"
              />
            </el-tab-pane>
          </el-tabs>
          <template v-if="selectedEdge"
            ><h3>{{ t('evidence') }}</h3>
            <dl>
              <dt>{{ t('relation') }}</dt>
              <dd>
                {{ contract?.relations.find((r) => r.id === selectedEdge?.relationId)?.name }} ·
                {{ text.option(selectedEdge.kind) }}
              </dd>
              <dt>{{ t('from') }}</dt>
              <dd>{{ nodeLabel(selectedEdge.from) }}</dd>
              <dt>{{ t('to') }}</dt>
              <dd>{{ nodeLabel(selectedEdge.to) }}</dd>
              <template v-for="(value, key) in selectedEdge.evidence" :key="key"
                ><dt>{{ evidenceLabel(key) }}</dt>
                <dd>{{ value }}</dd></template
              >
            </dl>
            <p v-if="selectedEdge.kind === 'Context'">{{ t('contextHint') }}</p></template
          >
        </aside>
      </div>
      <details>
        <summary>{{ t('observations') }}</summary>
        <p v-if="!result.sourceObservations.length">{{ t('unknown') }}</p>
        <p v-for="(observation, i) in result.sourceObservations" :key="i">
          {{ observation.sourceKey || observation.datasetRevisionNId }} ·
          {{ observation.observedOn || t('unknown') }} ·
          {{ observation.quality?.status || t('unknown') }}
          {{ observation.reason || observation.quality?.reason }}
        </p>
      </details>
    </template>
    <el-empty
      v-else-if="!loading && !busy"
      class="trace-empty"
      :description="
        !nId
          ? models.length
            ? t('chooseModel')
            : t('noModels')
          : selectedKeys.length
            ? t('readyGraph')
            : t('graphHint')
      "
    />
    <TraceLinkModal
      :link="modal"
      @close="runtime.closeLink"
      @denied="
        (message: string) => {
          runtime.invalidate()
          error = message
        }
      "
    />
  </AppPage>
</template>
<style scoped>
.trace-run-page {
  min-height: 0;
}
.trace-model-bar {
  display: flex;
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;
  padding: 12px;
  margin-bottom: 8px;
  background: var(--ip-color-bg-container);
  border: 1px solid var(--ip-color-border);
}
.trace-model-bar label {
  display: flex;
  align-items: center;
  gap: 8px;
  white-space: nowrap;
}
.trace-model-bar .el-select {
  width: min(300px, 55vw);
}
.trace-model-bar .trace-direction .el-select {
  width: 120px;
}
.trace-model-bar .el-button + .el-button {
  margin-left: 0;
}
.trace-selection-summary {
  color: var(--ip-color-text-secondary);
}
.trace-query-controls {
  display: flex;
  gap: 16px;
  flex-wrap: wrap;
  align-items: center;
  padding: 8px 12px;
}
.trace-query-controls label {
  display: flex;
  align-items: center;
  gap: 8px;
}
.trace-query-controls .el-select {
  width: 340px;
}
.trace-result-workspace {
  display: grid;
  grid-template-columns: minmax(0, 1fr);
  height: max(460px, calc(100dvh - 260px));
}
.trace-result-workspace.has-selection {
  grid-template-columns: minmax(0, 1fr) minmax(320px, 38%);
}
.trace-empty {
  min-height: max(360px, calc(100dvh - 260px));
  background: var(--ip-color-bg-container);
}
.trace-details {
  overflow: auto;
  padding: 12px;
  border-left: 1px solid var(--el-border-color);
  min-width: 0;
}
.trace-layout-direction {
  margin-left: auto;
}
.trace-detail-tabs :deep(.el-tabs__item) {
  padding-inline: 12px;
}
.trace-details dd {
  margin: 3px 0 12px;
  overflow-wrap: anywhere;
}
.trace-details small {
  display: block;
}
@media (max-width: 1024px) {
  .trace-result-workspace.has-selection {
    grid-template-columns: 1fr;
    height: auto;
  }
  .trace-result-workspace > :first-child {
    min-height: 440px;
  }
  .trace-details {
    max-height: 350px;
  }
}
</style>
