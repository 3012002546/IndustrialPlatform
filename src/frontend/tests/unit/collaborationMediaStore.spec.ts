import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import type { CollaborationRealtime, CollaborationRealtimeHandlers } from '@/api/collaborationHub'
import type { MediaBindingDto, MediaSignalRequest, ScreenDto, VoiceDto } from '@/api/collaborationMedia'
import { MediaIceQueue } from '@/stores/mediaIceQueue'
import { useCollaborationMediaStore } from '@/stores/collaborationMediaStore'

const storeSource = readFileSync(
  resolve(process.cwd(), 'src/stores/collaborationMediaStore.ts'),
  'utf8',
)
const hostSource = readFileSync(
  resolve(process.cwd(), 'src/components/collaboration/CollaborationMediaHost.vue'),
  'utf8',
).replaceAll('\r\n', '\n')
const screenPanelSource = readFileSync(
  resolve(process.cwd(), 'src/components/collaboration/ScreenSharePanel.vue'),
  'utf8',
)

describe('collaboration media WebRTC contract', () => {
  it('owns media lifecycle at the embedded page boundary', () => {
    expect(hostSource).toContain("if (embedded && !isCollaborationRoute(router.currentRoute.value.name)")
    expect(hostSource).toContain('await media.endAll()')
    expect(hostSource).toContain("window.addEventListener('pagehide', handlePageHide)")
    expect(hostSource).toContain('void realtime.stop().catch(() => undefined)')
    expect(hostSource).toContain("if (!embedded) return\n  void media.endAll()")
  })

  it('keeps RelayOnly, role-aware slots, negotiation ids, and bounded ICE queuing in the production store', () => {
    expect(storeSource).toContain("iceTransportPolicy: currentBinding.icePolicy === 'RelayOnly' ? 'relay' : 'all'")
    expect(storeSource).toContain("pc.addTransceiver('audio', { direction: currentBinding.voice ? 'sendrecv' : 'inactive' })")
    expect(storeSource).toContain("pc.addTransceiver('video', { direction: screenDirection(currentBinding, 'Low') })")
    expect(storeSource).toContain("pc.addTransceiver('video', { direction: screenDirection(currentBinding, 'High') })")
    expect(storeSource).toContain("if (currentBinding.endpointRole === 'Low') {")
    expect(storeSource).toContain('syncTransceiverDirections(pc, binding.value)')
    expect(storeSource).toContain('await attachLocalTracks(pc, binding.value)')
    expect(storeSource).toContain('await flushIce(event.negotiationNId, pc)')
    expect(storeSource).toContain("await sendDescription('Answer', pc.localDescription, event.negotiationNId)")
    expect(storeSource).toContain('iceQueue.add(event.negotiationNId, event.candidate as RTCIceCandidateInit)')
    expect(storeSource).toContain('discardOldNegotiations(event.negotiationNId)')
    expect(storeSource).toContain('const queued = iceQueue.take(negotiationId)')
    expect(storeSource).toContain('function expireIce(negotiationId: string)')
    expect(storeSource).toContain('event.negotiationNId !== negotiationNId')
    expect(storeSource).toContain('let screenGeneration = 0')
    expect(storeSource).toContain('let voiceGeneration = 0')
    expect(storeSource).toContain('addRemoteTrack(event.track)')
    expect(storeSource).toContain('markScreenPlayable')
    expect(storeSource).toContain('playbackBlocked')
    expect(storeSource).toContain("detachRemoteTracks('video')")
    expect(storeSource).toContain("detachRemoteTracks('audio')")
    expect(storeSource).toContain('return { senderDetached, captureTracksEnded, playbackDetached: true }')
    expect(hostSource).toContain('v-if="media.mediaSession"')
    expect(hostSource).toContain('media.voicePlaybackBlocked')
    expect(storeSource).toContain('voicePlaybackObserved')
    expect(storeSource).toContain('markVoicePlayable')
    expect(storeSource).toContain('captureVoice(captureWindow)')
    expect(storeSource).toContain('if (!value && !hadCapture) await stopLocalVoice()')
    expect(storeSource).toContain('await captureScreen(captureWindow)')
    expect(screenPanelSource).not.toContain('<video')
  })

  it('expires queued ICE with a timer even when no later signal arrives', () => {
    vi.useFakeTimers()
    try {
      const expired = vi.fn()
      const queue = new MediaIceQueue(expired)

      expect(queue.add('negotiation-1', { candidate: 'candidate:1' })).toBe(true)
      vi.advanceTimersByTime(9_999)
      expect(expired).not.toHaveBeenCalled()
      vi.advanceTimersByTime(1)

      expect(expired).toHaveBeenCalledOnce()
      expect(expired).toHaveBeenCalledWith('negotiation-1')
      expect(queue.take('negotiation-1')).toEqual([])
    } finally {
      vi.useRealTimers()
    }
  })

  it('cancels old negotiation timers when a newer negotiation becomes current', () => {
    vi.useFakeTimers()
    try {
      const expired = vi.fn()
      const queue = new MediaIceQueue(expired)
      queue.add('old', { candidate: 'candidate:old' })
      queue.add('new', { candidate: 'candidate:new' })
      queue.discardExcept('new')

      vi.advanceTimersByTime(10_000)

      expect(expired).toHaveBeenCalledOnce()
      expect(expired).toHaveBeenCalledWith('new')
    } finally {
      vi.useRealTimers()
    }
  })
})

class FakeTrack {
  readyState: MediaStreamTrackState = 'live'
  enabled = true
  contentHint = ''

  constructor(readonly kind: 'audio' | 'video') {}

  addEventListener(_type: string, _listener: EventListenerOrEventListenerObject): void {
    void _type
    void _listener
  }

  stop(): void {
    this.readyState = 'ended'
  }
}

class FakeMediaStream {
  private readonly tracks: FakeTrack[]

  constructor(tracks: FakeTrack[] = []) {
    this.tracks = tracks
  }

  getTracks(): FakeTrack[] {
    return [...this.tracks]
  }

  getAudioTracks(): FakeTrack[] {
    return this.tracks.filter((track) => track.kind === 'audio')
  }

  getVideoTracks(): FakeTrack[] {
    return this.tracks.filter((track) => track.kind === 'video')
  }
}

class FakeSender {
  track: FakeTrack | null = null

  async replaceTrack(track: MediaStreamTrack | null): Promise<void> {
    this.track = track as FakeTrack | null
  }
}

class FakeTransceiver {
  readonly sender = new FakeSender()

  constructor(public direction: RTCRtpTransceiverDirection) {}
}

