<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'

import { localeMessages } from '@/localization/i18n'
import { usePlatformLocale } from '@/localization/localeContext'
import { useAuthStore } from '@/stores/authStore'
import { useCollaborationMediaStore } from '@/stores/collaborationMediaStore'
import { formatMediaError } from '@/components/collaboration/mediaError'

const auth = useAuthStore()
const media = useCollaborationMediaStore()
const locale = usePlatformLocale()
const copy = computed(() => localeMessages[locale.value].collaboration.media)
const video = ref<HTMLVideoElement | null>(null)
const panel = ref<HTMLElement | null>(null)
const open = ref(false)
const fullscreen = ref(false)
const playbackState = ref<'connecting' | 'playing' | 'failed'>('connecting')
const position = ref({ left: 0, top: 0 })
const size = ref({ width: 520, height: 340 })
let lastSessionNId: string | null = null
let wasActive = false
let dragStart: { pointerX: number; pointerY: number; left: number; top: number } | null = null
let restoreState: { position: { left: number; top: number }; size: { width: number; height: number } } | null = null

const isViewer = computed(() => media.screen?.viewerUserNId === auth.user?.userId)
const active = computed(() => media.hasScreen && isViewer.value)
const screenErrorText = computed(() => formatMediaError(copy.value, media.screenErrorMessage))
const panelStyle = computed(() => fullscreen.value
  ? { left: '1rem', top: '1rem', width: 'calc(100vw - 2rem)', height: 'calc(100vh - 2rem)' }
  : { left: `${position.value.left}px`, top: `${position.value.top}px`, width: `${size.value.width}px`, height: `${size.value.height}px` })

watch(
  [() => media.screen?.sessionNId ?? null, active],
  ([sessionNId, isActive]) => {
    if (isActive && (!wasActive || sessionNId !== lastSessionNId)) {
      open.value = true
      fullscreen.value = false
      centerWindow()
    }
    if (!isActive) {
      open.value = false
      fullscreen.value = false
    }
    lastSessionNId = sessionNId
    wasActive = isActive
  },
  { immediate: true },
)

watch(
  [() => media.remoteStream, open],
  async ([stream, isOpen]) => {
    if (!isOpen) return
    playbackState.value = 'connecting'
    await nextTick()
    if (!video.value) return
    video.value.srcObject = videoOnly(stream)
    if (stream?.getVideoTracks().length) await media.resumePlayback(video.value, 'Screen')
  },
  { immediate: true },
)

watch(() => media.screenOperation, (operation) => {
  if (!active.value) return
  if (operation === 'Error') playbackState.value = 'failed'
  else if (operation === 'Connecting' || operation === 'Ringing') playbackState.value = 'connecting'
})

function centerWindow(): void {
  position.value = {
    left: Math.max(12, window.innerWidth - size.value.width - 24),
    top: Math.max(12, window.innerHeight - size.value.height - 72),
  }
}

function videoOnly(stream: MediaStream | null): MediaStream | null {
  return stream ? new MediaStream(stream.getVideoTracks()) : null
}

function minimize(): void {
  if (document.fullscreenElement === panel.value) void document.exitFullscreen()
  open.value = false
}

function restoreViewer(): void {
  open.value = true
}

function toggleFullscreen(): void {
  if (fullscreen.value) {
    if (document.fullscreenElement === panel.value) {
      void document.exitFullscreen()
    } else {
      restoreWindowState()
    }
    return
  }
  const rect = panel.value?.getBoundingClientRect()
  restoreState = {
    position: { left: rect?.left ?? position.value.left, top: rect?.top ?? position.value.top },
    size: { width: rect?.width ?? size.value.width, height: rect?.height ?? size.value.height },
  }
  if (panel.value?.requestFullscreen) {
    void panel.value.requestFullscreen().catch(() => { fullscreen.value = true })
  } else {
    fullscreen.value = true
  }
}

function restoreWindowState(): void {
  if (restoreState) {
    position.value = restoreState.position
    size.value = restoreState.size
  }
  restoreState = null
  fullscreen.value = false
}

function onFullscreenChange(): void {
  if (document.fullscreenElement === panel.value) {
    fullscreen.value = true
    return
  }
  if (fullscreen.value) restoreWindowState()
}

function startDrag(event: PointerEvent): void {
  if (fullscreen.value || event.button !== 0) return
  const rect = panel.value?.getBoundingClientRect()
  const width = rect?.width || panel.value?.offsetWidth || size.value.width
  const height = rect?.height || panel.value?.offsetHeight || size.value.height
  if (width > 0 && height > 0) {
    size.value = { width, height }
    if (rect) position.value = { left: rect.left, top: rect.top }
  }
  dragStart = { pointerX: event.clientX, pointerY: event.clientY, left: position.value.left, top: position.value.top }
  document.addEventListener('pointermove', drag)
  document.addEventListener('pointerup', stopDrag, { once: true })
}

function drag(event: PointerEvent): void {
  if (!dragStart) return
  const width = panel.value?.offsetWidth ?? size.value.width
  const height = panel.value?.offsetHeight ?? size.value.height
  position.value = {
    left: Math.min(Math.max(8, dragStart.left + event.clientX - dragStart.pointerX), Math.max(8, window.innerWidth - width - 8)),
    top: Math.min(Math.max(8, dragStart.top + event.clientY - dragStart.pointerY), Math.max(8, window.innerHeight - height - 8)),
  }
}

