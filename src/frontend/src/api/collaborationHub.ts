import { HubConnectionBuilder, LogLevel, type HubConnection } from '@microsoft/signalr'

import type { Message, Presence, SendMessageRequest } from './collaboration'
import { loadRuntimeConfig } from '@/config/runtimeConfig'

export interface CollaborationRealtimeOptions {
  getAccessToken?: () => string | null
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
  onReconnected: () => void | Promise<void>
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
}

export interface CollaborationRealtime {
  connection: HubConnection
  start(): Promise<void>
  stop(): Promise<void>
  joinConversation(conversationNId: string): Promise<void>
  leaveConversation(conversationNId: string): Promise<void>
  setPresence(state: string): Promise<Presence>
  sendMessage(conversationNId: string, request: SendMessageRequest): Promise<Message>
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
      accessTokenFactory: () => options.getAccessToken?.() ?? '',
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
  connection.onreconnected(() => {
    lastStartError = null
    setStatus('Connected')
    if (!stopped) void Promise.resolve(options.onReconnected()).catch(() => undefined)
  })
  connection.onclose(() => {
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

  constructor(getAccessToken: () => string | null) {
    const emit = (key: keyof CollaborationRealtimeHandlers, value?: unknown): void => {
      for (const listener of this.listeners) {
        const handler = listener[key] as ((value: unknown) => void) | undefined
        handler?.(value)
      }
    }
    this.realtime = createCollaborationRealtime({
      getAccessToken,
      onMessage: (message) => emit('onMessage', message),
      onMessageRetracted: (message) => emit('onMessageRetracted', message),
      onAck: (ack) => emit('onAck', ack),
      onPersonalMessageHidden: (event) => emit('onPersonalMessageHidden', event),
      onPresence: (presence) => emit('onPresence', presence),
      onTyping: (event) => emit('onTyping', event),
      onReadCursor: (event) => emit('onReadCursor', event),
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
}

let applicationRealtime: CollaborationRealtimeManager | null = null

export function registerCollaborationRealtime(manager: CollaborationRealtimeManager): void {
  applicationRealtime = manager
}

export function getCollaborationRealtime(): CollaborationRealtimeManager {
  return applicationRealtime ?? (applicationRealtime = new CollaborationRealtimeManager(() => null))
}