class FakePeerConnection {
  static readonly instances: FakePeerConnection[] = []
  readonly transceivers: FakeTransceiver[] = []
  readonly getTransceivers = (): FakeTransceiver[] => [...this.transceivers]
  localDescription: RTCSessionDescription | null = null
  remoteDescription: RTCSessionDescription | null = null
  signalingState: RTCSignalingState = 'stable'
  connectionState: RTCPeerConnectionState = 'new'
  createOfferCount = 0
  createAnswerCount = 0
  ontrack: ((event: RTCTrackEvent) => void) | null = null
  onicecandidate: ((event: RTCPeerConnectionIceEvent) => void) | null = null
  onconnectionstatechange: (() => void) | null = null

  constructor(_configuration?: RTCConfiguration) {
    void _configuration
    FakePeerConnection.instances.push(this)
  }

  addTransceiver(_kind: string, init: { direction: RTCRtpTransceiverDirection }): FakeTransceiver {
    const transceiver = new FakeTransceiver(init.direction)
    this.transceivers.push(transceiver)
    return transceiver
  }

  getSenders(): FakeSender[] {
    return this.transceivers.map((transceiver) => transceiver.sender)
  }

  async createOffer(): Promise<RTCSessionDescriptionInit> {
    this.createOfferCount += 1
    return { type: 'offer', sdp: `offer-${this.createOfferCount}` }
  }

  async createAnswer(): Promise<RTCSessionDescriptionInit> {
    this.createAnswerCount += 1
    return { type: 'answer', sdp: `answer-${this.createAnswerCount}` }
  }

  async setLocalDescription(description: RTCSessionDescriptionInit = { type: 'offer' }): Promise<void> {
    this.localDescription = { type: description.type ?? '', sdp: description.sdp ?? null } as RTCSessionDescription
    this.signalingState = description.type === 'offer' ? 'have-local-offer' : 'stable'
  }

  async setRemoteDescription(description: RTCSessionDescriptionInit): Promise<void> {
    this.remoteDescription = { type: description.type ?? '', sdp: description.sdp ?? null } as RTCSessionDescription
    if (description.type === 'offer') {
      if (this.transceivers.length === 0) {
        this.addTransceiver('audio', { direction: 'sendrecv' })
        this.addTransceiver('video', { direction: 'sendrecv' })
        this.addTransceiver('video', { direction: 'sendrecv' })
      }
      this.signalingState = 'have-remote-offer'
    } else {
      this.signalingState = 'stable'
    }
  }

  async addIceCandidate(_candidate: RTCIceCandidateInit): Promise<void> {
    void _candidate
  }

  close(): void {
    this.connectionState = 'closed'
    this.signalingState = 'closed'
  }
}

function makeScreen(state: string, sessionNId = 'screen-1'): ScreenDto {
  return {
    sessionNId,
    conversationNId: 'conversation-1',
    direction: 'ShareMine',
    initiatorUserNId: 'user-1',
    inviteeUserNId: 'user-2',
    sharerUserNId: 'user-1',
    viewerUserNId: 'user-2',
    state,
    answer: null,
    version: 1,
    deadlineOn: '2026-09-12T00:00:00Z',
    startedOn: state === 'Sharing' ? '2026-09-12T00:00:00Z' : null,
    endedOn: state === 'Ended' ? '2026-09-12T00:01:00Z' : null,
    endReason: state === 'Ended' ? 'IceTimeout' : null,
    initiatorStoppedReported: false,
    inviteeStoppedReported: false,
  }
}

function makeVoice(state: string, callNId = 'voice-1'): VoiceDto {
  return {
    callNId,
    conversationNId: 'conversation-1',
    callerUserNId: 'user-1',
    calleeUserNId: 'user-2',
    state,
    answer: null,
    version: 1,
    deadlineOn: '2026-09-12T00:00:00Z',
    endedOn: state === 'Ended' ? '2026-09-12T00:01:00Z' : null,
    endReason: state === 'Ended' ? 'IceTimeout' : null,
    startedOn: state === 'Active' ? '2026-09-12T00:00:00Z' : null,
    callerStoppedReported: false,
    calleeStoppedReported: false,
  }
}

function makeBinding(endpointRole: 'Low' | 'High', screen: ScreenDto | null, voice: VoiceDto | null): MediaBindingDto {
  return {
    mediaContextNId: 'context-1',
    contextRevision: 1,
    endpointRole,
    polite: true,
    screen,
    voice,
    icePolicy: 'All',
    iceServers: [],
    confirm: {
      screen: screen ? { sessionNId: screen.sessionNId, state: screen.state, version: screen.version, validForMs: 30_000, allowed: true, errorCode: null } : null,
      voice: voice ? { sessionNId: voice.callNId, state: voice.state, version: voice.version, validForMs: 30_000, allowed: true, errorCode: null } : null,
    },
  }
}

function ok<T>(data: T): { ok: true; data: T; error: null; traceId: string } {
  return { ok: true, data, error: null, traceId: 'trace-1' }
}

function createFakeRealtime(bindings: MediaBindingDto[]): {
  realtime: CollaborationRealtime
  handlers: CollaborationRealtimeHandlers
  signals: MediaSignalRequest[]
  endedScreens: string[]
  endedVoices: string[]
} {
  let bindingIndex = 0
  const handlers: CollaborationRealtimeHandlers = {}
  const signals: MediaSignalRequest[] = []
  const endedScreens: string[] = []
  const endedVoices: string[] = []
  const realtime = {
    connection: {} as never,
    start: async () => undefined,
    stop: async () => undefined,
    subscribe: (next: CollaborationRealtimeHandlers) => {
      Object.assign(handlers, next)
      return () => undefined
    },
    bindMedia: async () => ok(bindings[Math.min(bindingIndex++, bindings.length - 1)]!),
    signalMedia: async (request: MediaSignalRequest) => {
      signals.push(request)
      return ok({ status: 'Accepted' })
    },
    endScreenShare: async ({ sessionNId }: { sessionNId: string }) => {
      endedScreens.push(sessionNId)
      return ok(makeScreen('Ended', sessionNId))
    },
    endVoiceCall: async ({ callNId }: { callNId: string }) => {
      endedVoices.push(callNId)
      return ok(makeVoice('Ended', callNId))
    },
    reportMediaStopped: async () => ok(null),
    keepAliveMedia: async () => ok({ contextRevision: 1, screen: null, voice: null }),
  } as unknown as CollaborationRealtime
  return { realtime, handlers, signals, endedScreens, endedVoices }
}

type CapabilityErrors = {
  screenErrorMessage: string | null
  voiceErrorMessage: string | null
}

async function flushPromises(): Promise<void> {
  for (let index = 0; index < 10; index += 1) await Promise.resolve()
}

