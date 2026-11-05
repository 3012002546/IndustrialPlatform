<script setup lang="ts">
import { computed, onBeforeUnmount, onDeactivated, ref, watch } from 'vue'
import { onBeforeRouteLeave, onBeforeRouteUpdate, useRoute, useRouter } from 'vue-router'
import { ElMessageBox } from 'element-plus'
import {
  Box,
  Connection,
  Setting,
  TakeawayBox,
  Postcard,
  Tickets,
  Operation,
  CollectionTag,
} from '@element-plus/icons-vue'
import { ApiError } from '@/api/errors'
import { getStudioApi, type StudioDraft, type StudioResource } from '@/api/studio'
import { PermissionGate, usePermission } from '@/permissions'
import { useAuthStore } from '@/stores/authStore'
import { useLocalizationStore } from '@/stores/localizationStore'
import { returnFromWorkspace } from '@/workspace/returnFromWorkspace'
import AppPage from '@/components/base/AppPage.vue'
import AppFormDrawer from '@/components/management/AppFormDrawer.vue'
import StudioEditorShell from '@/components/studio/shared/StudioEditorShell.vue'
import GraphWorkbench from '@/components/studio/graph/GraphWorkbench.vue'
import GraphInteractionHint from '@/components/studio/graph/GraphInteractionHint.vue'
import GraphViewportControls from '@/components/studio/graph/GraphViewportControls.vue'
import type { Diagnostic, GraphIntent, Target } from '@/components/studio/graph/types'
import { clone, createHistory } from '@/components/studio/shared/model'
import { initialTraceDocument, readTraceDocument } from '@/components/studio/trace/model'
import { traceAdapter, type TracePropertyCommand } from '@/components/studio/trace/adapter'
import TraceGraphTools from '@/components/studio/trace/TraceGraphTools.vue'
import { downloadTraceDesign, TraceDesignFileError } from '@/components/studio/trace/designFile'
import TraceDesignImport from '@/components/studio/trace/TraceDesignImport.vue'
import type { TraceDocument } from '@/components/studio/trace/model'
import TraceInspector from '@/components/studio/trace/TraceInspector.vue'
import TracePreviewPanel from '@/components/studio/trace/TracePreviewPanel.vue'
import { useTraceCopy } from '@/components/studio/trace/copy'
import { useTracePresentation } from '@/components/studio/trace/presentation'
import '@/components/studio/shared/studio.css'
const route = useRoute(),
  router = useRouter(),
  auth = useAuthStore(),
  localization = useLocalizationStore(),
  permission = usePermission(),
  api = getStudioApi(),
  t = useTraceCopy(),
  text = useTracePresentation()
const doc = ref(initialTraceDocument()),
  resource = ref<StudioResource>(),
  datasets = ref<StudioResource[]>([]),
  saved = ref(''),
  busy = ref(false),
  loading = ref(true),
  error = ref(''),
  optionsError = ref(''),
  status = ref(''),
  blocked = ref(false),
  conflict = ref(false),
  serverDraft = ref<StudioDraft>(),
  preview = ref(false),
  importing = ref(false),
  comparison = ref(false),
  releaseNote = ref('')
const selection = ref<Target[]>([]),
  tool = ref<'select' | 'pan' | 'connect'>('select'),
  structureGroup = ref('objects'),
  editorShell = ref<InstanceType<typeof StudioEditorShell>>(),
  stamp = ref('0'),
  contextKey = ref(crypto.randomUUID()),
  serverDiagnostics = ref<Diagnostic[]>([]),
  graphRef = ref<InstanceType<typeof GraphWorkbench>>(),
  canvasZoom = ref(1),
  inspector = ref<InstanceType<typeof TraceInspector>>()
const history = createHistory(doc.value),
  historyTick = ref(0)
