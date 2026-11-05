<script setup lang="ts">
import { ref } from 'vue'
import type { StudioResource } from '@/api/studio'
import BindingEditor from '../shared/BindingEditor.vue'
import TraceDatasetPicker from './TraceDatasetPicker.vue'
import TraceFields from './TraceFields.vue'
import TraceMappings from './TraceMappings.vue'
import { rowBinding, type TraceDetailSection } from './model'
import { useTraceCopy } from './copy'

const model = defineModel<TraceDetailSection[]>({ required: true })
defineProps<{ datasets: StudioResource[] }>()
const t = useTraceCopy()
const active = ref('')
function add() {
  const section: TraceDetailSection = {
    id: crypto.randomUUID(),
    label: `${t('detail')} ${model.value.length + 1}`,
    kind: 'Table',
    datasetRevisionNId: '',
    parameterMap: [],
    collectionPath: [],
    fields: [],
    columns: [],
    rowKey: rowBinding('id'),
    pageSize: 20,
  }
  model.value = [...model.value, section]
  active.value = section.id
}
function remove(id: string) {
  model.value = model.value.filter((section) => section.id !== id)
  if (active.value === id) active.value = ''
}
function changeKind(section: TraceDetailSection) {
  if (section.kind === 'Fields') delete section.rowKey
  else section.rowKey ??= rowBinding('id')
}
</script>
<template>
  <section class="trace-sections-editor">
    <header>
      <strong>{{ t('detailSections') }}</strong
      ><el-button link @click="add">{{ t('addSection') }}</el-button>
    </header>
    <el-collapse v-model="active" accordion>
      <el-collapse-item
        v-for="section in model"
        :key="section.id"
        :name="section.id"
        :title="section.label || t('sectionName')"
      >
        <div class="trace-section-actions">
          <el-button link type="danger" @click="remove(section.id)">{{ t('remove') }}</el-button>
        </div>
        <label
          >{{ t('sectionName') }}<el-input v-model="section.label" :aria-label="t('sectionName')"
        /></label>
        <label
          >{{ t('sectionKind')
          }}<el-select
            v-model="section.kind"
            :aria-label="t('sectionKind')"
            @change="changeKind(section)"
            ><el-option value="Fields" :label="t('sectionFields')" /><el-option
              value="Table"
              :label="t('sectionTable')" /></el-select
        ></label>
        <TraceDatasetPicker v-model="section.datasetRevisionNId" :datasets="datasets" />
        <label
          >{{ t('collection')
          }}<el-input
            :model-value="section.collectionPath.join('.')"
            :aria-label="t('collection')"
            @update:model-value="
              (value: string) => (section.collectionPath = value.split('.').filter(Boolean))
            "
        /></label>
        <fieldset>
          <legend>{{ t('mapping') }}</legend>
          <p class="trace-section-hint">{{ t('sectionContextHint') }}</p>
          <TraceMappings
            v-model="section.parameterMap"
            :contexts="['Row', 'Node', 'Context', 'Constant']"
          />
        </fieldset>
        <template v-if="section.kind === 'Fields'">
          <p class="trace-section-hint">{{ t('sectionFieldsHint') }}</p>
          <TraceFields
            v-model="section.fields"
            :contexts="['Row', 'Node', 'Context', 'Constant']"
          />
        </template>
        <template v-else>
          <p class="trace-section-hint">{{ t('sectionTableHint') }}</p>
          <label
            >{{ t('rowKey')
            }}<BindingEditor
              :model-value="section.rowKey ?? rowBinding('id')"
              :contexts="['Row']"
              @update:model-value="(value) => (section.rowKey = value)"
          /></label>
          <label
            >{{ t('pageSize')
            }}<el-input-number
              v-model="section.pageSize"
              :min="1"
              :max="200"
              :aria-label="t('pageSize')"
          /></label>
          <fieldset>
            <legend>{{ t('columns') }}</legend>
            <div v-for="(column, index) in section.columns" :key="index" class="studio-field-block">
              <el-input
                v-model="column.fieldId"
                :aria-label="t('field')"
                :placeholder="t('field')"
              />
              <el-input v-model="column.label" :aria-label="t('label')" :placeholder="t('label')" />
              <el-input-number
                v-model="column.width"
                :min="40"
                :max="1000"
                :aria-label="t('width')"
              />
              <el-checkbox v-model="column.visible">{{ t('visible') }}</el-checkbox>
              <el-button link type="danger" @click="section.columns.splice(index, 1)">{{
                t('remove')
              }}</el-button>
            </div>
            <el-button
              @click="section.columns.push({ fieldId: '', label: '', width: 160, visible: true })"
              >{{ t('add') }}</el-button
            >
          </fieldset>
        </template>
      </el-collapse-item>
    </el-collapse>
  </section>
</template>
<style scoped>
.trace-sections-editor > header,
.trace-section-actions {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
}
.trace-section-actions {
  justify-content: flex-end;
}
.trace-section-hint {
  color: var(--el-text-color-secondary);
  font-size: 12px;
  line-height: 1.6;
}
</style>
