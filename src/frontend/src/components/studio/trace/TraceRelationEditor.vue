<script setup lang="ts">
import { ref } from 'vue'
import type { StudioResource } from '@/api/studio'
import BindingEditor from '../shared/BindingEditor.vue'
import TraceDatasetPicker from './TraceDatasetPicker.vue'
import TraceFields from './TraceFields.vue'
import TraceIdentityEditor from './TraceIdentityEditor.vue'
import TraceMappings from './TraceMappings.vue'
import { rowBinding, traceContexts, type TraceObject, type TraceRelation } from './model'
import { useTraceCopy } from './copy'
import { useTracePresentation } from './presentation'
const model = defineModel<TraceRelation>({ required: true })
defineProps<{ datasets: StudioResource[]; objects: TraceObject[] }>()
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
        >{{ t('from')
        }}<el-select v-model="model.fromTypeId" :aria-label="t('from')"
          ><el-option
            v-for="(o, i) in objects"
            :key="o.id"
            :value="o.id"
            :label="text.itemName(o, i)" /></el-select
      ></label>
      <label
        >{{ t('to')
        }}<el-select v-model="model.toTypeId" :aria-label="t('to')"
          ><el-option
            v-for="(o, i) in objects"
            :key="o.id"
            :value="o.id"
            :label="text.itemName(o, i)" /></el-select
      ></label>
      <label
        >{{ t('kind')
        }}<el-select v-model="model.kind" :aria-label="t('kind')"
          ><el-option
            v-for="v in ['Genealogy', 'Context']"
            :key="v"
            :value="v"
            :label="text.option(v)" /></el-select></label
      ><small v-if="model.kind === 'Context'">{{ t('contextHint') }}</small>
      <label
        >{{ t('semantic')
        }}<el-select v-model="model.semantic" :aria-label="t('semantic')"
          ><el-option
            v-for="v in [
              'Consume',
              'Produce',
              'Split',
              'Merge',
              'Ship',
              'Use',
              'Inspect',
              'Custom',
            ]"
            :key="v"
            :value="v"
            :label="text.option(v)" /></el-select
      ></label>
    </el-tab-pane>
    <el-tab-pane
      v-for="direction in ['forward', 'reverse'] as const"
      :key="direction"
      :name="direction"
      :label="t(direction)"
    >
      <TraceDatasetPicker
        v-model="model[direction].datasetRevisionNId"
        :datasets="datasets"
      /><TraceMappings v-model="model[direction].inputMap" />
      <label
        >{{ t('batch')
        }}<el-input v-model="model[direction].batchParameterName" :aria-label="t('batch')"
      /></label>
      <label
        >{{ t('collection')
        }}<el-input
          :model-value="model[direction].result.collectionPath.join('.')"
          :aria-label="t('collection')"
          @update:model-value="
            (v: string) => (model[direction].result.collectionPath = v.split('.').filter(Boolean))
          "
      /></label>
      <label
        >{{ t('result')
        }}<el-select v-model="model[direction].result.targetObjectTypeId" :aria-label="t('result')"
          ><el-option
            v-for="(o, i) in objects"
            :key="o.id"
            :value="o.id"
            :label="text.itemName(o, i)" /></el-select
      ></label>
      <TraceIdentityEditor v-model="model[direction].result.targetIdentity" /><TraceFields
        v-model="model[direction].result.summaryMap"
      />
    </el-tab-pane>
    <el-tab-pane name="evidence" :label="t('evidence')">
      <label
        >{{ t('identity') }}<BindingEditor v-model="model.identity" :contexts="traceContexts"
      /></label>
      <label
        >{{ t('label') }}<BindingEditor v-model="model.label" :contexts="traceContexts"
      /></label>
      <fieldset>
        <legend>{{ t('evidence') }}</legend>
        <label v-for="key in ['recordKey', 'occurredOn', 'quantity', 'unit'] as const" :key="key"
          >{{ t(key)
          }}<BindingEditor
            :model-value="model.evidence[key] ?? rowBinding()"
            :contexts="traceContexts"
            @update:model-value="(v) => (model.evidence[key] = v)"
        /></label>
      </fieldset>
    </el-tab-pane>
  </el-tabs>
</template>
