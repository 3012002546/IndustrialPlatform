<script setup lang="ts">
import { computed } from 'vue'
import type { Json, StudioResource } from '@/api/studio'
import { useDashboardText } from './dashboardCopy'
import DashboardStructuredEditor from './DashboardStructuredEditor.vue'
import DashboardConstantEditor from './DashboardConstantEditor.vue'
import { clone } from '@/components/studio/shared/model'

const props = defineProps<{
  modelValue: Json
  datasets: StudioResource[]
  contracts: Record<string, Json>
  instances: Json[]
  pageParameters: Json[]
  apiOnly?: boolean
  canManageDatasets?: boolean
}>()
const emit = defineEmits<{
  'update:modelValue': [value: Json]
  datasetChange: [value: Json]
  manageDatasets: []
}>()
const t = useDashboardText()
const source = computed(() => props.modelValue)
const kind = computed(() => source.value.sourceType ?? 'Dataset')
const contract = computed(
  () => props.contracts[`${source.value.datasetNId}/${source.value.revisionNId}`] ?? {},
)
const datasets = computed(() =>
  props.datasets.filter((dataset) => {
    if (kind.value !== 'Api') return true
    const query = props.contracts[`${dataset.nId}/${dataset.publishedRevisionNId}`]?.query
    return !query || (query.kind ?? query.type) === 'Api'
  }),
)
function set(key: string, value: unknown) {
  emit('update:modelValue', { ...source.value, [key]: value })
}
function setKind(value: unknown) {
  emit('update:modelValue', {
    ...source.value,
    sourceType: value,
    refreshSeconds: value === 'Constant' ? 0 : 30,
    ...(value === 'Constant' && !('value' in source.value) ? { value: [] } : {}),
    ...(value === 'Constant' ? { parameters: [], dependsOn: [] } : {}),
    ...(value === 'Api' ? { datasetNId: '', revisionNId: '' } : {}),
  })
}
function selectDataset(value: unknown) {
  const next = {
    ...source.value,
    datasetNId: value,
    revisionNId: props.datasets.find((item) => item.nId === value)?.publishedRevisionNId ?? '',
    parameters: [],
  }
  emit('update:modelValue', next)
  emit('datasetChange', next)
}
function addParameter() {
  set('parameters', [
    ...(source.value.parameters ?? []),
    { target: '', source: { from: 'Constant', value: '', nullPolicy: 'Error' } },
  ])
}
function parameter(index: number, key: string, value: unknown) {
  const next = clone(source.value.parameters ?? []) as Json[]
  const item = next[index]!
  if (key === 'target') item.target = value
  else {
    item.source[key] = value
    if (key === 'from') {
      item.source.path = []
      item.source.value = ''
    }
  }
  set('parameters', next)
}
const parameterRows = computed(() => (source.value.parameters as Json[]) ?? [])
</script>
<template>
  <el-form label-position="top" class="dashboard-source-editor">
    <el-form-item :label="t('sourceType')">
      <el-select
        :model-value="kind"
        :disabled="apiOnly"
        :aria-label="t('sourceType')"
        @update:model-value="setKind"
      >
        <el-option value="Dataset" :label="t('datasetSource')" />
        <el-option value="Api" :label="t('apiSource')" />
        <el-option value="Constant" :label="t('constant')" />
      </el-select>
    </el-form-item>
    <el-form-item :label="t('sourceName')"
      ><el-input :model-value="source.label" @update:model-value="set('label', $event)"
    /></el-form-item>
    <template v-if="kind === 'Constant'">
      <DashboardConstantEditor
        :model-value="source.value"
        @update:model-value="set('value', $event)"
      />
      <p class="source-hint">{{ t('sharedConstantHint') }}</p>
    </template>
    <template v-else>
      <p class="source-hint">{{ t(apiOnly ? 'independentApiHint' : 'sharedApiHint') }}</p>
      <template v-if="kind === 'Api'">
        <p class="source-hint">{{ t('apiDatasetOriginHint') }}</p>
        <el-button v-if="canManageDatasets" size="small" @click="emit('manageDatasets')">{{
          t('manageApiDatasets')
        }}</el-button>
      </template>
      <el-form-item :label="kind === 'Api' ? t('apiDataset') : t('datasetNId')">
        <el-select
          :model-value="source.datasetNId"
          filterable
          :aria-label="t('datasetNId')"
          @update:model-value="selectDataset"
        >
          <el-option
            v-for="item in datasets"
            :key="item.nId"
            :value="item.nId"
            :label="item.name"
          />
        </el-select>
      </el-form-item>
      <el-form-item :label="t('fixedRevisionNId')"
        ><el-input
          :model-value="source.revisionNId"
          @change="
            ($event: string) => {
              set('revisionNId', $event)
              emit('datasetChange', { ...source, revisionNId: $event })
            }
          "
      /></el-form-item>
      <div class="source-timing">
        <el-form-item :label="t('readIntervalSeconds')"
          ><el-input-number
            :model-value="source.refreshSeconds ?? 0"
            :min="0"
            :max="86400"
            :precision="0"
            :aria-label="t('readIntervalSeconds')"
            @update:model-value="set('refreshSeconds', $event ?? 0)"
        /></el-form-item>
        <el-form-item :label="t('staleSeconds')"
          ><el-input-number
            :model-value="source.staleAfterSeconds ?? 300"
            :min="1"
            :max="86400"
            :precision="0"
            @update:model-value="set('staleAfterSeconds', $event ?? 300)"
        /></el-form-item>
      </div>
      <p class="source-hint">{{ t('sourceRefreshHint') }}</p>
      <p v-if="kind === 'Api'" class="source-hint">{{ t('apiSourceHint') }}</p>
      <el-collapse>
        <el-collapse-item
          name="parameters"
          :title="`${t('instanceParameterMapping')} (${parameterRows.length})`"
        >
          <el-button size="small" @click="addParameter">{{ t('add') }}</el-button>
          <el-collapse accordion>
            <el-collapse-item
              v-for="(mapping, index) in parameterRows"
              :key="index"
              :title="mapping.target || t('datasetParameter')"
              :name="index"
            >
              <el-form-item :label="t('datasetParameter')"
                ><el-select
                  :model-value="mapping.target"
                  filterable
                  allow-create
                  @update:model-value="parameter(index, 'target', $event)"
                  ><el-option
                    v-for="item in contract.parameters ?? []"
                    :key="item.name"
                    :value="item.name"
                    :label="item.label || item.name" /></el-select
              ></el-form-item>
              <el-form-item :label="t('sourceType')"
                ><el-select
                  :model-value="mapping.source.from"
                  @update:model-value="parameter(index, 'from', $event)"
                  ><el-option
                    v-for="item in ['Constant', 'PageParam', 'Instance', 'Context']"
                    :key="item"
                    :value="item"
                    :label="
                      t(
                        {
                          Constant: 'constant',
                          PageParam: 'pageParameter',
                          Instance: 'dataInstanceTitle',
                          Context: 'trustedContext',
                        }[item],
                      )
                    " /></el-select
              ></el-form-item>
              <el-form-item v-if="mapping.source.from === 'Instance'" :label="t('instance')"
                ><el-select
                  :model-value="mapping.source.instanceId"
                  @update:model-value="parameter(index, 'instanceId', $event)"
                  ><el-option
                    v-for="item in instances.filter(
                      (item) => item.instanceId !== source.instanceId,
                    )"
                    :key="item.instanceId"
                    :value="item.instanceId"
                    :label="item.label || item.instanceId" /></el-select
              ></el-form-item>
              <el-form-item v-if="mapping.source.from !== 'Constant'" :label="t('fieldPath')"
                ><el-select
                  :model-value="(mapping.source.path ?? []).join('.')"
                  filterable
                  allow-create
                  @update:model-value="
                    parameter(index, 'path', String($event).split('.').filter(Boolean))
                  "
                  ><el-option
                    v-for="path in mapping.source.from === 'PageParam'
                      ? pageParameters.map((item) => item.name)
                      : mapping.source.from === 'Context'
                        ? ['tenant', 'user', 'authorizedFactoryIds']
                        : []"
                    :key="path"
                    :value="path"
                    :label="path" /></el-select
              ></el-form-item>
              <DashboardStructuredEditor
                v-else
                kind="rows"
                :model-value="[{ value: mapping.source.value }]"
                @update:model-value="parameter(index, 'value', ($event[0] as Json)?.value)"
              />
              <el-button
                type="danger"
                link
                @click="
                  set(
                    'parameters',
                    parameterRows.filter((_, i) => i !== index),
                  )
                "
                >{{ t('remove') }}</el-button
              >
            </el-collapse-item>
          </el-collapse>
        </el-collapse-item>
      </el-collapse>
    </template>
  </el-form>
</template>
<style scoped>
.dashboard-source-editor :deep(.el-select) {
  width: 100%;
}
.source-timing {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(145px, 1fr));
  gap: 12px;
}
.source-hint {
  color: var(--el-text-color-secondary);
  line-height: 1.6;
  font-size: 12px;
  margin: 4px 0 12px;
}
</style>
