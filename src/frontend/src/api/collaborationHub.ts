import { HubConnectionBuilder, LogLevel, type HubConnection } from '@microsoft/signalr'

import type { Message, Presence, SendMessageRequest } from './collaboration'
import type {
  ActiveMediaPageDto,
  ConversationMediaDto,
  EndAllMediaDto,
  KeepAliveMediaDto,
  MediaBindingDto,
  MediaContextChangedDto,
  MediaResult,
  MediaSignalRequest,
  ScreenDto,
  VoiceDto,
  VoiceMutedDto,
} from './collaborationMedia'
import { loadRuntimeConfig } from '@/config/runtimeConfig'

export interface CollaborationRealtimeOptions {
  getAccessToken?: () => string | null
  getEmbeddedSessionCredential?: (() => { token: string; binding: string } | null) | undefined
  onMessage: (message: Message) => void
  onMessageRetracted?: (message: Message) => void
  onAck?: (ack: {
    conversationNId: string
    clientMessageNId: string
    messageNId: string
    sequence: number
  }) => void
  onPersonalMessageHidden?: (event: { conversationNId: string; messageNId: string }) => void
  onPresence: (presence: Presence) => void
  onTyping?: (event: { conversationNId: string; userNId: string; isTyping: boolean }) => void
  onReadCursor?: (event: {
    conversationNId: string
    userNId: string
    sequence: number
    projectionVersion?: number
  }) => void
  onScreenChanged?: (event: ScreenDto) => void
  onVoiceChanged?: (event: VoiceDto) => void
  onMediaSignal?: (event: MediaSignalRequest) => void
  onMediaContextChanged?: (event: MediaContextChangedDto) => void
  onVoiceMuted?: (event: VoiceMutedDto) => void
  onReconnected: () => void | Promise<void>
  onAuthFailure?: () => void | Promise<void>
  initialRetryDelaysMs?: readonly number[]
}

export type CollaborationRealtimeStatus = 'Disconnected' | 'Connecting' | 'Connected' | 'Retrying'

export interface CollaborationRealtimeHandlers {
  onMessage?: (message: Message) => void
  onMessageRetracted?: (message: Message) => void
  onAck?: (ack: {
    conversationNId: string
    clientMessageNId: string
    messageNId: string
    sequence: number
  }) => void
  onPersonalMessageHidden?: (event: { conversationNId: string; messageNId: string }) => void
  onPresence?: (presence: Presence) => void
  onTyping?: (event: { conversationNId: string; userNId: string; isTyping: boolean }) => void
  onReadCursor?: (event: {
    conversationNId: string
    userNId: string
    sequence: number
    projectionVersion?: number
  }) => void
  onReconnected?: () => void | Promise<void>
  onAuthFailure?: () => void | Promise<void>
  onScreenChanged?: (event: ScreenDto) => void
  onVoiceChanged?: (event: VoiceDto) => void
  onMediaSignal?: (event: MediaSignalRequest) => void
  onMediaContextChanged?: (event: MediaContextChangedDto) => void
  onVoiceMuted?: (event: VoiceMutedDto) => void
}

