<script setup lang="ts">
import { ref } from 'vue'
import type { StudioResource } from '@/api/studio'
import BindingEditor from '../shared/BindingEditor.vue'
import TraceDatasetPicker from './TraceDatasetPicker.vue'
import TraceIdentityEditor from './TraceIdentityEditor.vue'
import TraceMappings from './TraceMappings.vue'
import { traceContexts, type TraceDocument, type TraceLayoutDirection } from './model'
import { useTraceCopy } from './copy'
import { useTracePresentation } from './presentation'
const model = defineModel<TraceDocument>({ required: true })
defineProps<{ datasets: StudioResource[] }>()
const activeTab = ref('basic')
const t = useTraceCopy()
const text = useTracePresentation()
const split = (value: string) =>
  value
    .split(',')
    .map((v) => v.trim())
    .filter(Boolean)
</script>
<template>
  <el-tabs v-model="activeTab" class="trace-property-tabs" stretch>
    <el-tab-pane name="presentation" :label="t('presentationTab')">
      <label
        >{{ t('defaultLayout') }}
        <el-select
          :model-value="model.presentation?.layoutDirection ?? 'LeftToRight'"
          :aria-label="t('defaultLayout')"
          @update:model-value="
            (value: TraceLayoutDirection) => (model.presentation = { layoutDirection: value })
          "
        >
          <el-option
            v-for="value in ['LeftToRight', 'TopToBottom'] as const"
            :key="value"
            :value="value"
            :label="t(value)"
          />
        </el-select>
      </label>
      <p>{{ t('layoutHint') }}</p>
    </el-tab-pane>
    <el-tab-pane name="basic" :label="t('dataTab')">
      <TraceDatasetPicker v-model="model.start.datasetRevisionNId" :datasets="datasets" />
      <label
        >{{ t('collection')
        }}<el-input
          :model-value="model.start.collectionPath.join('.')"
          :aria-label="t('collection')"
          @update:model-value="
            (v: string) => (model.start.collectionPath = v.split('.').filter(Boolean))
          "
      /></label>
      <label
        >{{ t('selectTypes')
        }}<el-select v-model="model.start.objectTypeIds" multiple :aria-label="t('selectTypes')"
          ><el-option
            v-for="(o, i) in model.objects"
            :key="o.id"
            :value="o.id"
            :label="text.itemName(o, i)" /></el-select
      ></label>
    </el-tab-pane>
    <el-tab-pane name="identity" :label="t('identityTab')">
      <label
        >{{ t('rowKey') }}<BindingEditor v-model="model.start.rowKey" :contexts="traceContexts"
      /></label>
      <label
        >{{ t('objectType')
        }}<BindingEditor v-model="model.start.objectType" :contexts="traceContexts"
      /></label>
      <TraceIdentityEditor v-model="model.start.identityMap" />
    </el-tab-pane>
    <el-tab-pane name="parameters" :label="t('parametersTab')">
      <fieldset>
        <legend>{{ t('mapping') }}</legend>
        <TraceMappings v-model="model.start.parameterMap" />
      </fieldset>
      <fieldset>
        <legend>{{ t('parameters') }}</legend>
        <div v-for="(p, i) in model.parameters" :key="i" class="studio-field-block">
          <el-input v-model="p.name" :aria-label="t('name')" :placeholder="t('name')" />
          <el-input v-model="p.label" :aria-label="t('label')" :placeholder="t('label')" />
          <el-select v-model="p.type" :aria-label="t('type')"
            ><el-option
              v-for="v in [
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
              :key="v"
              :value="v"
              :label="text.option(v)"
          /></el-select>
          <el-checkbox v-model="p.required">{{ t('required') }}</el-checkbox>
          <el-select v-model="p.source" :aria-label="t('parameterSource')"
            ><el-option value="Caller" :label="text.option('Caller')" /><el-option
              value="TrustedContext"
              :label="text.option('TrustedContext')"
          /></el-select>
          <el-select
            v-if="p.source === 'TrustedContext'"
            v-model="p.contextKey"
            :aria-label="t('sourceKey')"
            ><el-option
              v-for="key in ['tenant', 'user', 'authorizedFactoryIds']"
              :key="key"
              :value="key"
              :label="text.option(key)"
          /></el-select>
          <el-select v-if="p.type === 'Array'" v-model="p.itemType" :aria-label="t('type')"
            ><el-option
              v-for="type in ['String', 'Int32', 'Int64', 'Decimal', 'Guid']"
              :key="type"
              :value="type"
              :label="text.option(type)"
          /></el-select>
          <label
            >{{ t('defaultValue')
            }}<BindingEditor
              :model-value="{ from: 'Constant', value: p.defaultValue ?? null }"
              :contexts="['Constant']"
              @update:model-value="(v) => (p.defaultValue = v.value)"
          /></label>
          <el-button :aria-label="t('remove')" @click="model.parameters.splice(i, 1)">−</el-button>
        </div>
        <el-button
          @click="
            model.parameters.push({
              name: '',
              label: '',
              type: 'String',
              required: false,
              nullable: true,
              source: 'Caller',
            })
          "
          >{{ t('add') }}</el-button
        >
      </fieldset>
    </el-tab-pane>
    <el-tab-pane name="search" :label="t('searchTab')">
      <fieldset>
        <legend>{{ t('searchFields') }}</legend>
        <div v-for="(f, i) in model.start.searchFields" :key="i" class="studio-field-block">
          <el-select v-model="f.parameterName" filterable allow-create :aria-label="t('parameters')"
            ><el-option v-for="p in model.parameters" :key="p.name" :value="p.name" :label="p.name"
          /></el-select>
          <el-input v-model="f.label" :aria-label="t('label')" :placeholder="t('label')" />
          <el-select v-model="f.control" :aria-label="t('control')"
            ><el-option
              v-for="v in ['Text', 'Select', 'DateRange', 'BusinessSelector']"
              :key="v"
              :value="v"
              :label="text.option(v)"
          /></el-select>
          <el-checkbox v-model="f.required">{{ t('required') }}</el-checkbox>
          <template v-if="f.control === 'Select' || f.control === 'BusinessSelector'"
            ><TraceDatasetPicker
              :model-value="f.optionsDatasetRevisionNId ?? ''"
              :datasets="datasets"
              @update:model-value="(v) => (f.optionsDatasetRevisionNId = v)" /><el-input
              v-model="f.valueFieldId"
              :aria-label="t('valueField')"
              :placeholder="t('valueField')" /><el-input
              v-model="f.labelFieldId"
              :aria-label="t('labelField')"
              :placeholder="t('labelField')"
          /></template>
          <el-button :aria-label="t('remove')" @click="model.start.searchFields.splice(i, 1)"
            >−</el-button
          >
        </div>
        <el-button
          @click="
            model.start.searchFields.push({
              parameterName: '',
              label: '',
              control: 'Text',
              required: false,
            })
          "
          >{{ t('add') }}</el-button
        >
      </fieldset>
    </el-tab-pane>
    <el-tab-pane name="columns" :label="t('columnsTab')">
      <fieldset>
        <legend>{{ t('columns') }}</legend>
        <div v-for="(c, i) in model.start.columns" :key="i" class="studio-field-block">
          <el-input
            v-model="c.fieldId"
            :aria-label="t('field')"
            :placeholder="t('field')"
          /><el-input
            v-model="c.label"
            :aria-label="t('label')"
            :placeholder="t('label')"
          /><el-input-number
            v-model="c.width"
            :min="40"
            :max="1000"
            :aria-label="t('width')"
          /><el-checkbox v-model="c.visible">{{ t('visible') }}</el-checkbox
          ><el-button :aria-label="t('remove')" @click="model.start.columns.splice(i, 1)"
            >−</el-button
          >
        </div>
        <el-button
          @click="model.start.columns.push({ fieldId: '', label: '', width: 160, visible: true })"
          >{{ t('add') }}</el-button
        >
      </fieldset>
    </el-tab-pane>
    <el-tab-pane name="scope" :label="t('scopeTab')">
      <label
        >{{ t('factories')
        }}<el-input
          :model-value="model.scope.factoryIds.join(',')"
          :aria-label="t('factories')"
          @update:model-value="(v: string) => (model.scope.factoryIds = split(v))"
      /></label>
      <label
        >{{ t('tags')
        }}<el-input
          :model-value="model.scope.industryTags.join(',')"
          :aria-label="t('tags')"
          @update:model-value="(v: string) => (model.scope.industryTags = split(v))"
      /></label>
      <fieldset>
        <legend>{{ t('limits') }}</legend>
        <label
          >{{ t('depth')
          }}<el-input-number v-model="model.limits.maxDepth" :min="1" :max="12" /></label
        ><label
          >{{ t('nodes')
          }}<el-input-number v-model="model.limits.maxNodes" :min="1" :max="1000" /></label
        ><label
          >{{ t('edges')
          }}<el-input-number v-model="model.limits.maxEdges" :min="1" :max="3000" /></label
        ><label
          >{{ t('timeout')
          }}<el-input-number v-model="model.limits.timeoutSeconds" :min="1" :max="30"
        /></label>
      </fieldset>
    </el-tab-pane>
  </el-tabs>
</template>
