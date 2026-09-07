<script setup lang="ts">
import { computed, onBeforeUnmount, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { ApiError } from '@/api/errors'
import AppPage from '@/components/base/AppPage.vue'
import { getPf04Api } from '@/api/systemData/pf04Registry'
import type { UploadSessionDto } from '@/api/systemData/pf04Types'
import { sampleFingerprint, sha256File } from '@/utils/sha256File'
import { platformI18n } from '@/localization/i18n'

const selectedFile = ref<File | null>(null)
const busy = ref(false)
const status = ref('')
const uploadHash = ref('')
const currentSession = ref<UploadSessionDto | null>(null)
const candidates = ref<UploadSessionDto[]>([])
const stopped = ref(false)
const failed = ref(false)
let activeUploadController: AbortController | null = null
let uploadRun = 0
const copy = computed(() => {
  const translate = platformI18n.global as unknown as { t: (key: string, params?: Record<string, unknown>) => unknown }
  const t = (key: string, params?: Record<string, unknown>) => translate.t(`systemData.pages.mobileFileUpload.${key}`, params) as string
  return {
    title: t('title'), description: t('description'), chooseFile: t('chooseFile'), foundSessions: t('foundSessions'),
    continue: t('continue'), takeover: t('takeover'), newSession: t('newSession'), stop: t('stop'), resume: t('resume'),
    retry: t('retry'), start: t('start'), processing: t('processing'), loadingHash: t('loadingHash'),
    waitingChoice: t('waitingChoice'), uploading: (current: number, total: number) => t('uploading', { current, total }),
    completed: t('completed'), stopped: t('stopped'), failed: t('failed'),
  }
})

const progressPercent = computed(() => {
  const session = currentSession.value
  if (session === null || session.length <= 0) return 0
  return Math.min(100, Math.round((session.offset / session.length) * 100))
})

function isUserAbort(error: unknown): boolean {
  return error instanceof ApiError && error.kind === 'cancelled'
}

function selectFile(event: Event): void {
  selectedFile.value = (event.target as HTMLInputElement).files?.[0] ?? null
  status.value = ''; uploadHash.value = ''; currentSession.value = null; candidates.value = []; stopped.value = false
  failed.value = false
}

async function upload(): Promise<void> {
  const api = getPf04Api()
  if (api === null || selectedFile.value === null || busy.value) return
  busy.value = true
  try {
    const file = selectedFile.value
    failed.value = false
    status.value = copy.value.loadingHash
    const hash = uploadHash.value || await sha256File(file)
    uploadHash.value = hash
    const discovery = await api.discoverUpload({ fileName: file.name, length: file.size, sampleFingerprint: await sampleFingerprint(file), purpose: 'systemdata' })
    candidates.value = discovery.candidates
    if (candidates.value.length > 0) {
      if (candidates.value.length === 1) {
        const candidate = candidates.value[0]!
        candidates.value = []
        await startUpload(candidate)
      } else {
        status.value = copy.value.waitingChoice
      }
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
  const api = getPf04Api()
  if (api === null || selectedFile.value === null) return
  const run = ++uploadRun
  const controller = new AbortController()
  activeUploadController?.abort()
  activeUploadController = controller
  busy.value = true
  stopped.value = false
  failed.value = false
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
    stopped.value = false
    const chunkSize = 1024 * 1024
    while (ready.offset < file.size) {
      if (controller.signal.aborted || run !== uploadRun) return
      const offset = ready.offset
      status.value = copy.value.uploading(Math.min(offset + chunkSize, file.size), file.size)
      ready = await api.uploadChunk(
        ready.transportId,
        file.slice(offset, Math.min(offset + chunkSize, file.size)),
        offset,
        ready.writerEpoch,
        ready.resumeTicket ?? '',
        controller.signal,
      )
      if (controller.signal.aborted || run !== uploadRun) return
      currentSession.value = ready
    }
    if (controller.signal.aborted || run !== uploadRun) return
    await api.completeUpload(ready.sessionNId)
    status.value = copy.value.completed
    ElMessage.success(status.value)
    currentSession.value = null; uploadHash.value = ''; candidates.value = []
  } catch (error) {
    if (run !== uploadRun) return
    if (controller.signal.aborted || isUserAbort(error)) {
      status.value = copy.value.stopped
    } else {
      failed.value = true
      status.value = error instanceof Error ? error.message : copy.value.failed
    }
  } finally {
    if (activeUploadController === controller) activeUploadController = null
    if (run === uploadRun) busy.value = false
  }
}

async function continueCandidate(candidate: UploadSessionDto, takeover = false): Promise<void> {
  candidates.value = []
  await startUpload(candidate, takeover)
}

async function resumeCurrent(): Promise<void> {
  if (selectedFile.value === null) return
  await upload()
}

function stopCurrent(): void {
  if (currentSession.value === null) return
  uploadRun++
  stopped.value = true
  failed.value = false
  activeUploadController?.abort()
  status.value = copy.value.stopped
  busy.value = false
}

onBeforeUnmount(() => {
  uploadRun++
  activeUploadController?.abort()
})
</script>

<template>
  <AppPage :title="copy.title" :description="copy.description">
    <section class="mobile-file-upload" :aria-label="copy.title">
      <label><span class="sr-only">{{ copy.chooseFile }}</span><input type="file" :disabled="busy" @change="selectFile" /></label>
      <p v-if="selectedFile">{{ selectedFile.name }} · {{ selectedFile.size }} bytes</p>
      <div v-if="candidates.length" class="upload-candidates"><p>{{ copy.foundSessions }}</p><div v-for="candidate in candidates" :key="candidate.sessionNId"><span>{{ candidate.offset }} / {{ candidate.length }} · epoch {{ candidate.writerEpoch }}</span><button type="button" :disabled="busy" @click="continueCandidate(candidate)">{{ copy.continue }}</button><button type="button" :disabled="busy" @click="continueCandidate(candidate, true)">{{ copy.takeover }}</button></div><button type="button" :disabled="busy" @click="candidates = [] ; void startUpload(null)">{{ copy.newSession }}</button></div>
      <div v-if="currentSession" class="upload-progress" aria-live="polite">
        <div class="upload-progress__meta"><span>{{ currentSession.offset }} / {{ currentSession.length }} bytes</span><span>{{ progressPercent }}%</span></div>
        <div class="upload-progress__track" role="progressbar" :aria-valuenow="progressPercent" aria-valuemin="0" aria-valuemax="100"><span class="upload-progress__value" :style="{ width: `${progressPercent}%` }" /></div>
      </div>
      <div v-if="currentSession" class="upload-controls"><button type="button" :disabled="busy && stopped" @click="stopped ? resumeCurrent() : stopCurrent()">{{ stopped ? copy.resume : copy.stop }}</button><button v-if="failed" type="button" :disabled="busy" @click="resumeCurrent">{{ copy.retry }}</button></div>
      <button type="button" :disabled="busy || selectedFile === null || (currentSession !== null && !stopped)" @click="upload">
        {{ busy ? copy.processing : stopped ? copy.resume : copy.start }}
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
.upload-progress { display: grid; gap: var(--ip-space-2); }
.upload-progress__meta { display: flex; justify-content: space-between; color: var(--ip-color-text-secondary); font-size: var(--ip-font-size-sm); }
.upload-progress__track { height: 8px; overflow: hidden; border-radius: 999px; background: var(--ip-color-bg-muted); }
.upload-progress__value { display: block; height: 100%; border-radius: inherit; background: var(--ip-color-primary); transition: width 150ms ease; }
.upload-controls { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: var(--ip-space-3); }

@media (prefers-reduced-motion: reduce) {
  .upload-progress__value { transition: none; }
}
</style>
