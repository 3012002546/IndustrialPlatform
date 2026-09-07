<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { ElMessage } from 'element-plus'
import AppDataTable from '@/components/management/AppDataTable.vue'
import AppFormDrawer from '@/components/management/AppFormDrawer.vue'
import AppPage from '@/components/base/AppPage.vue'
import AppQueryPanel from '@/components/management/AppQueryPanel.vue'
import PermissionGate from '@/permissions/PermissionGate.vue'
import { PERMISSIONS } from '@/permissions'
import { getPf04Api } from '@/api/systemData/pf04Registry'
import type { FileObjectDto, UploadSessionDto } from '@/api/systemData/pf04Types'
import { useLocalizationStore } from '@/stores/localizationStore'
import { sampleFingerprint, sha256File } from '@/utils/sha256File'

const localization = useLocalizationStore()
const api = getPf04Api()
const rows = ref<FileObjectDto[]>([])
const total = ref(0)
const search = ref('')
const loading = ref(false)
const uploadOpen = ref(false)
const selectedFile = ref<File | null>(null)
const busy = ref(false)
const currentSession = ref<UploadSessionDto | null>(null)
const uploadHash = ref('')
const candidates = ref<UploadSessionDto[]>([])
const candidateOpen = ref(false)
const paused = ref(false)
const uploadError = ref('')
const errorMessage = ref('')
const title = computed(() => (localization.locale === 'zh-CN' ? '文件管理' : 'File management'))
const description = computed(() =>
  localization.locale === 'zh-CN' ? '分片上传、完整性校验与安全扫描状态。' : 'Resumable uploads, integrity checks, and scan state.',
)
const columns = computed(() => [
  { field: 'fileName', title: localization.locale === 'zh-CN' ? '文件名' : 'File', minWidth: 220 },
  { field: 'length', title: localization.locale === 'zh-CN' ? '大小' : 'Size', width: 110 },
  { field: 'scanStatus', title: localization.locale === 'zh-CN' ? '扫描状态' : 'Scan', width: 130 },
  { field: 'sha256', title: 'SHA-256', minWidth: 220 },
  { field: 'createdOn', title: localization.locale === 'zh-CN' ? '创建时间' : 'Created', minWidth: 180 },
  { field: 'actions', title: localization.locale === 'zh-CN' ? '操作' : 'Actions', width: 250 },
])

async function load(): Promise<void> {
  if (api === null) return
  loading.value = true
  try {
    const page = await api.listFiles(search.value)
    rows.value = page.items
    total.value = page.total
    errorMessage.value = ''
  } catch (error) {
    errorMessage.value = error instanceof Error ? error.message : (localization.locale === 'zh-CN' ? '文件加载失败，请重试。' : 'Unable to load files.')
  } finally { loading.value = false }
}

function selectFile(event: Event): void {
  selectedFile.value = (event.target as HTMLInputElement).files?.[0] ?? null
  uploadHash.value = ''; candidates.value = []; candidateOpen.value = false; uploadError.value = ''; currentSession.value = null
}

async function upload(): Promise<void> {
  if (api === null || selectedFile.value === null) return
  busy.value = true
  try {
    const file = selectedFile.value
    uploadHash.value = uploadHash.value || await sha256File(file)
    const discovery = await api.discoverUpload({ fileName: file.name, length: file.size, sampleFingerprint: await sampleFingerprint(file), purpose: 'systemdata' })
    candidates.value = discovery.candidates
    if (candidates.value.length > 0) {
      candidateOpen.value = true
      return
    }
    await startUpload(null)
  } catch (error) {
    uploadError.value = error instanceof Error ? error.message : '上传失败，请重试。'
  } finally { busy.value = false }
}

async function startUpload(candidate: UploadSessionDto | null, takeover = false): Promise<void> {
  if (api === null || selectedFile.value === null) return
  busy.value = true
  uploadError.value = ''
  try {
    const file = selectedFile.value
    const hash = uploadHash.value || await sha256File(file)
    uploadHash.value = hash
    let ready: UploadSessionDto
    if (candidate === null) {
      const created = await api.createUploadSession({ fileName: file.name, contentType: file.type || 'application/octet-stream', length: file.size, sha256: hash, purpose: 'systemdata', sampleFingerprint: await sampleFingerprint(file) })
      await api.setContentHash(created.sessionNId, hash)
      ready = await api.resumeProof(created.sessionNId, created.writerEpoch, hash)
    } else {
      ready = await api.getUploadSession(candidate.sessionNId)
      if (takeover) ready = await api.takeoverUpload(ready.sessionNId, ready.writerEpoch, hash, `ui-${crypto.randomUUID()}`)
      if (ready.status === 'Paused') ready = await api.resumeUpload(ready.sessionNId, ready.writerEpoch, hash)
      else if (ready.status === 'WaitingForProof') ready = await api.resumeProof(ready.sessionNId, ready.writerEpoch, hash)
    }
    currentSession.value = ready
    paused.value = false
    const chunkSize = 1024 * 1024
    while (ready.offset < file.size && !paused.value) {
      const offset = ready.offset
      const chunk = file.slice(offset, Math.min(offset + chunkSize, file.size))
      ready = await api.uploadChunk(ready.transportId, chunk, offset, ready.writerEpoch, ready.resumeTicket ?? '')
      currentSession.value = ready
    }
    if (paused.value) return
    await api.completeUpload(ready.sessionNId)
    ElMessage.success(localization.locale === 'zh-CN' ? '上传完成，等待安全扫描。' : 'Upload complete; waiting for scan.')
    uploadOpen.value = false
    currentSession.value = null; uploadHash.value = ''; candidates.value = []
    await load()
  } catch (error) {
    uploadError.value = error instanceof Error ? error.message : '上传失败，请重试。'
  } finally { busy.value = false }
}

