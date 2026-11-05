<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'
import type { StudioResource } from '@/api/studio'
import { getTraceApi, type TraceLinkSources } from '@/api/studioTrace'
import BindingEditor from '../shared/BindingEditor.vue'
import TraceDatasetPicker from './TraceDatasetPicker.vue'
import TraceFields from './TraceFields.vue'
import TraceIdentityEditor from './TraceIdentityEditor.vue'
import TraceMappings from './TraceMappings.vue'
import TraceSectionsEditor from './TraceSectionsEditor.vue'
import { rowBinding, traceContexts, type TraceObject } from './model'
import { useTraceCopy } from './copy'
import { useTracePresentation } from './presentation'
const model = defineModel<TraceObject>({ required: true })
defineProps<{ datasets: StudioResource[] }>()
const activeTab = ref('basic')
const t = useTraceCopy()
const text = useTracePresentation()
const sources = ref<TraceLinkSources>(),
  sourceError = ref('')
let sourceGeneration = 0
async function loadSources() {
  const current = ++sourceGeneration
  sourceError.value = ''
  try {
    const value = await getTraceApi().linkSources()
    if (current === sourceGeneration) sources.value = value
  } catch (e) {
    if (current === sourceGeneration) sourceError.value = e instanceof Error ? e.message : String(e)
  }
}
onMounted(loadSources)
onBeforeUnmount(() => {
  sourceGeneration++
})
const externalSource = (key: string | undefined) =>
  sources.value?.external.find((s) => s.key === key)
