<script setup lang="ts">
import { ref } from 'vue'
import type { StudioResource } from '@/api/studio'
import TraceDatasetPicker from './TraceDatasetPicker.vue'
import TraceFields from './TraceFields.vue'
import TraceIdentityEditor from './TraceIdentityEditor.vue'
import TraceMappings from './TraceMappings.vue'
import TraceSectionsEditor from './TraceSectionsEditor.vue'
import TraceLinksEditor from './TraceLinksEditor.vue'
import type { TraceObject } from './model'
import { useTraceCopy } from './copy'
import { useTracePresentation } from './presentation'
const model = defineModel<TraceObject>({ required: true })
defineProps<{ datasets: StudioResource[] }>()
const activeTab = ref('basic')
const t = useTraceCopy()
const text = useTracePresentation()
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
      <TraceLinksEditor v-model="model.detail.links" />
    </el-tab-pane>
  </el-tabs>
</template>