function stopDrag(): void {
  dragStart = null
  document.removeEventListener('pointermove', drag)
}

function markPlayable(): void {
  playbackState.value = 'playing'
  void media.markScreenPlayable()
}

function markFailed(): void {
  playbackState.value = 'failed'
  media.markPlaybackBlocked('Screen')
}

async function resumePlayback(): Promise<void> {
  if (!video.value) return
  playbackState.value = 'connecting'
  await media.resumePlayback(video.value, 'Screen')
  if (!media.screenPlaybackBlocked) playbackState.value = 'playing'
}

function endViewing(): void {
  void media.endScreenShare()
}

onMounted(() => document.addEventListener('fullscreenchange', onFullscreenChange))
onBeforeUnmount(() => {
  stopDrag()
  document.removeEventListener('fullscreenchange', onFullscreenChange)
})
</script>

<template>
  <button
    v-if="active && !open"
    type="button"
    class="collaboration-screen-float__reopen"
    @click="restoreViewer"
  >
    {{ copy.openViewer }}
  </button>
  <section
    v-if="active && open"
    ref="panel"
    class="collaboration-screen-float"
    :class="{ 'is-fullscreen': fullscreen }"
    :style="panelStyle"
    aria-live="polite"
  >
    <header class="collaboration-screen-float__header" @pointerdown="startDrag">
      <strong>{{ copy.remoteScreen }}</strong>
      <div class="collaboration-screen-float__actions" @pointerdown.stop>
        <button type="button" :aria-label="copy.minimize" @click="minimize">−</button>
        <button type="button" :aria-label="fullscreen ? copy.restore : copy.fullscreen" @click="toggleFullscreen">
          {{ fullscreen ? copy.restore : copy.fullscreen }}
        </button>
        <button type="button" @click="endViewing">{{ copy.leaveViewing }}</button>
      </div>
    </header>
    <div class="collaboration-screen-float__body">
      <video
        ref="video"
        autoplay
        muted
        playsinline
        :aria-label="copy.remoteScreen"
        @playing="markPlayable"
        @error="markFailed"
      />
      <p v-if="playbackState === 'connecting'" class="collaboration-screen-float__status">{{ copy.screenConnecting }}</p>
      <p v-else-if="playbackState === 'failed'" class="collaboration-screen-float__status collaboration-screen-float__status--error" role="alert">{{ screenErrorText ?? copy.screenFailed }}</p>
      <button v-if="media.screenPlaybackBlocked" type="button" @click="resumePlayback">{{ copy.resumePlayback }}</button>
    </div>
  </section>
</template>

<style scoped>
.collaboration-screen-float {
  position: fixed;
  box-sizing: border-box;
  z-index: 45;
  min-width: 320px;
  min-height: 220px;
  max-width: calc(100vw - 16px);
  max-height: calc(100vh - 16px);
  overflow: hidden;
  resize: both;
  border: 1px solid var(--color-border, #d9e0e7);
  border-radius: 0.75rem;
  background: #111827;
  box-shadow: 0 16px 40px rgb(0 0 0 / 24%);
}

.collaboration-screen-float.is-fullscreen {
  resize: none;
}

.collaboration-screen-float:fullscreen {
  inset: 0 !important;
  width: 100vw !important;
  height: 100vh !important;
  max-width: none;
  max-height: none;
  border: 0;
  border-radius: 0;
  resize: none;
}

.collaboration-screen-float__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.75rem;
  min-height: 2.3rem;
  padding: 0.35rem 0.55rem 0.35rem 0.75rem;
  color: #fff;
  background: #1f2937;
  cursor: grab;
  user-select: none;
}

.collaboration-screen-float__actions {
  display: flex;
  flex-wrap: wrap;
  gap: 0.3rem;
}

.collaboration-screen-float button {
  border: 1px solid rgb(255 255 255 / 30%);
  border-radius: 0.35rem;
  padding: 0.2rem 0.4rem;
  color: inherit;
  background: rgb(255 255 255 / 10%);
  cursor: pointer;
}

.collaboration-screen-float__body {
  display: grid;
  place-items: center;
  gap: 0.4rem;
  height: calc(100% - 2.3rem);
  padding: 0.45rem;
  box-sizing: border-box;
}

.collaboration-screen-float video {
  width: 100%;
  height: 100%;
  min-height: 0;
  object-fit: contain;
  background: #030712;
}

.collaboration-screen-float__status {
  position: absolute;
  margin: 0;
  color: #e5e7eb;
  text-align: center;
  pointer-events: none;
}

.collaboration-screen-float__status--error {
  color: #fecaca;
}

.collaboration-screen-float__reopen {
  position: fixed;
  z-index: 45;
  right: 1rem;
  bottom: 1rem;
  border: 1px solid var(--color-border, #d9e0e7);
  border-radius: 0.5rem;
  padding: 0.45rem 0.65rem;
  background: var(--color-surface, #fff);
  box-shadow: 0 8px 24px rgb(0 0 0 / 12%);
  cursor: pointer;
}
</style>