export interface CollaborationRealtime {
  connection: HubConnection
  start(): Promise<void>
  stop(): Promise<void>
  joinConversation(conversationNId: string): Promise<void>
  leaveConversation(conversationNId: string): Promise<void>
  setPresence(state: string): Promise<Presence>
  sendMessage(conversationNId: string, request: SendMessageRequest): Promise<Message>
  inviteScreenShare(request: { conversationNId: string; direction: string; requestNId: string }): Promise<MediaResult<ScreenDto>>
  respondScreenShare(request: { sessionNId: string; answer: string; expectedVersion: number | string }): Promise<MediaResult<ScreenDto>>
  endScreenShare(request: { sessionNId: string; reason: string }): Promise<MediaResult<ScreenDto>>
  inviteVoiceCall(request: { conversationNId: string; requestNId: string }): Promise<MediaResult<VoiceDto>>
  respondVoiceCall(request: { callNId: string; answer: string; expectedVersion: number | string }): Promise<MediaResult<VoiceDto>>
  endVoiceCall(request: { callNId: string; reason: string }): Promise<MediaResult<VoiceDto>>
  getConversationMedia(request: { conversationNId: string }): Promise<MediaResult<ConversationMediaDto>>
  getMyActiveMedia(request?: { cursor?: string | null }): Promise<MediaResult<ActiveMediaPageDto>>
  bindMedia(request: { conversationNId: string; screenSessionNId?: string | null; voiceCallNId?: string | null }): Promise<MediaResult<MediaBindingDto>>
  signalMedia(request: MediaSignalRequest): Promise<MediaResult<{ status: string }>>
  mediaReady(request: { mediaContextNId: string; kind: string; sessionNId: string }): Promise<MediaResult<ScreenDto | VoiceDto>>
  keepAliveMedia(request: { mediaContextNId: string; screenSessionNId?: string | null; voiceCallNId?: string | null }): Promise<MediaResult<KeepAliveMediaDto>>
  setVoiceMuted(request: { callNId: string; muted: boolean; sequence: number | string }): Promise<MediaResult<VoiceMutedDto>>
  reportMediaStopped(request: { conversationNId: string; kind: string; sessionNId: string; senderDetached: boolean; captureTracksEnded: boolean; playbackDetached: boolean }): Promise<MediaResult<ScreenDto | VoiceDto>>
  endAllMedia(request: { mediaContextNId: string; screenSessionNId?: string | null; voiceCallNId?: string | null }): Promise<MediaResult<{ screen: ScreenDto | null; voice: VoiceDto | null }>>
  subscribe?(handlers: CollaborationRealtimeHandlers): () => void
  readonly status: CollaborationRealtimeStatus
  readonly lastStartError: Error | null
}