async function continueCandidate(candidate: UploadSessionDto, takeover = false): Promise<void> {
  candidateOpen.value = false
  await startUpload(candidate, takeover)
}

async function pauseCurrent(): Promise<void> {
  if (api === null || currentSession.value === null) return
  paused.value = true
  currentSession.value = await api.pauseUpload(currentSession.value.sessionNId)
}

async function resumeCurrent(): Promise<void> {
  if (api === null || selectedFile.value === null || currentSession.value === null) return
  currentSession.value = await api.getUploadSession(currentSession.value.sessionNId)
  await startUpload(currentSession.value)
}

async function cancelCurrent(): Promise<void> {
  if (api === null || currentSession.value === null) return
  paused.value = true
  await api.cancelUpload(currentSession.value.sessionNId, 'user-cancelled')
  currentSession.value = null; paused.value = false
}

async function download(row: FileObjectDto): Promise<void> {
  if (api === null || row.scanStatus !== 'Clean') return
  const blob = await api.downloadFile(row.fileNId)
  const url = URL.createObjectURL(blob); const anchor = document.createElement('a')
  anchor.href = url; anchor.download = row.fileName; anchor.click(); URL.revokeObjectURL(url)
}

async function requestDeletion(row: FileObjectDto): Promise<void> {
  if (api === null) return
  await api.requestFileDeletion(row.fileNId); await load()
}

async function toggleRestriction(row: FileObjectDto): Promise<void> {
  if (api === null) return
  await api.setFileRestriction(row.fileNId, !row.restricted); await load()
}

onMounted(() => void load())
onBeforeUnmount(() => { paused.value = true })
</script>

<template>
  <AppPage :title="title" :description="description">
    <template #actions>
      <PermissionGate :permission-n-id="PERMISSIONS.systemDataFileUpload">
        <el-button type="primary" @click="uploadOpen = true">{{ localization.locale === 'zh-CN' ? '上传文件' : 'Upload' }}</el-button>
      </PermissionGate>
    </template>
    <AppQueryPanel :title="localization.locale === 'zh-CN' ? '查询' : 'Query'" show-actions @submit="load" @reset="search = ''; load()">
      <el-input v-model="search" clearable :placeholder="localization.locale === 'zh-CN' ? '文件名 / 标识' : 'File name / id'" />
    </AppQueryPanel>
    <p v-if="errorMessage" role="alert" class="pf04-error">{{ errorMessage }}</p>
    <AppDataTable table-key="systemdata-files" route-key="systemdata-files" row-key="fileNId" :rows="rows" :total="total" :columns="columns" :loading="loading">
      <template #cell-actions="{ row }">
        <el-button link type="primary" :disabled="row.scanStatus !== 'Clean'" @click="download(row)">下载</el-button>
        <el-button link @click="toggleRestriction(row)">{{ row.restricted ? '解除限制' : '限制' }}</el-button>
        <el-button link type="danger" @click="requestDeletion(row)">删除</el-button>
      </template>
    </AppDataTable>
  </AppPage>
  <AppFormDrawer v-model="uploadOpen" :busy="busy" :title="localization.locale === 'zh-CN' ? '上传文件' : 'Upload file'" @submit="upload">
    <el-form label-width="110px"><el-form-item :label="localization.locale === 'zh-CN' ? '文件' : 'File'"><input type="file" :disabled="busy" @change="selectFile" /></el-form-item><p>{{ localization.locale === 'zh-CN' ? '文件将在完整 SHA-256 校验后开始传输；扫描完成前不可下载。' : 'The full SHA-256 is verified before transfer; downloads stay blocked until scanning completes.' }}</p><p v-if="uploadError" role="alert">{{ uploadError }}</p><p v-if="currentSession">{{ currentSession.offset }} / {{ currentSession.length }} · {{ currentSession.status }}</p><div v-if="currentSession" class="upload-actions"><el-button v-if="!paused" @click="pauseCurrent">暂停</el-button><el-button v-else @click="resumeCurrent">继续</el-button><el-button @click="cancelCurrent">取消</el-button><el-button v-if="uploadError" :disabled="busy" type="primary" @click="resumeCurrent">重试</el-button></div></el-form>
  </AppFormDrawer>
  <el-dialog v-model="candidateOpen" title="发现未完成上传" width="520px"><p>检测到相同文件的未完成会话，请选择继续方式：</p><div v-for="candidate in candidates" :key="candidate.sessionNId" class="upload-candidate"><span>{{ candidate.fileName }} · {{ candidate.offset }} / {{ candidate.length }} · epoch {{ candidate.writerEpoch }}</span><el-button size="small" type="primary" @click="continueCandidate(candidate)">继续</el-button><el-button size="small" @click="continueCandidate(candidate, true)">接管</el-button></div><el-button @click="candidateOpen = false; void startUpload(null)">新建会话</el-button></el-dialog>
</template>
