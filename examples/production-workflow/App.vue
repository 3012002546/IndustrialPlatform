<script setup lang="ts">
import { computed, ref, shallowRef, watch } from 'vue'
import GraphWorkbench from '@/components/studio/graph/GraphWorkbench.vue'
import type { GraphIntent, RuntimeInstance, Target } from '@/components/studio/graph/types'
import { validateProjection } from '@/components/studio/graph/projection'
import { createHistory } from '@/components/studio/shared/model'
import {
  checkConnection,
  patchBox,
  project,
  published,
  snapshot,
  type Frame,
  type ProcessDefinition,
} from './fixture'

const mode = ref<'run' | 'design'>('run')
const frame = ref<Frame>(1)
const draft = shallowRef(structuredClone(published))
const history = createHistory(draft.value)
const selection = ref<Target[]>([{ kind: 'node', id: 'inspect' }])
const chosenInstance = ref('i-inspect-1')
const notice = ref('')
const draftKey = 'industrial-platform:standalone-workflow-sample:draft:1'
const replay = computed(() => snapshot(published, frame.value))
const draftScope = {
  contextKey: 'production-draft',
  definition: { kind: 'draft', resourceNId: published.id, documentStamp: '1' },
} as const
const view = computed(() =>
  mode.value === 'run' ? replay.value.graph : project(draft.value, draftScope),
)
const definition = computed(() => (mode.value === 'run' ? published : draft.value))
const activity = computed(() =>
  definition.value.activities.find((a) =>
    selection.value.some((t) => t.kind === 'node' && t.id === a.id),
  ),
)
const transition = computed(() =>
  definition.value.transitions.find((e) =>
    selection.value.some((t) => t.kind === 'edge' && t.id === e.id),
  ),
)
const box = computed(() => definition.value.layout.nodes.find((n) => n.id === activity.value?.id))
const records = computed(() =>
  replay.value.instances.filter((i) => i.definitionNodeId === activity.value?.id),
)
const selectedRecord = computed(
  () => records.value.find((i) => i.id === chosenInstance.value) ?? records.value[0],
)
const detail = shallowRef<RuntimeInstance>()
const editName = ref(''),
  x = ref(0),
  y = ref(0)
const activityName = (id: string) => definition.value.activities.find((a) => a.id === id)?.name
const time = (stamp: string | null) => (stamp ? stamp.slice(11, 16) : '—')
watch(
  [activity, box],
  () => {
    editName.value = activity.value?.name ?? ''
    x.value = box.value?.x ?? 0
    y.value = box.value?.y ?? 0
  },
  { immediate: true },
)
// A delayed fixture read belongs to the selected instance and snapshot.
watch(
  [mode, selectedRecord],
  ([current, record], _, onCleanup) => {
    detail.value = undefined
    if (current !== 'run' || !record) return
    const timer = setTimeout(
      () => {
        detail.value = record
      },
      record.id === 'i-inspect-1' ? 120 : 30,
    )
    onCleanup(() => clearTimeout(timer))
  },
  { immediate: true },
)