/** SignalR is the live transport; REST remains the initial load and reconnect backfill. */
export function createCollaborationRealtime(
  options: CollaborationRealtimeOptions,
): CollaborationRealtime {
  const baseUrl = loadRuntimeConfig().apiBaseUrl.replace(/\/$/, '')
  const connection = new HubConnectionBuilder()
    .withUrl(`${baseUrl}/collaboration/hubs/collaboration-v1`, {
      accessTokenFactory: () => {
        const embedded = options.getEmbeddedSessionCredential?.()
        return embedded === null || embedded === undefined
          ? options.getAccessToken?.() ?? ''
          : `embedded-session:${embedded.token}:${embedded.binding}`
      },
      // Embedded mode authenticates with the HttpOnly host session; bearer
      // token mode also remains compatible with credentialed SSO gateways.
      withCredentials: true,
    })
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Warning)
    .build()

  let stopped = false
  let startPromise: Promise<void> | null = null
  let retryPromise: Promise<void> | null = null
  let retryAbortController: AbortController | null = null
  let status: CollaborationRealtimeStatus = 'Disconnected'
  let lastStartError: Error | null = null
  const retryDelays = options.initialRetryDelaysMs?.length
    ? options.initialRetryDelaysMs
    : [0, 2000, 5000, 10000, 30000]

  const setStatus = (next: CollaborationRealtimeStatus): void => {
    status = next
  }

  const isAuthorizationFailure = (error: unknown): boolean =>
    error instanceof Error && /unauthorized|forbidden|401|403/i.test(error.message)

  const notifyAuthorizationFailure = (error: unknown): void => {
    if (isAuthorizationFailure(error))
      void Promise.resolve(options.onAuthFailure?.()).catch(() => undefined)
  }

  const wait = (delayMs: number, signal: AbortSignal): Promise<void> => {
    if (delayMs <= 0) return Promise.resolve()
    return new Promise((resolve) => {
      const timer = window.setTimeout(resolve, delayMs)
      signal.addEventListener(
        'abort',
        () => {
          window.clearTimeout(timer)
          resolve()
        },
        { once: true },
      )
    })
  }

  const ensureInitialRetry = (): Promise<void> => {
    if (retryPromise) return retryPromise
    const controller = new AbortController()
    retryAbortController = controller
    retryPromise = (async () => {
      let attempt = 0
      while (!stopped && connection.state === 'Disconnected') {
        setStatus('Retrying')
        await wait(
          retryDelays[Math.min(attempt, retryDelays.length - 1)] ?? 30000,
          controller.signal,
        )
        if (stopped || controller.signal.aborted) return
        setStatus('Connecting')
        try {
          await connection.start()
          lastStartError = null
          setStatus('Connected')
          if (!stopped) void Promise.resolve(options.onReconnected()).catch(() => undefined)
          return
        } catch (error) {
          lastStartError = error instanceof Error ? error : new Error(String(error))
          notifyAuthorizationFailure(error)
          setStatus('Retrying')
          attempt += 1
        }
      }
    })().finally(() => {
      retryPromise = null
      if (retryAbortController === controller) retryAbortController = null
    })
    return retryPromise
  }

  connection.on('message.accepted', options.onMessage)
  connection.on('message.retracted', (message) => options.onMessageRetracted?.(message))
  connection.on('message.ack', (ack) => options.onAck?.(ack))
  connection.on('message.personal-hidden', (event) =>
    options.onPersonalMessageHidden?.({
      conversationNId: String(event.conversationNId ?? event.ConversationNId ?? ''),
      messageNId: String(event.messageNId ?? event.MessageNId ?? ''),
    }),
  )
  connection.on('presence.changed', options.onPresence)
  connection.on('typing', (event) => options.onTyping?.(event))
  connection.on('read.cursor.advanced', (event) =>
    options.onReadCursor?.({
      conversationNId: String(event.conversationNId ?? event.ConversationNId ?? ''),
      userNId: String(event.userNId ?? event.UserNId ?? ''),
      sequence: Number(event.sequence ?? event.Sequence ?? 0),
      ...(event.projectionVersion === undefined && event.ProjectionVersion === undefined
        ? {}
        : {
            projectionVersion: Number(event.projectionVersion ?? event.ProjectionVersion ?? 0),
          }),
      }),
  )
  connection.on('screen-share.changed', (event) =>
    options.onScreenChanged?.((event?.payload ?? event?.Payload ?? event) as ScreenDto),
  )
  connection.on('voice-call.changed', (event) =>
    options.onVoiceChanged?.((event?.payload ?? event?.Payload ?? event) as VoiceDto),
  )
  connection.on('media.signal', (event) => {
    const payload = event?.payload ?? event?.Payload ?? event
    options.onMediaSignal?.(payload as MediaSignalRequest)
  })
  connection.on('media.context.changed', (event) =>
    options.onMediaContextChanged?.((event?.payload ?? event?.Payload ?? event) as MediaContextChangedDto),
  )
  connection.on('voice-call.muted', (event) =>
    options.onVoiceMuted?.((event?.payload ?? event?.Payload ?? event) as VoiceMutedDto),
  )
  connection.onreconnected(() => {
    lastStartError = null
    setStatus('Connected')
    if (!stopped) void Promise.resolve(options.onReconnected()).catch(() => undefined)
  })
  connection.onclose((error) => {
    notifyAuthorizationFailure(error)
    if (!stopped) void ensureInitialRetry().catch(() => undefined)
    else setStatus('Disconnected')
  })

  return {
    connection,
    async start(): Promise<void> {
      stopped = false
      if (connection.state === 'Connected') {
        setStatus('Connected')
        return
      }
      if (startPromise !== null) return startPromise
      startPromise = (async () => {
        try {
          if (connection.state === 'Disconnected') {
            setStatus('Connecting')
            await connection.start()
            lastStartError = null
            setStatus('Connected')
          }
        } catch (error) {
          lastStartError = error instanceof Error ? error : new Error(String(error))
          notifyAuthorizationFailure(error)
          setStatus('Retrying')
          void ensureInitialRetry().catch(() => undefined)
        } finally {
          startPromise = null
        }
      })()
      return startPromise
    },
    async stop(): Promise<void> {
      stopped = true
      retryAbortController?.abort()
      if (retryPromise) await retryPromise
      await connection.stop()
      setStatus('Disconnected')
    },
    async joinConversation(conversationNId: string): Promise<void> {
      if (connection.state === 'Connected')
        await connection.invoke('JoinConversation', conversationNId)
    },
    async leaveConversation(conversationNId: string): Promise<void> {
      if (connection.state === 'Connected')
        await connection.invoke('LeaveConversation', conversationNId)
    },
    async setPresence(state: string): Promise<Presence> {
      return connection.invoke<Presence>('SetPresence', state)
    },
    async sendMessage(conversationNId: string, request: SendMessageRequest): Promise<Message> {
      return connection.invoke<Message>('SendMessage', conversationNId, request)
    },
    inviteScreenShare(request) {
      return connection.invoke<MediaResult<ScreenDto>>('InviteScreenShare', request)
    },
    respondScreenShare(request) {
      return connection.invoke<MediaResult<ScreenDto>>('RespondScreenShare', request)
    },
    endScreenShare(request) {
      return connection.invoke<MediaResult<ScreenDto>>('EndScreenShare', request)
    },
    inviteVoiceCall(request) {
      return connection.invoke<MediaResult<VoiceDto>>('InviteVoiceCall', request)
    },
    respondVoiceCall(request) {
      return connection.invoke<MediaResult<VoiceDto>>('RespondVoiceCall', request)
    },
    endVoiceCall(request) {
      return connection.invoke<MediaResult<VoiceDto>>('EndVoiceCall', request)
    },
    getConversationMedia(request) {
      return connection.invoke<MediaResult<ConversationMediaDto>>('GetConversationMedia', request)
    },
    getMyActiveMedia(request = {}) {
      return connection.invoke<MediaResult<ActiveMediaPageDto>>('GetMyActiveMedia', request)
    },
    bindMedia(request) {
      return connection.invoke<MediaResult<MediaBindingDto>>('BindMedia', request)
    },
    signalMedia(request) {
      return connection.invoke<MediaResult<{ status: string }>>('SignalMedia', request)
    },
    mediaReady(request) {
      return connection.invoke<MediaResult<ScreenDto | VoiceDto>>('MediaReady', request)
    },
    keepAliveMedia(request) {
      return connection.invoke<MediaResult<KeepAliveMediaDto>>('KeepAliveMedia', request)
    },
    setVoiceMuted(request) {
      return connection.invoke<MediaResult<VoiceMutedDto>>('SetVoiceMuted', request)
    },
    reportMediaStopped(request) {
      return connection.invoke<MediaResult<ScreenDto | VoiceDto>>('ReportMediaStopped', request)
    },
    endAllMedia(request) {
      return connection.invoke<MediaResult<EndAllMediaDto>>('EndAllMedia', request)
    },
    get status(): CollaborationRealtimeStatus {
      return status
    },
    get lastStartError(): Error | null {
      return lastStartError
    },
  }
}

