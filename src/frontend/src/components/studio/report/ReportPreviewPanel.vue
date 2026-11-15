<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { getStudioApi, type Json } from '@/api/studio'
import ReportPaperViewport from './ReportPaperViewport.vue'
import {
  DArrowLeft,
  ArrowLeft,
  ArrowRight,
  DArrowRight,
  Files,
  Printer,
  Document,
  Minus,
  Plus,
  Search,
} from '@element-plus/icons-vue'
import { isolatedPage, inspectPage, printPhysicalPages } from './previewPhysicalPage'
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
const diagnostics = computed<string[]>(() =>
  (state.value?.diagnostics ?? []).map((item: Json) =>
    item.code === 'REPORT_FONT_SUBSTITUTED'
      ? t.value('fontUnavailable') +
        (Array.isArray(item.unavailable)
          ? item.unavailable.join(', ')
          : String(item.requested ?? '')) +
        '；' +
        t.value('fontFallback') +
        String(item.fallback ?? '')
      : String(item.message || item.code || t.value('error')),
  ),
)
const query = ref('')
const hit = ref(0)
const inspected = computed(() => inspectPage(content.value, query.value))
const searchUrl = ref('')
let printWindow: Window | null = null
watch([inspected, page], ([result], _, onCleanup) => {
  hit.value = 0
  searchUrl.value = ''
  if (!result.anchors.length) return
  const url = URL.createObjectURL(
    new Blob([isolatedPage(result.html, page.value, pages.value.length)], {
      type: 'text/html;charset=utf-8',
    }),
  )
  searchUrl.value = url
  onCleanup(() => URL.revokeObjectURL(url))
})
const searchLocation = computed(
  () => searchUrl.value && searchUrl.value + '#' + inspected.value.anchors[hit.value],
)
function stepHit(delta: number) {
  const count = inspected.value.anchors.length
  if (count) hit.value = (hit.value + delta + count) % count
}
const continuous = ref(false),
  zoom = ref('page'),
  scale = ref(1),
  showSearch = ref(false),
  printing = ref(false)
const zoomSteps = [0.25, 0.5, 0.75, 1, 1.25, 1.5, 2, 3, 4]
function toggleSearch() {
  showSearch.value = !showSearch.value
  if (!showSearch.value) query.value = ''
}
function zoomBy(up: boolean) {
  const step = up
    ? zoomSteps.find((n) => n > scale.value + 0.001)
    : zoomSteps
        .slice()
        .reverse()
        .find((n) => n < scale.value - 0.001)
  if (step) zoom.value = String(step)
}
async function readPage(item: Json): Promise<string> {
  if (typeof item.html === 'string') return item.html
  const blob = await api.client.getBlob?.(String(item.contentUrl))
  if (!blob) throw new Error(t.value('error'))
  return blob.text()
}
async function printPages(all: boolean) {
  if (!content.value || printing.value) return
  printWindow?.close()
  const popup = window.open('', '_blank', 'popup')
  if (!popup) {
    error.value = t.value('popupBlocked')
    return
  }
  printWindow = popup
  printing.value = true
  const current = () => printWindow === popup && !disposed
  try {
    await printPhysicalPages(
      popup,
      pages.value.length,
      all ? pages.value : [{ pageNo: page.value, html: content.value }],
      readPage,
      t.value(all ? 'printAll' : 'printPage'),
      current,
      t.value('printIncomplete'),
    )
  } catch (e) {
    if (current()) {
      popup.close()
      error.value = e instanceof Error ? e.message : t.value('error')
    }
  } finally {
    if (current()) printing.value = false
  }
}
let timer: ReturnType<typeof setTimeout> | undefined
let disposed = false,
  generation = 0,
  sourceGeneration = 0
