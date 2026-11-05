<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import AppDataTable from '@/components/management/AppDataTable.vue'
import type { AppDataTableRequest } from '@/components/management/AppDataTable'
import type { TraceDetailSectionSummary, TraceSectionResult } from '@/api/studioTrace'
import { useTraceCopy } from './copy'
import { useTracePresentation } from './presentation'

const props = defineProps<{
  section: TraceDetailSectionSummary
  tableKey: string
  load: (
    sectionId: string,
    page: number,
    pageSize: number,
    signal?: AbortSignal,
  ) => Promise<TraceSectionResult>
}>()
const t = useTraceCopy(),
  text = useTracePresentation()
const value = ref<TraceSectionResult>(),
  busy = ref(false),
  error = ref('')
let controller: AbortController | undefined
const columns = computed(() =>
  (value.value?.columns ?? [])
    .filter((column) => column.visible)
    .map((column, index) => ({
      field: `value${index}`,
      title: column.label,
      width: column.width,
      sortable: false,
      groupable: false,
      filter: false as const,
    })),
)
const rows = computed(() =>
  (value.value?.items ?? []).map((row) => ({
    key: row.key,
    ...Object.fromEntries(
      (value.value?.columns ?? [])
        .filter((column) => column.visible)
        .map((column, index) => [`value${index}`, row.values[column.fieldId]]),
    ),
  })),
)
async function fetchPage(page: number, pageSize: number) {
  controller?.abort()
  const active = new AbortController()
  controller = active
  const next = await props.load(props.section.id, page, pageSize, active.signal)
  if (active.signal.aborted) throw new DOMException('Aborted', 'AbortError')
  if (!Number.isSafeInteger(Number(next.totalCount)) || Number(next.totalCount) < 0)
    throw new Error(t('sectionTotalLimit'))
  value.value = next
  return next
}
async function initialLoad() {
  busy.value = true
  error.value = ''
  try {
    await fetchPage(1, props.section.pageSize)
  } catch (cause) {
    if (!controller?.signal.aborted)
      error.value = text.message(cause instanceof Error ? cause.message : String(cause))
  } finally {
    if (!controller?.signal.aborted) busy.value = false
  }
}
async function loadPage(request: AppDataTableRequest) {
  const next = await fetchPage(request.pageIndex, request.pageSize)
  return {
    items: rows.value,
    total: Number(next.totalCount),
    pageIndex: next.page,
    pageSize: next.pageSize,
  }
}
onMounted(initialLoad)
onBeforeUnmount(() => controller?.abort())
</script>
<template>
  <section class="trace-detail-section" :aria-label="section.label">
    <p v-if="busy" role="status">{{ t('loading') }}</p>
    <el-alert v-if="error" :title="error" type="error" :closable="false" />
    <el-button v-if="error" @click="initialLoad">{{ t('retry') }}</el-button>
    <template v-if="value">
      <template v-if="section.kind === 'Fields'">
        <dl v-if="value.fields.length">
          <template v-for="(field, index) in value.fields" :key="index"
            ><dt>{{ field.label }}</dt>
            <dd>{{ field.value ?? '—' }}</dd></template
          >
        </dl>
        <el-empty v-else :description="t('empty')" :image-size="48" />
      </template>
      <AppDataTable
        v-else
        :table-key="tableKey"
        :columns="columns"
        :rows="rows"
        :total="Number(value.totalCount)"
        :loader="loadPage"
        row-key="key"
        :page-size="section.pageSize"
        toolbar-profile="compact"
        :quick-search-enabled="false"
      />
      <small>{{ value.observedOn || t('unknown') }}</small>
    </template>
  </section>
</template>
<style scoped>
.trace-detail-section {
  min-width: 0;
}
.trace-detail-section dd {
  margin: 3px 0 12px;
  overflow-wrap: anywhere;
}
.trace-detail-section small {
  display: block;
  color: var(--el-text-color-secondary);
  margin-top: 8px;
}
</style>