function select(next: readonly Target[]) {
  selection.value = [...next]
  notice.value = ''
}
function switchMode(next: 'run' | 'design') {
  if (next === mode.value) return
  select([{ kind: 'node', id: activity.value?.id ?? 'inspect' }])
  mode.value = next
}
function commit(next: ProcessDefinition) {
  validateProjection(project(next, draftScope))
  history.commit(next)
  draft.value = next
  notice.value = ''
}
function applyProperties() {
  if (
    !activity.value ||
    !editName.value.trim() ||
    ![x.value, y.value].every((v) => Number.isFinite(v) && Math.abs(v) <= 1e6)
  ) {
    notice.value = '请填写活动名称和有效坐标。'
    return
  }
  const next = structuredClone(draft.value)
  const item = next.activities.find((a) => a.id === activity.value!.id)!
  item.name = editName.value.trim()
  patchBox(next, item.id, { x: x.value, y: y.value })
  commit(next)
}
function edit(intent: GraphIntent) {
  if (mode.value !== 'design') return
  const next = structuredClone(draft.value)
  if (intent.type === 'layout') {
    for (const node of intent.nodes) patchBox(next, node.id, node)
    next.layout.edges = next.layout.edges.map(
      (e) => intent.edges.find((item) => item.id === e.id) ?? e,
    )
  } else if (intent.type === 'connect') {
    const c = intent.connection
    const failures = checkConnection(next, c)
    if (failures.length) {
      notice.value = failures[0]!.message
      return
    }
    const id = c.edgeId ?? crypto.randomUUID()
    next.transitions = next.transitions.filter((e) => e.id !== id)
    next.transitions.push({ id, from: c.source!.nodeId, to: c.target!.nodeId })
    if (!next.layout.edges.some((e) => e.id === id))
      next.layout.edges = [...next.layout.edges, { id, vertices: [] }]
  } else if (intent.type === 'remove') {
    if (intent.targets.some((t) => t.kind !== 'edge')) {
      notice.value = '样例保留七个活动。可选择控制线后删除。'
      return
    }
    const ids = new Set(intent.targets.map((t) => t.id))
    next.transitions = next.transitions.filter((e) => !ids.has(e.id))
    next.layout.edges = next.layout.edges.filter((e) => !ids.has(e.id))
    selection.value = []
  } else return
  commit(next)
}
function restoreHistory(action: 'undo' | 'redo') {
  if (mode.value !== 'design') return
  draft.value = history[action]()
  notice.value = ''
}
function save() {
  try {
    localStorage.setItem(draftKey, JSON.stringify(draft.value))
    notice.value = '草稿已保存到此浏览器。'
  } catch {
    notice.value = '当前浏览器无法保存本地草稿。'
  }
}
function reopen() {
  try {
    const saved = localStorage.getItem(draftKey)
    if (!saved) {
      notice.value = '请先保存一份定义草稿。'
      return
    }
    const next = JSON.parse(saved) as ProcessDefinition
    if (
      next.id !== published.id ||
      next.revisionNId !== published.revisionNId ||
      next.activities.length !== published.activities.length ||
      !published.activities.every((a) =>
        next.activities.some(
          (b) => a.id === b.id && a.kind === b.kind && typeof b.name === 'string' && b.name.trim(),
        ),
      )
    )
      throw Error()
    validateProjection(project(next, draftScope))
    history.reset(next)
    draft.value = next
    notice.value = '已重开保存的草稿。'
  } catch {
    notice.value = '无法读取该草稿，请重新保存。'
  }
}
</script>
<template>
  <div class="sample">
    <header>
      <div>
        <p class="eyebrow">生产工作流 · 独立测试样例</p>
        <h1>批次放行流程</h1>
      </div>
      <nav aria-label="样例视图">
        <button :aria-pressed="mode === 'run'" @click="switchMode('run')">运行回放</button
        ><button :aria-pressed="mode === 'design'" @click="switchMode('design')">定义草稿</button>
      </nav>
    </header>
    <main>
      <section class="canvas-card">
        <div class="panel-head">
          <div>
            <h2>{{ mode === 'run' ? '流程实例' : '编辑流程定义' }}</h2>
            <p>
              {{
                mode === 'run'
                  ? 'process-001　/　固定修订 r1　/　模拟数据'
                  : '本地草稿　/　基于修订 r1'
              }}
            </p>
          </div>
          <div v-if="mode === 'run'" class="group" aria-label="模拟快照">
            <button :aria-pressed="frame === 1" @click="frame = 1">00:06 检验中</button
            ><button :aria-pressed="frame === 2" @click="frame = 2">00:08 已放行</button>
          </div>
          <div v-else class="group">
            <button :disabled="!history.canUndo" @click="restoreHistory('undo')">撤销</button
            ><button :disabled="!history.canRedo" @click="restoreHistory('redo')">重做</button
            ><button @click="save">保存草稿</button><button @click="reopen">重开草稿</button>
          </div>
        </div>
        <div class="diagram">
          <GraphWorkbench
            :view="view"
            :mode="mode"
            :selection="selection"
            :read-only="mode === 'run'"
            :show-interaction-hint="false"
            :check-connection="(c) => checkConnection(draft, c)"
            @selection-change="select"
            @intent="edit"
            @undo="restoreHistory('undo')"
            @redo="restoreHistory('redo')"
            @error="(m) => (notice = m)"
          />
        </div>
        <footer>
          <span>{{ view.nodes.length }} 个活动 · {{ view.edges.length }} 条控制线</span
          ><span>{{
            mode === 'run'
              ? '快照 ' + frame + ' / 2　·　UTC'
              : '拖动活动调整布局，连接端口设置控制线'
          }}</span>
        </footer>
      </section>
      <aside>
        <div class="aside-title">
          <p class="eyebrow">{{ mode === 'run' ? '运行明细' : '草稿属性' }}</p>
          <h2>{{ activity?.name ?? (transition ? '控制线' : '选择活动') }}</h2>
        </div>
        <template v-if="transition"
          ><p>
            {{ activityName(transition.from) }} →
            {{ activityName(transition.to) }}
          </p>
          <small>控制线 {{ transition.id }}</small></template
        >
        <template v-else-if="activity && mode === 'run'">
          <p class="dim">活动实例 · {{ records.length }} 条</p>
          <div class="records">
            <button
              v-for="r in records"
              :key="r.id"
              :aria-pressed="selectedRecord?.id === r.id"
              @click="chosenInstance = r.id"
            >
              <span>第 {{ r.iteration }} 次 · {{ r.stateLabel }}</span
              ><code>{{ r.id }}</code>
            </button>
          </div>
          <dl v-if="detail" data-testid="instance-detail">
            <dt>实例编号</dt>
            <dd>{{ detail.id }}</dd>
            <dt>状态</dt>
            <dd>{{ detail.stateLabel }}</dd>
            <dt>业务迭代</dt>
            <dd>{{ detail.iteration }}</dd>
            <dt>执行尝试</dt>
            <dd>{{ detail.attempt }}</dd>
            <dt>开始 / 结束</dt>
            <dd>{{ time(detail.startedOn) }} / {{ time(detail.endedOn) }}</dd>
          </dl>
          <p v-else role="status">正在读取模拟明细…</p>
        </template>
        <form v-else-if="activity" @submit.prevent="applyProperties">
          <label>活动名称<input v-model="editName" required maxlength="48" /></label>
          <div class="coordinates">
            <label>横坐标<input v-model.number="x" type="number" step="any" required /></label
            ><label>纵坐标<input v-model.number="y" type="number" step="any" required /></label>
          </div>
          <button class="primary">应用修改</button>
        </form>
        <p v-else class="dim">点击图中的活动或控制线查看详情。</p>
        <p v-if="notice" role="status" class="notice">{{ notice }}</p>
      </aside>
    </main>
  </div>
