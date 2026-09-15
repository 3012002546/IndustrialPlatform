<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'

import { getCollaborationRealtime } from '@/api/collaborationHub'
import { loadRuntimeConfig } from '@/config/runtimeConfig'
import { ROUTE_NAMES } from '@/router/routeNames'
import { isCollaborationRoute } from '@/systemData/runtime/collaborationRoute'
import { localeMessages } from '@/localization/i18n'
import { usePlatformLocale } from '@/localization/localeContext'
import { useAuthStore } from '@/stores/authStore'
import { useCollaborationMediaStore } from '@/stores/collaborationMediaStore'
import { formatMediaError } from '@/components/collaboration/mediaError'
import ScreenShareFloatingWindow from '@/components/collaboration/ScreenShareFloatingWindow.vue'

const auth = useAuthStore()
const media = useCollaborationMediaStore()
const router = useRouter()
const locale = usePlatformLocale()
const copy = computed(() => localeMessages[locale.value].collaboration.media)
const realtime = getCollaborationRealtime()
const embedded = loadRuntimeConfig().authMode === 'embedded'
const audio = ref<HTMLAudioElement | null>(null)
let subjectKey = ''
const mediaErrorText = computed(() => formatMediaError(
  copy.value,
  media.screenErrorMessage ?? media.voiceErrorMessage ?? media.errorMessage,
))

function currentSubjectKey(): string {
  const user = auth.user
  return user ? `${user.tenantId}:${user.userId}` : ''
}

function syncSubject(): void {
  const next = currentSubjectKey()
  if (next === subjectKey) return
  const previous = subjectKey
  subjectKey = next
  if (previous && next && previous !== next) void media.disposeAll()
  if (next) media.setRealtime(realtime)
  else void media.disposeAll()
}

let pageTransition = Promise.resolve()
function syncPage(): void {
  pageTransition = pageTransition.then(async () => {
    if (embedded && !isCollaborationRoute(router.currentRoute.value.name)) {
      await media.endAll()
      return
    }
    syncSubject()
  }).catch(() => undefined)
}

function handlePageHide(): void {
  if (!embedded) return
  void media.endAll()
  // Closing the page is best effort; the server also ends media by the
  // disconnected SignalR connection as a durable fallback.
  void realtime.stop().catch(() => undefined)
}

onMounted(() => {
  syncPage()
  window.addEventListener('pagehide', handlePageHide)
})
watch(() => auth.user, syncPage, { deep: true })
watch(() => router.currentRoute.value.name, syncPage)
watch(
  () => media.remoteStream,
  async (stream) => {
    await nextTick()
    if (!audio.value) return
    audio.value.srcObject = audioOnly(stream)
    if (stream?.getAudioTracks().length) await media.resumePlayback(audio.value, 'Voice')
  },
  { immediate: true },
)
onBeforeUnmount(() => {
  window.removeEventListener('pagehide', handlePageHide)
  if (embedded) void media.endAll()
  else void media.disposeAll()
})

function openConversation(): void {
  if (!media.mediaConversationNId) return
  void router.push({ name: ROUTE_NAMES.collaborationConversation, params: { conversationNId: media.mediaConversationNId } })
}

function statusLabel(): string {
  if (media.voice?.state === 'Active' || media.voiceOperation === 'Active') return copy.value.voiceActive
  if (media.screen?.state === 'Sharing' || media.screenOperation === 'Active') return copy.value.screenActive
  if (media.voice?.callNId && media.voiceOperation === 'Error') return copy.value.failed
  if (media.screen?.sessionNId && media.screenOperation === 'Error') return copy.value.failed
  if (media.voice?.state === 'Ringing') return copy.value.voice
  if (media.voice?.state === 'Accepted' || media.voice?.state === 'Connecting') {
    return media.hasLocalVoiceCapture ? copy.value.connecting : copy.value.authorizing
  }
  if (media.screen?.state === 'Pending') return copy.value.screen
  if (media.screen?.state === 'Accepted' || media.screen?.state === 'Connecting') {
    const hasLocalCapture = media.localScreenStream?.getVideoTracks().some((track) => track.readyState === 'live') ?? false
    return hasLocalCapture ? copy.value.connecting : copy.value.authorizing
  }
  return copy.value.connecting
}

function audioOnly(stream: MediaStream | null): MediaStream | null {
  return stream ? new MediaStream(stream.getAudioTracks()) : null
}
</script>

<template>
  <ScreenShareFloatingWindow />
  <div v-if="media.mediaSession" class="collaboration-media-host" role="status" aria-live="polite">
    <p v-if="mediaErrorText" class="collaboration-media-host__error" role="alert">{{ mediaErrorText }}</p>
    <template>
      <span>{{ statusLabel() }}</span>
      <button v-if="media.voice?.state === 'Ringing' && media.voice.calleeUserNId === auth.user?.userId" type="button" @click="media.respondVoiceCall(media.voice.callNId, 'Accept')">{{ copy.accept }}</button>
      <button v-if="media.voice?.state === 'Ringing' && media.voice.calleeUserNId === auth.user?.userId" type="button" @click="media.respondVoiceCall(media.voice.callNId, 'Decline')">{{ copy.decline }}</button>
      <button v-if="media.screen?.state === 'Pending' && media.screen.inviteeUserNId === auth.user?.userId" type="button" @click="media.respondScreenShare(media.screen.sessionNId, 'Accept')">{{ copy.accept }}</button>
      <button v-if="media.screen?.state === 'Pending' && media.screen.inviteeUserNId === auth.user?.userId" type="button" @click="media.respondScreenShare(media.screen.sessionNId, 'Decline')">{{ copy.decline }}</button>
      <button type="button" @click="openConversation">{{ copy.returnToChat }}</button>
      <button v-if="media.hasVoice" type="button" @click="media.endVoiceCall()">{{ copy.endVoice }}</button>
      <button v-if="media.hasScreen" type="button" @click="media.endScreenShare()">{{ copy.stopSharing }}</button>
      <button v-if="media.mediaSession" type="button" @click="media.endAll()">{{ copy.endAll }}</button>
      <button v-if="media.voicePlaybackBlocked" type="button" @click="audio && media.resumePlayback(audio, 'Voice')">{{ copy.resumePlayback }}</button>
      <audio v-if="media.remoteStream" ref="audio" autoplay :aria-label="copy.remoteVoice" @playing="media.markVoicePlayable" @error="media.markPlaybackBlocked('Voice')" />
    </template>
  </div>
</template>

<style scoped>
.collaboration-media-host {
  position: fixed;
  z-index: 40;
  right: 1rem;
  bottom: 1rem;
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.6rem 0.75rem;
  border: 1px solid var(--color-border, #d9e0e7);
  border-radius: 0.75rem;
  background: var(--color-surface, #fff);
  box-shadow: 0 8px 24px rgb(0 0 0 / 12%);
  font-size: 0.85rem;
}

.collaboration-media-host audio {
  width: 0;
  height: 0;
}

.collaboration-media-host__error {
  margin: 0;
  color: var(--color-danger, #b42318);
}

.collaboration-media-host button {
  border: 0;
  border-radius: 0.4rem;
  padding: 0.25rem 0.5rem;
  background: var(--color-primary, #2563eb);
  color: #fff;
  cursor: pointer;
}
</style>