function deferred<T>(): { promise: Promise<T>; resolve: (value: T) => void } {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((next) => { resolve = next })
  return { promise, resolve }
}

function mediaFailure(code = 'MEDIA_CONTEXT_STALE') {
  return { ok: false as const, data: null, error: { code, messageKey: 'contextError' }, traceId: 'trace-failure' }
}

describe('collaboration media integration behavior', () => {
  let originalPeerConnection: typeof globalThis.RTCPeerConnection | undefined
  let originalMediaStream: typeof globalThis.MediaStream | undefined
  let originalMediaDevices: MediaDevices | undefined

  beforeEach(() => {
    originalPeerConnection = globalThis.RTCPeerConnection
    originalMediaStream = globalThis.MediaStream
    originalMediaDevices = navigator.mediaDevices
    Object.defineProperty(globalThis, 'RTCPeerConnection', { configurable: true, writable: true, value: FakePeerConnection })
    Object.defineProperty(globalThis, 'MediaStream', { configurable: true, writable: true, value: FakeMediaStream })
    Object.defineProperty(navigator, 'mediaDevices', {
      configurable: true,
      value: {
        getUserMedia: async () => new FakeMediaStream([new FakeTrack('audio')]),
        getDisplayMedia: async () => new FakeMediaStream([new FakeTrack('video')]),
      },
    })
    FakePeerConnection.instances.length = 0
  })

  afterEach(() => {
    Object.defineProperty(globalThis, 'RTCPeerConnection', { configurable: true, writable: true, value: originalPeerConnection })
    Object.defineProperty(globalThis, 'MediaStream', { configurable: true, writable: true, value: originalMediaStream })
    Object.defineProperty(navigator, 'mediaDevices', { configurable: true, value: originalMediaDevices })
    vi.useRealTimers()
  })

  it('shows a localized HTTPS requirement before attempting microphone capture on HTTP', async () => {
    Object.defineProperty(window, 'isSecureContext', { configurable: true, value: false })
    const getUserMedia = vi.fn(async () => new FakeMediaStream([new FakeTrack('audio')]))
    Object.defineProperty(navigator, 'mediaDevices', {
      configurable: true,
      value: { getUserMedia },
    })
    const voice = makeVoice('Accepted')
    const binding = makeBinding('Low', null, voice)
    const fake = createFakeRealtime([binding])
    setActivePinia(createPinia())
    const store = useCollaborationMediaStore()
    store.voice = voice
    store.binding = binding
    store.mediaContextNId = 'context-1'
    store.setRealtime(fake.realtime)

    await store.startVoiceCapture()

    expect(store.errorMessage).toBe('MEDIA_SECURE_CONTEXT_REQUIRED')
    expect(getUserMedia).not.toHaveBeenCalled()
    await store.disposeAll()
  })

  it('keeps voice capture available when HTTPS lacks screen capture API', async () => {
    Object.defineProperty(window, 'isSecureContext', { configurable: true, value: true })
    const getUserMedia = vi.fn(async () => new FakeMediaStream([new FakeTrack('audio')]))
    Object.defineProperty(navigator, 'mediaDevices', {
      configurable: true,
      value: { getUserMedia },
    })
    const voice = makeVoice('Accepted')
    const binding = makeBinding('High', null, voice)
    const fake = createFakeRealtime([binding])
    setActivePinia(createPinia())
    const store = useCollaborationMediaStore()
    store.voice = voice
    store.binding = binding
    store.mediaContextNId = 'context-1'
    store.setRealtime(fake.realtime)

    await store.startVoiceCapture()

    expect(getUserMedia).toHaveBeenCalledOnce()
    expect(store.errorMessage).not.toBe('MEDIA_UNSUPPORTED')
    expect(store.voiceErrorMessage).not.toBe('MEDIA_UNSUPPORTED')
    await store.disposeAll()
  })

  it('keeps failed invite actions visible when realtime invocation rejects', async () => {
    const fake = createFakeRealtime([])
    Object.assign(fake.realtime, {
      inviteScreenShare: vi.fn().mockRejectedValue(new Error('Failed to invoke InviteScreenShare')),
      inviteVoiceCall: vi.fn().mockRejectedValue(new Error('Failed to invoke InviteVoiceCall')),
    })
    setActivePinia(createPinia())
    const store = useCollaborationMediaStore()
    store.conversationNId = 'conversation-1'
    store.setRealtime(fake.realtime)

    await expect(store.inviteScreenShare('ShareMine')).resolves.toBeNull()
    expect(store.errorMessage).toBe('MEDIA_SIGNAL_FAILED')
    expect(store.screenOperation).toBe('Error')

    await expect(store.inviteVoiceCall()).resolves.toBeNull()
    expect(store.errorMessage).toBe('MEDIA_SIGNAL_FAILED')
    expect(store.voiceOperation).toBe('Error')

    await store.disposeAll()
  })

  it('releases a newly captured microphone when the invite result is rejected by the hub', async () => {
    const fake = createFakeRealtime([])
    const track = new FakeTrack('audio')
    Object.defineProperty(navigator, 'mediaDevices', {
      configurable: true,
      value: { getUserMedia: async () => new FakeMediaStream([track]) },
    })
    Object.assign(fake.realtime, {
      inviteVoiceCall: vi.fn().mockResolvedValue({
        ok: false,
        data: null,
        error: { code: 'MEDIA_FORBIDDEN', messageKey: 'forbidden' },
        traceId: 'trace-rejected',
      }),
    })
    setActivePinia(createPinia())
    const store = useCollaborationMediaStore()
    store.conversationNId = 'conversation-1'
    store.setRealtime(fake.realtime)

    await expect(store.inviteVoiceCall()).resolves.toBeNull()
    expect(track.readyState).toBe('ended')
    expect(store.localVoiceStream).toBeNull()

    await store.disposeAll()
  })

  it('captures screen content before inviting and releases the pre-capture when the hub rejects it', async () => {
    const fake = createFakeRealtime([])
    const track = new FakeTrack('video')
    const order: string[] = []
    Object.defineProperty(navigator, 'mediaDevices', {
      configurable: true,
      value: { getDisplayMedia: async () => { order.push('capture'); return new FakeMediaStream([track]) } },
    })
    Object.assign(fake.realtime, {
      inviteScreenShare: vi.fn().mockImplementation(async () => {
        order.push('invite')
        return { ok: false, data: null, error: { code: 'MEDIA_FORBIDDEN', messageKey: 'forbidden' }, traceId: 'trace-rejected' }
      }),
    })
    setActivePinia(createPinia())
    const store = useCollaborationMediaStore()
    store.conversationNId = 'conversation-1'
    store.setRealtime(fake.realtime)

    await expect(store.inviteScreenShare('ShareMine')).resolves.toBeNull()
    expect(order).toEqual(['capture', 'invite'])
    expect(track.readyState).toBe('ended')
    expect(store.localScreenStream).toBeNull()

    await store.disposeAll()
  })

  it('surfaces a binding rejection on only the newly added capability', async () => {
    const screen = makeScreen('Sharing', 'screen-active')
    const voice = makeVoice('Accepted', 'voice-added')
    const binding = makeBinding('Low', screen, null)
    const fake = createFakeRealtime([binding])
    Object.assign(fake.realtime, {
      bindMedia: vi.fn().mockResolvedValue({
        ok: false,
        data: null,
        error: { code: 'MEDIA_ICE_CONFIGURATION', messageKey: 'iceConfiguration' },
        traceId: 'trace-bind-rejected',
      }),
    })
    setActivePinia(createPinia())
    const store = useCollaborationMediaStore()
    const errors = store as typeof store & CapabilityErrors
    store.conversationNId = 'conversation-1'
    store.mediaConversationNId = 'conversation-1'
    store.screen = screen
    store.voice = voice
    store.binding = binding
    store.screenOperation = 'Active'
    store.voiceOperation = 'Connecting'
    store.setRealtime(fake.realtime)

    await expect(store.ensureBinding()).resolves.toMatchObject(binding)

    expect(store.screenOperation).toBe('Active')
    expect(store.voiceOperation).toBe('Error')
    expect(errors.screenErrorMessage).toBeNull()
    expect(errors.voiceErrorMessage).toBe('MEDIA_ICE_CONFIGURATION')
    expect(store.errorMessage).toBeNull()
    expect(fake.realtime.bindMedia).toHaveBeenCalledOnce()

    await store.disposeAll()
  })

  it('starts binding once from a Connecting event and skips repeat events after a valid binding', async () => {
    const screen = makeScreen('Connecting', 'screen-connecting')
    const binding = makeBinding('Low', screen, null)
    const fake = createFakeRealtime([binding])
    const bindMedia = vi.fn().mockResolvedValue(ok(binding))
    Object.assign(fake.realtime, { bindMedia })
    setActivePinia(createPinia())
    const store = useCollaborationMediaStore()
    store.conversationNId = 'conversation-1'
    store.localScreenStream = new FakeMediaStream([new FakeTrack('video')]) as unknown as MediaStream
    store.setRealtime(fake.realtime)

    fake.handlers.onScreenChanged?.(screen)
    await flushPromises()
    fake.handlers.onScreenChanged?.(screen)
    await flushPromises()

    expect(bindMedia).toHaveBeenCalledOnce()
    expect(store.binding?.mediaContextNId).toBe(binding.mediaContextNId)
    expect(FakePeerConnection.instances).toHaveLength(1)

    await store.disposeAll()
  })

  it('does not auto-bind an unselected page from an accepted broadcast', async () => {
    const screen = makeScreen('Accepted', 'screen-broadcast')
    const binding = makeBinding('High', screen, null)
    const fake = createFakeRealtime([binding])
    const bindMedia = vi.fn().mockResolvedValue(mediaFailure('MEDIA_ENDPOINT_BOUND'))
    Object.assign(fake.realtime, { bindMedia })
    setActivePinia(createPinia())
    const store = useCollaborationMediaStore()
    store.setRealtime(fake.realtime)

    fake.handlers.onScreenChanged?.(screen)
    await flushPromises()

    expect(bindMedia).not.toHaveBeenCalled()
    expect(store.screenErrorMessage).toBeNull()

    await store.disposeAll()
  })

  it('keeps RequestPeer intent when the peer accepts without local capture', async () => {
    const pending = makeScreen('Pending', 'screen-request-peer')
    const accepted = makeScreen('Accepted', pending.sessionNId)
    const binding = makeBinding('Low', accepted, null)
    const fake = createFakeRealtime([binding])
    const bindMedia = vi.fn().mockResolvedValue(ok(binding))
    Object.assign(fake.realtime, {
      inviteScreenShare: vi.fn().mockResolvedValue(ok(pending)),
      bindMedia,
    })
    setActivePinia(createPinia())
    const store = useCollaborationMediaStore()
    store.conversationNId = 'conversation-1'
    store.setRealtime(fake.realtime)

    await expect(store.inviteScreenShare('RequestPeer')).resolves.toMatchObject({ sessionNId: pending.sessionNId })
    fake.handlers.onScreenChanged?.(accepted)
    await flushPromises()

    expect(bindMedia).toHaveBeenCalledOnce()
    await store.disposeAll()
  })

  it('accepts a voice-only context change after ended screen history', async () => {
    const screen = makeScreen('Ended', 'screen-ended')
    const voice = makeVoice('Accepted', 'voice-only')
    const binding = makeBinding('Low', null, voice)
    const fake = createFakeRealtime([binding])
    setActivePinia(createPinia())
    const store = useCollaborationMediaStore()
    store.conversationNId = 'conversation-1'
    store.mediaConversationNId = 'conversation-1'
    store.screen = screen
    store.voice = voice
    store.binding = binding
    store.mediaContextNId = 'context-1'
    store.peerConnection = new FakePeerConnection() as unknown as RTCPeerConnection
    store.setRealtime(fake.realtime)

    fake.handlers.onMediaContextChanged?.({
      mediaContextNId: 'context-1',
      contextRevision: 2,
      screenSessionNId: null,
      voiceCallNId: voice.callNId,
    })
    await flushPromises()

    expect(store.binding?.contextRevision).toBe(2)
    expect(fake.signals.at(-1)).toMatchObject({ kind: 'Offer', contextRevision: 2 })

    await store.disposeAll()
  })

  it('drops a late screen signal failure after screen finalization starts', async () => {
    const screen = makeScreen('Sharing', 'screen-late-signal')
    const binding = makeBinding('High', screen, null)
    const pendingSignal = deferred<ReturnType<typeof mediaFailure>>()
    const pendingStopped = deferred<ReturnType<typeof ok<null>>>()
    const fake = createFakeRealtime([binding])
    Object.assign(fake.realtime, {
      bindMedia: vi.fn().mockResolvedValue(ok(binding)),
      signalMedia: vi.fn().mockImplementation(async (request: MediaSignalRequest) => {
        fake.signals.push(request)
        return pendingSignal.promise
      }),
      reportMediaStopped: vi.fn().mockImplementation(() => pendingStopped.promise),
    })
    setActivePinia(createPinia())
    const store = useCollaborationMediaStore()
    store.conversationNId = 'conversation-1'
    store.mediaConversationNId = 'conversation-1'
    store.screen = screen
    store.binding = binding
    store.mediaContextNId = 'context-1'
    store.setRealtime(fake.realtime)

    await store.ensureBinding()
    const pc = store.peerConnection as unknown as FakePeerConnection
    pc.onicecandidate?.({ candidate: { candidate: 'candidate-late-screen', sdpMid: '0', sdpMLineIndex: 0, usernameFragment: null } } as RTCPeerConnectionIceEvent)
    await flushPromises()

    fake.handlers.onScreenChanged?.(makeScreen('Ended', screen.sessionNId))
    await flushPromises()
    pendingSignal.resolve(mediaFailure('MEDIA_NOT_ACCEPTED'))
    await flushPromises()

    expect(store.errorMessage).toBeNull()
    pendingStopped.resolve(ok(null))
    await flushPromises()
    expect(store.screen).toBeNull()

    await store.disposeAll()
  })

  it('drops a late screen ready failure while keeping an active voice call', async () => {
    const screen = makeScreen('Sharing', 'screen-late-ready')
    const voice = makeVoice('Active', 'voice-kept')
    const binding = makeBinding('High', screen, voice)
    const pendingReady = deferred<ReturnType<typeof mediaFailure>>()
    const pendingStopped = deferred<ReturnType<typeof ok<null>>>()
    const fake = createFakeRealtime([binding])
    Object.assign(fake.realtime, {
      bindMedia: vi.fn().mockResolvedValue(ok(binding)),
      mediaReady: vi.fn().mockImplementation(() => pendingReady.promise),
      reportMediaStopped: vi.fn().mockImplementation(() => pendingStopped.promise),
    })
    setActivePinia(createPinia())
    const store = useCollaborationMediaStore()
    store.conversationNId = 'conversation-1'
    store.mediaConversationNId = 'conversation-1'
    store.screen = screen
    store.voice = voice
    store.binding = binding
    store.mediaContextNId = 'context-1'
    store.setRealtime(fake.realtime)

    const readyPromise = store.markScreenPlayable()
    await flushPromises()
    fake.handlers.onScreenChanged?.(makeScreen('Ended', screen.sessionNId))
    await flushPromises()
    pendingReady.resolve(mediaFailure('MEDIA_NOT_ACCEPTED'))
    await flushPromises()

    expect(store.errorMessage).toBeNull()
    expect(store.voice?.callNId).toBe(voice.callNId)
    pendingStopped.resolve(ok(null))
    await readyPromise
    await flushPromises()

    expect(store.screen).toBeNull()
    expect(store.voice?.callNId).toBe(voice.callNId)
    await store.disposeAll()
  })

  it('keeps a real MEDIA_NOT_ACCEPTED error for the current screen invitation', async () => {
    const fake = createFakeRealtime([])
    Object.assign(fake.realtime, {
      inviteScreenShare: vi.fn().mockResolvedValue(mediaFailure('MEDIA_NOT_ACCEPTED')),
    })
    setActivePinia(createPinia())
    const store = useCollaborationMediaStore()
    store.conversationNId = 'conversation-1'
    store.setRealtime(fake.realtime)

    await expect(store.inviteScreenShare('RequestPeer')).resolves.toBeNull()

    expect(store.errorMessage).toBe('MEDIA_NOT_ACCEPTED')
    await store.disposeAll()
  })

  it('does not rebind an ended screen from a late bind success while voice continues', async () => {
    const screen = makeScreen('Sharing', 'screen-late-bind')
    const endedScreen = makeScreen('Ended', screen.sessionNId)
    const voice = makeVoice('Active', 'voice-bind-kept')
    const voiceBinding = makeBinding('High', null, voice)
    const fullBinding = makeBinding('High', screen, voice)
    const pendingBind = deferred<ReturnType<typeof ok<MediaBindingDto>>>()
    const pendingStopped = deferred<ReturnType<typeof ok<null>>>()
    const fake = createFakeRealtime([fullBinding])
    Object.assign(fake.realtime, {
      bindMedia: vi.fn().mockImplementation(() => pendingBind.promise),
      reportMediaStopped: vi.fn().mockImplementation(() => pendingStopped.promise),
    })
    setActivePinia(createPinia())
    const store = useCollaborationMediaStore()
    store.conversationNId = 'conversation-1'
    store.mediaConversationNId = 'conversation-1'
    store.screen = screen
    store.voice = voice
    store.binding = voiceBinding
    store.mediaContextNId = 'context-1'
    store.setRealtime(fake.realtime)

    const bindingPromise = store.ensureBinding()
    await flushPromises()
    fake.handlers.onScreenChanged?.(endedScreen)
    await flushPromises()
    pendingBind.resolve(ok(fullBinding))
    await flushPromises()
    pendingStopped.resolve(ok(null))
    await bindingPromise
    await flushPromises()

    expect(store.screen).toBeNull()
    expect(store.voice?.callNId).toBe(voice.callNId)
    expect(store.binding?.screen).toBeNull()
    await store.disposeAll()
  })

  it('does not roll the binding revision back from a late keep-alive result', async () => {
    vi.useFakeTimers()
    const voice = makeVoice('Accepted', 'voice-heartbeat')
    const binding = { ...makeBinding('High', null, voice), contextRevision: 5 }
    const fake = createFakeRealtime([binding])
    Object.assign(fake.realtime, {
      keepAliveMedia: vi.fn().mockResolvedValue(ok({
        contextRevision: 4,
        screen: null,
        voice: { callNId: voice.callNId, state: voice.state, version: voice.version, validForMs: 30_000, allowed: true, errorCode: null },
      })),
    })
    setActivePinia(createPinia())
    const store = useCollaborationMediaStore()
    store.conversationNId = 'conversation-1'
    store.mediaConversationNId = 'conversation-1'
    store.voice = voice
    store.binding = binding
    store.mediaContextNId = 'context-1'
    store.setRealtime(fake.realtime)

    await store.startVoiceCapture()
    vi.advanceTimersByTime(10_000)
    await flushPromises()

    expect(store.binding?.contextRevision).toBe(5)
    expect(fake.realtime.keepAliveMedia).toHaveBeenCalledOnce()

    await store.disposeAll()
  })

  it.each([
    { role: 'Low' as const, direction: 'screen-to-voice' as const },
    { role: 'High' as const, direction: 'screen-to-voice' as const },
    { role: 'Low' as const, direction: 'voice-to-screen' as const },
    { role: 'High' as const, direction: 'voice-to-screen' as const },
  ])('keeps the latest revision when $role receives a $direction capability', async ({ role, direction }) => {
    const existingScreen = direction === 'screen-to-voice' ? makeScreen('Sharing', 'screen-existing') : null
    const existingVoice = direction === 'voice-to-screen' ? makeVoice('Active', 'voice-existing') : null
    const addedScreen = direction === 'voice-to-screen' ? makeScreen('Connecting', 'screen-added') : null
    const addedVoice = direction === 'screen-to-voice' ? makeVoice('Accepted', 'voice-added') : null
    const initialBinding = { ...makeBinding(role, existingScreen, existingVoice), contextRevision: 1 }
    const fullBinding = { ...makeBinding(role, addedScreen ?? existingScreen, addedVoice ?? existingVoice), contextRevision: 3 }
    const fake = createFakeRealtime([fullBinding])
    let serverRevision = 1
    const staleSignals: MediaSignalRequest[] = []
    Object.assign(fake.realtime, {
      bindMedia: vi.fn().mockResolvedValue(ok(fullBinding)),
      signalMedia: vi.fn().mockImplementation(async (request: MediaSignalRequest) => {
        fake.signals.push(request)
        if (Number(request.contextRevision) !== serverRevision) {
          staleSignals.push(request)
          return { ok: false, data: null, error: { code: 'MEDIA_CONTEXT_STALE', messageKey: 'contextError' }, traceId: 'trace-stale' }
        }
        return ok({ status: 'Accepted' })
      }),
    })
    setActivePinia(createPinia())
    const store = useCollaborationMediaStore()
    store.conversationNId = 'conversation-1'
    store.mediaConversationNId = 'conversation-1'
    store.screen = existingScreen
    store.voice = existingVoice
    store.binding = initialBinding
    store.mediaContextNId = 'context-1'
    const pc = new FakePeerConnection()
    pc.addTransceiver('audio', { direction: existingVoice ? 'sendrecv' : 'inactive' })
    pc.addTransceiver('video', { direction: 'inactive' })
    pc.addTransceiver('video', { direction: 'inactive' })
    store.peerConnection = pc as unknown as RTCPeerConnection
    store.setRealtime(fake.realtime)

    serverRevision = 2
    fake.handlers.onMediaContextChanged?.({
      mediaContextNId: 'context-1',
      contextRevision: serverRevision,
      screenSessionNId: existingScreen?.sessionNId ?? addedScreen?.sessionNId ?? null,
      voiceCallNId: addedVoice?.callNId ?? existingVoice?.callNId ?? null,
    })
    await flushPromises()

    expect(store.binding?.contextRevision).toBe(2)
    const firstOffer = fake.signals.at(-1)
    serverRevision = 3
    if (addedVoice) fake.handlers.onVoiceChanged?.(addedVoice)
    else fake.handlers.onScreenChanged?.(addedScreen!)
    await flushPromises()

    if (role === 'Low' && firstOffer) {
      fake.handlers.onMediaSignal?.({
        mediaContextNId: 'context-1',
        contextRevision: 2,
        negotiationNId: firstOffer.negotiationNId,
        sequence: 2,
        kind: 'Answer',
        description: { type: 'answer', sdp: 'old-answer' },
      })
      await flushPromises()
    }

    expect(fake.realtime.bindMedia).toHaveBeenCalledOnce()
    expect(store.binding?.contextRevision).toBe(3)
    expect(staleSignals).toHaveLength(0)
    if (role === 'Low') expect(fake.signals.map((signal) => Number(signal.contextRevision))).toEqual([2, 3])
    else expect(fake.signals).toHaveLength(0)

    await store.disposeAll()
  })

  it('does not surface a stale negotiation failure after a newer revision negotiates successfully', async () => {
    const voice = makeVoice('Active', 'voice-stale-negotiation')
    const binding = makeBinding('Low', null, voice)
    const pendingOffer = deferred<ReturnType<typeof mediaFailure>>()
    const fake = createFakeRealtime([binding])
    let deferNextOffer = false
    Object.assign(fake.realtime, {
      bindMedia: vi.fn().mockResolvedValue(ok(binding)),
      signalMedia: vi.fn().mockImplementation(async (request: MediaSignalRequest) => {
        fake.signals.push(request)
        if (deferNextOffer && request.kind === 'Offer' && Number(request.contextRevision) === 1) {
          deferNextOffer = false
          return pendingOffer.promise
        }
        return ok({ status: 'Accepted' })
      }),
    })
    setActivePinia(createPinia())
    const store = useCollaborationMediaStore()
    store.conversationNId = 'conversation-1'
    store.mediaConversationNId = 'conversation-1'
    store.voice = voice
    store.binding = binding
    store.mediaContextNId = 'context-1'
    store.setRealtime(fake.realtime)

    await store.ensureBinding()
    deferNextOffer = true
    const startPromise = store.startVoiceCapture()
    await flushPromises()

    fake.handlers.onMediaContextChanged?.({
      mediaContextNId: 'context-1',
      contextRevision: 2,
      screenSessionNId: null,
      voiceCallNId: voice.callNId,
    })
    await flushPromises()

    pendingOffer.resolve(mediaFailure())
    await startPromise
    await flushPromises()

    expect(fake.signals).toContainEqual(expect.objectContaining({ kind: 'Offer', contextRevision: 2 }))
    expect(store.errorMessage).toBeNull()
    await store.disposeAll()
  })

  it('clears a current signal failure when a newer revision recovers', async () => {
    const voice = makeVoice('Active', 'voice-recovering-signal')
    const binding = makeBinding('Low', null, voice)
    const fake = createFakeRealtime([binding])
    Object.assign(fake.realtime, {
      bindMedia: vi.fn().mockResolvedValue(ok(binding)),
      signalMedia: vi.fn().mockImplementation(async (request: MediaSignalRequest) => {
        fake.signals.push(request)
        if (request.kind === 'IceCandidate' && Number(request.contextRevision) === 1) return mediaFailure()
        return ok({ status: 'Accepted' })
      }),
    })
    setActivePinia(createPinia())
    const store = useCollaborationMediaStore()
    store.conversationNId = 'conversation-1'
    store.mediaConversationNId = 'conversation-1'
    store.voice = voice
    store.binding = binding
    store.mediaContextNId = 'context-1'
    store.setRealtime(fake.realtime)

    await store.ensureBinding()
    const pc = store.peerConnection as unknown as FakePeerConnection
    pc.onicecandidate?.({ candidate: { candidate: 'candidate-current', sdpMid: '0', sdpMLineIndex: 0, usernameFragment: null } } as RTCPeerConnectionIceEvent)
    await flushPromises()
    expect(store.errorMessage).toBe('MEDIA_CONTEXT_STALE')

    fake.handlers.onMediaContextChanged?.({
      mediaContextNId: 'context-1',
      contextRevision: 2,
      screenSessionNId: null,
      voiceCallNId: voice.callNId,
    })
    await flushPromises()

    expect(fake.signals).toContainEqual(expect.objectContaining({ kind: 'Offer', contextRevision: 2 }))
    expect(store.errorMessage).toBeNull()
    await store.disposeAll()
  })

  it('keeps a current signal failure visible alongside another capability failure', async () => {
    const screen = makeScreen('Sharing', 'screen-error-visible')
    const voice = makeVoice('Active', 'voice-error-visible')
    const binding = makeBinding('High', screen, voice)
    const fake = createFakeRealtime([binding])
    Object.assign(fake.realtime, {
      bindMedia: vi.fn().mockResolvedValue(ok(binding)),
      signalMedia: vi.fn().mockImplementation(async (request: MediaSignalRequest) => {
        fake.signals.push(request)
        if (request.kind === 'IceCandidate') return mediaFailure('MEDIA_SIGNAL_FAILED')
        return ok({ status: 'Accepted' })
      }),
    })
    setActivePinia(createPinia())
    const store = useCollaborationMediaStore()
    const errors = store as typeof store & CapabilityErrors
    store.conversationNId = 'conversation-1'
    store.mediaConversationNId = 'conversation-1'
    store.screen = screen
    store.voice = voice
    store.binding = binding
    store.mediaContextNId = 'context-1'
    store.setRealtime(fake.realtime)

    await store.ensureBinding()
    errors.screenErrorMessage = 'MEDIA_ICE_CONFIGURATION'
    const pc = store.peerConnection as unknown as FakePeerConnection
    pc.onicecandidate?.({ candidate: { candidate: 'candidate-valid', sdpMid: '0', sdpMLineIndex: 0, usernameFragment: null } } as RTCPeerConnectionIceEvent)
    await flushPromises()

    expect(store.errorMessage).toBe('MEDIA_SIGNAL_FAILED')
    expect(errors.screenErrorMessage).toBe('MEDIA_ICE_CONFIGURATION')
    await store.disposeAll()
  })

  it('ignores a stale media-ready failure and preserves current failure recovery', async () => {
    const screen = makeScreen('Sharing', 'screen-ready-stale')
    const binding = makeBinding('Low', screen, null)
    const pendingReady = deferred<ReturnType<typeof mediaFailure>>()
    let readyCall = 0
    const fake = createFakeRealtime([binding])
    Object.assign(fake.realtime, {
      bindMedia: vi.fn().mockResolvedValue(ok(binding)),
      signalMedia: vi.fn().mockImplementation(async (request: MediaSignalRequest) => {
        fake.signals.push(request)
        return ok({ status: 'Accepted' })
      }),
      mediaReady: vi.fn().mockImplementation(async () => {
        readyCall += 1
        if (readyCall === 1) return pendingReady.promise
        if (readyCall === 2) return mediaFailure('MEDIA_SIGNAL_FAILED')
        return ok(screen)
      }),
    })
    setActivePinia(createPinia())
    const store = useCollaborationMediaStore()
    store.conversationNId = 'conversation-1'
    store.mediaConversationNId = 'conversation-1'
    store.screen = screen
    store.binding = binding
    store.mediaContextNId = 'context-1'
    store.setRealtime(fake.realtime)

    await store.ensureBinding()
    const staleReady = store.markScreenPlayable()
    await flushPromises()
    expect(fake.realtime.mediaReady).toHaveBeenCalledOnce()

    fake.handlers.onMediaContextChanged?.({
      mediaContextNId: 'context-1',
      contextRevision: 2,
      screenSessionNId: screen.sessionNId,
      voiceCallNId: null,
    })
    await flushPromises()
    expect(fake.signals).toContainEqual(expect.objectContaining({ kind: 'Offer', contextRevision: 2 }))

    pendingReady.resolve(mediaFailure())
    await staleReady
    await flushPromises()
    expect(store.errorMessage).toBeNull()

    await store.markScreenPlayable()
    expect(store.errorMessage).toBe('MEDIA_SIGNAL_FAILED')
    await store.markScreenPlayable()
    expect(store.errorMessage).toBeNull()

    await store.disposeAll()
  })

  it('ends pending and ringing media without a context and allows a new invite', async () => {
    const fake = createFakeRealtime([])
    const endAllMedia = vi.fn()
    Object.assign(fake.realtime, {
      endAllMedia,
      inviteScreenShare: vi.fn().mockResolvedValue(ok(makeScreen('Pending', 'screen-reinvited'))),
      inviteVoiceCall: vi.fn().mockResolvedValue(ok(makeVoice('Ringing', 'voice-reinvited'))),
    })
    setActivePinia(createPinia())
    const store = useCollaborationMediaStore()
    store.conversationNId = 'conversation-1'
    store.screen = makeScreen('Pending', 'screen-pending')
    store.voice = makeVoice('Ringing', 'voice-ringing')
    store.setRealtime(fake.realtime)

    await store.endAll()

    expect(endAllMedia).not.toHaveBeenCalled()
    expect(fake.endedScreens).toEqual(['screen-pending'])
    expect(fake.endedVoices).toEqual(['voice-ringing'])
    expect(store.screen).toBeNull()
    expect(store.voice).toBeNull()

    await expect(store.inviteScreenShare('ShareMine')).resolves.toMatchObject({ sessionNId: 'screen-reinvited' })
    await expect(store.inviteVoiceCall()).resolves.toMatchObject({ callNId: 'voice-reinvited' })
    expect(fake.realtime.inviteScreenShare).toHaveBeenCalledOnce()
    expect(fake.realtime.inviteVoiceCall).toHaveBeenCalledOnce()

    await store.disposeAll()
  })

  it('High waits for a remote offer and attaches a pre-offer screen track to slot 3', async () => {
    const originalMediaDevices = navigator.mediaDevices
    const track = new FakeTrack('video')
    Object.defineProperty(navigator, 'mediaDevices', { configurable: true, value: { getDisplayMedia: async () => new FakeMediaStream([track]) } })
    const screen = makeScreen('Accepted', 'screen-high')
    const fake = createFakeRealtime([makeBinding('High', screen, null)])
    setActivePinia(createPinia())
    const store = useCollaborationMediaStore()
    store.screen = screen
    store.binding = makeBinding('High', screen, null)
    store.mediaContextNId = 'context-1'
    store.setRealtime(fake.realtime)

    try {
      await store.startScreenCapture()
      const pc = FakePeerConnection.instances.at(-1)!
      expect(pc.createOfferCount).toBe(0)
      expect(fake.signals.filter((signal) => signal.kind === 'Offer')).toHaveLength(0)

      fake.handlers.onMediaSignal?.({
        mediaContextNId: 'context-1',
        contextRevision: 1,
        negotiationNId: 'remote-screen-offer',
        sequence: 1,
        kind: 'Offer',
        description: { type: 'offer', sdp: 'remote-screen-offer' },
      })
      await flushPromises()

      expect(pc.createAnswerCount).toBe(1)
      expect(pc.getTransceivers()).toHaveLength(3)
      expect(pc.getTransceivers()[2]?.sender.track).toBe(track)
      expect(fake.signals.at(-1)).toMatchObject({ kind: 'Answer', negotiationNId: 'remote-screen-offer' })
    } finally {
      await store.disposeAll()
      Object.defineProperty(navigator, 'mediaDevices', { configurable: true, value: originalMediaDevices })
    }
  })

  it('High waits for a remote offer and attaches a pre-offer microphone track to slot 1', async () => {
    const originalMediaDevices = navigator.mediaDevices
    const track = new FakeTrack('audio')
    Object.defineProperty(navigator, 'mediaDevices', { configurable: true, value: { getUserMedia: async () => new FakeMediaStream([track]) } })
    const voice = makeVoice('Accepted', 'voice-high')
    const binding = makeBinding('High', null, voice)
    const fake = createFakeRealtime([binding])
    setActivePinia(createPinia())
    const store = useCollaborationMediaStore()
    store.voice = voice
    store.binding = binding
    store.mediaContextNId = 'context-1'
    store.setRealtime(fake.realtime)

    try {
      await store.startVoiceCapture()
      const pc = FakePeerConnection.instances.at(-1)!
      expect(pc.createOfferCount).toBe(0)
      expect(fake.signals.filter((signal) => signal.kind === 'Offer')).toHaveLength(0)

      fake.handlers.onMediaSignal?.({
        mediaContextNId: 'context-1',
        contextRevision: 1,
        negotiationNId: 'remote-voice-offer',
        sequence: 1,
        kind: 'Offer',
        description: { type: 'offer', sdp: 'remote-voice-offer' },
      })
      await flushPromises()

      expect(pc.createAnswerCount).toBe(1)
      expect(pc.getTransceivers()).toHaveLength(3)
      expect(pc.getTransceivers()[0]?.sender.track).toBe(track)
      expect(fake.signals.at(-1)).toMatchObject({ kind: 'Answer', negotiationNId: 'remote-voice-offer' })
    } finally {
      await store.disposeAll()
      Object.defineProperty(navigator, 'mediaDevices', { configurable: true, value: originalMediaDevices })
    }
  })

  it('ends only a newly added voice capability when its zero-candidate negotiation times out', async () => {
    vi.useFakeTimers()
    const screen = makeScreen('Sharing', 'screen-active')
    const voice = makeVoice('Accepted', 'voice-added')
    const fake = createFakeRealtime([
      makeBinding('Low', screen, null),
      makeBinding('Low', screen, voice),
    ])
    setActivePinia(createPinia())
    const store = useCollaborationMediaStore()
    store.conversationNId = 'conversation-1'
    store.mediaConversationNId = 'conversation-1'
    store.screen = screen
    store.setRealtime(fake.realtime)

    await store.ensureBinding()
    const firstOffer = fake.signals.find((signal) => signal.kind === 'Offer')!
    fake.handlers.onMediaSignal?.({ mediaContextNId: 'context-1', contextRevision: 1, negotiationNId: firstOffer.negotiationNId, sequence: 2, kind: 'Answer', description: { type: 'answer', sdp: 'answer-1' } })
    await flushPromises()

    store.voice = voice
    await store.ensureBinding()
    vi.advanceTimersByTime(10_000)
    await flushPromises()

    expect(fake.endedVoices).toEqual(['voice-added'])
    expect(fake.endedScreens).toEqual([])
    expect(store.screen?.state).toBe('Sharing')
  })

  it('ends only the added capability when the ICE queue is full', async () => {
    vi.useFakeTimers()
    const screen = makeScreen('Sharing', 'screen-active')
    const voice = makeVoice('Accepted', 'voice-added')
    const fake = createFakeRealtime([
      makeBinding('Low', screen, null),
      makeBinding('Low', screen, voice),
    ])
    setActivePinia(createPinia())
    const store = useCollaborationMediaStore()
    store.conversationNId = 'conversation-1'
    store.mediaConversationNId = 'conversation-1'
    store.screen = screen
    store.setRealtime(fake.realtime)

    await store.ensureBinding()
    const firstOffer = fake.signals.find((signal) => signal.kind === 'Offer')!
    fake.handlers.onMediaSignal?.({ mediaContextNId: 'context-1', contextRevision: 1, negotiationNId: firstOffer.negotiationNId, sequence: 2, kind: 'Answer', description: { type: 'answer', sdp: 'answer-1' } })
    await flushPromises()
    store.voice = voice
    await store.ensureBinding()
    const addedOffer = fake.signals.filter((signal) => signal.kind === 'Offer').at(-1)!
    FakePeerConnection.instances.at(-1)!.remoteDescription = null

    for (let index = 0; index < 129; index += 1) {
      fake.handlers.onMediaSignal?.({
        mediaContextNId: 'context-1',
        contextRevision: 1,
        negotiationNId: addedOffer.negotiationNId,
        sequence: index + 3,
        kind: 'IceCandidate',
        candidate: { candidate: `candidate:${index}`, sdpMid: '0', sdpMLineIndex: 0, usernameFragment: null },
      })
    }
    await flushPromises()

    expect(fake.endedVoices).toEqual(['voice-added'])
    expect(fake.endedScreens).toEqual([])
    expect(store.screen?.state).toBe('Sharing')
  })

  it('discards a stale no-candidate timer when a newer remote negotiation succeeds', async () => {
    vi.useFakeTimers()
    const screen = makeScreen('Accepted', 'screen-stale')
    const binding = makeBinding('Low', screen, null)
    const fake = createFakeRealtime([binding])
    setActivePinia(createPinia())
    const store = useCollaborationMediaStore()
    store.conversationNId = 'conversation-1'
    store.mediaConversationNId = 'conversation-1'
    store.screen = screen
    store.setRealtime(fake.realtime)

    await store.ensureBinding()
    const firstOffer = fake.signals.find((signal) => signal.kind === 'Offer')!
    fake.handlers.onMediaSignal?.({
      mediaContextNId: 'context-1',
      contextRevision: 1,
      negotiationNId: 'newer-remote-offer',
      sequence: 2,
      kind: 'Offer',
      description: { type: 'offer', sdp: 'newer-offer' },
    })
    await flushPromises()
    vi.advanceTimersByTime(10_000)
    await flushPromises()

    expect(firstOffer.negotiationNId).not.toBe('newer-remote-offer')
    expect(fake.endedScreens).toEqual([])
  })
})