const clearContent = () => {
  printWindow?.close()
  printWindow = null
  printing.value = false
  generation++
  content.value = ''
}
async function loadPage() {
  const current = pages.value.find((item) => Number(item.pageNo) === page.value)
  clearContent()
  const own = generation
  error.value = ''
  if (!current) return
  try {
    const html = await readPage(current)
    if (!disposed && own === generation) content.value = html
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
    state.value = undefined
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
watch(page, () => {
  if (state.value?.status === 'Succeeded') void loadPage()
})
watch(
  () => [props.previewNId, props.preview] as const,
  () => {
    query.value = ''
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
  <section class="viewer">
    <div class="viewer-tools" role="toolbar" :aria-label="t('preview')">
      <el-button-group>
        <el-button
          :icon="DArrowLeft"
          :title="t('firstSheet')"
          :aria-label="t('firstSheet')"
          :disabled="page <= 1"
          @click="page = 1"
        />
        <el-button
          :icon="ArrowLeft"
          :title="t('prevSheet')"
          :aria-label="t('prevSheet')"
          :disabled="page <= 1"
          @click="page--"
        />
      </el-button-group>
      <el-select v-model="page" filterable :aria-label="t('pages')" style="width: 110px">
        <el-option
          v-for="item in pages"
          :key="item.pageNo"
          :value="Number(item.pageNo)"
          :label="item.pageNo + ' / ' + pages.length"
        />
      </el-select>
      <el-button-group>
        <el-button
          :icon="ArrowRight"
          :title="t('nextSheet')"
          :aria-label="t('nextSheet')"
          :disabled="page >= pages.length"
          @click="page++"
        />
        <el-button
          :icon="DArrowRight"
          :title="t('lastSheet')"
          :aria-label="t('lastSheet')"
          :disabled="page >= pages.length"
          @click="page = pages.length"
        />
      </el-button-group>
      <el-button
        :icon="Files"
        :title="t('continuous')"
        :aria-label="t('continuous')"
        :aria-pressed="continuous"
        :class="{ selected: continuous }"
        @click="continuous = !continuous"
      />
      <i />
      <el-button
        :icon="Minus"
        :aria-label="t('zoomOut')"
        :title="t('zoomOut')"
        :disabled="scale <= 0.25"
        @click="zoomBy(false)"
      />
      <el-select v-model="zoom" :aria-label="t('canvasZoom')" style="width: 112px">
        <el-option :label="t('wholePage')" value="page" /><el-option
          :label="t('fitWidth')"
          value="width"
        />
        <el-option v-for="n in zoomSteps" :key="n" :label="n * 100 + '%'" :value="String(n)" />
      </el-select>
      <el-button
        :icon="Plus"
        :aria-label="t('zoomIn')"
        :title="t('zoomIn')"
        :disabled="scale >= 4"
        @click="zoomBy(true)"
      />
      <i />
      <el-button
        :icon="Printer"
        :aria-label="t('printAll')"
        :title="t('printAll')"
        :disabled="!content || printing"
        @click="printPages(true)"
      />
      <el-button
        :icon="Document"
        :aria-label="t('printPage')"
        :title="t('printPage')"
        :disabled="!content || printing"
        @click="printPages(false)"
      />
      <el-button
        :icon="Search"
        :aria-label="t('findPage')"
        :title="t('findPage')"
        :aria-expanded="showSearch"
        @click="toggleSearch"
      />
      <slot name="actions" />
    </div>
    <div v-if="showSearch" class="find-tools">
      <el-input
        v-model="query"
        clearable
        :placeholder="t('findPage')"
        :aria-label="t('findPage')"
        style="width: 210px"
        @keydown.enter.prevent="stepHit($event.shiftKey ? -1 : 1)"
      />
      <span role="status"
        >{{ inspected.anchors.length ? hit + 1 : 0 }} / {{ inspected.anchors.length }}</span
      >
      <el-button :disabled="inspected.anchors.length < 2" @click="stepHit(-1)">{{
        t('previousHit')
      }}</el-button>
      <el-button :disabled="inspected.anchors.length < 2" @click="stepHit(1)">{{
        t('nextHit')
      }}</el-button>
    </div>
    <details v-if="diagnostics.length" class="preview-diagnostics" open>
      <summary>{{ t('previewDiagnostics') }} ({{ diagnostics.length }})</summary>
      <ul>
        <li v-for="(message, index) in diagnostics" :key="index">{{ message }}</li>
      </ul>
    </details>
    <slot />
    <el-alert
      v-if="error || state?.errorCode"
      :title="error || String(state?.errorCode)"
      type="error"
      :closable="false"
    />
    <el-button
      v-if="state && ['PendingDispatch', 'Queued', 'Running'].includes(String(state.status))"
      :loading="busy"
      @click="cancel"
      >{{ t('cancel') }}</el-button
    >
    <span v-if="state && !pages.length">{{
      t(
        state.status === 'Unknown'
          ? 'unknown'
          : state.status === 'Failed'
            ? 'failed'
            : state.status === 'Cancelled'
              ? 'cancelled'
              : 'running',
      )
    }}</span>
    <ReportPaperViewport
      :key="sourceGeneration"
      v-if="content || (pages.length && !error && state?.status === 'Succeeded')"
      :pages="pages"
      :page="page"
      :continuous="continuous"
      :zoom="zoom"
      :title="t('preview')"
      :content="content"
      :search-location="searchLocation"
      :read="readPage"
      @scroll-page="page = $event"
      @scale="scale = $event"
    />
  </section>
</template>
<style scoped>
.viewer {
  height: 100%;
  min-height: 0;
  display: flex;
  flex-direction: column;
  background: var(--el-bg-color);
}
.preview-diagnostics {
  flex: 0 0 auto;
  max-height: 20vh;
  overflow: auto;
  padding: 8px 12px;
  color: var(--el-text-color-primary);
  background: var(--el-color-warning-light-9);
  border-bottom: 1px solid var(--el-border-color);
  overflow-wrap: anywhere;
}
.preview-diagnostics summary {
  cursor: pointer;
  font-weight: 600;
}
.preview-diagnostics ul {
  margin: 6px 0 0;
  padding-left: 20px;
}
.viewer-tools,
.find-tools {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 4px;
  padding: 6px 12px;
  background: var(--el-fill-color-lighter);
  border-bottom: 1px solid var(--el-border-color-lighter);
}
.viewer-tools :deep(.el-button) {
  margin: 0;
  border: 0;
  background: transparent;
  font-size: 18px;
  padding: 6px 8px;
  color: var(--el-text-color-regular);
}
.viewer-tools :deep(.el-button.is-disabled) {
  opacity: 0.32;
}
.viewer-tools :deep(.el-button:hover),
.viewer-tools .selected {
  background: var(--el-fill-color-darker);
}
.viewer-tools i:not(.el-icon) {
  height: 20px;
  border-left: 1px solid var(--el-border-color);
  margin: 0 5px;
}
</style>
