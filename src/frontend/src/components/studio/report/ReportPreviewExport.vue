<script setup lang="ts">
import { onBeforeUnmount, ref, watch } from 'vue'
import { Download } from '@element-plus/icons-vue'
import { getStudioApi, type Json } from '@/api/studio'
import { PermissionGate } from '@/permissions'
import { downloadBlob } from '@/components/management/download'
import ReportFormatSelect from './ReportFormatSelect.vue'
import { useReportCopy } from './copy'
const props = defineProps<{ input: Json }>()
const api = getStudioApi(),
  t = useReportCopy()
const format = ref('Pdf'),
  busy = ref(false),
  error = ref('')
let request: AbortController | undefined
function reset() {
  request?.abort()
  request = undefined
  busy.value = false
  error.value = ''
}
watch(() => props.input, reset, { flush: 'sync' })
onBeforeUnmount(reset)
async function download() {
  if (busy.value) return
  const controller = new AbortController()
  request = controller
  busy.value = true
  error.value = ''
  const selected = format.value
  try {
    const blob = await api.reportPreviewExport(props.input, selected, controller.signal)
    if (request !== controller || controller.signal.aborted) return
    const extension =
      selected === 'Text'
        ? 'txt'
        : ['Png', 'Jpeg'].includes(selected)
          ? 'zip'
          : selected.toLowerCase()
    downloadBlob(blob, 'preview-' + selected.toLowerCase() + '.' + extension)
  } catch (e) {
    if (request === controller && !controller.signal.aborted)
      error.value = e instanceof Error ? e.message : t.value('error')
  } finally {
    if (request === controller) {
      request = undefined
      busy.value = false
    }
  }
}
</script>
<template>
  <PermissionGate permission-n-id="studio.report.export">
    <div class="preview-export" :title="t('draftExportHint')">
      <ReportFormatSelect v-model="format" :disabled="busy" />
      <el-button
        :icon="Download"
        :loading="busy"
        :aria-label="t('draftDownload')"
        @click="download"
        >{{ t('draftDownload') }}</el-button
      >
      <el-tooltip :content="t('draftExportHint')"
        ><span class="export-note">{{ t('draftExportNote') }}</span></el-tooltip
      >
      <span v-if="error" role="alert" class="export-error">{{ error }}</span>
    </div>
  </PermissionGate>
</template>
<style scoped>
.preview-export {
  display: flex;
  align-items: center;
  gap: 6px;
  flex-wrap: wrap;
}
.export-note {
  font-size: 12px;
  color: var(--el-text-color-secondary);
}
.export-error {
  color: var(--el-color-danger);
  font-size: 13px;
  max-width: 360px;
  overflow-wrap: anywhere;
}
</style>
