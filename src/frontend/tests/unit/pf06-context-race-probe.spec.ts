import { createPinia, setActivePinia } from 'pinia'
import { expect, it, vi } from 'vitest'
import type { CollaborationRealtime, CollaborationRealtimeHandlers } from '@/api/collaborationHub'
import type { MediaBindingDto, MediaSignalRequest, ScreenDto, VoiceDto } from '@/api/collaborationMedia'
import { useCollaborationMediaStore } from '@/stores/collaborationMediaStore'
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

async function flushPromises(): Promise<void> {
  for (let index = 0; index < 10; index += 1) await Promise.resolve()
}

it('retains newer peer revision delivered before the local Bind result', async () => {
  vi.useFakeTimers(); vi.stubGlobal('RTCPeerConnection', FakePeerConnection); vi.stubGlobal('MediaStream', FakeMediaStream)
  setActivePinia(createPinia())
  const store = useCollaborationMediaStore(), screen = makeScreen('Accepted'), fake = createFakeRealtime([])
  let resolveBind!: (value: ReturnType<typeof ok<MediaBindingDto>>) => void
  fake.realtime.bindMedia = () => new Promise(resolve => { resolveBind = resolve })
  fake.realtime.signalMedia = async request => {
    fake.signals.push(request)
    return Number(request.contextRevision) === 2 ? ok({status:'Accepted'}) : {ok:false,data:null,error:{code:'MEDIA_CONTEXT_STALE',messageKey:'stale'},traceId:'race-probe'}
  }
  store.conversationNId = screen.conversationNId; store.mediaConversationNId = screen.conversationNId; store.screen = screen; store.setRealtime(fake.realtime)
  const pending = store.ensureBinding()
  fake.handlers.onMediaContextChanged?.({mediaContextNId:'context-1',contextRevision:2,screenSessionNId:screen.sessionNId,voiceCallNId:null})
  resolveBind(ok(makeBinding('Low',screen,null)))
  const failure = await pending.then(()=>null,error=>String(error))
  console.log(JSON.stringify({scenario:'context notice before Bind completion',failure,offerRevisions:fake.signals.map(x=>x.contextRevision)}))
  await store.disposeAll();vi.unstubAllGlobals();vi.useRealTimers()
  expect(failure).toBeNull()
  expect(fake.signals.some(x=>x.kind==='Offer' && Number(x.contextRevision)===2)).toBe(true)
})

it('retains peer Offer delivered before the local Bind result', async () => {
  vi.useFakeTimers();vi.stubGlobal('RTCPeerConnection',FakePeerConnection);vi.stubGlobal('MediaStream',FakeMediaStream)
  setActivePinia(createPinia())
  const store=useCollaborationMediaStore(),screen=makeScreen('Accepted'),fake=createFakeRealtime([])
  let resolveBind!: (value:ReturnType<typeof ok<MediaBindingDto>>)=>void
  fake.realtime.bindMedia=()=>new Promise(resolve=>{resolveBind=resolve})
  store.conversationNId=screen.conversationNId;store.mediaConversationNId=screen.conversationNId;store.screen=screen;store.setRealtime(fake.realtime)
  const pending=store.ensureBinding()
  fake.handlers.onMediaSignal?.({mediaContextNId:'context-1',contextRevision:1,negotiationNId:'early-offer',sequence:1,kind:'Offer',description:{type:'offer',sdp:'early-offer'}})
  await flushPromises()
  resolveBind(ok(makeBinding('High',screen,null)))
  await pending;await flushPromises()
  const received=store.peerConnection?.remoteDescription?.type??null
  console.log(JSON.stringify({scenario:'Offer before Bind completion',received,answerCount:fake.signals.filter(x=>x.kind==='Answer').length}))
  await store.disposeAll();vi.unstubAllGlobals();vi.useRealTimers()
  expect(received).toBe('offer')
})

it('queues a capability rebind behind an in-flight Bind', async () => {
  vi.stubGlobal('RTCPeerConnection', FakePeerConnection); vi.stubGlobal('MediaStream', FakeMediaStream)
  setActivePinia(createPinia())
  const store = useCollaborationMediaStore(), voice = makeVoice('Accepted'), screen = makeScreen('Accepted'), fake = createFakeRealtime([])
  const resolvers: Array<(value: ReturnType<typeof ok<MediaBindingDto>>) => void> = []
  let bindCalls = 0
  fake.realtime.bindMedia = () => {
    bindCalls += 1
    return new Promise(resolve => { resolvers.push(resolve) })
  }
  store.conversationNId = voice.conversationNId; store.mediaConversationNId = voice.conversationNId; store.voice = voice; store.setRealtime(fake.realtime)
  const first = store.ensureBinding()
  store.screen = screen
  const second = store.ensureBinding()
  expect(bindCalls).toBe(1)
  resolvers[0]!(ok(makeBinding('High', null, voice)))
  await flushPromises()
  expect(bindCalls).toBe(2)
  resolvers[1]!(ok(makeBinding('High', screen, voice)))
  const result = await second
  await first
  expect(result?.screen?.sessionNId).toBe(screen.sessionNId)
  await store.disposeAll(); vi.unstubAllGlobals()
})

it('does not let an invalidated Bind promise block the next lifecycle', async () => {
  vi.stubGlobal('RTCPeerConnection', FakePeerConnection); vi.stubGlobal('MediaStream', FakeMediaStream)
  setActivePinia(createPinia())
  const store = useCollaborationMediaStore(), oldVoice = makeVoice('Accepted', 'voice-old'), newVoice = makeVoice('Accepted', 'voice-new'), fake = createFakeRealtime([])
  const resolvers: Array<(value: ReturnType<typeof ok<MediaBindingDto>>) => void> = []
  let bindCalls = 0
  fake.realtime.bindMedia = () => {
    bindCalls += 1
    return new Promise(resolve => { resolvers.push(resolve) })
  }
  store.conversationNId = oldVoice.conversationNId; store.mediaConversationNId = oldVoice.conversationNId; store.voice = oldVoice; store.setRealtime(fake.realtime)
  const first = store.ensureBinding()
  await store.disposeAll()
  store.conversationNId = newVoice.conversationNId; store.mediaConversationNId = newVoice.conversationNId; store.voice = newVoice
  const second = store.ensureBinding()
  expect(bindCalls).toBe(2)
  resolvers[1]!(ok(makeBinding('High', null, newVoice)))
  const result = await second
  resolvers[0]!(ok(makeBinding('High', null, oldVoice)))
  await first
  expect(result?.voice?.callNId).toBe(newVoice.callNId)
  expect(store.binding?.voice?.callNId).toBe(newVoice.callNId)
  await store.disposeAll(); vi.unstubAllGlobals()
})
