<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'
import { onBeforeRouteLeave, useRoute, useRouter } from 'vue-router'
import { ElMessageBox } from 'element-plus'
import AppPage from '@/components/base/AppPage.vue'
import AppFormDrawer from '@/components/management/AppFormDrawer.vue'
import { getStudioApi, type Json, type StudioResource } from '@/api/studio'
import { initialPlaylistDocument, type PlaylistDocument } from './model'
import { useStudioCopy } from '@/components/studio/shared/copy'
import { useDashboardText } from '@/components/studio/dashboard/dashboardCopy'
import { listDashboardResources } from '@/components/studio/dashboard/designerResources'
import '@/components/studio/shared/studio.css'

const route = useRoute(),
  router = useRouter(),
  t = useStudioCopy(),
  dashboardT = useDashboardText()
const resource = ref<StudioResource | null>(null),
  documentValue = ref<PlaylistDocument>(initialPlaylistDocument()),
  dashboards = ref<StudioResource[]>([]),
  contracts = ref<Record<string, Json[]>>({}),
  saved = ref(''),
  error = ref(''),
  notice = ref(''),
  busy = ref(false),
  drawer = ref<'publish' | ''>(''),
  releaseNote = ref('')
const resourcesLoading = ref(false),
  resourcesError = ref('')
