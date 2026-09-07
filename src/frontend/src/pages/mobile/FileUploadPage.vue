<script setup lang="ts">
import { computed, onBeforeUnmount, ref } from 'vue'
import { ElMessage } from 'element-plus'
import AppPage from '@/components/base/AppPage.vue'
import { getPf04Api } from '@/api/systemData/pf04Registry'
import type { UploadSessionDto } from '@/api/systemData/pf04Types'
import { sampleFingerprint, sha256File } from '@/utils/sha256File'
import { platformI18n } from '@/localization/i18n'

const api = getPf04Api()
const selectedFile = ref<File | null>(null)
const busy = ref(false)
const status = ref('')
const uploadHash = ref('')
const currentSession = ref<UploadSessionDto | null>(null)
const candidates = ref<UploadSessionDto[]>([])
const paused = ref(false)
const copy = computed(() => {
  const translate = platformI18n.global as unknown as { t: (key: string, params?: Record<string, unknown>) => unknown }
  const t = (key: string, params?: Record<string, unknown>) => translate.t(`systemData.pages.mobileFileUpload.${key}`, params) as string
  return {
    title: t('title'), description: t('description'), chooseFile: t('chooseFile'), foundSessions: t('foundSessions'),
    continue: t('continue'), takeover: t('takeover'), newSession: t('newSession'), pause: t('pause'), resume: t('resume'),
    cancel: t('cancel'), retry: t('retry'), start: t('start'), processing: t('processing'), loadingHash: t('loadingHash'),
    waitingChoice: t('waitingChoice'), uploading: (current: number, total: number) => t('uploading', { current, total }),
    completed: t('completed'), paused: t('paused'), cancelled: t('cancelled'), failed: t('failed'),
  }
})

function selectFile(event: Event): void {
  selectedFile.value = (event.target as HTMLInputElement).files?.[0] ?? null
  status.value = ''; uploadHash.value = ''; currentSession.value = null; candidates.value = []
}

async function upload(): Promise<void> {
  if (api === null || selectedFile.value === null || busy.value) return
  busy.value = true
  try {
    const file = selectedFile.value
    status.value = copy.value.loadingHash
    const hash = uploadHash.value || await sha256File(file)
    uploadHash.value = hash
    const discovery = await api.discoverUpload({ fileName: file.name, length: file.size, sampleFingerprint: await sampleFingerprint(file), purpose: 'systemdata' })
    candidates.value = discovery.candidates
    if (candidates.value.length > 0) {
      status.value = copy.value.waitingChoice
      return
    }
    await startUpload(null)
  } catch (error) {
    status.value = error instanceof Error ? error.message : copy.value.failed
  } finally {
    busy.value = false
  }
}

async function startUpload(candidate: UploadSessionDto | null, takeover = false): Promise<void> {
  if (api === null || selectedFile.value === null) return
  busy.value = true
  try {
    const file = selectedFile.value
    const hash = uploadHash.value || await sha256File(file)
    uploadHash.value = hash
    let ready: UploadSessionDto
    if (candidate === null) {
      const session = await api.createUploadSession({
        fileName: file.name,
        contentType: file.type || 'application/octet-stream',
        length: file.size,
        sha256: hash,
        purpose: 'systemdata',
        sampleFingerprint: await sampleFingerprint(file),
      })
      await api.setContentHash(session.sessionNId, hash)
      ready = await api.resumeProof(session.sessionNId, session.writerEpoch, hash)
    } else {
      ready = await api.getUploadSession(candidate.sessionNId)
      if (takeover) ready = await api.takeoverUpload(ready.sessionNId, ready.writerEpoch, hash, `mobile-${crypto.randomUUID()}`)
      if (ready.status === 'Paused') ready = await api.resumeUpload(ready.sessionNId, ready.writerEpoch, hash)
      else if (ready.status === 'WaitingForProof') ready = await api.resumeProof(ready.sessionNId, ready.writerEpoch, hash)
    }
    currentSession.value = ready
    paused.value = false
    const chunkSize = 1024 * 1024
    while (ready.offset < file.size && !paused.value) {
      const offset = ready.offset
      status.value = copy.value.uploading(Math.min(offset + chunkSize, file.size), file.size)
      ready = await api.uploadChunk(ready.transportId, file.slice(offset, Math.min(offset + chunkSize, file.size)), offset, ready.writerEpoch, ready.resumeTicket ?? '')
      currentSession.value = ready
    }
    if (paused.value) return
    await api.completeUpload(ready.sessionNId)
    status.value = copy.value.completed
    ElMessage.success(status.value)
    currentSession.value = null; uploadHash.value = ''; candidates.value = []
  } catch (error) {
    status.value = error instanceof Error ? error.message : copy.value.failed
  } finally {
    busy.value = false
  }
}