const mesSource = (key: string | undefined) => sources.value?.mes.find((s) => s.key === key)
function addLink() {
  model.value.detail.links.push({
    id: crypto.randomUUID(),
    label: '',
    kind: 'File',
    value: rowBinding(),
    openMode: 'Modal',
    fileSource: { mode: 'Owned' },
  })
}
</script>
<template>
  <el-tabs v-model="activeTab" class="trace-property-tabs" stretch>
    <el-tab-pane name="basic" :label="t('basic')">
      <label>{{ t('name') }}<el-input v-model="model.name" :aria-label="t('name')" /></label>
      <details class="trace-technical-id">
        <summary>{{ t('stableId') }}</summary>
        <small>{{ model.id }}</small>
      </details>
      <label
        >{{ t('grain')
        }}<el-select v-model="model.identity.grain" :aria-label="t('grain')"
          ><el-option
            v-for="v in ['Batch', 'Package', 'Serial', 'WorkOrder', 'Operation', 'Custom']"
            :key="v"
            :value="v"
            :label="text.option(v)" /></el-select
      ></label>
      <fieldset>
        <legend>{{ t('style') }}</legend>
        <div class="trace-size-fields">
          <label
            >{{ t('width')
            }}<el-input-number v-model="model.style.width" :min="1" :max="10000" /></label
          ><label
            >{{ t('height') }}<el-input-number v-model="model.style.height" :min="1" :max="10000"
          /></label>
        </div>
      </fieldset>
    </el-tab-pane>
    <el-tab-pane name="identity" :label="t('identityTab')">
      <TraceIdentityEditor v-model="model.identity" />
    </el-tab-pane>
    <el-tab-pane name="summary" :label="t('summary')">
      <fieldset>
        <legend>{{ t('summary') }}</legend>
        <TraceFields v-model="model.summary" />
      </fieldset>
    </el-tab-pane>
    <el-tab-pane name="detail" :label="t('detail')">
      <fieldset>
        <legend>{{ t('detail') }}</legend>
        <TraceDatasetPicker
          v-model="model.detail.datasetRevisionNId"
          :datasets="datasets"
        /><TraceMappings v-model="model.detail.parameterMap" /><TraceFields
          v-model="model.detail.fields"
        />
      </fieldset>
    </el-tab-pane>
    <el-tab-pane name="sections" :label="t('detailSections')">
      <TraceSectionsEditor
        :model-value="model.detail.sections ?? []"
        :datasets="datasets"
        @update:model-value="(value) => (model.detail.sections = value)"
      />
    </el-tab-pane>
    <el-tab-pane name="links" :label="t('attachments')">
      <fieldset>
        <legend>{{ t('links') }}</legend>
        <p>{{ t('filePolicy') }}</p>
        <el-alert v-if="sourceError" :title="sourceError" type="warning" :closable="false"
          ><el-button @click="loadSources">{{ t('retry') }}</el-button></el-alert
        >
        <div v-for="(link, i) in model.detail.links" :key="link.id" class="studio-field-block">
          <el-input v-model="link.label" :aria-label="t('label')" :placeholder="t('label')" />
          <el-select
            v-model="link.kind"
            :aria-label="t('kind')"
            @change="
              () => {
                if (link.kind === 'External') {
                  delete link.fileSource
                  link.allowedOriginKey = ''
                } else {
                  delete link.allowedOriginKey
                  link.fileSource = { mode: 'Owned' }
                }
              }
            "
            ><el-option value="File" :label="t('links')" /><el-option
              value="External"
              :label="t('external')"
          /></el-select>
          <BindingEditor v-model="link.value" :contexts="traceContexts" />
          <template v-if="link.kind === 'External'"
            ><el-select
              v-model="link.allowedOriginKey"
              filterable
              :aria-label="t('origin')"
              :placeholder="t('origin')"
              ><el-option
                v-for="source in sources?.external ?? []"
                :key="source.key"
                :value="source.key"
                :label="source.key"
            /></el-select>
            <small v-if="!externalSource(link.allowedOriginKey)?.embeddable">{{
              t('blockedFrame')
            }}</small>
            <small v-else>{{
              externalSource(link.allowedOriginKey)?.allowedParameters.join(', ')
            }}</small>
            <TraceMappings
              :model-value="link.parameterMap ?? []"
              @update:model-value="(v) => (link.parameterMap = v)"
          /></template>
          <template v-else-if="link.fileSource"
            ><el-select v-model="link.fileSource.mode" :aria-label="t('kind')"
              ><el-option
                value="Owned"
                :label="t('owned')"
                :disabled="!sources?.ownedAvailable" /><el-option
                value="Mes"
                :label="t('mes')" /></el-select
            ><template v-if="link.fileSource.mode === 'Mes'"
              ><el-select
                v-model="link.fileSource.sourceKey"
                filterable
                :aria-label="t('sourceKey')"
                :placeholder="t('sourceKey')"
                ><el-option
                  v-for="source in sources?.mes ?? []"
                  :key="source.key"
                  :value="source.key"
                  :label="`${source.key} · ${text.option(source.scanPolicy)}`"
                  :disabled="!source.available"
              /></el-select>
              <small v-if="mesSource(link.fileSource.sourceKey)"
                >{{ text.option(mesSource(link.fileSource.sourceKey)?.scanPolicy ?? '') }} ·
                {{ mesSource(link.fileSource.sourceKey)?.allowedContentTypes.join(', ') }} ·
                {{ mesSource(link.fileSource.sourceKey)?.maxBytes }} {{ t('bytes') }}
                <span v-if="!mesSource(link.fileSource.sourceKey)?.available">{{
                  t('blocked')
                }}</span></small
              >
              <label
                >{{ t('version')
                }}<BindingEditor
                  :model-value="link.fileSource.version ?? rowBinding()"
                  :contexts="traceContexts"
                  @update:model-value="(v) => (link.fileSource!.version = v)" /></label></template
          ></template>
          <el-button :aria-label="t('remove')" @click="model.detail.links.splice(i, 1)"
            >−</el-button
          >
        </div>
        <el-button @click="addLink">{{ t('add') }}</el-button>
      </fieldset>
    </el-tab-pane>
  </el-tabs>
</template>
