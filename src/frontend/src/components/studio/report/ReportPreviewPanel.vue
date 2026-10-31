<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { getStudioApi, type Json } from '@/api/studio'
import { useReportCopy } from './copy'
const props = defineProps<{ previewNId?: string | undefined; preview?: Json | undefined }>()
const api = getStudioApi(),
  t = useReportCopy(),
  state = ref<Json>(),
  error = ref(''),
  page = ref(1),
  content = ref(''),
  busy = ref(false)
const pages = computed<Json[]>(() => state.value?.pages ?? [])
let timer: ReturnType<typeof setTimeout> | undefined
let disposed = false,
  generation = 0,
  sourceGeneration = 0
const clearContent = () => {
  generation++
  content.value = ''
}
async function loadPage() {
  const current = pages.value.find((item) => Number(item.pageNo) === page.value)
  const own = ++generation
  content.value = ''
  error.value = ''
  if (!current) return
  try {
    let html: string
    if (typeof current.html === 'string') html = current.html
    else {
      const blob = await api.client.getBlob?.(String(current.contentUrl))
      if (!blob) throw new Error(t.value('error'))
      html = await blob.text()
    }
    if (!disposed && own === generation)
      content.value = `<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; img-src data:; font-src data:;">${html}`
  } catch (e) {
    if (own === generation) {
      clearContent()
      error.value = e instanceof Error ? e.message : t.value('error')
    }
  }
}
async function refresh() {
  if (disposed) return
  const requested = props.previewNId
  const own = sourceGeneration
  if (!requested || props.preview) return
  try {
    const result = await api.reportPreviewState(requested)
    if (disposed || own !== sourceGeneration || props.preview || requested !== props.previewNId)
      return
    error.value = ''
    state.value = result
    if (result.status === 'Succeeded' && !content.value) await loadPage()
    if (result.status === 'Failed' || result.status === 'Unknown' || result.status === 'Cancelled')
      clearContent()
    // Recheck current access while visible; elapsed design time does not expire content.
    if (
      !disposed &&
      own === sourceGeneration &&
      !props.preview &&
      !['Failed', 'Cancelled', 'Unknown'].includes(String(result.status))
    )
      timer = setTimeout(() => void refresh(), result.status === 'Succeeded' ? 30000 : 500)
  } catch (e) {
    if (disposed || own !== sourceGeneration || props.preview || requested !== props.previewNId)
      return
    clearContent()
    error.value = e instanceof Error ? e.message : t.value('error')
  }
}
async function cancel() {
  if (!state.value || busy.value || !props.previewNId) return
  const own = sourceGeneration
  busy.value = true
  try {
    await api.reportCancel(props.previewNId, String(state.value.stateVersion))
    if (disposed || own !== sourceGeneration) return
    if (timer) clearTimeout(timer)
    await refresh()
  } catch (e) {
    if (!disposed && own === sourceGeneration)
      error.value = e instanceof Error ? e.message : t.value('error')
  } finally {
    if (own === sourceGeneration) busy.value = false
  }
}
watch(page, () => void loadPage())
watch(
  () => [props.previewNId, props.preview] as const,
  () => {
    sourceGeneration++
    busy.value = false
    if (timer) clearTimeout(timer)
    clearContent()
    error.value = ''
    state.value = undefined
    page.value = 1
    if (props.preview) {
      state.value = props.preview
      void loadPage()
    } else void refresh()
  },
  { immediate: true },
)
onBeforeUnmount(() => {
  disposed = true
  sourceGeneration++
  clearContent()
  if (timer) clearTimeout(timer)
})
</script>
<template>
  <div>
    <el-alert
      v-if="error || state?.errorCode"
      :title="error || String(state?.errorCode)"
      type="error"
      :closable="false"
    />
    <div class="studio-toolbar">
      <el-select v-if="pages.length" v-model="page" :aria-label="t('pages')" style="width: 130px"
        ><el-option
          v-for="item in pages"
          :key="item.pageNo"
          :value="Number(item.pageNo)"
          :label="`${item.pageNo} / ${pages.length}`"
      /></el-select>
      <el-button
        v-if="state && ['PendingDispatch', 'Queued', 'Running'].includes(String(state.status))"
        :loading="busy"
        @click="cancel"
        >{{ t('cancel') }}</el-button
      >
      <span v-if="state && !pages.length">{{
        state.status === 'Unknown'
          ? t('unknown')
          : state.status === 'Failed'
            ? t('failed')
            : state.status === 'Cancelled'
              ? t('cancelled')
              : t('running')
      }}</span>
    </div>
    <iframe
      v-if="content"
      :title="t('preview')"
      :srcdoc="content"
      sandbox=""
      referrerpolicy="no-referrer"
      class="report-preview-frame"
    />
  </div>
</template>
<style scoped>
.report-preview-frame {
  width: 100%;
  height: 70vh;
  border: 1px solid var(--el-border-color);
  background: var(--el-fill-color-light);
}
</style>
