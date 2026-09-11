import type { Pinia } from 'pinia'
import type { Plugin } from 'vue'
import { watch } from 'vue'

import { getCollaborationRealtime } from '@/api/collaborationHub'
import { useAuthStore } from '@/stores/authStore'
import { useCollaborationChatStore } from '@/stores/collaborationChatStore'

/** Keeps the Collaboration SignalR connection alive at application scope. */
export function createCollaborationRuntimePlugin(pinia: Pinia): Plugin {
  return {
    install() {
      const auth = useAuthStore(pinia)
      const chatSession = useCollaborationChatStore(pinia)
      const realtime = getCollaborationRealtime()
      let transition = Promise.resolve()
      let requestedIdentity: string | null = null
      let presenceTimer: ReturnType<typeof setInterval> | undefined
      const stopPresenceHeartbeat = (): void => {
        if (presenceTimer === undefined) return
        clearInterval(presenceTimer)
        presenceTimer = undefined
      }
      const setPresence = (): void => {
        if (realtime.status !== 'Connected') return
        void realtime.setPresence('Online').catch(() => undefined)
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
      const sync = (): void => {
        transition = transition
          .catch(() => undefined)
          .then(async () => {
            if (auth.isAuthenticated && auth.session?.accessToken) {
              const identity = `${auth.user?.userId ?? ''}:${auth.session.accessToken}`
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
        onReconnected: () => {
          const identity =
            auth.user?.userId && auth.session?.accessToken
              ? `${auth.user.userId}:${auth.session.accessToken}`
              : null
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
      // Keep the connection owned by the app shell. Pages only subscribe/join a
      // conversation; leaving the chat route must not stop tenant notifications.
      watch(() => [auth.user?.userId, auth.isAuthenticated, auth.session?.accessToken], sync, {
        immediate: true,
      })
    },
  }
}
