<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import type { StudioRevisionSummary } from '@/api/studio'
import { useAuthStore } from '@/stores/authStore'
import { useLocalizationStore } from '@/stores/localizationStore'
import { useReportCopy } from './copy'
import { useRevisionLoader } from './revisionLoader'

const props = defineProps<{
  modelValue?: string
  reportNId?: string
  publishedRevisionNId?: string | null | undefined
  publishedRevisionNo?: string | null | undefined
  disabled?: boolean
}>()
const emit = defineEmits<{
  'update:modelValue': [value: string]
  loaded: [reportNId: string, revisions: StudioRevisionSummary[]]
}>()
const t = useReportCopy(),
  locale = useLocalizationStore(),
  auth = useAuthStore(),
  loader = useRevisionLoader()
const revisions = ref<StudioRevisionSummary[]>([]),
  loading = ref(false),
  failed = ref(false)
let request = 0
async function load(preserve = false) {
  const id = props.reportNId,
    current = ++request
  if (!preserve) revisions.value = []
  failed.value = false
  loading.value = !!id
  if (!id) return
  try {
    const rows = await loader.load(id, props.publishedRevisionNId)
    if (current !== request) return
    revisions.value = rows
    emit('loaded', id, rows)
  } catch {
    if (current === request) failed.value = true
  } finally {
    if (current === request) loading.value = false
  }
}
watch([() => props.reportNId, () => props.publishedRevisionNId], () => load(), { immediate: true })
watch(
  () => auth.session,
  () => {
    loader.clear()
    void load()
  },
)
onBeforeUnmount(() => {
  request++
})
function label(revision: StudioRevisionSummary) {
  const date = new Date(revision.createdOn)
  return [
    'V' + revision.revisionNo,
    Number.isNaN(date.getTime()) ? '' : date.toLocaleString(locale.locale, { hour12: false }),
    revision.releaseNote,
  ]
    .filter(Boolean)
    .join(' · ')
}
const options = computed(() => {
  const rows = revisions.value.map((revision) => ({ value: revision.nId, label: label(revision) }))
  if (props.modelValue && !rows.some((row) => row.value === props.modelValue)) {
    rows.unshift({
      value: props.modelValue,
      label:
        props.modelValue === props.publishedRevisionNId && props.publishedRevisionNo
          ? 'V' + props.publishedRevisionNo
          : t.value(loading.value || failed.value ? 'boundRevision' : 'missingRevision'),
    })
  }
  return rows
})
</script>

<template>
  <div class="report-revision-select">
    <el-select
      :model-value="modelValue || undefined"
      :aria-label="t('revision')"
      :placeholder="t('chooseRevision')"
      :loading="loading"
      :disabled="disabled || !reportNId || (loading && !revisions.length) || failed"
      :no-data-text="t('noPublishedRevision')"
      @update:model-value="emit('update:modelValue', $event)"
      @visible-change="(visible: boolean) => visible && load(true)"
    >
      <el-option
        v-for="option in options"
        :key="option.value"
        :value="option.value"
        :label="option.label"
        :title="option.label"
      />
    </el-select>
    <p class="studio-muted">{{ t('revisionHint') }}</p>
    <p v-if="failed" role="alert">
      {{ t('revisionLoadFailed') }}
      <el-button link @click="load()">{{ t('retryRevisions') }}</el-button>
    </p>
  </div>
</template>

<style scoped>
.report-revision-select {
  width: 100%;
  min-width: 0;
}
</style>