/** One application-level connection shared by chat pages and the PC shell. */
export class CollaborationRealtimeManager implements CollaborationRealtime {
  private readonly listeners = new Set<CollaborationRealtimeHandlers>()
  private readonly realtime: CollaborationRealtime

  constructor(
    getAccessToken: () => string | null,
    getEmbeddedSessionCredential?: () => { token: string; binding: string } | null,
  ) {
    const emit = (key: keyof CollaborationRealtimeHandlers, value?: unknown): void => {
      for (const listener of this.listeners) {
        const handler = listener[key] as ((value: unknown) => void) | undefined
        handler?.(value)
      }
    }
    this.realtime = createCollaborationRealtime({
      getAccessToken,
      getEmbeddedSessionCredential,
      onMessage: (message) => emit('onMessage', message),
      onMessageRetracted: (message) => emit('onMessageRetracted', message),
      onAck: (ack) => emit('onAck', ack),
      onPersonalMessageHidden: (event) => emit('onPersonalMessageHidden', event),
      onPresence: (presence) => emit('onPresence', presence),
      onTyping: (event) => emit('onTyping', event),
      onReadCursor: (event) => emit('onReadCursor', event),
      onScreenChanged: (event) => emit('onScreenChanged', event),
      onVoiceChanged: (event) => emit('onVoiceChanged', event),
      onMediaSignal: (event) => emit('onMediaSignal', event),
      onMediaContextChanged: (event) => emit('onMediaContextChanged', event),
      onVoiceMuted: (event) => emit('onVoiceMuted', event),
      onAuthFailure: () => {
        const callbacks = [...this.listeners].map((listener) => listener.onAuthFailure?.())
        return Promise.all(callbacks).then(() => undefined)
      },
      onReconnected: () => {
        const callbacks = [...this.listeners].map((listener) => listener.onReconnected?.())
        return Promise.all(callbacks).then(() => undefined)
      },
    })
  }