let generation = 0
const nId = computed(() => String(route.params.nId)),
  scope = computed(() => ({
    contextKey: contextKey.value,
    definition: { kind: 'draft' as const, resourceNId: nId.value, documentStamp: stamp.value },
  })),
  projected = computed(() => traceAdapter.project(doc.value, scope.value)),
  graph = computed(() => (projected.value.ok ? projected.value.value : null)),
  diagnostics = computed(() =>
    [...projected.value.diagnostics, ...serverDiagnostics.value].map((d) => ({
      ...d,
      ...text.diagnostic(d, doc.value),
    })),
  ),
  dirty = computed(() => !!resource.value && JSON.stringify(doc.value) !== saved.value),
  readOnly = computed(() => blocked.value || !permission.has('studio.trace.design')),
  canUndo = computed(() => {
    void historyTick.value
    return history.canUndo
  }),
  canRedo = computed(() => {
    void historyTick.value
    return history.canRedo
  })
const target = computed(() => selection.value[0] ?? null),
  selectedRelation = computed(() => (target.value?.kind === 'edge' ? target.value.id : undefined))
function advance() {
  stamp.value = (BigInt(stamp.value) + 1n).toString()
  historyTick.value++
  serverDiagnostics.value = []
}
function normalizeDiagnostics(values: unknown[]): Diagnostic[] {
  return values.map((value, i) => {
    const item = value as { code?: string; path?: string; message?: string; severity?: string }
    const match = /^(objects|relations)\[(\d+)\]/.exec(item.path ?? '')
    const entity = match
      ? (match[1] === 'objects' ? doc.value.objects : doc.value.relations)[Number(match[2])]
      : undefined
    return {
      id: `server-${i}`,
      code: item.code ?? 'STUDIO_VALIDATION',
      message: item.message ?? String(value),
      severity: item.severity?.toLowerCase() === 'warning' ? 'warning' : 'error',
      ...(item.path ? { path: item.path } : {}),
      ...(entity
        ? {
            target: {
              kind: match?.[1] === 'objects' ? ('node' as const) : ('edge' as const),
              id: entity.id,
            },
          }
        : {}),
    }
  })
}
function apply(command: GraphIntent | TracePropertyCommand) {
  if (readOnly.value) return
  const prepared = traceAdapter.edit(doc.value, command, {
    scope: scope.value,
    expectedDocumentStamp: stamp.value,
  })
  if (!prepared.ok) {
    error.value = prepared.code
    return
  }
  if (JSON.stringify(prepared.value.document) === JSON.stringify(doc.value)) return
  doc.value = prepared.value.document
  history.commit(doc.value)
  advance()
  if (prepared.value.selection.length || command.type === 'remove')
    selection.value = [...prepared.value.selection]
}
function undo(redo = false) {
  if (readOnly.value) return
  doc.value = redo ? history.redo() : history.undo()
  advance()
  selection.value = selection.value.filter((s) =>
    s.kind === 'node'
      ? doc.value.objects.some((o) => o.id === s.id)
      : s.kind === 'edge'
        ? doc.value.relations.some((r) => r.id === s.id)
        : false,
  )
}
async function loadDatasets(owner: number) {
  try {
    const value = await api.list('Dataset', { pageSize: 200 })
    if (owner === generation) {
      datasets.value = value.items
      optionsError.value = ''
    }
  } catch (e) {
    if (owner === generation) optionsError.value = e instanceof Error ? e.message : String(e)
  }
}
async function load() {
  const owner = ++generation
  loading.value = true
  error.value = ''
  status.value = ''
  preview.value = false
  importing.value = false
  contextKey.value = crypto.randomUUID()
  void loadDatasets(owner)
  try {
    const value = await api.draft(nId.value)
    if (owner !== generation) return
    if (value.resource.kind !== 'TraceModel') throw new Error('TRACE_DOCUMENT_INVALID')
    doc.value = readTraceDocument(value.draftDocument)
    resource.value = value.resource
    saved.value = JSON.stringify(doc.value)
    history.reset(doc.value)
    selection.value = []
    advance()
    blocked.value = false
    conflict.value = false
    serverDraft.value = undefined
    serverDiagnostics.value = normalizeDiagnostics(value.diagnostics)
  } catch (e) {
    if (owner === generation) {
      error.value = e instanceof Error ? e.message : String(e)
      blocked.value = true
    }
  } finally {
    if (owner === generation) loading.value = false
  }
}
function failed(e: unknown) {
  error.value = e instanceof Error ? e.message : String(e)
  if (e instanceof ApiError) {
    if (e.details.status === 409) conflict.value = true
    if (['unauthorized', 'forbidden', 'notFound'].includes(e.kind)) {
      blocked.value = true
      preview.value = false
    }
  }
}
async function saveDraft(): Promise<boolean> {
  if (!resource.value || readOnly.value) return false
  const owner = generation,
    captured = clone(doc.value),
    documentStamp = stamp.value
  try {
    const value = await api.save(resource.value, captured)
    if (owner !== generation) return false
    resource.value = value.resource
    saved.value = JSON.stringify(value.draftDocument)
    if (
      documentStamp === stamp.value &&
      JSON.stringify(value.draftDocument) !== JSON.stringify(doc.value)
    ) {
      doc.value = readTraceDocument(value.draftDocument)
      history.commit(doc.value)
      advance()
    }
    conflict.value = false
    serverDiagnostics.value = normalizeDiagnostics(value.diagnostics)
    status.value = t('saved')
    return true
  } catch (e) {
    if (owner === generation) {
      failed(e)
      if (e instanceof ApiError && ['network', 'timeout'].includes(e.kind)) {
        // A transport failure may follow a successful PUT. Reconcile through an authorized
        // read; do not repeat the write or replace newer local edits.
        conflict.value = true
        try {
          const fresh = await api.draft(nId.value)
          if (owner !== generation) return false
          if (JSON.stringify(fresh.draftDocument) === JSON.stringify(captured)) {
            resource.value = fresh.resource
            saved.value = JSON.stringify(fresh.draftDocument)
            conflict.value = false
            error.value = ''
            status.value = t('saved')
            return true
          }
          serverDraft.value = fresh
          comparison.value = true
        } catch {
          /* Keep local edits; the comparison action remains available for retry. */
        }
      }
    }
    return false
  }
}
async function action(kind: 'save' | 'validate' | 'publish') {
  if (busy.value || readOnly.value || !resource.value) return
  inspector.value?.apply()
  busy.value = true
  error.value = ''
  status.value = ''
  const owner = generation
  try {
    if (kind === 'save') {
      await saveDraft()
      return
    }
    if (kind === 'publish' && dirty.value && !(await saveDraft())) return
    const documentStamp = stamp.value,
      validation = await api.validate(nId.value, clone(doc.value))
    if (owner !== generation || documentStamp !== stamp.value) return
    serverDiagnostics.value = normalizeDiagnostics(validation.diagnostics)
    if (
      kind !== 'publish' ||
      !validation.valid ||
      projected.value.diagnostics.some((d) => d.severity === 'error')
    )
      return
    await api.publish(
      resource.value,
      releaseNote.value,
      validation.dependencyPlanHash ?? validation.dependencyPlan?.hash,
    )
    if (owner !== generation) return
    status.value = t('published')
    const fresh = await api.draft(nId.value)
    if (owner === generation) resource.value = fresh.resource
  } catch (e) {
    if (owner === generation) failed(e)
  } finally {
    if (owner === generation) busy.value = false
  }
}
async function compare() {
  if (busy.value) return
  busy.value = true
  const owner = generation
  try {
    const value = await api.draft(nId.value)
    if (owner === generation) {
      serverDraft.value = value
      comparison.value = true
    }
  } catch (e) {
    if (owner === generation) failed(e)
  } finally {
    if (owner === generation) busy.value = false
  }
}
function acceptServer() {
  if (!serverDraft.value) return
  doc.value = readTraceDocument(serverDraft.value.draftDocument)
  resource.value = serverDraft.value.resource
  saved.value = JSON.stringify(doc.value)
  history.reset(doc.value)
  advance()
  conflict.value = false
  comparison.value = false
}
function showPreview() {
  inspector.value?.apply()
  preview.value = true
}
function select(targets: Target[]) {
  inspector.value?.apply()
  selection.value = targets
}
function configureStart() {
  select([])
  editorShell.value?.showProperties()
}
function focus(d: Diagnostic) {
  if (d.target) {
    select([d.target])
    graphRef.value?.focus(d.target)
  }
}
const grainIcons = {
  Batch: Box,
  Package: TakeawayBox,
  Serial: Postcard,
  WorkOrder: Tickets,
  Operation,
  Custom: CollectionTag,
}
function removeTargets(targets: Target[]) {
  inspector.value?.apply()
  apply({ type: 'remove', targets })
}
function importDesign(value: TraceDocument) {
  if (readOnly.value || busy.value) return
  inspector.value?.apply()
  doc.value = clone(value)
  history.commit(doc.value)
  selection.value = []
  advance()
  importing.value = false
  error.value = ''
  status.value = t('imported')
}
function exportDesign() {
  inspector.value?.apply()
  try {
    downloadTraceDesign(resource.value?.name || t('title'), doc.value)
  } catch (e) {
    error.value = e instanceof TraceDesignFileError ? t(e.reason) : String(e)
  }
}
async function mayLeave() {
  inspector.value?.apply()
  if (!dirty.value) return true
  try {
    await ElMessageBox.confirm(t('leave'), t('dirty'), { type: 'warning' })
    return true
  } catch {
    return false
  }
}
onBeforeRouteLeave(mayLeave)
onBeforeRouteUpdate(mayLeave)
watch(
  nId,
  () => {
    void load()
  },
  { immediate: true },
)
watch(
  () => JSON.stringify(auth.user),
  () => {
    generation++
    busy.value = false
    blocked.value = true
    preview.value = false
    error.value = t('permissions')
  },
)
onDeactivated(() => {
  preview.value = false
  importing.value = false
})
onBeforeUnmount(() => {
  generation++
})
</script>
<template>
  <AppPage :title="resource?.name || t('title')" class="studio-page trace-designer">
    <template #actions>
      <el-button @click="returnFromWorkspace(router, '/pc/studio/trace-models')">{{
        t('back')
      }}</el-button>
      <el-button :disabled="readOnly || busy || !canUndo" @click="undo()">{{
        t('undo')
      }}</el-button>
      <el-button :disabled="readOnly || busy || !canRedo" @click="undo(true)">{{
        t('redo')
      }}</el-button>
      <PermissionGate permission-n-id="studio.trace.design">
        <el-button :disabled="readOnly || busy || loading" @click="showPreview">{{
          t('preview')
        }}</el-button>
        <el-button :disabled="readOnly || busy || loading" @click="importing = true">{{
          t('importDesign')
        }}</el-button>
        <el-button :disabled="busy || loading || !resource" @click="exportDesign">{{
          t('exportDesign')
        }}</el-button>
        <el-button :disabled="readOnly || busy" @click="action('validate')">{{
          t('validate')
        }}</el-button>
        <el-button :disabled="readOnly || busy" :loading="busy" @click="action('save')">{{
          t('save')
        }}</el-button>
      </PermissionGate>
      <PermissionGate permission-n-id="studio.trace.publish">
        <el-button type="primary" :disabled="readOnly || busy" @click="action('publish')">{{
          t('publish')
        }}</el-button>
      </PermissionGate>
    </template>
    <el-alert v-if="error" :title="text.message(error)" type="error" :closable="false">
      <el-button v-if="blocked && !resource" :disabled="loading" @click="load">{{
        t('retry')
      }}</el-button> </el-alert
    ><el-alert v-if="status" :title="status" type="success" :closable="false" /><el-alert
      v-if="conflict"
      :title="t('conflict')"
      type="warning"
      :closable="false"
      ><el-button @click="compare">{{ t('compare') }}</el-button></el-alert
    >
    <p v-if="loading">{{ t('loading') }}</p>
    <StudioEditorShell
      v-else
      ref="editorShell"
      graph-mode
      free-canvas
      :model-value="doc"
      :properties-open="true"
      :sidebar-widths="[280, 360]"
      :left-title="t('structure')"
    >
      <template #toolbar="{ toggleLeft, toggleRight, leftOpen, rightOpen }">
        <div class="trace-toolbar">
          <GraphViewportControls
            size="default"
            :zoom="canvasZoom"
            :locale="localization.locale"
            :disabled="!graph"
            @zoom="graphRef?.setZoom($event)"
            @fit="graphRef?.fit()"
          />
          <div class="trace-toolbar-group">
            <el-button :icon="Setting" :aria-pressed="!target" @click="configureStart">{{
              t('start')
            }}</el-button>
            <el-button :aria-expanded="leftOpen" @click="toggleLeft">{{
              t(leftOpen ? 'collapseStructure' : 'expandStructure')
            }}</el-button>
            <el-button :aria-expanded="rightOpen" @click="toggleRight">{{
              t(rightOpen ? 'collapseProperties' : 'expandProperties')
            }}</el-button>
          </div>
          <el-tag v-if="dirty" type="warning" effect="plain">{{ t('dirty') }}</el-tag>
          <GraphInteractionHint
            class="trace-canvas-hint"
            :tool="tool"
            :locale="localization.locale"
          />
          <el-input
            v-model="releaseNote"
            :placeholder="t('releaseNote')"
            :aria-label="t('releaseNote')"
            class="trace-release-note"
          />
        </div>
      </template>
      <template #structure
        ><div class="trace-structure">
          <div class="trace-panel-heading">
            <h3>{{ t('structure') }}</h3>
          </div>
          <TraceGraphTools v-model:tool="tool" :read-only="readOnly" />
          <el-collapse v-model="structureGroup" accordion class="trace-structure-groups">
            <el-collapse-item
              name="objects"
              :title="`${t('configuredObjects')} (${doc.objects.length})`"
            >
              <div class="trace-node-list trace-object-list">
                <div v-for="(o, i) in doc.objects" :key="o.id" class="trace-list-row">
                  <button
                    type="button"
                    class="studio-node-item"
                    :class="{ active: target?.kind === 'node' && target.id === o.id }"
                    :aria-pressed="target?.kind === 'node' && target.id === o.id"
                    @click="select([{ kind: 'node', id: o.id }])"
                  >
                    <el-icon><component :is="grainIcons[o.identity.grain] || Box" /></el-icon>
                    <span class="trace-item-caption"
                      ><span>{{ text.itemName(o, i) }}</span
                      ><small>{{ text.option(o.identity.grain) }}</small></span
                    >
                  </button>
                </div>
              </div>
            </el-collapse-item>
            <el-collapse-item
              name="relations"
              :title="`${t('relation')} (${doc.relations.length})`"
            >
              <p v-if="!doc.relations.length" class="trace-panel-hint">{{ t('relationHint') }}</p>
              <div class="trace-node-list">
                <div v-for="(r, i) in doc.relations" :key="r.id" class="trace-list-row">
                  <button
                    type="button"
                    class="studio-node-item"
                    :class="{ active: target?.kind === 'edge' && target.id === r.id }"
                    :aria-pressed="target?.kind === 'edge' && target.id === r.id"
                    @click="select([{ kind: 'edge', id: r.id }])"
                  >
                    <el-icon><Connection /></el-icon
                    ><span>{{ text.itemName(r, i, 'relation') }}</span>
                  </button>
                </div>
              </div>
            </el-collapse-item>
            <el-collapse-item
              name="diagnostics"
              class="trace-diagnostics"
              :title="`${t('diagnostics')} (${diagnostics.length})`"
            >
              <button
                v-for="d in diagnostics"
                :key="d.id"
                class="trace-diagnostic"
                :title="d.path"
                @click="focus(d)"
              >
                <span v-if="d.location" class="trace-diagnostic-path">{{ d.location }}</span
                ><span>{{ d.message }}</span>
              </button>
              <p v-if="!diagnostics.length" class="trace-panel-hint">{{ t('noDiagnostics') }}</p>
            </el-collapse-item>
          </el-collapse>
          <el-alert v-if="optionsError" :title="optionsError" type="warning" :closable="false"
            ><el-button @click="loadDatasets(generation)">{{ t('retry') }}</el-button></el-alert
          >
        </div></template
      >
      <template #workspace>
        <GraphWorkbench
          v-if="graph"
          ref="graphRef"
          v-model:tool="tool"
          :show-interaction-tools="false"
          :show-interaction-hint="false"
          :show-viewport-tools="false"
          :view="graph"
          mode="design"
          :read-only="readOnly"
          :selection="selection"
          :check-connection="
            (intent) =>
              traceAdapter
                .checkConnection(doc, intent)
                .map((d) => ({ ...d, message: text.message(d.message) }))
          "
          @intent="apply"
          @selection-change="select"
          @viewport-change="canvasZoom = $event.zoom"
          @undo="undo()"
          @redo="undo(true)"
          @error="error = $event"
        />
        <el-alert
          v-else
          :title="text.message('GRAPH_STRUCTURE_INVALID')"
          type="error"
          :closable="false"
        />
      </template>
      <template #properties
        ><TraceInspector
          ref="inspector"
          :document="doc"
          :target="target"
          :datasets="datasets"
          :read-only="readOnly"
          @apply="apply"
        >
          <template #actions>
            <el-tooltip v-if="selection.length" :content="t('removeHint')" placement="bottom">
              <el-button
                link
                type="danger"
                :disabled="readOnly"
                :aria-label="t('removeSelected')"
                @click="removeTargets(selection)"
                >{{ t('remove') }}</el-button
              >
            </el-tooltip>
          </template>
        </TraceInspector>
      </template>
    </StudioEditorShell>
    <TraceDesignImport
      v-if="importing && !readOnly"
      @close="importing = false"
      @apply="importDesign"
    />
    <AppFormDrawer v-model="preview" :title="t('preview')" size="fullscreen" hide-footer
      ><TracePreviewPanel
        v-if="preview"
        :document="doc"
        :resource-n-id="nId"
        :relation-id="selectedRelation"
    /></AppFormDrawer>
    <AppFormDrawer v-model="comparison" :title="t('compare')" size="extra-wide" hide-footer
      ><template v-if="serverDraft"
        ><el-button type="primary" @click="acceptServer">{{ t('acceptServer') }}</el-button>
        <div class="trace-compare">
          <section>
            <h3>{{ t('local') }}</h3>
            <pre>{{ JSON.stringify(doc, null, 2) }}</pre>
          </section>
          <section>
            <h3>{{ t('server') }}</h3>
            <pre>{{ JSON.stringify(serverDraft.draftDocument, null, 2) }}</pre>
          </section>
        </div></template
      ></AppFormDrawer
    >
  </AppPage>