async function continueCandidate(candidate: UploadSessionDto, takeover = false): Promise<void> {
  candidates.value = []
  await startUpload(candidate, takeover)
}

async function pauseCurrent(): Promise<void> {
  if (api === null || currentSession.value === null) return
  paused.value = true
  currentSession.value = await api.pauseUpload(currentSession.value.sessionNId)
  status.value = copy.value.paused
}

async function resumeCurrent(): Promise<void> {
  if (api === null || currentSession.value === null) return
  currentSession.value = await api.getUploadSession(currentSession.value.sessionNId)
  await startUpload(currentSession.value)
}

async function cancelCurrent(): Promise<void> {
  if (api === null || currentSession.value === null) return
  paused.value = true
  await api.cancelUpload(currentSession.value.sessionNId, 'user-cancelled')
  currentSession.value = null; paused.value = false; status.value = copy.value.cancelled
}

onBeforeUnmount(() => { paused.value = true })
</script>

<template>
  <AppPage :title="copy.title" :description="copy.description">
    <section class="mobile-file-upload" :aria-label="copy.title">
      <label><span class="sr-only">{{ copy.chooseFile }}</span><input type="file" :disabled="busy" @change="selectFile" /></label>
      <p v-if="selectedFile">{{ selectedFile.name }} · {{ selectedFile.size }} bytes</p>
      <div v-if="candidates.length" class="upload-candidates"><p>{{ copy.foundSessions }}</p><div v-for="candidate in candidates" :key="candidate.sessionNId"><span>{{ candidate.offset }} / {{ candidate.length }} · epoch {{ candidate.writerEpoch }}</span><button type="button" :disabled="busy" @click="continueCandidate(candidate)">{{ copy.continue }}</button><button type="button" :disabled="busy" @click="continueCandidate(candidate, true)">{{ copy.takeover }}</button></div><button type="button" :disabled="busy" @click="candidates = [] ; void startUpload(null)">{{ copy.newSession }}</button></div>
      <div v-if="currentSession" class="upload-controls"><button type="button" @click="paused ? resumeCurrent() : pauseCurrent()">{{ paused ? copy.resume : copy.pause }}</button><button type="button" @click="cancelCurrent">{{ copy.cancel }}</button><button v-if="status.includes(copy.failed)" type="button" :disabled="busy" @click="resumeCurrent">{{ copy.retry }}</button></div>
      <button type="button" :disabled="busy || selectedFile === null || currentSession !== null" @click="upload">
        {{ busy ? copy.processing : copy.start }}
      </button>
      <p role="status">{{ status }}</p>
    </section>
  </AppPage>
</template>

<style scoped>
.mobile-file-upload { display: grid; gap: var(--ip-space-4); padding: var(--ip-space-4); background: var(--ip-color-bg-container); border: 1px solid var(--ip-color-border); border-radius: var(--ip-radius-lg); }
.mobile-file-upload p { margin: 0; color: var(--ip-color-text-secondary); word-break: break-word; }
.mobile-file-upload button { min-height: var(--ip-touch-min-size-mobile); border: 1px solid var(--ip-color-primary); border-radius: var(--ip-radius-md); background: var(--ip-color-primary); color: var(--ip-color-text-on-primary); font-size: var(--ip-font-size-md); }
.mobile-file-upload button:disabled { cursor: not-allowed; opacity: 0.6; }
</style>
