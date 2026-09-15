import { computed, ref, shallowRef } from 'vue'
import { defineStore } from 'pinia'

import type { CollaborationRealtime } from '@/api/collaborationHub'
import { createCorrelationId } from '@/api/correlation'
import type {
  ConversationMediaDto,
  KeepAliveMediaDto,
  MediaBindingDto,
  MediaSignalRequest,
  ScreenDto,
  VoiceDto,
  VoiceMutedDto,
  MediaResult,
  MediaContextChangedDto,
} from '@/api/collaborationMedia'
import { mediaLong } from '@/api/collaborationMedia'
import { useAuthStore } from '@/stores/authStore'
import { MediaIceQueue } from '@/stores/mediaIceQueue'

type MediaOperation = 'Idle' | 'Ringing' | 'Connecting' | 'Active' | 'Stopping' | 'Error'
type MediaSignalCapability = 'Screen' | 'Voice' | null
type MediaSignalErrorSource = {
  lifecycleGeneration: number
  mediaContextNId: string
  contextRevision: number
  negotiationNId: string
  capability: MediaSignalCapability
  capabilityNId: string | null
}

export const useCollaborationMediaStore = defineStore('collaboration-media', () => {
  const auth = useAuthStore()
  const conversationNId = ref<string | null>(null)
  const mediaConversationNId = ref<string | null>(null)
  const screen = shallowRef<ScreenDto | null>(null)
  const voice = shallowRef<VoiceDto | null>(null)
  const binding = shallowRef<MediaBindingDto | null>(null)
  const mediaContextNId = ref<string | null>(null)
  const screenOperation = ref<MediaOperation>('Idle')
  const voiceOperation = ref<MediaOperation>('Idle')
  const muted = ref(false)
  const errorMessage = ref<string | null>(null)
  const screenErrorMessage = ref<string | null>(null)
  const voiceErrorMessage = ref<string | null>(null)
  const screenPlaybackBlocked = ref(false)
  const voicePlaybackBlocked = ref(false)
  const playbackBlocked = computed(() => screenPlaybackBlocked.value || voicePlaybackBlocked.value)
  const remoteStream = shallowRef<MediaStream | null>(null)
  const localScreenStream = shallowRef<MediaStream | null>(null)
  const localVoiceStream = shallowRef<MediaStream | null>(null)
  const peerConnection = shallowRef<RTCPeerConnection | null>(null)

  let realtime: CollaborationRealtime | null = null
  let unsubscribe: (() => void) | null = null
  let keepAliveTimer: number | undefined
  let signalSequence = 0
  let screenGeneration = 0
  let voiceGeneration = 0
  let negotiationNId = ''
  let makingOffer = false
  let settingRemoteAnswer = false
  let ignoreOffer = false
  let waitingForPeer = false
  let negotiationQueued = false
  let contextNegotiationQueued = false
  let bindingPromise: Promise<MediaBindingDto | null> | null = null
  let bindingPromiseKey: string | null = null
  let bindingQueuedKey: string | null = null
  let bindingFailureKey: string | null = null
  let mediaLifecycleGeneration = 0
  let mediaSignalErrorSource: MediaSignalErrorSource | null = null
  let mediaSignalErrorCode: string | null = null
  let pendingContextChange: MediaContextChangedDto | null = null
  const pendingSignals: MediaSignalRequest[] = []
  let voicePlaybackObserved = false
  let screenIntent = false
  let voiceIntent = false
  let endingAllPromise: Promise<void> | null = null
  const readyKeys = new Set<string>()
  const iceQueue = new MediaIceQueue((negotiationNId) => expireIce(negotiationNId))
  const negotiationFailureTargets = new Map<string, {
    screenSessionNId: string | null
    screenGeneration: number
    voiceCallNId: string | null
    voiceGeneration: number
  }>()
  const stoppedReportKeys = new Set<string>()

  type StopResult = {
    senderDetached: boolean
    captureTracksEnded: boolean
    playbackDetached: boolean
  }
  const stopResults = new Map<string, StopResult>()

  const hasScreen = computed(() => screen.value?.state === 'Accepted' || screen.value?.state === 'Connecting' || screen.value?.state === 'Sharing')
  const hasVoice = computed(() => voice.value?.state === 'Accepted' || voice.value?.state === 'Connecting' || voice.value?.state === 'Active')
  const hasLocalVoiceCapture = computed(() => localVoiceStream.value?.getAudioTracks().some((track) => track.readyState === 'live') ?? false)
  const mediaSession = computed(() =>
    screen.value?.state === 'Pending' || hasScreen.value || voice.value?.state === 'Ringing' || hasVoice.value,
  )
  const isActive = computed(() => hasScreen.value || hasVoice.value)

  function hasLiveScreenCapture(): boolean {
    return localScreenStream.value?.getVideoTracks().some((track) => track.readyState === 'live') ?? false
  }

  function setRealtime(next: CollaborationRealtime): void {
    if (realtime === next) return
    unsubscribe?.()
    realtime = next
    unsubscribe = realtime.subscribe?.({
      onScreenChanged: (event) => {
        if (mediaConversationNId.value && mediaConversationNId.value !== event.conversationNId) return
        const ownsScreenEndpoint = screenIntent
          || hasLiveScreenCapture()
          || (binding.value !== null && binding.value.mediaContextNId === mediaContextNId.value)
        mediaConversationNId.value = event.conversationNId
        applyScreen(event)
        if (ownsScreenEndpoint && (event.state === 'Accepted' || event.state === 'Connecting') && mediaConversationNId.value)
          void ensureBindingIfNeeded()
      },
      onVoiceChanged: (event) => {
        if (mediaConversationNId.value && mediaConversationNId.value !== event.conversationNId) return
        const ownsVoiceEndpoint = voiceIntent
          || hasLocalVoiceCapture.value
          || (binding.value !== null && binding.value.mediaContextNId === mediaContextNId.value)
        mediaConversationNId.value = event.conversationNId
        applyVoice(event)
        if (ownsVoiceEndpoint && (event.state === 'Accepted' || event.state === 'Connecting') && mediaConversationNId.value)
          void ensureBindingIfNeeded()
      },
      onMediaSignal: (event) => void receiveSignal(event),
      onMediaContextChanged: (event) => {
        if (!rememberContextChange(event)) return
        const currentBinding = binding.value
        if (currentBinding?.mediaContextNId !== event.mediaContextNId) return
        const previousRevision = mediaLong(currentBinding.contextRevision)
        const shouldNegotiate = waitingForPeer || mediaLong(event.contextRevision) > previousRevision
        if (mediaLong(event.contextRevision) > previousRevision)
          binding.value = { ...currentBinding, contextRevision: event.contextRevision }
        clearStaleMediaSignalError()
        const nextBinding = binding.value
        if (shouldNegotiate && nextBinding?.endpointRole === 'Low') {
          waitingForPeer = false
          contextNegotiationQueued = true
          void restartNegotiationForContext(nextBinding).catch(() => undefined)
        }
      },
      onVoiceMuted: (event) => applyMuted(event),
      onReconnected: async () => {
        if (mediaConversationNId.value) await refresh(mediaConversationNId.value, true)
        else if (conversationNId.value) await refresh(conversationNId.value)
      },
    }) ?? null
    void realtime.start().catch(() => undefined)
  }

  async function refresh(nextConversationNId: string, preserveView = false): Promise<ConversationMediaDto | null> {
    if (!preserveView) conversationNId.value = nextConversationNId
    if (!realtime) return null
    const result = await realtime.getConversationMedia({ conversationNId: nextConversationNId })
    if (!result.ok || !result.data) {
      errorMessage.value = result.error?.code ?? 'MEDIA_DEPENDENCY_UNAVAILABLE'
      return null
    }
    if (mediaSession.value && mediaConversationNId.value !== null && mediaConversationNId.value !== nextConversationNId)
      return result.data
    applyConversation(result.data)
    mediaConversationNId.value = nextConversationNId
    if ((hasScreen.value || hasVoice.value)
      && (screenIntent || voiceIntent || hasLiveScreenCapture() || hasLocalVoiceCapture.value || binding.value !== null))
      await ensureBinding()
    return result.data
  }

  async function inviteScreenShare(direction: string, captureWindow: Window = window): Promise<ScreenDto | null> {
    if (!realtime || !conversationNId.value) {
      errorMessage.value = 'MEDIA_DEPENDENCY_UNAVAILABLE'
      screenOperation.value = 'Error'
      return null
    }
    if (mediaSession.value && mediaConversationNId.value !== conversationNId.value) {
      errorMessage.value = 'MEDIA_BUSY'
      screenOperation.value = 'Error'
      return null
    }
    const shouldCapture = direction === 'ShareMine'
    const hadCapture = hasLiveScreenCapture()
    let capturedHere = false
    screenIntent = true
    try {
      if (shouldCapture && !hadCapture) {
        await captureScreen(captureWindow)
        capturedHere = true
      }
      mediaConversationNId.value = conversationNId.value
      screenOperation.value = 'Ringing'
      const result = await realtime.inviteScreenShare({ conversationNId: conversationNId.value, direction, requestNId: requestId() })
      const value = consumeResult(result, screen, (next) => {
        screenOperation.value = next?.state === 'Pending' ? 'Ringing' : operationForScreen(next)
      })
      if (!value) {
        screenIntent = false
        if (capturedHere) await stopLocalScreen()
      }
      return value
    } catch (error) {
      screenIntent = false
      if (capturedHere || (!hadCapture && shouldCapture)) await stopLocalScreen()
      errorMessage.value = extractMediaErrorCode(error, 'MEDIA_SIGNAL_FAILED', 'Screen')
      screenOperation.value = 'Error'
      return null
    }
  }

  async function respondScreenShare(sessionNId: string, answer: string, captureWindow: Window = window): Promise<ScreenDto | null> {
    if (!realtime) return null
    const current = screen.value
    const shouldCapture = answer === 'Accept' && current?.sharerUserNId === auth.user?.userId
    const hadCapture = hasLiveScreenCapture()
    let capturedHere = false
    try {
      if (shouldCapture && !hadCapture) {
        await captureScreen(captureWindow)
        capturedHere = true
      }
      const result = await realtime.respondScreenShare({ sessionNId, answer, expectedVersion: mediaLong(current?.version) })
      const value = consumeResult(result, screen, (next) => {
        screenOperation.value = operationForScreen(next)
      })
      if (value?.state === 'Accepted') screenIntent = true
      if (!value) {
        screenIntent = false
        if (capturedHere) await stopLocalScreen()
      }
      if (value?.state === 'Accepted' && mediaConversationNId.value) await ensureBinding()
      return value
    } catch (error) {
      if (answer === 'Accept') screenIntent = false
      if (capturedHere || (!hadCapture && shouldCapture)) await stopLocalScreen()
      errorMessage.value = extractMediaErrorCode(error, 'MEDIA_SIGNAL_FAILED', shouldCapture ? 'Screen' : null)
      screenOperation.value = 'Error'
      return null
    }
  }

  async function endScreenShare(reason = 'BrowserStopped'): Promise<ScreenDto | null> {
    if (!realtime || !screen.value) return screen.value
    const localStop = await stopLocalScreen()
    const playbackDetached = detachRemoteTracks('video')
    screenOperation.value = 'Stopping'
    const result = await realtime.endScreenShare({ sessionNId: screen.value.sessionNId, reason })
    const value = consumeResult(result, screen, (next) => {
      screenOperation.value = operationForScreen(next)
    })
    if (value?.endedOn) screenIntent = false
    if (value?.endedOn) await reportStopped(value, 'Screen', { ...localStop, playbackDetached })
    return value
  }

  async function inviteVoiceCall(captureWindow: Window = window): Promise<VoiceDto | null> {
    if (!realtime || !conversationNId.value) {
      errorMessage.value = 'MEDIA_DEPENDENCY_UNAVAILABLE'
      voiceOperation.value = 'Error'
      return null
    }
    if (mediaSession.value && mediaConversationNId.value !== conversationNId.value) {
      errorMessage.value = 'MEDIA_BUSY'
      voiceOperation.value = 'Error'
      return null
    }
    const hadCapture = hasLocalVoiceCapture.value
    voiceIntent = true
    mediaConversationNId.value = conversationNId.value
    voiceOperation.value = 'Ringing'
    try {
      voicePlaybackObserved = false
      if (!hadCapture) await captureVoice(captureWindow)
      const result = await realtime.inviteVoiceCall({ conversationNId: conversationNId.value, requestNId: requestId() })
      const value = consumeResult(result, voice, (next) => {
        voiceOperation.value = next?.state === 'Ringing' ? 'Ringing' : operationForVoice(next)
      })
      if (!value && !hadCapture) await stopLocalVoice()
      if (!value) voiceIntent = false
      return value
    } catch (error) {
      voiceIntent = false
      if (!hadCapture) await stopLocalVoice()
      errorMessage.value = extractMediaErrorCode(error, 'MEDIA_SIGNAL_FAILED', 'Voice')
      voiceOperation.value = 'Error'
      return null
    }
  }

  async function respondVoiceCall(callNId: string, answer: string, captureWindow: Window = window): Promise<VoiceDto | null> {
    if (!realtime) return null
    const accepted = answer === 'Accept'
    const hadCapture = hasLocalVoiceCapture.value
    try {
      if (accepted) {
        voicePlaybackObserved = false
        if (!hadCapture) await captureVoice(captureWindow)
      }
      const result = await realtime.respondVoiceCall({ callNId, answer, expectedVersion: mediaLong(voice.value?.version) })
      const value = consumeResult(result, voice, (next) => {
        voiceOperation.value = operationForVoice(next)
      })
      if (value?.state === 'Accepted') voiceIntent = true
      if (value?.state === 'Accepted' && mediaConversationNId.value) {
        await ensureBinding()
        maybeSendVoiceReady()
      } else if (accepted && !value && !hadCapture) {
        voiceIntent = false
        await stopLocalVoice()
      }
      return value
    } catch (error) {
      if (accepted) voiceIntent = false
      if (accepted && !hadCapture) await stopLocalVoice()
      errorMessage.value = extractMediaErrorCode(error, accepted ? 'MEDIA_CAPTURE_FAILED' : 'MEDIA_SIGNAL_FAILED', accepted ? 'Voice' : null)
      voiceOperation.value = 'Error'
      return null
    }
  }

  async function endVoiceCall(reason = 'HungUp'): Promise<VoiceDto | null> {
    if (!realtime || !voice.value) return voice.value
    const localStop = await stopLocalVoice()
    const playbackDetached = detachRemoteTracks('audio')
    voiceOperation.value = 'Stopping'
    const result = await realtime.endVoiceCall({ callNId: voice.value.callNId, reason })
    const value = consumeResult(result, voice, (next) => {
      voiceOperation.value = operationForVoice(next)
    })
    if (value?.endedOn) voiceIntent = false
    if (value?.endedOn) await reportStopped(value, 'Voice', { ...localStop, playbackDetached })
    return value
  }

  function matchesActiveMedia(event: MediaContextChangedDto): boolean {
    const currentScreenSessionNId = activeScreenSessionNId()
    const currentVoiceCallNId = activeVoiceCallNId()
    return (currentScreenSessionNId === null
      || event.screenSessionNId === null
      || event.screenSessionNId === currentScreenSessionNId)
      && (currentVoiceCallNId === null
        || event.voiceCallNId === null
        || event.voiceCallNId === currentVoiceCallNId)
  }

  function activeScreenSessionNId(): string | null {
    return hasScreen.value ? (screen.value?.sessionNId ?? null) : null
  }

  function activeVoiceCallNId(): string | null {
    return hasVoice.value ? (voice.value?.callNId ?? null) : null
  }

  function rememberContextChange(event: MediaContextChangedDto): boolean {
    if (!matchesActiveMedia(event)) return false
    const knownContextNId = binding.value?.mediaContextNId ?? mediaContextNId.value
    if (knownContextNId && knownContextNId !== event.mediaContextNId) return false
    if (!pendingContextChange || pendingContextChange.mediaContextNId !== event.mediaContextNId
      || mediaLong(event.contextRevision) >= mediaLong(pendingContextChange.contextRevision)) {
      pendingContextChange = event
    }
    return true
  }

  function applyPendingContextChange(value: MediaBindingDto): MediaBindingDto {
    const pending = pendingContextChange
    if (!pending || pending.mediaContextNId !== value.mediaContextNId) {
      if (pending && pending.mediaContextNId !== value.mediaContextNId) pendingContextChange = null
      return value
    }
    pendingContextChange = null
    if (!matchesActiveMedia(pending) || mediaLong(pending.contextRevision) <= mediaLong(value.contextRevision)) return value
    return { ...value, contextRevision: pending.contextRevision }
  }

  function currentBindingKey(): string | null {
    const targetConversationNId = mediaConversationNId.value ?? conversationNId.value
    if (!targetConversationNId || (!hasScreen.value && !hasVoice.value)) return null
    return [
      targetConversationNId,
      hasScreen.value ? (screen.value?.sessionNId ?? '') : '',
      hasVoice.value ? (voice.value?.callNId ?? '') : '',
    ].join('|')
  }

  function bindingMatchesActiveMedia(): boolean {
    const currentBinding = binding.value
    if (!currentBinding) return false
    return (!hasScreen.value || currentBinding.screen?.sessionNId === screen.value?.sessionNId)
      && (!hasVoice.value || currentBinding.voice?.callNId === voice.value?.callNId)
  }

  function bindingNeedsRefresh(): boolean {
    return !bindingMatchesActiveMedia() || !peerConnection.value
  }

  async function ensureBindingIfNeeded(): Promise<MediaBindingDto | null> {
    const targetKey = currentBindingKey()
    if (!targetKey || !bindingNeedsRefresh() || bindingFailureKey === targetKey) return binding.value
    return ensureBinding()
  }

  function queueMediaSignal(event: MediaSignalRequest): void {
    if (binding.value && mediaLong(event.contextRevision) < mediaLong(binding.value.contextRevision)) return
    const knownContextNId = binding.value?.mediaContextNId ?? mediaContextNId.value ?? pendingContextChange?.mediaContextNId
    if (knownContextNId && knownContextNId !== event.mediaContextNId) return
    if (binding.value && mediaLong(event.contextRevision) > mediaLong(binding.value.contextRevision)) {
      pendingContextChange = {
        mediaContextNId: event.mediaContextNId,
        contextRevision: event.contextRevision,
        screenSessionNId: activeScreenSessionNId(),
        voiceCallNId: activeVoiceCallNId(),
      }
    }
    if (pendingSignals.some((item) => item.mediaContextNId === event.mediaContextNId && item.sequence === event.sequence)) return
    pendingSignals.push(event)
    if (pendingSignals.length > 32) pendingSignals.shift()
  }

  async function flushPendingSignals(): Promise<void> {
    const currentBinding = binding.value
    if (!currentBinding || pendingSignals.length === 0) return
    const queued = pendingSignals.splice(0)
    for (const event of queued) {
      if (event.mediaContextNId !== currentBinding.mediaContextNId) continue
      if (mediaLong(event.contextRevision) !== mediaLong(currentBinding.contextRevision)) continue
      await processSignal(event)
    }
  }

  async function ensureBinding(): Promise<MediaBindingDto | null> {
    const targetKey = currentBindingKey()
    if (!targetKey) return null
    if (bindingPromise) {
      if (bindingPromiseKey === targetKey) return bindingPromise
      bindingQueuedKey = targetKey
      const pending = bindingPromise
      await pending.catch(() => null)
      if (bindingQueuedKey === targetKey) bindingQueuedKey = null
      return ensureBinding()
    }
    const pending = performEnsureBinding(targetKey)
    bindingPromise = pending
    bindingPromiseKey = targetKey
    try {
      return await pending
    } finally {
      if (bindingPromise === pending) {
        bindingPromise = null
        bindingPromiseKey = null
      }
    }
  }

  async function performEnsureBinding(targetKey: string): Promise<MediaBindingDto | null> {
    const targetConversationNId = mediaConversationNId.value ?? conversationNId.value
    if (!realtime || !targetConversationNId || (!screen.value && !voice.value)) return null
    const generation = mediaLifecycleGeneration
    const hadPeerConnection = peerConnection.value !== null
    const previousBinding = binding.value
    try {
      const result = await realtime.bindMedia({
        conversationNId: targetConversationNId,
        screenSessionNId: hasScreen.value ? (screen.value?.sessionNId ?? null) : null,
        voiceCallNId: hasVoice.value ? (voice.value?.callNId ?? null) : null,
      })
      if (generation !== mediaLifecycleGeneration || currentBindingKey() !== targetKey) return binding.value
      if (!result.ok || !result.data) {
        markBindingFailure(targetKey, previousBinding, result.error?.code ?? 'MEDIA_DEPENDENCY_UNAVAILABLE')
        return binding.value
      }
      const value = consumeResult(result, binding, (next) => {
        mediaContextNId.value = next?.mediaContextNId ?? null
      })
      if (value) {
        const currentBinding = applyPendingContextChange(value)
        if (currentBinding !== value) binding.value = currentBinding
        clearStaleMediaSignalError()
        await ensurePeerConnection(currentBinding)
        bindingFailureKey = null
        clearBindingErrors(currentBinding)
        const capabilityChanged = previousBinding?.screen?.sessionNId !== currentBinding.screen?.sessionNId
          || previousBinding?.voice?.callNId !== currentBinding.voice?.callNId
        if (currentBinding.screen) screenOperation.value = operationForScreen(currentBinding.screen)
        if (currentBinding.voice) voiceOperation.value = operationForVoice(currentBinding.voice)
        if (hadPeerConnection && capabilityChanged && currentBinding.endpointRole === 'Low') {
          contextNegotiationQueued = true
          await restartNegotiationForContext(currentBinding)
        }
        await flushPendingSignals()
      }
      return binding.value
    } catch (error) {
      if (generation === mediaLifecycleGeneration)
        markBindingFailure(targetKey, previousBinding, extractMediaErrorCode(error, 'MEDIA_DEPENDENCY_UNAVAILABLE'))
      return binding.value
    }
  }

  function markBindingFailure(targetKey: string, previousBinding: MediaBindingDto | null, code: string): void {
    if (currentBindingKey() !== targetKey) return
    let screenTarget = hasScreen.value && screen.value?.sessionNId !== previousBinding?.screen?.sessionNId
    let voiceTarget = hasVoice.value && voice.value?.callNId !== previousBinding?.voice?.callNId
    if (!screenTarget && !voiceTarget) {
      screenTarget = hasScreen.value
      voiceTarget = hasVoice.value
    }
    bindingFailureKey = targetKey
    if (screenTarget) {
      screenErrorMessage.value = code
      screenOperation.value = 'Error'
    }
    if (voiceTarget) {
      voiceErrorMessage.value = code
      voiceOperation.value = 'Error'
    }
  }

  function clearBindingErrors(currentBinding: MediaBindingDto): void {
    if (currentBinding.screen && currentBinding.screen.sessionNId === screen.value?.sessionNId) {
      screenErrorMessage.value = null
      screenOperation.value = operationForScreen(screen.value)
    }
    if (currentBinding.voice && currentBinding.voice.callNId === voice.value?.callNId) {
      voiceErrorMessage.value = null
      voiceOperation.value = operationForVoice(voice.value)
    }
  }

  async function startScreenCapture(captureWindow: Window = window): Promise<void> {
    if (!screen.value || !hasScreen.value) {
      errorMessage.value = 'MEDIA_SCREEN_NOT_ACCEPTED'
      screenOperation.value = 'Error'
      return
    }
    if (captureWindow.isSecureContext === false) {
      errorMessage.value = 'MEDIA_SECURE_CONTEXT_REQUIRED'
      screenOperation.value = 'Error'
      return
    }
    if (!captureWindow.navigator.mediaDevices?.getDisplayMedia) {
      errorMessage.value = 'MEDIA_UNSUPPORTED'
      screenOperation.value = 'Error'
      return
    }
    try {
      await captureScreen(captureWindow)
      const currentBinding = binding.value ?? (await ensureBinding())
      if (!currentBinding) return
      const pc = await ensurePeerConnection(currentBinding)
      const sender = screenSender(pc)
      await sender?.replaceTrack(localScreenStream.value?.getVideoTracks()[0] ?? null)
      if (currentBinding.endpointRole === 'Low') await negotiate()
    } catch (error) {
      errorMessage.value = extractMediaErrorCode(error, 'MEDIA_CAPTURE_FAILED', 'Screen')
      screenOperation.value = 'Error'
    }
  }

  async function captureScreen(captureWindow: Window): Promise<MediaStream> {
    const existing = localScreenStream.value
    if (hasLiveScreenCapture()) return existing as MediaStream
    if (captureWindow.isSecureContext === false) throw new Error('MEDIA_SECURE_CONTEXT_REQUIRED')
    const generation = ++screenGeneration
    if (!captureWindow.navigator.mediaDevices?.getDisplayMedia) throw new Error('MEDIA_UNSUPPORTED')
    // Keep getDisplayMedia at the beginning of the click path so browser activation is retained.
    const constraints = {
      video: { frameRate: { ideal: 30 } },
      audio: false,
      selfBrowserSurface: 'include',
    } as DisplayMediaStreamOptions & { selfBrowserSurface: 'include' }
    const stream = await captureWindow.navigator.mediaDevices.getDisplayMedia(constraints)
    if (generation !== screenGeneration) {
      stopTracks(stream)
      throw new Error('MEDIA_CAPTURE_FAILED')
    }
    localScreenStream.value = stream
    const captureGeneration = screenGeneration
    stream.getVideoTracks().forEach((track) => {
      track.contentHint = 'detail'
      track.addEventListener('ended', () => {
        if (captureGeneration !== screenGeneration) return
        localScreenStream.value = null
        if (hasScreen.value || screen.value?.state === 'Pending') void endScreenShare('SharerStopped')
      }, { once: true })
    })
    return stream
  }

  async function startVoiceCapture(captureWindow: Window = window): Promise<void> {
    if (!voice.value || !hasVoice.value) {
      errorMessage.value = 'MEDIA_VOICE_NOT_ACCEPTED'
      voiceOperation.value = 'Error'
      return
    }
    try {
      const stream = await captureVoice(captureWindow)
      const generation = voiceGeneration
      if (!hasVoice.value) {
        stopTracks(stream)
        return
      }
      const currentBinding = binding.value ?? (await ensureBinding())
      if (!currentBinding || generation !== voiceGeneration) {
        stopTracks(stream)
        return
      }
      const pc = await ensurePeerConnection(currentBinding)
      const sender = audioSender(pc)
      await sender?.replaceTrack(stream.getAudioTracks()[0] ?? null)
      if (currentBinding.endpointRole === 'Low') await negotiate()
      maybeSendVoiceReady()
    } catch (error) {
      errorMessage.value = extractMediaErrorCode(error, 'MEDIA_CAPTURE_FAILED', 'Voice')
      voiceOperation.value = 'Error'
    }
  }

  async function captureVoice(captureWindow: Window): Promise<MediaStream> {
    const existing = localVoiceStream.value
    if (existing?.getAudioTracks().some((track) => track.readyState === 'live')) return existing
    if (captureWindow.isSecureContext === false) throw new Error('MEDIA_SECURE_CONTEXT_REQUIRED')
    const generation = ++voiceGeneration
    if (!captureWindow.navigator.mediaDevices?.getUserMedia) throw new Error('MEDIA_UNSUPPORTED')
    const stream = await captureWindow.navigator.mediaDevices.getUserMedia({ audio: { echoCancellation: true, noiseSuppression: true, autoGainControl: true }, video: false })
    if (generation !== voiceGeneration) {
      stopTracks(stream)
      throw new Error('MEDIA_CAPTURE_FAILED')
    }
    localVoiceStream.value = stream
    const captureGeneration = voiceGeneration
    stream.getAudioTracks().forEach((track) => {
      track.addEventListener('ended', () => {
        if (captureGeneration === voiceGeneration) void endVoiceCall('CaptureEnded')
      }, { once: true })
    })
    maybeSendVoiceReady()
    return stream
  }

  async function setVoiceMuted(next: boolean): Promise<void> {
    muted.value = next
    localVoiceStream.value?.getAudioTracks().forEach((track) => { track.enabled = !next })
    if (!realtime || !voice.value) return
    const result = await realtime.setVoiceMuted({ callNId: voice.value.callNId, muted: next, sequence: ++signalSequence })
    if (!result.ok) errorMessage.value = result.error?.code ?? 'MEDIA_DEPENDENCY_UNAVAILABLE'
  }

  function endAll(): Promise<void> {
    if (endingAllPromise !== null) return endingAllPromise
    const operation = endAllInternal()
    endingAllPromise = operation
    void operation.finally(() => {
      if (endingAllPromise === operation) endingAllPromise = null
    })
    return operation
  }

  async function endAllInternal(): Promise<void> {
    ++screenGeneration
    ++voiceGeneration
    const currentContext = mediaContextNId.value
    const screenSessionNId = screen.value?.sessionNId ?? null
    const voiceCallNId = voice.value?.callNId ?? null
    const screenStop = await stopLocalScreen()
    const voiceStop = await stopLocalVoice()
    const screenPlaybackDetached = detachRemoteTracks('video')
    const voicePlaybackDetached = detachRemoteTracks('audio')
    try {
      if (realtime && currentContext) {
        const result = await realtime.endAllMedia({ mediaContextNId: currentContext, screenSessionNId, voiceCallNId })
        if (!result.ok) errorMessage.value = result.error?.code ?? 'MEDIA_DEPENDENCY_UNAVAILABLE'
        if (result.ok && result.data?.screen?.endedOn) await reportStopped(result.data.screen, 'Screen', { ...screenStop, playbackDetached: screenPlaybackDetached })
        if (result.ok && result.data?.voice?.endedOn) await reportStopped(result.data.voice, 'Voice', { ...voiceStop, playbackDetached: voicePlaybackDetached })
      } else if (realtime) {
        const [screenResult, voiceResult] = await Promise.allSettled([
          screenSessionNId
            ? realtime.endScreenShare({ sessionNId: screenSessionNId, reason: 'EndedAll' })
            : Promise.resolve(null),
          voiceCallNId
            ? realtime.endVoiceCall({ callNId: voiceCallNId, reason: 'EndedAll' })
            : Promise.resolve(null),
        ])
        const failures = [screenResult, voiceResult].filter((result): result is PromiseRejectedResult => result.status === 'rejected')
        if (failures.length > 0) errorMessage.value = extractMediaErrorCode(failures[0]?.reason, 'MEDIA_DEPENDENCY_UNAVAILABLE')
        if (screenResult.status === 'fulfilled' && screenResult.value && !screenResult.value.ok)
          errorMessage.value = screenResult.value.error?.code ?? 'MEDIA_DEPENDENCY_UNAVAILABLE'
        if (voiceResult.status === 'fulfilled' && voiceResult.value && !voiceResult.value.ok)
          errorMessage.value = voiceResult.value.error?.code ?? 'MEDIA_DEPENDENCY_UNAVAILABLE'
        if (screenResult.status === 'fulfilled' && screenResult.value?.ok && screenResult.value.data?.endedOn)
          await reportStopped(screenResult.value.data, 'Screen', { ...screenStop, playbackDetached: screenPlaybackDetached })
        if (voiceResult.status === 'fulfilled' && voiceResult.value?.ok && voiceResult.value.data?.endedOn)
          await reportStopped(voiceResult.value.data, 'Voice', { ...voiceStop, playbackDetached: voicePlaybackDetached })
      }
    } catch (error) {
      errorMessage.value = extractMediaErrorCode(error, 'MEDIA_DEPENDENCY_UNAVAILABLE')
    } finally {
      await disposeAll()
    }
  }

  async function disposeAll(): Promise<void> {
    ++mediaLifecycleGeneration
    ++screenGeneration
    ++voiceGeneration
    stopTracks(localScreenStream.value)
    stopTracks(localVoiceStream.value)
    localScreenStream.value = null
    localVoiceStream.value = null
    remoteStream.value = null
    binding.value = null
    mediaContextNId.value = null
    readyKeys.clear()
    mediaConversationNId.value = null
    screen.value = null
    voice.value = null
    screenOperation.value = 'Idle'
    voiceOperation.value = 'Idle'
    if (keepAliveTimer !== undefined) window.clearInterval(keepAliveTimer)
    keepAliveTimer = undefined
    const pc = peerConnection.value
    peerConnection.value = null
    pc?.getSenders().forEach((sender) => { void sender.replaceTrack(null).catch(() => undefined) })
    pc?.close()
    iceQueue.clear()
    negotiationFailureTargets.clear()
    stoppedReportKeys.clear()
    stopResults.clear()
    pendingContextChange = null
    pendingSignals.splice(0)
    bindingPromise = null
    bindingPromiseKey = null
    bindingQueuedKey = null
    bindingFailureKey = null
    waitingForPeer = false
    negotiationQueued = false
    contextNegotiationQueued = false
    voicePlaybackObserved = false
    screenIntent = false
    voiceIntent = false
    screenPlaybackBlocked.value = false
    voicePlaybackBlocked.value = false
  }

  async function ensurePeerConnection(currentBinding: MediaBindingDto): Promise<RTCPeerConnection> {
    if (peerConnection.value) {
      syncTransceiverDirections(peerConnection.value, currentBinding)
      await attachLocalTracks(peerConnection.value, currentBinding)
      return peerConnection.value
    }
    if (typeof RTCPeerConnection === 'undefined') throw new Error('MEDIA_UNSUPPORTED')
    const pc = new RTCPeerConnection({
      iceServers: currentBinding.iceServers,
      iceTransportPolicy: currentBinding.icePolicy === 'RelayOnly' ? 'relay' : 'all',
    })
    peerConnection.value = pc
    remoteStream.value = new MediaStream()
    pc.ontrack = (event) => {
      addRemoteTrack(event.track)
    }
    pc.onicecandidate = (event) => {
      if (event.candidate) void sendCandidate(event.candidate)
    }
    pc.onconnectionstatechange = () => {
      if (pc.connectionState === 'failed' || pc.connectionState === 'closed') {
        screenOperation.value = hasScreen.value ? 'Error' : screenOperation.value
        voiceOperation.value = hasVoice.value ? 'Error' : voiceOperation.value
      }
    }
    if (currentBinding.endpointRole === 'Low') {
      pc.addTransceiver('audio', { direction: currentBinding.voice ? 'sendrecv' : 'inactive' })
      pc.addTransceiver('video', { direction: screenDirection(currentBinding, 'Low') })
      pc.addTransceiver('video', { direction: screenDirection(currentBinding, 'High') })
      await attachLocalTracks(pc, currentBinding)
      await negotiate()
    }
    installKeepAlive()
    return pc
  }

  function addRemoteTrack(track: MediaStreamTrack): void {
    const current = remoteStream.value
    if (!current || current.getTracks().some((item) => item.id === track.id)) return
    const next = new MediaStream([...current.getTracks(), track])
    track.addEventListener('ended', () => {
      const latest = remoteStream.value
      if (!latest) return
      remoteStream.value = new MediaStream(latest.getTracks().filter((item) => item.id !== track.id))
    }, { once: true })
    remoteStream.value = next
    if (track.kind === 'audio') maybeSendVoiceReady()
  }

  function screenDirection(currentBinding: MediaBindingDto, slotRole: 'Low' | 'High'): RTCRtpTransceiverDirection {
    const currentScreen = currentBinding.screen
    if (!currentScreen) return 'inactive'
    const localUserIsSharer = currentScreen.sharerUserNId === auth.user?.userId
    const localRoleOwnsSlot = currentBinding.endpointRole === slotRole
    const slotUserIsSharer = localRoleOwnsSlot ? localUserIsSharer : !localUserIsSharer
    if (!slotUserIsSharer) return 'inactive'
    return localRoleOwnsSlot ? 'sendonly' : 'recvonly'
  }

  function syncTransceiverDirections(pc: RTCPeerConnection, currentBinding: MediaBindingDto): void {
    const transceivers = pc.getTransceivers()
    if (transceivers[0]) transceivers[0].direction = currentBinding.voice ? 'sendrecv' : 'inactive'
    if (transceivers[1]) transceivers[1].direction = screenDirection(currentBinding, 'Low')
    if (transceivers[2]) transceivers[2].direction = screenDirection(currentBinding, 'High')
  }

  async function attachLocalTracks(pc: RTCPeerConnection, currentBinding: MediaBindingDto): Promise<void> {
    if (currentBinding.voice)
      await audioSender(pc)?.replaceTrack(localVoiceStream.value?.getAudioTracks()[0] ?? null)
    if (currentBinding.screen)
      await screenSender(pc)?.replaceTrack(localScreenStream.value?.getVideoTracks()[0] ?? null)
  }

  async function receiveSignal(event: MediaSignalRequest): Promise<void> {
    const currentBinding = binding.value
    if (!currentBinding || event.mediaContextNId !== currentBinding.mediaContextNId
      || mediaLong(event.contextRevision) !== mediaLong(currentBinding.contextRevision)) {
      queueMediaSignal(event)
      return
    }
    await processSignal(event)
  }

  async function processSignal(event: MediaSignalRequest): Promise<void> {
    const currentBinding = binding.value
    if (!currentBinding || event.mediaContextNId !== currentBinding.mediaContextNId
      || mediaLong(event.contextRevision) !== mediaLong(currentBinding.contextRevision)) return
    const pc = peerConnection.value ?? (await ensurePeerConnection(currentBinding))
    const offerCollision = event.kind === 'Offer' && (makingOffer || pc.signalingState !== 'stable' || settingRemoteAnswer)
    ignoreOffer = !currentBinding.polite && offerCollision
    if (ignoreOffer) return
    try {
      if (event.kind === 'Offer' && event.description) {
        negotiationNId = event.negotiationNId
        discardOldNegotiations(event.negotiationNId)
        registerNegotiation(event.negotiationNId)
        await pc.setRemoteDescription(event.description as RTCSessionDescriptionInit)
        if (!binding.value) return
        syncTransceiverDirections(pc, binding.value)
        await attachLocalTracks(pc, binding.value)
        await flushIce(event.negotiationNId, pc)
        await pc.setLocalDescription(await pc.createAnswer())
        await sendDescription('Answer', pc.localDescription, event.negotiationNId)
      } else if (event.kind === 'Answer' && event.description) {
        settingRemoteAnswer = true
        negotiationNId = event.negotiationNId
        discardOldNegotiations(event.negotiationNId)
        registerNegotiation(event.negotiationNId)
        await pc.setRemoteDescription(event.description as RTCSessionDescriptionInit)
        await flushIce(event.negotiationNId, pc)
        settingRemoteAnswer = false
        if (negotiationQueued && pc.signalingState === 'stable') {
          negotiationQueued = false
          void negotiate().catch(() => undefined)
        }
      } else if (event.kind === 'IceCandidate' && event.candidate) {
        if (pc.remoteDescription && event.negotiationNId !== negotiationNId) return
        if (!pc.remoteDescription) {
          if (!iceQueue.add(event.negotiationNId, event.candidate as RTCIceCandidateInit)) expireIce(event.negotiationNId)
        } else {
          await pc.addIceCandidate(event.candidate as RTCIceCandidateInit)
        }
      }
    } catch {
      settingRemoteAnswer = false
      const target = negotiationFailureTargets.get(event.negotiationNId)
      negotiationFailureTargets.delete(event.negotiationNId)
      iceQueue.cancel(event.negotiationNId)
      if (target?.screenSessionNId && target.screenGeneration === screenGeneration
        && (screen.value?.state === 'Accepted' || screen.value?.state === 'Connecting')
        && screen.value.sessionNId === target.screenSessionNId) screenOperation.value = 'Error'
      if (target?.voiceCallNId && target.voiceGeneration === voiceGeneration
        && (voice.value?.state === 'Accepted' || voice.value?.state === 'Connecting')
        && voice.value.callNId === target.voiceCallNId) voiceOperation.value = 'Error'
    }
  }

  async function restartNegotiationForContext(currentBinding: MediaBindingDto): Promise<void> {
    if (currentBinding.endpointRole !== 'Low') {
      contextNegotiationQueued = false
      return
    }
    if (makingOffer) return
    contextNegotiationQueued = false
    const pc = peerConnection.value
    if (!pc) {
      await ensurePeerConnection(currentBinding)
      return
    }
    if (pc.signalingState !== 'stable') {
      iceQueue.cancel(negotiationNId)
      negotiationFailureTargets.delete(negotiationNId)
      negotiationQueued = false
      await pc.setLocalDescription({ type: 'rollback' })
      if (String(pc.signalingState) !== 'stable') return
    }
    await negotiate()
  }

  async function negotiate(): Promise<void> {
    const pc = peerConnection.value
    const currentBinding = binding.value
    if (!pc || !currentBinding) return
    if (makingOffer || pc.signalingState !== 'stable') {
      negotiationQueued = true
      return
    }
    if (currentBinding.endpointRole === 'High' && pc.getTransceivers().length === 0) return
    makingOffer = true
    try {
      negotiationNId = createCorrelationId().replaceAll('-', '')
      registerNegotiation(negotiationNId)
      await pc.setLocalDescription(await pc.createOffer())
      await sendDescription('Offer', pc.localDescription, negotiationNId)
      waitingForPeer = false
    } catch (error) {
      iceQueue.cancel(negotiationNId)
      negotiationFailureTargets.delete(negotiationNId)
      if (pc.signalingState !== 'stable') {
        try { await pc.setLocalDescription({ type: 'rollback' }) } catch { /* browser may already have rolled back */ }
      }
      if (String(error).includes('MEDIA_BUSY')) {
        waitingForPeer = true
        return
      }
      throw error
    } finally {
      makingOffer = false
      if (contextNegotiationQueued) {
        void restartNegotiationForContext(binding.value ?? currentBinding).catch(() => undefined)
      } else if (negotiationQueued && pc.signalingState === 'stable') {
        negotiationQueued = false
        void negotiate().catch(() => undefined)
      }
    }
  }

  async function sendDescription(kind: 'Offer' | 'Answer', description: RTCSessionDescription | null, negotiationId = negotiationNId): Promise<void> {
    const currentBinding = binding.value
    if (!realtime || !currentBinding || !description) return
    const source = mediaSignalErrorSourceFor(currentBinding, negotiationId)
    const result = await realtime.signalMedia({ mediaContextNId: currentBinding.mediaContextNId, contextRevision: mediaLong(currentBinding.contextRevision), negotiationNId: negotiationId, sequence: ++signalSequence, kind, description: { type: description.type, sdp: description.sdp } })
    if (!result.ok) {
      const code = result.error?.code ?? 'MEDIA_SIGNAL_FAILED'
      if (code === 'MEDIA_BUSY') throw new Error(code)
      if (!isCurrentMediaSignalSource(source)) return
      setMediaSignalError(code, source)
      throw new Error(code)
    }
    clearStaleMediaSignalError()
  }

  async function sendCandidate(candidate: RTCIceCandidate): Promise<void> {
    const currentBinding = binding.value
    if (!realtime || !currentBinding) return
    const source = mediaSignalErrorSourceFor(currentBinding, negotiationNId)
    const result = await realtime.signalMedia({ mediaContextNId: currentBinding.mediaContextNId, contextRevision: mediaLong(currentBinding.contextRevision), negotiationNId, sequence: ++signalSequence, kind: 'IceCandidate', candidate: { candidate: candidate.candidate, sdpMid: candidate.sdpMid, sdpMLineIndex: candidate.sdpMLineIndex, usernameFragment: candidate.usernameFragment } })
    if (!result.ok && result.error?.code !== 'MEDIA_BUSY' && isCurrentMediaSignalSource(source))
      setMediaSignalError(result.error?.code ?? 'MEDIA_SIGNAL_FAILED', source)
  }

  function mediaSignalErrorSourceFor(currentBinding: MediaBindingDto, negotiationId: string, capability: MediaSignalCapability = capabilityForBinding(currentBinding), capabilityNId: string | null = capability === 'Screen'
    ? currentBinding.screen?.sessionNId ?? null
    : capability === 'Voice'
      ? currentBinding.voice?.callNId ?? null
      : null): MediaSignalErrorSource {
    return {
      lifecycleGeneration: mediaLifecycleGeneration,
      mediaContextNId: currentBinding.mediaContextNId,
      contextRevision: mediaLong(currentBinding.contextRevision),
      negotiationNId: negotiationId,
      capability,
      capabilityNId,
    }
  }

  function capabilityForBinding(currentBinding: MediaBindingDto): MediaSignalCapability {
    if (currentBinding.screen && !currentBinding.voice) return 'Screen'
    if (currentBinding.voice && !currentBinding.screen) return 'Voice'
    return null
  }

  function isCurrentMediaSignalSource(source: MediaSignalErrorSource): boolean {
    const currentBinding = binding.value
    const capabilityIsCurrent = source.capability === null
      || (source.capability === 'Screen'
        ? hasScreen.value && screen.value?.sessionNId === source.capabilityNId
        : hasVoice.value && voice.value?.callNId === source.capabilityNId)
    return source.lifecycleGeneration === mediaLifecycleGeneration
      && currentBinding?.mediaContextNId === source.mediaContextNId
      && mediaLong(currentBinding.contextRevision) === source.contextRevision
      && capabilityIsCurrent
      && (!source.negotiationNId || source.negotiationNId === negotiationNId)
  }

  function setMediaSignalError(code: string, source: MediaSignalErrorSource): void {
    if (!isCurrentMediaSignalSource(source)) return
    mediaSignalErrorSource = source
    mediaSignalErrorCode = code
    errorMessage.value = code
  }

  function clearStaleMediaSignalError(): void {
    if (!mediaSignalErrorSource) return
    if (!isCurrentMediaSignalSource(mediaSignalErrorSource)) {
      mediaSignalErrorSource = null
      if (errorMessage.value === mediaSignalErrorCode) errorMessage.value = null
      mediaSignalErrorCode = null
    }
  }

  function clearMediaSignalErrorForCapability(kind: 'Screen' | 'Voice', mediaContextId: string): void {
    if (mediaSignalErrorSource?.mediaContextNId !== mediaContextId || mediaSignalErrorSource.capability !== kind) return
    mediaSignalErrorSource = null
    if (errorMessage.value === mediaSignalErrorCode) errorMessage.value = null
    mediaSignalErrorCode = null
  }

  async function flushIce(negotiationId: string, pc: RTCPeerConnection): Promise<void> {
    const queued = iceQueue.take(negotiationId)
    negotiationFailureTargets.delete(negotiationId)
    for (const item of queued) {
      await pc.addIceCandidate(item.candidate)
    }
  }

  function registerNegotiation(negotiationId: string): void {
    if (!negotiationFailureTargets.has(negotiationId)) {
      negotiationFailureTargets.set(negotiationId, {
        screenSessionNId: screen.value?.state === 'Accepted' || screen.value?.state === 'Connecting' ? screen.value.sessionNId : null,
        screenGeneration,
        voiceCallNId: voice.value?.state === 'Accepted' || voice.value?.state === 'Connecting' ? voice.value.callNId : null,
        voiceGeneration,
      })
    }
    iceQueue.start(negotiationId)
  }

  function discardOldNegotiations(currentNegotiationId: string): void {
    iceQueue.discardExcept(currentNegotiationId)
    for (const negotiationId of negotiationFailureTargets.keys()) {
      if (negotiationId !== currentNegotiationId) negotiationFailureTargets.delete(negotiationId)
    }
  }

  function expireIce(negotiationId: string): void {
    const target = negotiationFailureTargets.get(negotiationId)
    negotiationFailureTargets.delete(negotiationId)
    iceQueue.cancel(negotiationId)
    if (negotiationId !== negotiationNId) return
    waitingForPeer = true
    if (target?.screenSessionNId && target.screenGeneration === screenGeneration
      && (screen.value?.state === 'Accepted' || screen.value?.state === 'Connecting')
      && screen.value.sessionNId === target.screenSessionNId) {
      screenOperation.value = 'Error'
      void endScreenShare('IceTimeout').catch(() => undefined)
    }
    if (target?.voiceCallNId && target.voiceGeneration === voiceGeneration
      && (voice.value?.state === 'Accepted' || voice.value?.state === 'Connecting')
      && voice.value.callNId === target.voiceCallNId) {
      voiceOperation.value = 'Error'
      void endVoiceCall('IceTimeout').catch(() => undefined)
    }
  }

  async function sendMediaReady(kind: 'Screen' | 'Voice', sessionNId: string): Promise<void> {
    const currentContextNId = mediaContextNId.value
    if (!realtime || !currentContextNId) return
    const key = `${kind}:${sessionNId}`
    if (readyKeys.has(key)) return
    readyKeys.add(key)
    const source = binding.value ? mediaSignalErrorSourceFor(binding.value, '', kind, sessionNId) : null
    const result = await realtime.mediaReady({ mediaContextNId: currentContextNId, kind, sessionNId })
    if (!result.ok || !result.data) {
      readyKeys.delete(key)
      const code = result.error?.code ?? 'MEDIA_DEPENDENCY_UNAVAILABLE'
      if (source) {
        if (isCurrentMediaSignalSource(source)) setMediaSignalError(code, source)
        return
      }
      errorMessage.value = code
      return
    }
    if (kind === 'Screen') applyScreen(result.data as ScreenDto)
    else applyVoice(result.data as VoiceDto)
    clearMediaSignalErrorForCapability(kind, currentContextNId)
  }

  async function markScreenPlayable(): Promise<void> {
    if (screen.value?.sessionNId) await sendMediaReady('Screen', screen.value.sessionNId)
    screenPlaybackBlocked.value = false
  }

  async function markVoicePlayable(): Promise<void> {
    voicePlaybackObserved = true
    voicePlaybackBlocked.value = false
    maybeSendVoiceReady()
  }

  function maybeSendVoiceReady(): void {
    const callNId = voice.value?.callNId
    const hasRemoteAudio = remoteStream.value?.getAudioTracks().some((track) => track.readyState === 'live') ?? false
    if (!callNId || !voicePlaybackObserved || !hasLocalVoiceCapture.value || !hasRemoteAudio) return
    void sendMediaReady('Voice', callNId)
  }

  function markPlaybackBlocked(kind: 'Screen' | 'Voice' = 'Voice'): void {
    if (kind === 'Screen') screenPlaybackBlocked.value = true
    else voicePlaybackBlocked.value = true
  }

  async function resumePlayback(element: HTMLMediaElement, kind: 'Screen' | 'Voice' = 'Voice'): Promise<void> {
    try {
      await element.play()
      if (kind === 'Screen') screenPlaybackBlocked.value = false
      else voicePlaybackBlocked.value = false
    } catch {
      markPlaybackBlocked(kind)
    }
  }

  function installKeepAlive(): void {
    if (keepAliveTimer !== undefined || !realtime || !mediaContextNId.value) return
    keepAliveTimer = window.setInterval(() => {
      if (!realtime || !mediaContextNId.value) return
      void realtime.keepAliveMedia({ mediaContextNId: mediaContextNId.value, screenSessionNId: hasScreen.value ? (screen.value?.sessionNId ?? null) : null, voiceCallNId: hasVoice.value ? (voice.value?.callNId ?? null) : null }).then((result) => applyKeepAlive(result)).catch(() => undefined)
    }, 10000)
  }

  function applyKeepAlive(result: MediaResult<KeepAliveMediaDto>): void {
    if (!result.ok || !result.data) return
    if (binding.value && result.data.contextRevision !== undefined
      && mediaLong(result.data.contextRevision) >= mediaLong(binding.value.contextRevision))
      binding.value = { ...binding.value, contextRevision: result.data.contextRevision }
    if (result.data.screen && !result.data.screen.allowed) void endScreenShare('HeartbeatTimeout')
    if (result.data.voice && !result.data.voice.allowed) void endVoiceCall('HeartbeatTimeout')
  }

  async function reportStopped(item: ScreenDto | VoiceDto, kind: 'Screen' | 'Voice', actual?: StopResult): Promise<void> {
    if (!realtime) return
    const reportKey = `${kind}:${kind === 'Screen' ? (item as ScreenDto).sessionNId : (item as VoiceDto).callNId}`
    if (stoppedReportKeys.has(reportKey)) return
    stoppedReportKeys.add(reportKey)
    const itemConversationNId = kind === 'Screen' ? (item as ScreenDto).conversationNId : (item as VoiceDto).conversationNId
    const stop = actual ?? {
      senderDetached: true,
      captureTracksEnded: true,
      playbackDetached: true,
    }
    await realtime.reportMediaStopped({
      conversationNId: itemConversationNId,
      kind,
      sessionNId: kind === 'Screen' ? (item as ScreenDto).sessionNId : (item as VoiceDto).callNId,
      senderDetached: stop.senderDetached,
      captureTracksEnded: stop.captureTracksEnded,
      playbackDetached: stop.playbackDetached,
    })
  }

  async function stopLocalScreen(): Promise<StopResult> {
    ++screenGeneration
    const pc = peerConnection.value
    const sender = screenSender(pc)
    const stream = localScreenStream.value
    let senderDetached = !sender || sender.track === null
    let captureTracksEnded = !stream || stream.getTracks().every((track) => track.readyState === 'ended')
    try {
      stopTracks(localScreenStream.value)
      captureTracksEnded = !stream || stream.getTracks().every((track) => track.readyState === 'ended')
    } finally {
      localScreenStream.value = null
    }
    try {
      if (sender) {
        await sender.replaceTrack(null)
        senderDetached = sender.track === null
      }
    } catch {
      senderDetached = false
    }
    return { senderDetached, captureTracksEnded, playbackDetached: true }
  }

  async function stopLocalVoice(): Promise<StopResult> {
    ++voiceGeneration
    const sender = audioSender(peerConnection.value)
    const stream = localVoiceStream.value
    let senderDetached = !sender || sender.track === null
    let captureTracksEnded = !stream || stream.getTracks().every((track) => track.readyState === 'ended')
    try {
      stopTracks(localVoiceStream.value)
      captureTracksEnded = !stream || stream.getTracks().every((track) => track.readyState === 'ended')
    } finally {
      localVoiceStream.value = null
    }
    try {
      if (sender) {
        await sender.replaceTrack(null)
        senderDetached = sender.track === null
      }
    } catch {
      senderDetached = false
    }
    return { senderDetached, captureTracksEnded, playbackDetached: true }
  }

  function screenSender(pc: RTCPeerConnection | null): RTCRtpSender | null {
    if (!pc) return null
    const index = binding.value?.endpointRole === 'High' ? 2 : 1
    return pc.getTransceivers()[index]?.sender ?? null
  }

  function audioSender(pc: RTCPeerConnection | null): RTCRtpSender | null {
    return pc?.getTransceivers()[0]?.sender ?? null
  }

  function applyConversation(value: ConversationMediaDto): void {
    applyScreen(value.screen)
    applyVoice(value.voice)
    mediaContextNId.value = value.mediaContextNId
  }

  function applyScreen(value: ScreenDto | null): void {
    const previous = screen.value
    if (previous?.sessionNId !== value?.sessionNId) screenErrorMessage.value = null
    screen.value = value
    screenOperation.value = value && previous?.sessionNId === value.sessionNId && screenErrorMessage.value && !isTerminalScreen(value.state)
      ? 'Error'
      : operationForScreen(value)
    if (value && isTerminalScreen(value.state) && previous?.sessionNId === value.sessionNId)
      void finalizeScreen(value)
  }

  function applyVoice(value: VoiceDto | null): void {
    const previous = voice.value
    if (previous?.callNId !== value?.callNId) voicePlaybackObserved = false
    if (previous?.callNId !== value?.callNId) voiceErrorMessage.value = null
    voice.value = value
    voiceOperation.value = value && previous?.callNId === value.callNId && voiceErrorMessage.value && !isTerminalVoice(value.state)
      ? 'Error'
      : operationForVoice(value)
    if (value && isTerminalVoice(value.state) && previous?.callNId === value.callNId)
      void finalizeVoice(value)
  }

  async function finalizeScreen(item: ScreenDto): Promise<void> {
    screenIntent = false
    const currentContextNId = mediaContextNId.value
    screenErrorMessage.value = null
    if (currentContextNId) clearMediaSignalErrorForCapability('Screen', currentContextNId)
    const localStop = await stopLocalScreen()
    const playbackDetached = detachRemoteTracks('video')
    if (item.endedOn) await reportStopped(item, 'Screen', { ...localStop, playbackDetached })
    if (screen.value?.sessionNId === item.sessionNId) {
      screen.value = null
      screenOperation.value = 'Idle'
    }
    closePeerIfIdle()
  }

  async function finalizeVoice(item: VoiceDto): Promise<void> {
    voiceIntent = false
    const localStop = await stopLocalVoice()
    const playbackDetached = detachRemoteTracks('audio')
    if (item.endedOn) await reportStopped(item, 'Voice', { ...localStop, playbackDetached })
    if (voice.value?.callNId === item.callNId) {
      voice.value = null
      voiceOperation.value = 'Idle'
    }
    closePeerIfIdle()
  }

  function detachRemoteTracks(kind: 'audio' | 'video'): boolean {
    if (!remoteStream.value || typeof MediaStream === 'undefined') return true
    const remaining = remoteStream.value.getTracks().filter((track) => track.kind !== kind)
    remoteStream.value = new MediaStream(remaining)
    return !remaining.some((track) => track.kind === kind)
  }

  function closePeerIfIdle(): void {
    if (hasScreen.value || hasVoice.value) return
    ++mediaLifecycleGeneration
    peerConnection.value?.close()
    peerConnection.value = null
    remoteStream.value = null
    binding.value = null
    mediaContextNId.value = null
    mediaConversationNId.value = null
    pendingContextChange = null
    pendingSignals.splice(0)
    bindingPromise = null
    bindingPromiseKey = null
    bindingQueuedKey = null
    bindingFailureKey = null
    if (errorMessage.value === mediaSignalErrorCode) errorMessage.value = null
    mediaSignalErrorSource = null
    mediaSignalErrorCode = null
    negotiationQueued = false
    contextNegotiationQueued = false
    voicePlaybackObserved = false
    screenIntent = false
    voiceIntent = false
  }

  function isTerminalScreen(state: string): boolean {
    return ['Ended', 'Declined', 'Cancelled', 'Expired', 'Failed'].includes(state)
  }

  function isTerminalVoice(state: string): boolean {
    return ['Ended', 'Declined', 'Cancelled', 'Missed', 'Failed'].includes(state)
  }

  function applyMuted(value: VoiceMutedDto): void {
    if (value.callNId === voice.value?.callNId) muted.value = value.muted
  }

  function consumeResult<T>(result: MediaResult<T>, target: { value: T | null }, after: (value: T | null) => void): T | null {
    if (!result.ok || !result.data) {
      errorMessage.value = result.error?.code ?? 'MEDIA_DEPENDENCY_UNAVAILABLE'
      after(null)
      return null
    }
    target.value = result.data
    errorMessage.value = null
    after(result.data)
    return result.data
  }

  function extractMediaErrorCode(error: unknown, fallback: string, captureKind: 'Screen' | 'Voice' | null = null): string {
    if (error instanceof DOMException) {
      if (error.name === 'NotAllowedError' || error.name === 'SecurityError')
        return captureKind === 'Screen' ? 'MEDIA_SCREEN_CAPTURE_CANCELLED' : 'MEDIA_MIC_PERMISSION_DENIED'
      if (error.name === 'NotFoundError' || error.name === 'DevicesNotFoundError')
        return captureKind === 'Voice' ? 'MEDIA_MIC_UNAVAILABLE' : 'MEDIA_CAPTURE_FAILED'
      if (captureKind === 'Screen' && error.name === 'AbortError') return 'MEDIA_SCREEN_CAPTURE_CANCELLED'
      if (error.name === 'AbortError' || error.name === 'NotReadableError' || error.name === 'OverconstrainedError') return 'MEDIA_CAPTURE_FAILED'
    }
    const message = error instanceof Error ? error.message : ''
    return message.match(/\bMEDIA_[A-Z_]+\b/)?.[0] ?? fallback
  }

  function operationForScreen(value: ScreenDto | null): MediaOperation {
    if (!value) return 'Idle'
    if (value.state === 'Pending') return 'Ringing'
    if (value.state === 'Accepted' || value.state === 'Connecting') return 'Connecting'
    if (value.state === 'Sharing') return 'Active'
    if (value.state === 'Ended' || value.state === 'Declined' || value.state === 'Cancelled' || value.state === 'Expired' || value.state === 'Failed') return 'Idle'
    return 'Error'
  }

  function operationForVoice(value: VoiceDto | null): MediaOperation {
    if (!value) return 'Idle'
    if (value.state === 'Ringing') return 'Ringing'
    if (value.state === 'Accepted' || value.state === 'Connecting') return 'Connecting'
    if (value.state === 'Active') return 'Active'
    return 'Idle'
  }

  function stopTracks(stream: MediaStream | null): void {
    stream?.getTracks().forEach((track) => track.stop())
  }

  function requestId(): string {
    return createCorrelationId().replaceAll('-', '')
  }

  return {
    conversationNId,
    mediaConversationNId,
    screen,
    voice,
    binding,
    mediaContextNId,
    screenOperation,
    voiceOperation,
    muted,
    errorMessage,
    screenErrorMessage,
    voiceErrorMessage,
    playbackBlocked,
    screenPlaybackBlocked,
    voicePlaybackBlocked,
    remoteStream,
    localScreenStream,
    localVoiceStream,
    peerConnection,
    hasScreen,
    hasVoice,
    hasLocalVoiceCapture,
    mediaSession,
    isActive,
    setRealtime,
    refresh,
    inviteScreenShare,
    respondScreenShare,
    endScreenShare,
    inviteVoiceCall,
    respondVoiceCall,
    endVoiceCall,
    ensureBinding,
    startScreenCapture,
    startVoiceCapture,
    setVoiceMuted,
    markScreenPlayable,
    markVoicePlayable,
    markPlaybackBlocked,
    resumePlayback,
    endAll,
    disposeAll,
  }
})
