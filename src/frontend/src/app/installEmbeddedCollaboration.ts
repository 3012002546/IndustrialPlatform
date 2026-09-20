import type { Pinia } from 'pinia'
import type { Router } from 'vue-router'

import { createHttpClient, type HttpAuthRefresh } from '@/api/httpClient'
import { createCollaborationApi } from '@/api/collaboration'
import { registerCollaborationApi } from '@/api/collaborationRegistry'
import { CollaborationRealtimeManager, registerCollaborationRealtime } from '@/api/collaborationHub'
import { createEmbeddedAuthGateway, getCurrentSession, setAuthGateway } from '@/auth'
import { loadRuntimeConfig } from '@/config/runtimeConfig'
import { ROUTE_NAMES } from '@/router/routeNames'
import { useAuthStore } from '@/stores/authStore'

/** 独立宿主的 HTTP、SignalR 和页级会话共用同一凭据。 */
export function installEmbeddedCollaboration(pinia: Pinia, router: Router): void {
  const config = loadRuntimeConfig()
  const getEmbeddedSession = () => {
    const session = getCurrentSession()
    return session?.embeddedSessionToken !== undefined &&
      session.embeddedSessionBinding !== undefined
      ? { token: session.embeddedSessionToken, binding: session.embeddedSessionBinding }
      : null
  }
  const authRefresh: HttpAuthRefresh = {
    isAuthPath: (path) => ['/auth/login', '/auth/refresh', '/auth/logout', '/api/v1/embedded/session']
      .some((marker) => path.includes(marker)),
    refreshSession: () => useAuthStore(pinia).refresh(),
    onSessionExpired: () => {
      useAuthStore(pinia).clearLocalSession()
      void router.replace({ name: ROUTE_NAMES.embeddedSessionRequired })
    },
  }
  const client = createHttpClient({
    baseUrl: config.apiBaseUrl,
    timeoutMs: config.requestTimeoutMs,
    getToken: () => null,
    getEmbeddedSession,
    authRefresh,
    withCredentials: true,
  })
  setAuthGateway(createEmbeddedAuthGateway({
    baseUrl: config.apiBaseUrl,
    requestTimeoutMs: config.requestTimeoutMs,
    standaloneAutoLogin: config.standaloneCollaboration === true,
    ...(config.embeddedAccount === undefined ? {} : { account: config.embeddedAccount }),
  }))
  registerCollaborationApi(createCollaborationApi(client))
  registerCollaborationRealtime(new CollaborationRealtimeManager(() => null, getEmbeddedSession))
}
