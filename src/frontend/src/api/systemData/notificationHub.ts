import {
  HubConnectionBuilder,
  LogLevel,
  type HubConnection,
} from '@microsoft/signalr'

import { loadRuntimeConfig } from '@/config/runtimeConfig'

export interface NotificationRealtimeOptions {
  getAccessToken?: () => string | null
  onRefresh: () => void | Promise<void>
}

export interface NotificationRealtime {
  connection: HubConnection
  start(): Promise<void>
  stop(): Promise<void>
}

/**
 * SignalR is an acceleration path; polling remains the availability fallback.
 * Refresh requests are coalesced so a reconnect plus duplicate server event
 * cannot fan out into duplicate API calls.
 */
export function createNotificationRealtime(options: NotificationRealtimeOptions): NotificationRealtime {
  const baseUrl = loadRuntimeConfig().apiBaseUrl.replace(/\/$/, '')
  const connection = new HubConnectionBuilder()
    .withUrl(`${baseUrl}/systemdata/hubs/notifications`, {
      accessTokenFactory: () => options.getAccessToken?.() ?? '',
    })
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Warning)
    .build()

  let refreshQueued = false
  let stopped = false
  const refresh = (): void => {
    if (refreshQueued || stopped) return
    refreshQueued = true
    queueMicrotask(() => {
      refreshQueued = false
      if (!stopped) void Promise.resolve(options.onRefresh()).catch(() => undefined)
    })
  }
  const onVisibilityChange = (): void => {
    if (document.visibilityState === 'visible') refresh()
  }

  connection.on('notification.changed', refresh)
  connection.onreconnected(refresh)

  return {
    connection,
    async start(): Promise<void> {
      stopped = false
      document.addEventListener('visibilitychange', onVisibilityChange)
      try {
        await connection.start()
        refresh()
      } catch {
        // The page's polling fallback remains active when negotiation is unavailable.
      }
    },
    async stop(): Promise<void> {
      stopped = true
      document.removeEventListener('visibilitychange', onVisibilityChange)
      await connection.stop()
    },
  }
}