const parameterRequests = new Map<string, Promise<void>>()
let generation = 0
let disposed = false
async function loadResources(token = generation) {
  resourcesLoading.value = true
  resourcesError.value = ''
  try {
    const list = await listDashboardResources(['Dashboard'])
    if (disposed || token !== generation) return
    dashboards.value = list
    await Promise.all(documentValue.value.items.map(loadParameters))
  } catch (e) {
    if (!disposed && token === generation)
      resourcesError.value = e instanceof Error ? e.message : t.value('error')
  } finally {
    if (!disposed && token === generation) resourcesLoading.value = false
  }
}
async function load() {
  const token = ++generation
  busy.value = true
  error.value = ''
  void loadResources(token)
  try {
    const draft = await getStudioApi().draft(String(route.params.nId))
    if (disposed || token !== generation) return
    resource.value = draft.resource
    documentValue.value = {
      ...initialPlaylistDocument(),
      ...draft.draftDocument,
      items: Array.isArray(draft.draftDocument.items) ? draft.draftDocument.items : [],
    } as PlaylistDocument
    saved.value = JSON.stringify(documentValue.value)
    await Promise.all(documentValue.value.items.map(loadParameters))
  } catch (e) {
    if (!disposed && token === generation)
      error.value = e instanceof Error ? e.message : t.value('error')
  } finally {
    if (!disposed && token === generation) busy.value = false
  }
}
function addItem() {
  const item = {
    id: crypto.randomUUID(),
    dashboardNId: dashboards.value[0]?.nId ?? '',
    revisionNId: dashboards.value[0]?.publishedRevisionNId ?? '',
    parameterMap: [],
    staySeconds: 20,
    enabled: true,
    label: dashboards.value[0]?.name ?? '',
  }
  documentValue.value.items.push(item)
  void loadParameters(item)
}
function move(index: number, delta: number) {
  const target = index + delta
  if (target < 0 || target >= documentValue.value.items.length) return
  const [item] = documentValue.value.items.splice(index, 1)
  if (item) documentValue.value.items.splice(target, 0, item)
}
function remove(index: number) {
  documentValue.value.items.splice(index, 1)
}
async function removeParameter(parameter: Json) {
  const referencesParameter = (mapping: Json) =>
    mapping.source?.from === 'PageParam' && mapping.source.path?.[0] === parameter.name
  const count = documentValue.value.items.reduce(
    (total, item) => total + (item.parameterMap ?? []).filter(referencesParameter).length,
    0,
  )
  if (count) {
    try {
      await ElMessageBox.confirm(
        dashboardT.value('playlistRemoveParameterInUse', { parameter: parameter.name, count }),
        dashboardT.value('remove'),
        {
          type: 'warning',
          confirmButtonText: dashboardT.value('remove'),
          cancelButtonText: dashboardT.value('cancel'),
        },
      )
    } catch {
      return
    }
  }
  documentValue.value.parameters = documentValue.value.parameters.filter(
    (value) => value !== parameter,
  )
  for (const item of documentValue.value.items) {
    if (item.parameterMap)
      item.parameterMap = item.parameterMap.filter((mapping: Json) => !referencesParameter(mapping))
  }
}
async function dashboardChanged(item: Json) {
  const dashboard = dashboards.value.find((value) => value.nId === item.dashboardNId)
  item.revisionNId = dashboard?.publishedRevisionNId ?? ''
  item.parameterMap = []
  await loadParameters(item)
}
function parametersOf(item: Json): Json[] {
  const revision =
    item.revisionNId ||
    dashboards.value.find((value) => value.nId === item.dashboardNId)?.publishedRevisionNId
  return contracts.value[`${item.dashboardNId}/${revision}`] ?? []
}
async function loadParameters(item: Json) {
  const revision =
    item.revisionNId ||
    dashboards.value.find((value) => value.nId === item.dashboardNId)?.publishedRevisionNId
  if (!item.dashboardNId || !revision) return
  const key = `${item.dashboardNId}/${revision}`
  if (contracts.value[key]) return
  const pending = parameterRequests.get(key)
  if (pending) return pending
  const request = getStudioApi()
    .revision(item.dashboardNId, revision)
    .then((value) => {
      if (!disposed) contracts.value[key] = value.document.parameters ?? []
    })
    .catch((e) => {
      if (!disposed) error.value = e instanceof Error ? e.message : t.value('error')
    })
    .finally(() => {
      parameterRequests.delete(key)
    })
  parameterRequests.set(key, request)
  return request
}
function addMapping(item: Json) {
  item.parameterMap ??= []
  item.parameterMap.push({
    target:
      parametersOf(item).find(
        (parameter) =>
          !item.parameterMap.some((mapping: Json) => mapping.target === parameter.name),
      )?.name ?? '',
    source: { from: 'Constant', value: '', nullPolicy: 'Error' },
  })
}
function mappingType(item: Json, mapping: Json): string {
  return String(
    parametersOf(item).find((parameter) => parameter.name === mapping.target)?.type ?? 'String',
  )
}
async function save() {
  if (!resource.value) return
  busy.value = true
  error.value = ''
  try {
    const result = await getStudioApi().save(resource.value, documentValue.value)
    resource.value = result.resource
    documentValue.value = result.draftDocument as PlaylistDocument
    saved.value = JSON.stringify(documentValue.value)
    notice.value = dashboardT.value('playlistDraftSaved')
  } catch (e) {
    error.value = e instanceof Error ? e.message : t.value('error')
  } finally {
    busy.value = false
  }
}
async function publish() {
  if (!resource.value) return
  busy.value = true
  error.value = ''
  try {
    const result = await getStudioApi().validate(resource.value.nId, documentValue.value)
    if (!result.valid)
      throw new Error(
        (result.diagnostics as Json[])
          .map((item) => String(item.message ?? item.code ?? ''))
          .join('；') || dashboardT.value('validationFailed'),
      )
    drawer.value = 'publish'
  } catch (e) {
    error.value = e instanceof Error ? e.message : t.value('error')
  } finally {
    busy.value = false
  }
}
async function confirmPublish() {
  if (!resource.value || !releaseNote.value.trim()) return
  busy.value = true
  try {
    if (saved.value !== JSON.stringify(documentValue.value)) {
      const updated = await getStudioApi().save(resource.value, documentValue.value)
      resource.value = updated.resource
      documentValue.value = updated.draftDocument as PlaylistDocument
      saved.value = JSON.stringify(documentValue.value)
    }
    const result = await getStudioApi().validate(resource.value.nId, documentValue.value)
    if (!result.valid) throw new Error(dashboardT.value('validationNotPassed'))
    await getStudioApi().publish(resource.value, releaseNote.value, result.dependencyPlanHash)
    drawer.value = ''
    notice.value = dashboardT.value('playlistPublished')
    await load()
  } catch (e) {
    error.value = e instanceof Error ? e.message : t.value('error')
  } finally {
    busy.value = false
  }
}
onBeforeRouteLeave(async () => {
  if (!saved.value || saved.value === JSON.stringify(documentValue.value)) return true
  try {
    await ElMessageBox.confirm(t.value('unsavedLeave'), t.value('unsaved'))
    return true
  } catch {
    return false
  }
})
onMounted(load)
onBeforeUnmount(() => {
  disposed = true
  generation++
  parameterRequests.clear()
})
</script>