</template>
<style scoped>
.trace-designer {
  min-height: 0;
}
.trace-designer :deep(.studio-editor) {
  gap: 10px;
  height: clamp(560px, calc(100dvh - 260px), 1050px);
  min-height: 0;
  border: 0;
  background: transparent;
}
.trace-designer :deep(.studio-sidebar),
.trace-designer :deep(.studio-viewport) {
  border: 1px solid var(--el-border-color);
}
.trace-designer :deep(.studio-sidebar:last-child) {
  overflow: hidden;
}
.trace-designer :deep(.studio-sidebar) {
  scrollbar-width: thin;
  scrollbar-gutter: stable;
}
.trace-toolbar,
.trace-toolbar-group {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px;
}
.trace-toolbar {
  width: 100%;
  margin-bottom: 10px;
  padding: 10px 12px;
  border: 1px solid var(--el-border-color);
  background: var(--el-bg-color);
  box-sizing: border-box;
}
.trace-toolbar :deep(.el-button + .el-button) {
  margin-left: 0;
}
.trace-release-note {
  width: 220px;
  margin-left: auto;
}
.trace-canvas-hint {
  flex: 1 1 260px;
  min-width: 0;
}
.trace-structure {
  display: flex;
  flex-direction: column;
  gap: 12px;
  font-size: 13px;
}
.trace-panel-heading {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
}
.trace-panel-heading h3 {
  margin: 0;
  font-size: 16px;
}
.trace-panel-hint {
  color: var(--el-text-color-secondary);
  font-size: 12px;
}
.trace-panel-hint {
  margin: 8px 0;
  line-height: 1.7;
}
.trace-structure-groups :deep(.el-collapse-item__content) {
  max-height: 320px;
  overflow-y: auto;
  scrollbar-width: thin;
  padding-bottom: 8px;
}
.trace-structure-groups :deep(.el-collapse-item__header) {
  font-weight: 600;
}
.trace-list-row {
  display: flex;
  align-items: center;
  gap: 4px;
}
.trace-list-row > .studio-node-item {
  flex: 1;
  min-width: 0;
}
.trace-item-caption {
  display: grid;
  gap: 3px;
}
.trace-item-caption small {
  color: var(--el-text-color-secondary);
  font-size: 11px;
}
.trace-node-list {
  display: grid;
  gap: 4px;
}
.trace-object-list {
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 8px;
}
.trace-object-list .trace-list-row {
  aspect-ratio: 1;
}
.trace-object-list .studio-node-item {
  flex-direction: column;
  justify-content: center;
  width: 100%;
  height: 100%;
  min-height: 96px;
  text-align: center;
  border: 1px solid var(--el-border-color);
  background: var(--el-bg-color);
  white-space: normal;
}
.trace-structure .studio-node-item {
  padding: 10px;
  gap: 8px;
  border-radius: var(--el-border-radius-base);
  text-align: start;
  cursor: pointer;
  overflow-wrap: anywhere;
}
.trace-structure .studio-node-item:hover {
  background: var(--el-fill-color-light);
}
.trace-structure .studio-node-item.active {
  background: var(--el-color-primary-light-9);
}
.trace-structure .studio-node-item:focus-visible,
.trace-diagnostic:focus-visible {
  outline: 2px solid var(--el-color-primary);
  outline-offset: -2px;
}
.trace-diagnostic {
  display: grid;
  gap: 4px;
  width: 100%;
  text-align: start;
  background: var(--el-fill-color-lighter);
  color: var(--el-text-color-regular);
  border: 1px solid var(--el-border-color-lighter);
  border-radius: var(--el-border-radius-base);
  padding: 8px 10px;
  margin-bottom: 6px;
  cursor: pointer;
  font: inherit;
  font-size: 12px;
  line-height: 1.6;
  overflow-wrap: anywhere;
}
.trace-diagnostic-path {
  color: var(--el-text-color-secondary);
}
.trace-compare {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 16px;
}
.trace-compare pre {
  white-space: pre-wrap;
  overflow-wrap: anywhere;
}
@media (max-width: 1024px) {
  .trace-compare {
    grid-template-columns: 1fr;
  }
}
</style>
