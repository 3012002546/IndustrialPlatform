<script setup lang="ts">
import type { Json, StudioResource } from '@/api/studio'
import BindingEditor from '@/components/studio/shared/BindingEditor.vue'
import ReportMappings from './ReportMappings.vue'
import { useReportCopy } from './copy'
const props = defineProps<{
  modelValue: Json
  reports: StudioResource[]
  instances: string[]
  resourceNId: string
  allowOutline: boolean
}>()
const emit = defineEmits<{ 'update:modelValue': [value: Json] }>()
const t = useReportCopy()
function set(key: string, value: unknown) {
  const next = JSON.parse(JSON.stringify(props.modelValue))
  if (value === undefined) delete next[key]
  else next[key] = value
  emit('update:modelValue', next)
}
function sub(key: string, value: unknown) {
  set('subReport', { ...props.modelValue.subReport, [key]: value })
}
function choose(id: string) {
  const report = props.reports.find((r) => r.nId === id)
  set('subReport', {
    reportNId: id,
    revisionNId: report?.publishedRevisionNId ?? '',
    parameterMap: [],
    instanceMap: [],
  })
}
</script>
<template>
  <div class="report-document-editor">
    <template v-if="modelValue.type === 'SubReport'">
      <p>{{ t('subReportHint') }}</p>
      <el-form label-position="top"
        ><el-form-item :label="t('subReportControl')">
          <el-select
            :model-value="modelValue.subReport.reportNId"
            :aria-label="t('subReportControl')"
            @update:model-value="choose"
          >
            <el-option
              v-for="r in reports.filter((r) => r.nId !== resourceNId && r.publishedRevisionNId)"
              :key="r.nId"
              :label="r.name"
              :value="r.nId"
            />
          </el-select> </el-form-item
        ><el-form-item :label="t('revision')"
          ><el-input :model-value="modelValue.subReport.revisionNId" readonly /></el-form-item
      ></el-form>
      <ReportMappings
        :model-value="modelValue.subReport.parameterMap"
        :instances="instances"
        :label="t('params')"
        @update:model-value="sub('parameterMap', $event)"
      />
      <ReportMappings
        :model-value="modelValue.subReport.instanceMap"
        :instances="instances"
        :label="t('instance')"
        @update:model-value="sub('instanceMap', $event)"
      />
    </template>
    <el-form v-if="modelValue.type === 'TableOfContents'" label-position="top"
      ><el-form-item :label="t('tocTitle')"
        ><el-input
          :model-value="modelValue.toc.title"
          :aria-label="t('tocTitle')"
          maxlength="200"
          @update:model-value="set('toc', { title: $event })" /></el-form-item
    ></el-form>
    <template v-if="allowOutline && modelValue.type !== 'TableOfContents'">
      <el-checkbox
        :model-value="!!modelValue.outline"
        @update:model-value="
          set(
            'outline',
            $event
              ? {
                  title: { from: 'Constant', value: t('outlineTitle'), nullPolicy: 'Error' },
                  level: 1,
                }
              : undefined,
          )
        "
        >{{ t('outlineEnabled') }}</el-checkbox
      >
      <el-form v-if="modelValue.outline" label-position="top"
        ><el-form-item :label="t('outlineTitle')"
          ><BindingEditor
            :model-value="modelValue.outline.title"
            :instances="instances"
            @update:model-value="
              set('outline', { ...modelValue.outline, title: $event })
            " /></el-form-item
        ><el-form-item :label="t('outlineLevel')"
          ><el-input-number
            :model-value="modelValue.outline.level"
            :min="1"
            :max="3"
            :step="1"
            step-strictly
            @update:model-value="
              set('outline', { ...modelValue.outline, level: $event })
            " /></el-form-item
      ></el-form>
    </template>
  </div>
</template>