</template>
<style scoped>
:global(body) {
  margin: 0;
  background: #f1f4f5;
  color: #1c3037;
  font:
    15px/1.55 'Segoe UI',
    'Microsoft YaHei',
    sans-serif;
}
* {
  box-sizing: border-box;
}
.sample {
  max-width: 1600px;
  margin: auto;
  padding: 32px clamp(16px, 3.6vw, 60px);
}
header,
.panel-head,
footer,
nav,
.group,
.coordinates {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}
header,
.panel-head,
footer {
  justify-content: space-between;
}
header {
  margin-bottom: 24px;
}
h1,
h2,
p {
  margin: 0;
}
h1 {
  font-size: clamp(26px, 3vw, 38px);
  letter-spacing: 0.02em;
}
h2 {
  font-size: 20px;
}
.eyebrow {
  color: #23695e;
  font-weight: 650;
  font-size: 13px;
  margin-bottom: 6px;
}
main {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(230px, 280px);
  gap: 16px;
  align-items: start;
}
.canvas-card,
aside {
  background: white;
  border: 1px solid #cdd8da;
  border-radius: 10px;
  overflow: hidden;
}
.panel-head {
  padding: 16px 20px;
  min-height: 82px;
  border-bottom: 1px solid #dce4e5;
}
.panel-head p,
.dim,
small,
footer {
  color: #51676e;
  font-size: 13px;
}
.diagram {
  height: clamp(420px, 60vh, 650px);
}
.diagram :deep(.graph-workbench) {
  min-height: 0;
}
.diagram :deep(.graph-surface) {
  min-height: 0;
}
button,
input {
  font: inherit;
}
button {
  cursor: pointer;
  background: #fff;
  border: 1px solid #c7d4d7;
  border-radius: 6px;
  padding: 6px 12px;
  color: #28454c;
  font-size: 14px;
}
button:hover:enabled {
  border-color: #28766b;
}
button:focus-visible,
input:focus-visible {
  outline: 2px solid #15786b;
  outline-offset: 2px;
}
button[aria-pressed='true'],
button.primary {
  background: #155f55;
  color: #fff;
  border-color: #155f55;
}
button:disabled {
  color: #829194;
  cursor: default;
}
footer {
  padding: 10px 18px;
  border-top: 1px solid #e2e8e9;
}
aside {
  padding: 20px;
}
.aside-title {
  border-bottom: 1px solid #dce5e6;
  padding-bottom: 12px;
  margin-bottom: 14px;
}
.records {
  display: grid;
  gap: 8px;
  margin: 10px 0 18px;
}
.records button {
  display: flex;
  justify-content: space-between;
  align-items: center;
  text-align: left;
}
.records code {
  font-size: 12px;
}
dl {
  display: grid;
  grid-template-columns: auto 1fr;
  gap: 8px 12px;
  margin: 0;
  font-size: 14px;
}
dt {
  color: #60767c;
}
dd {
  margin: 0;
  text-align: right;
  font-variant-numeric: tabular-nums;
}
form label {
  display: grid;
  gap: 5px;
  font-size: 13px;
  color: #435e65;
}
input {
  width: 100%;
  padding: 6px 8px;
  border-radius: 5px;
  border: 1px solid #c5d1d5;
}
.coordinates {
  margin: 12px 0;
}
.coordinates label {
  flex: 1;
  min-width: 64px;
}
.notice {
  margin-top: 12px;
  font-size: 13px;
  color: #226156;
}
@media (max-width: 850px) {
  main {
    grid-template-columns: 1fr;
  }
  .diagram {
    height: 380px;
  }
  .sample {
    padding-top: 16px;
  }
  header {
    margin-bottom: 14px;
  }
}
</style>
