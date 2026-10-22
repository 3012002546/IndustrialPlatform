<script setup lang="ts">
import { computed, onBeforeUnmount, ref } from 'vue'
import { getStudioApi, type Json } from '@/api/studio'
import { createSystemDataOperationsApi } from '@/api/systemData/systemDataOperationsApi'
import type { UploadSessionDto, FileObjectDto } from '@/api/systemData/systemDataOperationsTypes'
import { sha256File, sampleFingerprint } from '@/utils/sha256File'
import { uploadFileChunks } from '@/utils/uploadFileChunks'
import { useStudioCopy } from './copy'
const props = defineProps<{ modelValue?: Json | null; modelNId: string; disabled?: boolean }>()
const emit = defineEmits<{ 'update:modelValue': [value: Json] }>()
const t = useStudioCopy(),
  busy = ref(false),
  error = ref(''),
  offset = ref(0),
  total = ref(0),
  session = ref<UploadSessionDto | null>(null)
const progress = computed(() => (total.value ? Math.round((offset.value / total.value) * 100) : 0))
let controller: AbortController | null = null
const referenceNId = ref(crypto.randomUUID())
const pendingFile = ref<FileObjectDto | null>(null)
async function finalize(file: FileObjectDto) {
  pendingFile.value = file
  if (file.scanStatus !== 'Clean') {
    error.value = t.value('fileUnavailable') + ' (' + file.scanStatus + ')'
    return
  }
  await getStudioApi().client.put(
    `/api/v1/studio/files/${encodeURIComponent(file.fileNId)}/references/${encodeURIComponent(referenceNId.value)}`,
    { purpose: 'LedgerField' },
  )
  emit('update:modelValue', {
    fileNId: file.fileNId,
    referenceNId: referenceNId.value,
    name: file.fileName,
  })
  referenceNId.value = crypto.randomUUID()
  pendingFile.value = null
  session.value = null
  error.value = ''
}
async function checkScan() {
  if (!pendingFile.value || busy.value) return
  busy.value = true
  try {
    await finalize(await fileApi().getFile(pendingFile.value.fileNId))
  } catch (e) {
    error.value = e instanceof Error ? e.message : t.value('error')
  } finally {
    busy.value = false
  }
}
function fileApi() {
  return createSystemDataOperationsApi(getStudioApi().client, '/api/v1/studio')
}
async function upload(event: Event) {
  const file = (event.target as HTMLInputElement).files?.[0]
  if (!file || busy.value) return
  busy.value = true
  error.value = ''
  total.value = file.size
  offset.value = 0
  controller = new AbortController()
  try {
    const api = fileApi(),
      hash = await sha256File(file)
    let ready = await api.createUploadSession({
      fileName: file.name,
      contentType: file.type || 'application/octet-stream',
      length: String(file.size),
      sha256: hash,
      purpose: 'LedgerField',
      sampleFingerprint: await sampleFingerprint(file),
      usage: { moduleKey: 'LowCode', resourceNId: props.modelNId, purpose: 'LedgerField' },
    })
    session.value = ready
    await api.setContentHash(ready.sessionNId, hash)
    ready = await api.resumeProof(ready.sessionNId, ready.writerEpoch, hash)
    const uploaded = await uploadFileChunks(api, file, ready, {
      signal: controller.signal,
      onProgress: (next) => {
        session.value = next
        offset.value = Number(next.offset)
      },
    })
    if (!uploaded) return
    const completed = await api.completeUpload(uploaded.sessionNId)
    await finalize(completed)
  } catch (e) {
    error.value = e instanceof Error ? e.message : t.value('error')
  } finally {
    busy.value = false
  }
}
async function cancel() {
  controller?.abort()
  if (session.value) {
    try {
      await fileApi().cancelUpload(session.value.sessionNId)
    } catch (e) {
      error.value = e instanceof Error ? e.message : t.value('error')
    }
  }
}
async function download() {
  if (!props.modelValue) return
  error.value = ''
  try {
    const blob = await getStudioApi().client.getBlob?.(
      `/api/v1/studio/files/${encodeURIComponent(String(props.modelValue.fileNId))}/content?referenceNId=${encodeURIComponent(String(props.modelValue.referenceNId))}`,
    )
    if (!blob) throw new Error(t.value('fileUnavailable'))
    const url = URL.createObjectURL(blob)
    const anchor = document.createElement('a')
    anchor.href = url
    anchor.download = String(props.modelValue.name ?? 'file')
    anchor.click()
    setTimeout(() => URL.revokeObjectURL(url), 1000)
  } catch (e) {
    error.value = e instanceof Error ? e.message : t.value('error')
  }
}
onBeforeUnmount(() => controller?.abort())
</script>
<template>
  <div>
    <el-button v-if="modelValue" link type="primary" @click="download"
      >{{ modelValue.name }} · {{ t('download') }}</el-button
    ><input
      v-if="!disabled"
      type="file"
      :aria-label="t('upload')"
      :disabled="busy"
      @change="upload"
    /><el-progress v-if="busy" :percentage="progress" /><el-button
      v-if="busy"
      link
      @click="cancel"
      >{{ t('cancel') }}</el-button
    ><el-alert v-if="error" :title="error" type="error" :closable="false" />
    <el-button v-if="pendingFile" :disabled="busy" @click="checkScan"
      >{{ t('check') }} · {{ pendingFile.scanStatus }}</el-button
    >
  </div>
</template>