<template>
  <AppPage :title="resource?.name ?? dashboardT('playlistDesign')" class="studio-page"
    ><template #actions
      ><el-button @click="router.push('/pc/studio/playlists')">{{ dashboardT('back') }}</el-button
      ><el-button :disabled="busy" @click="save">{{ dashboardT('save') }}</el-button
      ><el-button type="primary" :disabled="busy" @click="publish">{{
        dashboardT('publish')
      }}</el-button></template
    ><el-alert v-if="error" :title="error" type="error" :closable="false" /><el-alert
      v-if="notice"
      :title="notice"
      type="success"
      :closable="false" />
    <el-alert v-if="resourcesError" :title="resourcesError" type="warning" :closable="false">
      <el-button link :disabled="resourcesLoading" @click="loadResources()">{{
        dashboardT('refresh')
      }}</el-button>
    </el-alert>
    <div v-if="!resource && busy" class="playlist-initial-loading" role="status">
      {{ dashboardT('loading') }}
    </div>
    <template v-if="resource">
      <div class="playlist-options">
        <el-switch v-model="documentValue.loop" :active-text="dashboardT('loop')" /><el-select
          v-model="documentValue.onFailure"
          style="width: 130px"
          ><el-option :label="dashboardT('failurePause')" value="Pause" /><el-option
            :label="dashboardT('failureSkip')"
            value="Skip" /></el-select
        ><span>{{ dashboardT('preload') }}</span
        ><el-input-number v-model="documentValue.preloadCount" :min="0" :max="1" />
      </div>
      <div class="playlist-options playlist-parameter-options">
        <strong>{{ dashboardT('playlistSharedParameters') }}</strong>
        <el-button
          link
          @click="
            documentValue.parameters.push({
              name: `parameter${documentValue.parameters.length + 1}`,
              type: 'String',
              required: false,
              nullable: true,
              source: 'Caller',
              defaultValue: '',
            })
          "
          >{{ dashboardT('addParameter') }}</el-button
        >
        <p class="playlist-hint">{{ dashboardT('playlistParametersHint') }}</p>
      </div>
      <div
        v-for="(parameter, index) in documentValue.parameters"
        :key="index"
        class="playlist-mapping playlist-parameter-row"
      >
        <el-input v-model="parameter.name" :placeholder="dashboardT('parameterName')" />
        <el-select v-model="parameter.type" :placeholder="dashboardT('parameterType')"
          ><el-option
            v-for="kind in [
              'String',
              'Int32',
              'Int64',
              'Decimal',
              'Boolean',
              'Date',
              'DateTime',
              'Guid',
            ]"
            :key="kind"
            :value="kind"
            :label="kind"
        /></el-select>
        <el-switch v-if="parameter.type === 'Boolean'" v-model="parameter.defaultValue" />
        <el-input-number
          v-else-if="['Int32', 'Decimal'].includes(parameter.type)"
          v-model="parameter.defaultValue"
        />
        <el-input
          v-else
          v-model="parameter.defaultValue"
          :placeholder="dashboardT('defaultValue')"
        />
        <el-switch v-model="parameter.required" :active-text="dashboardT('parameterRequired')" />
        <el-button link type="danger" :disabled="busy" @click="removeParameter(parameter)">{{
          dashboardT('remove')
        }}</el-button>
      </div>
      <el-empty v-if="!documentValue.items.length" :description="dashboardT('noPlaylistItems')"
        ><el-button
          type="primary"
          :disabled="resourcesLoading || !dashboards.length"
          @click="addItem"
          >{{ dashboardT('addFirstDashboard') }}</el-button
        ></el-empty
      >
      <div v-for="(item, index) in documentValue.items" :key="item.id" class="playlist-item">
        <div class="playlist-order">
          <strong>{{ index + 1 }}</strong
          ><el-button link :disabled="index === 0" @click="move(index, -1)">{{
            dashboardT('moveUp')
          }}</el-button
          ><el-button
            link
            :disabled="index === documentValue.items.length - 1"
            @click="move(index, 1)"
            >{{ dashboardT('moveDown') }}</el-button
          >
        </div>
        <el-form label-position="top" class="playlist-form"
          ><el-form-item :label="dashboardT('dashboard')"
            ><el-select
              v-model="item.dashboardNId"
              filterable
              :loading="resourcesLoading"
              :disabled="resourcesLoading"
              @change="dashboardChanged(item)"
              ><el-option
                v-if="!dashboards.some((d) => d.nId === item.dashboardNId)"
                :value="item.dashboardNId"
                :label="resourcesLoading ? dashboardT('loading') : dashboardT('dashboard')"
                disabled />
              <el-option
                v-for="dashboard in dashboards"
                :key="dashboard.nId"
                :label="dashboard.name"
                :value="dashboard.nId" /></el-select></el-form-item
          ><el-form-item :label="dashboardT('publishedRevision')"
            ><el-input
              v-model="item.revisionNId"
              @change="loadParameters(item)"
              :placeholder="dashboardT('publishAutoFixed')" /></el-form-item
          ><el-form-item :label="dashboardT('staySeconds')"
            ><el-input-number v-model="item.staySeconds" :min="3" :max="3600" /></el-form-item
          ><el-form-item :label="dashboardT('label')"
            ><el-input v-model="item.label" /></el-form-item
          ><el-form-item :label="dashboardT('enabled')"
            ><el-switch v-model="item.enabled" /></el-form-item></el-form
        ><el-button type="danger" link @click="remove(index)">{{ dashboardT('delete') }}</el-button>
        <div class="playlist-parameters">
          <strong>{{ dashboardT('targetParameterMapping') }}</strong>
          <el-button link @click="addMapping(item)">{{ dashboardT('addBinding') }}</el-button>
          <small v-for="parameter in parametersOf(item)" :key="parameter.name"
            >{{ parameter.label || parameter.name }} · {{ parameter.name }} · {{ parameter.type
            }}{{ parameter.required ? ' *' : '' }}</small
          >
          <div
            v-for="(mapping, mappingIndex) in item.parameterMap"
            :key="mappingIndex"
            class="playlist-mapping"
          >
            <el-select v-model="mapping.target" :placeholder="dashboardT('targetParameter')">
              <el-option
                v-for="parameter in parametersOf(item)"
                :key="parameter.name"
                :value="parameter.name"
                :label="`${parameter.name} · ${parameter.type}`"
              />
            </el-select>
            <el-select v-model="mapping.source.from">
              <el-option value="Constant" :label="dashboardT('constant')" />
              <el-option value="PageParam" :label="dashboardT('pageParameter')" />
            </el-select>
            <el-select
              v-if="mapping.source.from === 'PageParam'"
              :model-value="mapping.source.path?.[0]"
              filterable
              allow-create
              :placeholder="dashboardT('parameterName')"
              @update:model-value="(value: string) => (mapping.source.path = [value])"
            >
              <el-option
                v-for="parameter in documentValue.parameters"
                :key="parameter.name"
                :value="parameter.name"
                :label="parameter.label || parameter.name"
              />
            </el-select>
            <el-switch
              v-else-if="mappingType(item, mapping) === 'Boolean'"
              v-model="mapping.source.value"
            />
            <el-input-number
              v-else-if="['Int32', 'Decimal'].includes(mappingType(item, mapping))"
              v-model="mapping.source.value"
            />
            <el-input
              v-else
              v-model="mapping.source.value"
              :placeholder="dashboardT('constantValue')"
            />
            <el-button link type="danger" @click="item.parameterMap.splice(mappingIndex, 1)">{{
              dashboardT('remove')
            }}</el-button>
          </div>
        </div>
      </div>
      <el-button
        v-if="documentValue.items.length"
        class="playlist-add"
        :disabled="resourcesLoading || !dashboards.length"
        @click="addItem"
        >{{ dashboardT('addDashboard') }}</el-button
      ><AppFormDrawer
        :model-value="drawer === 'publish'"
        :title="dashboardT('publish')"
        :busy="busy"
        @update:model-value="(v) => !v && (drawer = '')"
        @submit="confirmPublish"
        ><el-form label-position="top"
          ><el-form-item :label="dashboardT('publishNote')" required
            ><el-input
              v-model="releaseNote"
              type="textarea" /></el-form-item></el-form></AppFormDrawer></template
  ></AppPage>
