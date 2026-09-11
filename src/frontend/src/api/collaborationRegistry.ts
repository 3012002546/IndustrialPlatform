import type { CollaborationApi } from './collaboration'

let collaborationApi: CollaborationApi | null = null

export function registerCollaborationApi(api: CollaborationApi): void {
  collaborationApi = api
}

export function getCollaborationApi(): CollaborationApi {
  if (collaborationApi === null)
    throw new Error('CollaborationApi 未注册:仅在 authMode=http 下可用')
  return collaborationApi
}

export function getOptionalCollaborationApi(): CollaborationApi | null {
  return collaborationApi
}
