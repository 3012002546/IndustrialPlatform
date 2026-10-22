<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { onBeforeRouteLeave, useRoute, useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { ApiError } from '@/api/errors'
import {
  getStudioApi,
  type Json,
  type StudioResource,
  type StudioSource,
  type StudioRegistry,
  type StudioRuntime,
} from '@/api/studio'
import AppPage from '@/components/base/AppPage.vue'
import AppFormDrawer from '@/components/management/AppFormDrawer.vue'
import StudioSampleView from '@/components/studio/shared/StudioSampleView.vue'
import { PermissionGate } from '@/permissions'
import StudioEditorShell from '@/components/studio/shared/StudioEditorShell.vue'
import StudioSchemaEditor from '@/components/studio/shared/StudioSchemaEditor.vue'
import BindingEditor from '@/components/studio/shared/BindingEditor.vue'
import StudioRuntimeView from '@/components/studio/shared/StudioRuntimeView.vue'
import { useStudioCopy } from '@/components/studio/shared/copy'
import {
  clone,
  newNode,
  nodes,
  type LayoutNode,
  type Binding,
  syncGridPlacement,
} from '@/components/studio/shared/model'
import '@/components/studio/shared/studio.css'

const newId = () => crypto.randomUUID()
const route = useRoute(),
  router = useRouter(),
  t = useStudioCopy(),
  nId = String(route.params.nId)
const resource = ref<StudioResource | null>(null),
  document = ref<Json>({}),
  saved = ref(''),
  error = ref(''),
  diagnostics = ref<unknown[]>([]),
  busy = ref(false),
  loading = ref(true),
  conflict = ref(false)
const registry = ref<StudioRegistry>({ components: [], actions: [], codeComponents: [] }),
  sources = ref<StudioSource[]>([]),
  models = ref<StudioResource[]>([]),
  datasets = ref<StudioResource[]>([])
const editor = ref<InstanceType<typeof StudioEditorShell> | null>(null),
  tab = ref('properties'),
  selected = ref(''),
  drawer = ref(''),
  json = ref(''),
  remote = ref<Json | null>(null),
  note = ref('')
const preview = ref<StudioRuntime | null>(null),
  sample = ref<unknown>(null),
  parametersText = ref('{}'),
  actionContextText = ref('{"form":{},"pageParameters":{}}'),
  parsedInput = ref<Json>({})
const sourceCatalog = ref<Json | null>(null)
const modelFields = ref<Json[]>([])
let modelGeneration = 0
async function loadModelFields() {
  const generation = ++modelGeneration
  modelFields.value = []
  const model = models.value.find(
    (m) => m.publishedRevisionNId === document.value.ledgerModelRevisionNId,
  )
  if (!model) return
  try {
    const revision = await getStudioApi().revision(
      model.nId,
      String(document.value.ledgerModelRevisionNId),
    )
    if (generation === modelGeneration)
      modelFields.value = ((revision.document.fields as Json[]) ?? []).filter((f) => !f.retired)
  } catch (e) {
    error.value = e instanceof Error ? e.message : t.value('error')
  }
}
let catalogGeneration = 0
const kind = computed(
  () =>
    resource.value?.kind ??
    (route.path.includes('datasets')
      ? 'Dataset'
      : route.path.includes('ledger-models')
        ? 'LedgerModel'
        : 'LowCodePage'),
)
const dirty = computed(() => JSON.stringify(document.value) !== saved.value)
const fields = computed(() => (document.value.fields as Json[]) ?? [])
const parameters = computed(() => (document.value.parameters as Json[]) ?? [])
const instances = computed(() => (document.value.instances as Json[]) ?? [])
const actions = computed(() => (document.value.actions as Json[]) ?? [])
const layout = computed(() => document.value.layout as unknown as LayoutNode)
const flat = computed(() => (layout.value ? nodes(layout.value) : []))
const node = computed(() => flat.value.find((n) => n.id === selected.value))
const publishPermission = computed(() =>
  kind.value === 'Dataset' ? 'studio.dataset.publish' : 'studio.lowcode.publish',
)
const query = computed(() => (document.value.query as Json) ?? {})
const queryMappings = computed(
  () =>
    ((query.value.kind === 'Api' ? query.value.parameterMap : query.value.parameters) as Json[]) ??
    [],
)
function addQueryMapping() {
  const key = query.value.kind === 'Api' ? 'parameterMap' : 'parameters'
  query.value[key] ??= []
  query.value[key].push({
    target: '',
    type: 'String',
    source: { from: 'PageParam', path: [], nullPolicy: 'Error' },
  })
}
const allSchemaFields = computed(() => {
  const result: Json[] = []
  function walk(field: Json) {
    result.push(field)
    for (const child of (field.children as Json[]) ?? []) walk(child)
    if (field.item) walk(field.item as Json)
  }
  if (document.value.result) walk(document.value.result as Json)
  return result
})
const instanceIds = computed(() => instances.value.map((i) => String(i.instanceId)))
function edit(fn: (doc: Json) => void) {
  const value = clone(document.value)
  fn(value)
  document.value = value
}
function changeNode(change: (n: LayoutNode) => void) {
  edit((doc) => {
    const target = nodes(doc.layout as unknown as LayoutNode).find((n) => n.id === selected.value)
    if (target) change(target)
  })
}
function changeProp(key: string, value: unknown) {
  changeNode((n) => {
    n.props[key] = value
  })
}
function parentId() {
  return flat.value.find((n) => n.children.some((child) => child.id === selected.value))?.id
}
const parentCandidates = computed(() => {
  const excluded = node.value ? new Set(nodes(node.value).map((n) => n.id)) : new Set()
  return flat.value.filter((n) => ['Container', 'Grid'].includes(n.type) && !excluded.has(n.id))
})
function reparent(parentId: string) {
  edit((doc) => {
    const root = doc.layout as LayoutNode,
      target = nodes(root).find((n) => n.id === parentId),
      parent = nodes(root).find((n) => n.children.some((child) => child.id === selected.value))
    if (!target || !parent) return
    const index = parent.children.findIndex((n) => n.id === selected.value)
    const child = parent.children.splice(index, 1)[0]!
    child.grid.order = target.children.length
    target.children.push(child)
  })
}
function geometryValue(property: string) {
  if (!node.value) return 0
  const index = flat.value.filter((n) => n !== layout.value).indexOf(node.value)
  return Number(
    (node.value.grid as unknown as Json)[property] ??
      (property === 'x'
        ? 24 + (index % 2) * 430
        : property === 'y'
          ? 24 + Math.floor(index / 2) * 150
          : property === 'width'
            ? 400
            : 120),
  )
}
function geometryChange(property: string, value: number | undefined) {
  edit((doc) => {
    const root = doc.layout as LayoutNode
    const target = nodes(root).find((n) => n.id === selected.value)
    if (!target) return
    target.grid[property] = value ?? 0
    syncGridPlacement(root, target.id, root.grid.canvasWidth ?? 1200)
  })
}
function conditionValue(owner: Json, key: string, value: string) {
  try {
    if (!value.trim()) delete owner[key]
    else owner[key] = JSON.parse(value)
  } catch {
    error.value = t.value('invalidJson')
  }
}
async function guard(action: () => Promise<void>) {
  if (busy.value) return
  busy.value = true
  error.value = ''
  try {
    await action()
  } catch (e) {
    error.value = e instanceof Error ? e.message : t.value('error')
    if (e instanceof ApiError && e.details.status === 409) conflict.value = true
  } finally {
    busy.value = false
  }
}
async function load() {
  loading.value = true
  error.value = ''
  try {
    const draft = await getStudioApi().draft(nId)
    resource.value = draft.resource
    document.value = draft.draftDocument
    saved.value = JSON.stringify(document.value)
    diagnostics.value = draft.diagnostics
    const results = await Promise.allSettled([
      getStudioApi().registry(),
      getStudioApi().sources({ pageSize: 200 }),
      getStudioApi().list('LedgerModel', { pageSize: 200 }),
      getStudioApi().list('Dataset', { pageSize: 200 }),
    ])
    if (results[0].status === 'fulfilled') registry.value = results[0].value
    else error.value = t.value('noRegistry') + ': ' + String(results[0].reason)
    if (results[1].status === 'fulfilled') sources.value = results[1].value.items
    if (results[2].status === 'fulfilled') models.value = results[2].value.items
    if (results[3].status === 'fulfilled') datasets.value = results[3].value.items
    if (kind.value === 'LowCodePage' && document.value.ledgerModelRevisionNId)
      await loadModelFields()
    for (const result of results.slice(1))
      if (result.status === 'rejected')
        diagnostics.value.push({
          message: result.reason instanceof Error ? result.reason.message : String(result.reason),
        })
  } catch (e) {
    error.value = e instanceof Error ? e.message : t.value('error')
  } finally {
    loading.value = false
  }
}
async function saveDraft() {
  if (!resource.value) return
  const result = await getStudioApi().save(resource.value, document.value)
  resource.value = result.resource
  diagnostics.value = result.diagnostics
  saved.value = JSON.stringify(document.value)
  conflict.value = false
  ElMessage.success(t.value('saved'))
}
function save() {
  void guard(saveDraft)
}
function validate() {
  void guard(async () => {
    const result = await getStudioApi().validate(nId, document.value)
    diagnostics.value = result.diagnostics
    if (!result.valid) throw new Error(t.value('validateFirst'))
    ElMessage.success(t.value('saved'))
  })
}
function publish() {
  note.value = ''
  drawer.value = 'publish'
}
function commitPublish() {
  void guard(async () => {
    if (!note.value.trim()) throw new Error(t.value('releaseNote'))
    if (dirty.value) await saveDraft()
    const result = await getStudioApi().validate(nId, document.value)
    diagnostics.value = result.diagnostics
    if (!result.valid) throw new Error(t.value('validateFirst'))
    await getStudioApi().publish(resource.value!, note.value, result.dependencyPlanHash)
    const draft = await getStudioApi().draft(nId)
    resource.value = draft.resource
    drawer.value = ''
    ElMessage.success(t.value('publishSuccess'))
  })
}
function showJson() {
  json.value = JSON.stringify(document.value, null, 2)
  drawer.value = 'json'
}
function applyJson() {
  try {
    const value = JSON.parse(json.value) as unknown
    if (!value || Array.isArray(value) || typeof value !== 'object')
      throw new Error(t.value('invalidJson'))
    document.value = value as Json
    drawer.value = ''
  } catch (e) {
    error.value = e instanceof Error ? e.message : t.value('invalidJson')
  }
}
function tryRun() {
  void guard(async () => {
    parsedInput.value = JSON.parse(parametersText.value) as Json
    if (kind.value === 'Dataset')
      sample.value = await getStudioApi().datasetPreview(document.value, parsedInput.value)
    else if (kind.value === 'LedgerModel') {
      if (dirty.value) await saveDraft()
      sample.value = await getStudioApi().upgrade(nId, resource.value!)
    } else
      preview.value = {
        ...(await getStudioApi().preview(nId, document.value, parsedInput.value)),
        resource: resource.value!,
      }
    drawer.value = 'preview'
  })
}
function addComponent(type: string) {
  const value = newNode(type)
  if (type === 'FormField')
    value.props = { fieldId: modelFields.value[0]?.id ?? '', control: 'Text' }
  if (type === 'Table')
    value.props = {
      instanceId: '',
      collectionPath: [],
      rowKeyPath: 'nId',
      columns: [],
      queryMode: 'top',
      selection: 'none',
    }
  if (type === 'Tree')
    value.props = { instanceId: '', keyPath: 'id', labelPath: 'name', parentPath: 'parentId' }
  if (type === 'Tabs') value.props = { items: [] }
  if (type === 'File') value.props = { multiple: false, maxCount: 1 }
  if (type === 'Chart')
    value.props = { chartType: 'Bar', datasetInstanceId: '', xField: '', series: [], options: {} }
  if (type === 'RegisteredComponent')
    value.props = { registryKey: 'studio.summary', registryVersion: '1', parameters: {} }
  value.grid.order = flat.value.length
  edit((doc) => (doc.layout as unknown as LayoutNode).children.push(value))
  selected.value = value.id
  void editor.value?.select(value.id)
}
function removeNode() {
  if (!node.value || node.value.id === layout.value.id) return
  const id = selected.value
  edit((doc) => {
    function remove(n: LayoutNode) {
      n.children = n.children.filter((c) => c.id !== id)
      n.children.forEach(remove)
    }
    remove(doc.layout as unknown as LayoutNode)
  })
  selected.value = ''
}
function moveNode(direction: number) {
  edit((doc) => {
    function move(parent: LayoutNode): boolean {
      const at = parent.children.findIndex((n) => n.id === selected.value)
      if (at >= 0) {
        const to = at + direction
        if (to >= 0 && to < parent.children.length) {
          const item = parent.children.splice(at, 1)[0]!
          parent.children.splice(to, 0, item)
          parent.children.forEach((n, i) => (n.grid.order = i))
        }
        return true
      }
      return parent.children.some(move)
    }
    move(doc.layout as unknown as LayoutNode)
  })
}
function addField() {
  const count = fields.value.length + 1
  edit((doc) =>
    (doc.fields as Json[]).push({
      id: newId(),
      name: 'field' + count,
      label: '',
      type: 'String',
      required: false,
      nullable: true,
      queryable: false,
      sortable: false,
      unique: false,
      retired: false,
      maxLength: 512,
    }),
  )
  if (!document.value.displayFieldId) document.value.displayFieldId = (fields.value[0] as Json).id
}
function addBinding() {
  if (!node.value) return
  edit((doc) =>
    (doc.fields as Json[]).push({
      componentId: selected.value,
      property: 'value',
      source: { from: 'Form', path: [String(node.value?.props.fieldId ?? '')], nullPolicy: 'Null' },
      writeTarget: node.value?.props.fieldId,
      readOnly: false,
      validators: [],
    }),
  )
}
function addAction() {
  const item = registry.value.actions[0]
  edit((doc) =>
    (doc.actions as Json[]).push({
      id: newId(),
      label: item?.label ?? item?.key ?? '',
      event: 'Click',
      actionKey: item?.key ?? '',
      executorVersion: item?.executorVersion ?? item?.version ?? '1',
      inputMap: [],
      success: { refreshInstanceIds: [], closeForm: false },
    }),
  )
}
function actionSelect(action: Json, key: string) {
  const item = registry.value.actions.find((a) => a.key === key)
  action.actionKey = key
  action.executorVersion = item?.executorVersion ?? item?.version ?? '1'
  action.inputMap = []
}
function actionTargets(action: Json): string[] {
  const entry = registry.value.actions.find((a) => a.key === action.actionKey)
  const input = entry?.inputSchema
  if (!input) return []
  if (Array.isArray(input)) return (input as Json[]).map((p) => String(p.name ?? p.id))
  return Object.keys((input.properties as Json | undefined) ?? input)
}
function simulateAction(action: Json) {
  void guard(async () => {
    const context = JSON.parse(actionContextText.value) as Json
    if (!context || typeof context !== 'object' || Array.isArray(context))
      throw new Error(t.value('invalidJson') + ': context')
    for (const [key, value] of Object.entries(context)) {
      if (
        !['form', 'pageParameters', 'row'].includes(key) ||
        !value ||
        typeof value !== 'object' ||
        Array.isArray(value)
      )
        throw new Error(t.value('invalidJson') + ': context.' + key)
    }
    sample.value = await getStudioApi().action(
      nId,
      String(action.id),
      {
        pageDocument: document.value,
        context,
      },
      true,
    )
    drawer.value = 'sample'
  })
}
function addParameter() {
  edit((doc) =>
    (doc.parameters as Json[]).push({
      name: 'param' + (parameters.value.length + 1),
      label: '',
      type: 'String',
      required: false,
      nullable: true,
      source: 'Caller',
    }),
  )
}
function sourceChange(id: string) {
  const source = sources.value.find((s) => s.nId === id)
  document.value.sourceNId = id
  document.value.sourceConfigurationVersion = source?.configurationVersion ?? ''
  void loadCatalog()
}
async function loadCatalog() {
  const request = ++catalogGeneration
  sourceCatalog.value = null
  try {
    const result = await getStudioApi().sourceCatalog(
      String(document.value.sourceNId),
      String(document.value.sourceConfigurationVersion),
    )
    if (request === catalogGeneration) sourceCatalog.value = result
  } catch (e) {
    if (request === catalogGeneration)
      error.value = e instanceof Error ? e.message : t.value('error')
  }
}
function useCatalogObject(qualifiedName: string) {
  const object = ((sourceCatalog.value?.objects as Json[]) ?? []).find(
    (o) => o.qualifiedName === qualifiedName,
  )
  if (!object) return
  document.value.parameters = clone(object.parameters ?? [])
  document.value.result = clone(object.resultSchema)
  const ids: string[] = []
  function walk(field: Json) {
    ids.push(String(field.id))
    for (const child of field.children ?? []) walk(child)
    if (field.item) walk(field.item)
  }
  walk(document.value.result as Json)
  document.value.readableFieldIds = ids
  document.value.query =
    object.kind === 'Api' || object.kind === 'ApiOperation'
      ? { kind: 'Api', operationKey: qualifiedName, method: 'GET', parameterMap: [] }
      : object.kind === 'Procedure'
        ? { kind: 'Procedure', qualifiedName, parameters: [], resultSets: [] }
        : { kind: 'Sql', statement: 'SELECT * FROM ' + qualifiedName, parameters: [] }
  const key = query.value.kind === 'Api' ? 'parameterMap' : 'parameters'
  query.value[key] = parameters.value.map((p) => ({
    target: p.name,
    type: p.type,
    source: { from: 'PageParam', path: [p.name], nullPolicy: p.required ? 'Error' : 'Null' },
  }))
}
function queryKind(value: string) {
  document.value.query =
    value === 'Api'
      ? { kind: value, operationKey: '', method: 'GET', parameterMap: [] }
      : value === 'Sql'
        ? { kind: value, statement: '', parameters: [] }
        : { kind: value, qualifiedName: '', parameters: [], resultSets: [] }
}
async function compare() {
  await guard(async () => {
    remote.value = (await getStudioApi().draft(nId)).draftDocument
    drawer.value = 'compare'
  })
}
function saveAs() {
  void guard(async () => {
    const created = await getStudioApi().create({
      kind: kind.value,
      name: resource.value!.name + ' (copy)',
      document: document.value,
    })
    saved.value = JSON.stringify(document.value)
    window.location.assign(
      route.path.replace(encodeURIComponent(nId), encodeURIComponent(created.nId)),
    )
  })
}
onBeforeRouteLeave(async () => {
  if (!dirty.value) return true
  try {
    await ElMessageBox.confirm(t.value('unsavedLeave'), t.value('unsaved'))
    return true
  } catch {
    return false
  }
})
onMounted(load)
</script>

<template>
  <AppPage
    :title="resource?.name ?? t('design')"
    :description="
      resource
        ? `${t('version')} ${resource.publishedRevisionNo ?? '—'} · ${t('draftVersion')} ${resource.optimisticVersion}`
        : ''
    "
    class="studio-page"
    v-loading="loading"
  >
    <template #actions
      ><el-button
        @click="
          router.push(
            '/pc/studio/' +
              (kind === 'Dataset'
                ? 'datasets'
                : kind === 'LedgerModel'
                  ? 'ledger-models'
                  : 'low-code'),
          )
        "
        >{{ t('back') }}</el-button
      ><el-button :disabled="busy || !resource" @click="showJson">{{ t('advanced') }}</el-button
      ><el-button :disabled="busy || !resource" @click="validate">{{ t('validate') }}</el-button
      ><el-button :loading="busy" :disabled="!resource" @click="save">{{ t('save') }}</el-button
      ><el-button :disabled="busy || !resource" @click="tryRun">{{
        t(kind === 'LedgerModel' ? 'upgrade' : 'preview')
      }}</el-button
      ><PermissionGate :permission-n-id="publishPermission"
        ><el-button type="primary" :disabled="busy || !resource" @click="publish">{{
          t('publish')
        }}</el-button></PermissionGate
      ></template
    >
    <el-alert v-if="error" :title="error" type="error" :closable="false" />
    <div v-if="conflict" class="studio-tools">
      <el-alert :title="t('conflict')" type="warning" :closable="false" /><el-button
        @click="compare"
        >{{ t('compare') }}</el-button
      ><el-button @click="saveAs">{{ t('saveAs') }}</el-button>
    </div>
    <template v-if="resource">
      <div v-if="kind !== 'LedgerModel'" class="studio-tools">
        <label>{{ t('parametersInput') }}</label
        ><el-input
          v-model="parametersText"
          :aria-label="t('parametersInput')"
          style="width: 320px"
        />
      </div>
      <template v-if="kind === 'LedgerModel'">
        <el-alert :title="t('stable')" :closable="false" />
        <div class="studio-tools">
          <el-select
            v-model="document.displayFieldId"
            :placeholder="t('label')"
            :aria-label="t('label')"
            ><el-option
              v-for="f in fields"
              :key="String(f.id)"
              :label="String(f.label || f.name)"
              :value="f.id" /></el-select
          ><el-select v-model="(document.numbering as Json).mode" :aria-label="t('mode')"
            ><el-option label="Manual" value="Manual" /><el-option
              label="Rule"
              value="Rule" /></el-select
          ><el-input
            v-if="(document.numbering as Json).mode === 'Rule'"
            v-model="(document.numbering as Json).ruleKey"
            :placeholder="t('key')"
          /><el-button type="primary" @click="addField">{{ t('add') }} {{ t('field') }}</el-button>
        </div>
        <div class="studio-scroll">
          <table class="studio-structural-table">
            <thead>
              <tr>
                <th>{{ t('fieldId') }}</th>
                <th>{{ t('fieldName') }}</th>
                <th>{{ t('label') }}</th>
                <th>{{ t('type') }}</th>
                <th>{{ t('required') }}</th>
                <th>{{ t('unique') }}</th>
                <th>{{ t('queryable') }}</th>
                <th>{{ t('sortable') }}</th>
                <th>{{ t('retired') }}</th>
                <th>{{ t('more') }}</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="(f, i) in fields" :key="String(f.id)">
                <td>
                  <small :title="String(f.id)">{{ String(f.id).slice(0, 8) }}</small>
                </td>
                <td><el-input v-model="f.name" :aria-label="t('fieldName')" /></td>
                <td><el-input v-model="f.label" :aria-label="t('label')" /></td>
                <td>
                  <el-select v-model="f.type" :aria-label="t('type')"
                    ><el-option
                      v-for="type in [
                        'String',
                        'Text',
                        'Decimal',
                        'Int32',
                        'Boolean',
                        'Date',
                        'DateTime',
                        'Enum',
                        'FileReference',
                      ]"
                      :key="type"
                      :label="type"
                      :value="type" /></el-select
                  ><el-input
                    v-if="f.type === 'Enum'"
                    :model-value="((f.enumValues as string[]) ?? []).join(',')"
                    :placeholder="t('options')"
                    @update:model-value="(v: Json[string]) => (f.enumValues = v.split(','))"
                  /><el-input-number
                    v-if="f.type === 'Decimal'"
                    v-model="f.scale"
                    :min="0"
                    :max="6"
                    :aria-label="t('precision')"
                  /><el-input-number
                    v-if="f.type === 'String' || f.type === 'Text'"
                    v-model="f.maxLength"
                    :min="1"
                    :max="f.type === 'Text' ? 65536 : 512"
                    :aria-label="t('maxLength')"
                  />
                </td>
                <td>
                  <el-checkbox
                    v-model="f.required"
                    :aria-label="t('required')"
                    @update:model-value="(v: Json[string]) => (f.nullable = !v)"
                  />
                </td>
                <td><el-checkbox v-model="f.unique" :aria-label="t('unique')" /></td>
                <td><el-checkbox v-model="f.queryable" :aria-label="t('queryable')" /></td>
                <td><el-checkbox v-model="f.sortable" :aria-label="t('sortable')" /></td>
                <td><el-checkbox v-model="f.retired" :aria-label="t('retired')" /></td>
                <td>
                  <el-button link @click="fields.splice(i, 1)">{{ t('remove') }}</el-button
                  ><el-button
                    link
                    :disabled="i === 0"
                    @click="fields.splice(i - 1, 0, fields.splice(i, 1)[0]!)"
                    >↑</el-button
                  >
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </template>
      <template v-else-if="kind === 'Dataset'">
        <div class="studio-form-grid">
          <el-form-item :label="t('source')"
            ><el-select :model-value="document.sourceNId" @update:model-value="sourceChange"
              ><el-option
                v-for="s in sources"
                :key="s.nId"
                :label="`${s.name} · ${s.provider} · v${s.configurationVersion}`"
                :value="s.nId" /></el-select></el-form-item
          ><el-form-item :label="t('sourceVersion')"
            ><el-input v-model="document.sourceConfigurationVersion" readonly /></el-form-item
          ><el-form-item :label="t('queryKind')"
            ><el-select :model-value="query.kind" @update:model-value="queryKind"
              ><el-option
                v-for="type in ['Api', 'Sql', 'Procedure']"
                :key="type"
                :label="type"
                :value="type" /></el-select></el-form-item
          ><el-form-item :label="t('readPolicy')"
            ><el-input v-model="document.rowPolicyKey"
          /></el-form-item>
        </div>
        <el-form label-position="top"
          ><el-form-item :label="t('sourceOperation')"
            ><el-select
              :model-value="query.operationKey ?? query.qualifiedName"
              :disabled="!document.sourceNId"
              filterable
              @visible-change="(v: boolean) => v && loadCatalog()"
              @update:model-value="useCatalogObject"
              ><el-option
                v-for="object in (sourceCatalog?.objects as Json[]) ?? []"
                :key="String(object.qualifiedName)"
                :label="String(object.kind) + ' · ' + String(object.qualifiedName)"
                :value="object.qualifiedName" /></el-select
            ><el-button :disabled="!document.sourceNId" @click="loadCatalog">{{
              t('refresh')
            }}</el-button></el-form-item
          ><el-form-item :label="t(query.kind === 'Api' ? 'sourceOperation' : 'statement')"
            ><el-input v-if="query.kind === 'Api'" v-model="query.operationKey" /><el-input
              v-else-if="query.kind === 'Sql'"
              v-model="query.statement"
              type="textarea"
              :rows="5" /><el-input v-else v-model="query.qualifiedName" /></el-form-item
        ></el-form>
        <el-tabs
          ><el-tab-pane :label="t('parameters')"
            ><div class="studio-scroll">
              <table class="studio-structural-table">
                <thead>
                  <tr>
                    <th>{{ t('name') }}</th>
                    <th>{{ t('label') }}</th>
                    <th>{{ t('type') }}</th>
                    <th>{{ t('required') }}</th>
                    <th>{{ t('context') }}</th>
                    <th>{{ t('defaultValue') }}</th>
                    <th />
                  </tr>
                </thead>
                <tbody>
                  <tr v-for="(p, i) in parameters" :key="i">
                    <td><el-input v-model="p.name" :aria-label="t('name')" /></td>
                    <td><el-input v-model="p.label" :aria-label="t('label')" /></td>
                    <td>
                      <el-select v-model="p.type" :aria-label="t('type')"
                        ><el-option
                          v-for="type in [
                            'String',
                            'Int32',
                            'Int64',
                            'Decimal',
                            'Boolean',
                            'Date',
                            'DateTime',
                            'Guid',
                            'Array',
                          ]"
                          :key="type"
                          :label="type"
                          :value="type"
                      /></el-select>
                    </td>
                    <td><el-checkbox v-model="p.required" :aria-label="t('required')" /></td>
                    <td>
                      <el-select v-model="p.source" :aria-label="t('context')"
                        ><el-option label="Caller" value="Caller" /><el-option
                          label="TrustedContext"
                          value="TrustedContext" /></el-select
                      ><el-input
                        v-if="p.source === 'TrustedContext'"
                        v-model="p.contextKey"
                        :placeholder="t('key')"
                      />
                    </td>
                    <td><el-input v-model="p.defaultValue" :aria-label="t('defaultValue')" /></td>
                    <td>
                      <el-button link @click="parameters.splice(i, 1)">{{ t('remove') }}</el-button>
                    </td>
                  </tr>
                </tbody>
              </table>
            </div>
            <el-button @click="addParameter">{{ t('add') }}</el-button>
            <h3>{{ t('parameterMap') }}</h3>
            <div v-for="(mapping, i) in queryMappings" :key="i" class="studio-binding">
              <div class="studio-tools">
                <el-input v-model="mapping.target" :placeholder="t('target')" /><el-select
                  v-model="mapping.type"
                  :placeholder="t('type')"
                  ><el-option
                    v-for="type in [
                      'String',
                      'Int32',
                      'Int64',
                      'Decimal',
                      'Boolean',
                      'Date',
                      'DateTime',
                      'Guid',
                    ]"
                    :key="type"
                    :label="type"
                    :value="type" /></el-select
                ><el-button link @click="queryMappings.splice(i, 1)">{{ t('remove') }}</el-button>
              </div>
              <BindingEditor v-model="mapping.source" />
            </div>
            <el-button @click="addQueryMapping">{{ t('add') }} {{ t('mappings') }}</el-button>
            <template v-if="query.kind === 'Procedure'"
              ><h3>Result sets</h3>
              <div v-for="(set, i) in query.resultSets as Json[]" :key="i" class="studio-binding">
                <div class="studio-tools">
                  <el-input-number v-model="set.index" :min="0" :aria-label="t('order')" /><el-input
                    v-model="set.name"
                    :placeholder="t('name')"
                  /><el-button @click="query.resultSets.splice(i, 1)">{{ t('remove') }}</el-button>
                </div>
                <StudioSchemaEditor v-model="set.schema" />
              </div>
              <el-button
                @click="
                  query.resultSets.push({
                    index: query.resultSets.length,
                    name: 'rows' + query.resultSets.length,
                    schema: clone(document.result),
                  })
                "
                >{{ t('add') }} {{ t('schema') }}</el-button
              ></template
            >
          </el-tab-pane>
          <el-tab-pane :label="t('schema')"
            ><StudioSchemaEditor
              v-if="document.result"
              v-model="document.result as Json" /><el-form-item :label="t('readFields')"
              ><el-select v-model="document.readableFieldIds" multiple
                ><el-option
                  v-for="f in allSchemaFields"
                  :key="String(f.id)"
                  :label="`${f.label || f.name} (${f.type})`"
                  :value="f.id" /></el-select></el-form-item
          ></el-tab-pane>
          <el-tab-pane :label="t('configured')"
            ><div class="studio-form-grid">
              <el-form-item :label="t('timeout')"
                ><el-input-number
                  v-model="document.timeoutSeconds"
                  :min="1"
                  :max="300" /></el-form-item
              ><el-form-item :label="t('maxRows')"
                ><el-input-number v-model="document.maxRows" :min="1" /></el-form-item
              ><el-form-item :label="t('maxBytes')"
                ><el-input v-model="document.maxBytes"
              /></el-form-item></div></el-tab-pane
        ></el-tabs>
      </template>
      <template v-else>
        <div class="studio-tools">
          <el-select v-model="document.mode" :aria-label="t('mode')"
            ><el-option
              v-for="v in ['Crud', 'Compose', 'Code']"
              :key="v"
              :label="v"
              :value="v" /></el-select
          ><el-select
            v-model="document.ledgerModelRevisionNId"
            :placeholder="t('selectLedger')"
            :aria-label="t('ledgerModel')"
            filterable
            clearable
            @update:model-value="loadModelFields"
            ><el-option
              v-for="m in models.filter((x) => x.publishedRevisionNId)"
              :key="m.nId"
              :label="m.name + ' · v' + m.publishedRevisionNo"
              :value="m.publishedRevisionNId" /></el-select
          ><el-select
            v-if="document.mode === 'Code'"
            v-model="document.codeComponentKey"
            :placeholder="t('registeredKey')"
            ><el-option
              v-for="code in registry.codeComponents ?? []"
              :key="code.key"
              :label="code.label ?? code.key"
              :value="code.key"
          /></el-select>
        </div>
        <StudioEditorShell
          ref="editor"
          v-model="document"
          @select="(v: Json[string]) => (selected = v)"
        >
          <template #structure="slot"
            ><h3>{{ t('component') }}</h3>
            <div class="studio-palette">
              <el-button
                v-for="type in [
                  'Text',
                  'FormField',
                  'Table',
                  'Tree',
                  'Tabs',
                  'File',
                  'Chart',
                  'Container',
                  'Grid',
                  'RegisteredComponent',
                ]"
                :key="type"
                @click="addComponent(type)"
                >{{ type }}</el-button
              >
            </div>
            <h3>{{ t('structure') }}</h3>
            <button
              v-for="n in slot.nodes"
              :key="n.id"
              class="studio-node-item"
              :class="{ active: slot.selected === n.id }"
              @click="slot.select(n.id)"
            >
              {{ n.type }} · {{ n.id.slice(0, 8) }}
            </button></template
          >
          <template #properties
            ><el-tabs v-model="tab"
              ><el-tab-pane :label="t('properties')" name="properties"
                ><template v-if="node"
                  ><h3>{{ node.type }}</h3>
                  <el-form label-position="top"
                    ><div class="studio-prop-row">
                      <el-form-item :label="t('span')"
                        ><el-input-number
                          :model-value="node.grid.span"
                          :min="1"
                          :max="24"
                          @update:model-value="
                            (v: Json[string]) => changeNode((n) => (n.grid.span = v ?? 12))
                          " /></el-form-item
                      ><el-form-item :label="t('minWidth')"
                        ><el-input-number
                          :model-value="node.grid.minWidth"
                          :min="0"
                          @update:model-value="
                            (v: Json[string]) => changeNode((n) => (n.grid.minWidth = v ?? 0))
                          "
                      /></el-form-item>
                    </div>
                    <el-form-item v-if="node.id !== layout.id" :label="t('parent')"
                      ><el-select :model-value="parentId()" @update:model-value="reparent"
                        ><el-option
                          v-for="parent in parentCandidates"
                          :key="parent.id"
                          :label="parent.type + ' ' + parent.id.slice(0, 8)"
                          :value="parent.id" /></el-select
                    ></el-form-item>
                    <div class="studio-prop-row">
                      <el-form-item
                        v-for="property in ['x', 'y', 'width', 'height']"
                        :key="property"
                        :label="t(property)"
                        ><el-input-number
                          :model-value="geometryValue(property)"
                          :min="property === 'width' || property === 'height' ? 20 : 0"
                          @update:model-value="
                            (v: number | undefined) => geometryChange(property, v)
                          "
                      /></el-form-item>
                    </div>
                    <el-form-item v-if="node.type === 'Text'" :label="t('content')"
                      ><el-input
                        :model-value="node.props.text"
                        type="textarea"
                        @update:model-value="(v: Json[string]) => changeProp('text', v)"
                    /></el-form-item>
                    <template v-if="node.type === 'FormField'"
                      ><el-form-item :label="t('write')"
                        ><el-select
                          :model-value="node.props.fieldId"
                          filterable
                          allow-create
                          @update:model-value="(v: Json[string]) => changeProp('fieldId', v)"
                          ><el-option
                            v-for="field in modelFields"
                            :key="String(field.id)"
                            :label="String(field.label || field.name) + ' · ' + field.id"
                            :value="field.id" /></el-select></el-form-item
                      ><el-form-item :label="t('fieldControl')"
                        ><el-select
                          :model-value="node.props.control"
                          @update:model-value="(v: Json[string]) => changeProp('control', v)"
                          ><el-option
                            v-for="v in [
                              'Text',
                              'Number',
                              'Select',
                              'Date',
                              'DateTime',
                              'Checkbox',
                              'BusinessSelector',
                            ]"
                            :key="v"
                            :label="v"
                            :value="v" /></el-select></el-form-item
                      ><el-form-item :label="t('label')"
                        ><el-input
                          :model-value="node.props.placeholder"
                          @update:model-value="
                            (v: Json[string]) => changeProp('placeholder', v)
                          " /></el-form-item
                    ></template>
                    <template v-if="['Table', 'Tree', 'Chart'].includes(node.type)"
                      ><el-form-item :label="t('instanceId')"
                        ><el-select
                          :model-value="
                            node.props[node.type === 'Chart' ? 'datasetInstanceId' : 'instanceId']
                          "
                          @update:model-value="
                            (v: Json[string]) =>
                              changeProp(
                                node!.type === 'Chart' ? 'datasetInstanceId' : 'instanceId',
                                v,
                              )
                          "
                          ><el-option
                            v-for="i in instanceIds"
                            :key="i"
                            :label="i"
                            :value="i" /></el-select></el-form-item
                    ></template>
                    <template v-if="node.type === 'Table'"
                      ><el-form-item :label="t('rowKey')"
                        ><el-input
                          :model-value="node.props.rowKeyPath"
                          @update:model-value="
                            (v: Json[string]) => changeProp('rowKeyPath', v)
                          " /></el-form-item
                      ><el-form-item :label="t('collection')"
                        ><el-input
                          :model-value="
                            Array.isArray(node.props.collectionPath)
                              ? node.props.collectionPath.join('.')
                              : String(node.props.collectionPath ?? '')
                          "
                          @update:model-value="
                            (value: string) =>
                              changeProp('collectionPath', value.split('.').filter(Boolean))
                          "
                      /></el-form-item>
                      <div
                        v-for="(col, i) in node.props.columns as Json[]"
                        :key="i"
                        class="studio-binding"
                      >
                        <el-input v-model="col.field" :placeholder="t('fieldName')" /><el-input
                          v-model="col.title"
                          :placeholder="t('label')"
                        /><el-checkbox v-model="col.sortable">{{ t('sortable') }}</el-checkbox
                        ><el-button link @click="(node!.props.columns as Json[]).splice(i, 1)">{{
                          t('remove')
                        }}</el-button>
                      </div>
                      <el-button
                        @click="
                          (node!.props.columns as Json[]).push({
                            field: '',
                            title: '',
                            sortable: false,
                          })
                        "
                        >{{ t('add') }} {{ t('columns') }}</el-button
                      ><el-form-item :label="t('selection')"
                        ><el-select v-model="node.props.selection"
                          ><el-option
                            v-for="v in ['none', 'single', 'multiple']"
                            :key="v"
                            :label="v"
                            :value="v" /></el-select></el-form-item
                    ></template>
                    <template v-if="node.type === 'Tree'"
                      ><el-form-item
                        v-for="p in ['keyPath', 'labelPath', 'parentPath']"
                        :key="p"
                        :label="p"
                        ><el-input
                          :model-value="node.props[p]"
                          @update:model-value="
                            (v: Json[string]) => changeProp(p, v)
                          " /></el-form-item
                    ></template>
                    <template v-if="node.type === 'File'"
                      ><el-checkbox v-model="node.props.multiple">Multiple</el-checkbox
                      ><el-form-item :label="t('maxRows')"
                        ><el-input-number
                          v-model="node.props.maxCount"
                          :min="1"
                          :max="20" /></el-form-item
                    ></template>
                    <template v-if="node.type === 'Tabs'"
                      ><div
                        v-for="(item, i) in node.props.items as Json[]"
                        :key="i"
                        class="studio-binding"
                      >
                        <el-input v-model="item.label" :placeholder="t('label')" /><el-select
                          v-model="item.childLayoutId"
                          :placeholder="t('component')"
                          ><el-option
                            v-for="n in flat.filter((n) => n.id !== node?.id)"
                            :key="n.id"
                            :label="n.type + ' ' + n.id.slice(0, 8)"
                            :value="n.id" /></el-select
                        ><el-button link @click="(node!.props.items as Json[]).splice(i, 1)">{{
                          t('remove')
                        }}</el-button>
                      </div>
                      <el-button
                        @click="
                          (node!.props.items as Json[]).push({
                            id: newId(),
                            label: 'Tab',
                            childLayoutId: '',
                          })
                        "
                        >{{ t('add') }}</el-button
                      ></template
                    >
                    <template v-if="node.type === 'Container' || node.type === 'Grid'"
                      ><el-form-item :label="t('columns')"
                        ><el-input-number
                          v-model="node.props.columns"
                          :min="1"
                          :max="24" /></el-form-item
                      ><el-form-item label="Gap"
                        ><el-input-number
                          v-model="node.props.gapPx"
                          :min="0"
                          :max="48" /></el-form-item
                    ></template>
                    <template v-if="node.type === 'Chart'"
                      ><el-form-item :label="t('chartType')"
                        ><el-select v-model="node.props.chartType"
                          ><el-option
                            v-for="v in ['Bar', 'Line', 'Pie']"
                            :key="v"
                            :label="t(v.toLowerCase())"
                            :value="v" /></el-select></el-form-item
                      ><el-form-item :label="t('category')"
                        ><el-input v-model="node.props.xField"
                      /></el-form-item>
                      <div v-for="(s, i) in node.props.series as Json[]" :key="i">
                        <el-input v-model="s.name" :placeholder="t('name')" /><el-input
                          v-model="s.valueField"
                          :placeholder="t('valueField')"
                        /><el-button link @click="(node!.props.series as Json[]).splice(i, 1)">{{
                          t('remove')
                        }}</el-button>
                      </div>
                      <el-button
                        @click="(node!.props.series as Json[]).push({ name: '', valueField: '' })"
                        >{{ t('add') }} {{ t('series') }}</el-button
                      ></template
                    >
                    <template v-if="node.type.startsWith('Registered')"
                      ><el-form-item :label="t('registeredKey')"
                        ><el-select v-model="node.props.registryKey"
                          ><el-option
                            label="studio.summary"
                            value="studio.summary" /></el-select></el-form-item
                      ><el-form-item :label="t('codeVersion')"
                        ><el-input v-model="node.props.registryVersion" /></el-form-item
                    ></template>
                  </el-form>
                  <div class="studio-tools">
                    <el-button @click="moveNode(-1)">↑</el-button
                    ><el-button @click="moveNode(1)">↓</el-button
                    ><el-button
                      type="danger"
                      :disabled="node.id === layout.id"
                      @click="removeNode"
                      >{{ t('remove') }}</el-button
                    >
                  </div></template
                ><el-empty v-else :description="t('noSelection')"
              /></el-tab-pane>
              <el-tab-pane :label="t('bindings')" name="bindings"
                ><p>{{ t('fieldHelp') }}</p>
                <div
                  v-for="(f, i) in fields.filter((f) => f.componentId === selected)"
                  :key="i"
                  class="studio-binding"
                >
                  <el-input v-model="f.property" :placeholder="t('property')" /><BindingEditor
                    v-model="f.source as unknown as Binding"
                    :instances="instanceIds"
                  /><el-select
                    v-model="f.writeTarget"
                    :placeholder="t('write')"
                    filterable
                    allow-create
                    ><el-option
                      v-for="field in modelFields"
                      :key="String(field.id)"
                      :label="String(field.label || field.name) + ' · ' + field.id"
                      :value="field.id" /></el-select
                  ><el-checkbox v-model="f.readOnly">{{ t('readOnly') }}</el-checkbox
                  ><el-input v-model="f.formatterKey" :placeholder="t('formatter')" />
                  <el-input
                    :model-value="f.visibleWhen ? JSON.stringify(f.visibleWhen) : ''"
                    :placeholder="t('visibleWhen')"
                    @change="(v: string) => conditionValue(f, 'visibleWhen', v)"
                  />
                  <el-input
                    :model-value="f.requiredWhen ? JSON.stringify(f.requiredWhen) : ''"
                    :placeholder="t('requiredWhen')"
                    @change="(v: string) => conditionValue(f, 'requiredWhen', v)"
                  />
                  <div v-for="(v, vi) in f.validators as Json[]" :key="vi">
                    <el-select v-model="v.kind"
                      ><el-option
                        v-for="kind in ['Required', 'Range', 'Length', 'Pattern']"
                        :key="kind"
                        :label="kind"
                        :value="kind" /></el-select
                    ><el-input
                      v-if="v.kind === 'Pattern'"
                      v-model="(v.options as Json).pattern"
                      :placeholder="t('pattern')"
                    /><template v-if="v.kind === 'Range' || v.kind === 'Length'"
                      ><el-input-number
                        v-model="(v.options as Json).min"
                        :aria-label="t('min')" /><el-input-number
                        v-model="(v.options as Json).max"
                        :aria-label="t('max')" /></template
                    ><el-button link @click="(f.validators as Json[]).splice(vi, 1)">{{
                      t('remove')
                    }}</el-button>
                  </div>
                  <el-button
                    link
                    @click="(f.validators as Json[]).push({ kind: 'Required', options: {} })"
                    >{{ t('add') }} {{ t('validators') }}</el-button
                  ><el-button link type="danger" @click="fields.splice(fields.indexOf(f), 1)">{{
                    t('remove')
                  }}</el-button>
                </div>
                <el-button :disabled="!node" @click="addBinding"
                  >{{ t('add') }} {{ t('bindings') }}</el-button
                ></el-tab-pane
              >
              <el-tab-pane :label="t('actions')" name="actions"
                ><p>{{ t('actionsHelp') }}</p>
                <el-form-item :label="t('context') + ' (form, pageParameters, row)'">
                  <el-input
                    v-model="actionContextText"
                    type="textarea"
                    :rows="5"
                    :aria-label="t('context') + ' (form, pageParameters, row)'"
                  />
                </el-form-item>
                <div v-for="(action, i) in actions" :key="String(action.id)" class="studio-binding">
                  <el-input v-model="action.label" :placeholder="t('label')" /><el-select
                    :model-value="action.actionKey"
                    :placeholder="t('action')"
                    @update:model-value="(v: Json[string]) => actionSelect(action, v)"
                    ><el-option
                      v-for="a in registry.actions"
                      :key="a.key"
                      :label="a.label ?? a.key"
                      :value="a.key" /></el-select
                  ><el-select v-model="action.event"
                    ><el-option
                      v-for="v in ['Click', 'RowClick', 'Submit']"
                      :key="v"
                      :label="v"
                      :value="v" /></el-select
                  ><small>{{ t('actionVersion') }} {{ action.executorVersion }}</small>
                  <div
                    v-for="(map, mi) in action.inputMap as Json[]"
                    :key="mi"
                    class="studio-binding"
                  >
                    <el-select
                      v-model="map.target"
                      :placeholder="t('target')"
                      filterable
                      allow-create
                      ><el-option
                        v-for="target in actionTargets(action)"
                        :key="target"
                        :label="target"
                        :value="target" /></el-select
                    ><BindingEditor
                      v-model="map.source as unknown as Binding"
                      :instances="instanceIds"
                    /><el-button link @click="(action.inputMap as Json[]).splice(mi, 1)">{{
                      t('remove')
                    }}</el-button>
                  </div>
                  <el-button
                    link
                    @click="
                      (action.inputMap as Json[]).push({
                        target: '',
                        source: { from: 'Form', path: [], nullPolicy: 'Error' },
                      })
                    "
                    >{{ t('add') }} {{ t('mappings') }}</el-button
                  ><el-input
                    :model-value="
                      ((action.success as Json).refreshInstanceIds as string[]).join(',')
                    "
                    :placeholder="t('successRefresh')"
                    @update:model-value="
                      (v: Json[string]) =>
                        ((action.success as Json).refreshInstanceIds = v.split(',').filter(Boolean))
                    "
                  /><el-checkbox v-model="(action.success as Json).closeForm">{{
                    t('closeForm')
                  }}</el-checkbox
                  ><el-button @click="simulateAction(action)">{{ t('simulate') }}</el-button
                  ><el-button link type="danger" @click="actions.splice(i, 1)">{{
                    t('remove')
                  }}</el-button>
                </div>
                <el-button @click="addAction"
                  >{{ t('add') }} {{ t('action') }}</el-button
                ></el-tab-pane
              >
              <el-tab-pane :label="t('instances')" name="instances"
                ><div v-for="(instance, i) in instances" :key="i" class="studio-binding">
                  <el-input
                    v-model="instance.instanceId"
                    :placeholder="t('instanceId')"
                  /><el-select
                    v-model="instance.datasetNId"
                    :placeholder="t('dataSetId')"
                    @update:model-value="
                      (v: Json[string]) =>
                        (instance.revisionNId = datasets.find(
                          (d) => d.nId === v,
                        )?.publishedRevisionNId)
                    "
                    ><el-option
                      v-for="d in datasets.filter((x) => x.publishedRevisionNId)"
                      :key="d.nId"
                      :label="d.name + ' · v' + d.publishedRevisionNo"
                      :value="d.nId" /></el-select
                  ><el-input v-model="instance.revisionNId" :placeholder="t('revision')" readonly />
                  <div v-for="(map, mi) in instance.parameters as Json[]" :key="mi">
                    <el-input v-model="map.target" :placeholder="t('target')" /><BindingEditor
                      v-model="map.source as unknown as Binding"
                      :instances="instanceIds"
                    /><el-button link @click="(instance.parameters as Json[]).splice(mi, 1)">{{
                      t('remove')
                    }}</el-button>
                  </div>
                  <el-button
                    link
                    @click="
                      (instance.parameters as Json[]).push({
                        target: '',
                        source: { from: 'PageParam', path: [], nullPolicy: 'Error' },
                      })
                    "
                    >{{ t('add') }} {{ t('mappings') }}</el-button
                  ><el-button link type="danger" @click="instances.splice(i, 1)">{{
                    t('remove')
                  }}</el-button>
                </div>
                <el-button
                  @click="
                    instances.push({
                      instanceId: 'data' + (instances.length + 1),
                      datasetNId: '',
                      revisionNId: '',
                      parameters: [],
                      refreshSeconds: 5,
                      staleAfterSeconds: 15,
                    })
                  "
                  >{{ t('add') }} {{ t('instances') }}</el-button
                >
                <h3>{{ t('parameters') }}</h3>
                <div v-for="(p, i) in parameters" :key="i" class="studio-binding">
                  <el-input v-model="p.name" :placeholder="t('name')" /><el-input
                    v-model="p.label"
                    :placeholder="t('label')"
                  /><el-select v-model="p.type"
                    ><el-option
                      v-for="type in [
                        'String',
                        'Int32',
                        'Int64',
                        'Decimal',
                        'Boolean',
                        'Date',
                        'DateTime',
                        'Guid',
                        'Array',
                      ]"
                      :key="type"
                      :label="type"
                      :value="type" /></el-select
                  ><el-checkbox v-model="p.required">{{ t('required') }}</el-checkbox
                  ><el-button link @click="parameters.splice(i, 1)">{{ t('remove') }}</el-button>
                </div>
                <el-button @click="addParameter"
                  >{{ t('add') }} {{ t('parameters') }}</el-button
                ></el-tab-pane
              >
            </el-tabs></template
          >
        </StudioEditorShell>
      </template>
      <div class="studio-status">
        <span>{{ t(dirty ? 'unsaved' : 'saved') }}</span
        ><span>{{ t('draftVersion') }} {{ resource.optimisticVersion }}</span
        ><span>{{ diagnostics.length }} {{ t('diagnostic') }}</span>
      </div>
      <el-collapse v-if="diagnostics.length"
        ><el-collapse-item :title="t('diagnostic')">
          <pre class="studio-json">{{ JSON.stringify(diagnostics, null, 2) }}</pre>
        </el-collapse-item></el-collapse
      >
    </template>
    <AppFormDrawer
      :model-value="!!drawer"
      :title="t(drawer === 'json' ? 'advanced' : drawer === 'sample' ? 'simulate' : drawer)"
      size="wide"
      :busy="busy"
      :hide-footer="drawer === 'preview' || drawer === 'sample' || drawer === 'compare'"
      @update:model-value="(v: Json[string]) => !v && (drawer = '')"
      @submit="drawer === 'json' ? applyJson() : commitPublish()"
    >
      <el-alert v-if="error" :title="error" type="error" :closable="false" /><el-input
        v-if="drawer === 'json'"
        v-model="json"
        type="textarea"
        :rows="26"
      /><el-input
        v-if="drawer === 'publish'"
        v-model="note"
        :placeholder="t('releaseNote')"
        type="textarea"
        :rows="4"
      />
      <template v-if="drawer === 'preview'"
        ><el-alert
          v-if="kind === 'LowCodePage'"
          :title="t('mock')"
          :closable="false"
        /><StudioRuntimeView
          v-if="preview && kind === 'LowCodePage'"
          :runtime="preview"
          :preview="true"
          :parameters="parsedInput"
        />
        <StudioSampleView v-else :value="sample" />
      </template>
      <pre v-if="drawer === 'sample'" class="studio-json">{{
        JSON.stringify(sample, null, 2)
      }}</pre>
      <template v-if="drawer === 'compare'"
        ><p>{{ t('compareHelp') }}</p>
        <h3>{{ t('compare') }}</h3>
        <pre class="studio-json">{{ JSON.stringify(remote, null, 2) }}</pre>
        <h3>{{ t('unsaved') }}</h3>
        <pre class="studio-json">{{ JSON.stringify(document, null, 2) }}</pre>
      </template>
    </AppFormDrawer>
  </AppPage>
</template>