</template>

<style scoped>
.playlist-initial-loading {
  padding: 48px;
  text-align: center;
  color: var(--el-text-color-secondary);
}
.playlist-options {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 12px;
  margin-bottom: 12px;
  border: 1px solid var(--el-border-color);
  background: var(--el-bg-color);
}
.playlist-parameters {
  grid-column: 1 / -1;
}
.playlist-parameter-options {
  flex-wrap: wrap;
}
.playlist-hint {
  flex-basis: 100%;
  margin: 0;
  color: var(--el-text-color-secondary);
  font-size: 13px;
  line-height: 1.6;
}
.playlist-parameters small {
  display: block;
  margin: 4px 0;
}
.playlist-mapping {
  display: grid;
  grid-template-columns: 1fr 1fr 1fr auto;
  gap: 8px;
  margin-top: 8px;
}
.playlist-item {
  display: grid;
  grid-template-columns: 100px minmax(0, 1fr) 60px;
  gap: 12px;
  align-items: end;
  padding: 16px;
  margin: 10px 0;
  border: 1px solid var(--el-border-color);
  border-radius: 6px;
  background: var(--el-bg-color);
}
.playlist-parameter-row {
  grid-template-columns: 1fr 1fr 1fr auto auto;
  align-items: center;
}
.playlist-order {
  display: flex;
  flex-direction: column;
  gap: 6px;
}
.playlist-form {
  display: grid;
  grid-template-columns: 2fr 2fr 1fr 1fr 80px;
  gap: 10px;
}
.playlist-form .el-form-item {
  margin-bottom: 0;
}
.playlist-add {
  margin-top: 8px;
}
@media (max-width: 900px) {
  .playlist-item {
    grid-template-columns: 1fr;
  }
  .playlist-form {
    grid-template-columns: 1fr 1fr;
  }
}
</style>