  get connection(): HubConnection {
    return this.realtime.connection
  }

  get status(): CollaborationRealtimeStatus {
    return this.realtime.status
  }

  get lastStartError(): Error | null {
    return this.realtime.lastStartError
  }

  subscribe(handlers: CollaborationRealtimeHandlers): () => void {
    this.listeners.add(handlers)
    return () => this.listeners.delete(handlers)
  }

  start(): Promise<void> {
    return this.realtime.start()
  }
  stop(): Promise<void> {
    return this.realtime.stop()
  }
  joinConversation(conversationNId: string): Promise<void> {
    return this.realtime.joinConversation(conversationNId)
  }
  leaveConversation(conversationNId: string): Promise<void> {
    return this.realtime.leaveConversation(conversationNId)
  }
  setPresence(state: string): Promise<Presence> {
    return this.realtime.setPresence(state)
  }
  sendMessage(conversationNId: string, request: SendMessageRequest): Promise<Message> {
    return this.realtime.sendMessage(conversationNId, request)
  }
  inviteScreenShare(request: Parameters<CollaborationRealtime['inviteScreenShare']>[0]) { return this.realtime.inviteScreenShare(request) }
  respondScreenShare(request: Parameters<CollaborationRealtime['respondScreenShare']>[0]) { return this.realtime.respondScreenShare(request) }
  endScreenShare(request: Parameters<CollaborationRealtime['endScreenShare']>[0]) { return this.realtime.endScreenShare(request) }
  inviteVoiceCall(request: Parameters<CollaborationRealtime['inviteVoiceCall']>[0]) { return this.realtime.inviteVoiceCall(request) }
  respondVoiceCall(request: Parameters<CollaborationRealtime['respondVoiceCall']>[0]) { return this.realtime.respondVoiceCall(request) }
  endVoiceCall(request: Parameters<CollaborationRealtime['endVoiceCall']>[0]) { return this.realtime.endVoiceCall(request) }
  getConversationMedia(request: Parameters<CollaborationRealtime['getConversationMedia']>[0]) { return this.realtime.getConversationMedia(request) }
  getMyActiveMedia(request?: Parameters<CollaborationRealtime['getMyActiveMedia']>[0]) { return this.realtime.getMyActiveMedia(request) }
  bindMedia(request: Parameters<CollaborationRealtime['bindMedia']>[0]) { return this.realtime.bindMedia(request) }
  signalMedia(request: Parameters<CollaborationRealtime['signalMedia']>[0]) { return this.realtime.signalMedia(request) }
  mediaReady(request: Parameters<CollaborationRealtime['mediaReady']>[0]) { return this.realtime.mediaReady(request) }
  keepAliveMedia(request: Parameters<CollaborationRealtime['keepAliveMedia']>[0]) { return this.realtime.keepAliveMedia(request) }
  setVoiceMuted(request: Parameters<CollaborationRealtime['setVoiceMuted']>[0]) { return this.realtime.setVoiceMuted(request) }
  reportMediaStopped(request: Parameters<CollaborationRealtime['reportMediaStopped']>[0]) { return this.realtime.reportMediaStopped(request) }
  endAllMedia(request: Parameters<CollaborationRealtime['endAllMedia']>[0]) { return this.realtime.endAllMedia(request) }
}

let applicationRealtime: CollaborationRealtimeManager | null = null

export function registerCollaborationRealtime(manager: CollaborationRealtimeManager): void {
  applicationRealtime = manager
}

export function getCollaborationRealtime(): CollaborationRealtimeManager {
  return applicationRealtime ?? (applicationRealtime = new CollaborationRealtimeManager(() => null))
}
