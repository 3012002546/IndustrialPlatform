import type { Pinia } from 'pinia'
import type { Plugin } from 'vue'
import { watch } from 'vue'
import type { Router } from 'vue-router'

import { getCollaborationRealtime } from '@/api/collaborationHub'
import { loadRuntimeConfig } from '@/config/runtimeConfig'
import { useAuthStore } from '@/stores/authStore'
import { useCollaborationChatStore } from '@/stores/collaborationChatStore'
import { isCollaborationRoute } from './collaborationRoute'

export interface CollaborationRuntimeOptions {
  router?: Router
}

function collaborationIdentity(auth: ReturnType<typeof useAuthStore>): string | null {
  if (!auth.isAuthenticated || auth.user === null || auth.session === null) return null
  const session = auth.session
  // Expiry is a lease that changes on every heartbeat, not an identity change.
  // The page credential is intentionally included because it scopes an embedded
  // connection to one browser page.
  return [
    auth.user.tenantId,
    auth.user.userId,
    session.transport ?? 'bearer',
    session.embeddedSessionBinding ?? '',
    session.embeddedSessionToken ?? '',
  ].join(':')
}

/** Keeps the Collaboration SignalR connection alive for the owning page. */
export function createCollaborationRuntimePlugin(
  pinia: Pinia,
  options: CollaborationRuntimeOptions = {},
): Plugin {
  return {
    install() {
      const auth = useAuthStore(pinia)
      const chatSession = useCollaborationChatStore(pinia)
      const realtime = getCollaborationRealtime()
      let transition = Promise.resolve()
      let requestedIdentity: string | null = null
      let presenceTimer: ReturnType<typeof setInterval> | undefined
      let embeddedInvalidated = false
      const embedded = loadRuntimeConfig().authMode === 'embedded'
      const ownsCurrentPage = (): boolean =>
        !embedded || options.router === undefined || isCollaborationRoute(options.router.currentRoute.value.name)
      const stopPresenceHeartbeat = (): void => {
        if (presenceTimer === undefined) return
        clearInterval(presenceTimer)
        presenceTimer = undefined
      }
      const setPresence = (): void => {
        if (realtime.status !== 'Connected') return
        void realtime.setPresence('Online').catch((error: unknown) => {
          if (embedded && isEmbeddedAuthFailure(error)) void invalidateEmbeddedSession()
        })
        if (auth.session?.transport === 'embedded-cookie')
          void auth.keepAliveEmbeddedSession().catch((error: unknown) => {
            if (embedded && isEmbeddedAuthFailure(error)) void invalidateEmbeddedSession()
          })
      }
      const startPresenceHeartbeat = (): void => {
        if (presenceTimer !== undefined) return
        presenceTimer = setInterval(setPresence, 20_000)
      }
      const resetChatSession = async (): Promise<void> => {
        const conversationNId = chatSession.selectedConversation?.conversationNId
        chatSession.resetSession()
        if (conversationNId !== undefined)
          await realtime.leaveConversation(conversationNId).catch(() => undefined)
      }
      const invalidateEmbeddedSession = async (): Promise<void> => {
        if (embeddedInvalidated) return
        embeddedInvalidated = true
        stopPresenceHeartbeat()
        auth.clearLocalSession()
        await resetChatSession()
        requestedIdentity = null
        await realtime.stop().catch(() => undefined)
      }
      const sync = (): void => {
        transition = transition
          .catch(() => undefined)
          .then(async () => {
            if (!ownsCurrentPage()) {
              stopPresenceHeartbeat()
              await resetChatSession()
              requestedIdentity = null
              await realtime.stop().catch(() => undefined)
              return
            }
            if (auth.isAuthenticated && auth.user !== null && auth.session !== null) {
              const identity = collaborationIdentity(auth)!
              if (requestedIdentity !== null && requestedIdentity !== identity) {
                stopPresenceHeartbeat()
                await resetChatSession()
                await realtime.stop().catch(() => undefined)
              }
              requestedIdentity = identity
              await realtime.start()
              if (realtime.status === 'Connected') {
                setPresence()
                startPresenceHeartbeat()
              } else stopPresenceHeartbeat()
            } else {
              stopPresenceHeartbeat()
              await resetChatSession()
              requestedIdentity = null
              await realtime.stop().catch(() => undefined)
            }
          })
          .catch(() => undefined)
      }
      realtime.subscribe?.({
        onAuthFailure: () => {
          if (embedded) return invalidateEmbeddedSession()
        },
        onReconnected: () => {
          const identity = ownsCurrentPage() ? collaborationIdentity(auth) : null
          if (
            identity !== null &&
            identity === requestedIdentity &&
            realtime.status === 'Connected'
          ) {
            setPresence()
            startPresenceHeartbeat()
          }
        },
      })
      watch(() => [
        auth.user?.tenantId,
        auth.user?.userId,
        auth.isAuthenticated,
        auth.session?.transport,
        auth.session?.embeddedSessionBinding,
        auth.session?.embeddedSessionToken,
        options.router?.currentRoute.value.name,
      ], sync, {
        immediate: true,
      })
    },
  }
}

function isEmbeddedAuthFailure(error: unknown): boolean {
  if (!(error instanceof Error)) return false
  const details = (error as { details?: { status?: number } }).details
  return details?.status === 401
    || details?.status === 403
    || /unauthorized|forbidden|401|403/i.test(error.message)
}
