import type { DashboardRuntime, Json } from './studio'
import { getStudioApi } from './studio'

export interface DashboardScreenLink {
  id: string
  revisionNId: string
  createdOn: string
  expiresOn: string | null
  revokedOn: string | null
  followPublished: boolean
  key?: string | null
}
export type ScreenResourceKind = 'Dashboard' | 'Playlist'
const path = (nId: string, kind: ScreenResourceKind = 'Dashboard') =>
  `/api/v1/studio/${kind === 'Playlist' ? 'playlists' : 'dashboards'}/${encodeURIComponent(nId)}/screens`
export const listDashboardScreens = (nId: string, kind: ScreenResourceKind = 'Dashboard') =>
  getStudioApi().client.get<DashboardScreenLink[]>(path(nId, kind))
export const createDashboardScreen = (
  nId: string,
  revisionNId: string,
  parameters: Json,
  lifetimeHours: number | null,
  followPublished = true,
  kind: ScreenResourceKind = 'Dashboard',
) =>
  getStudioApi().client.post<
    Pick<DashboardScreenLink, 'id' | 'revisionNId' | 'expiresOn' | 'followPublished'> & {
      key: string
    }
  >(path(nId, kind), {
    revisionNId,
    parameters,
    lifetimeHours,
    followPublished,
  })
export const revokeDashboardScreen = (
  nId: string,
  id: string,
  kind: ScreenResourceKind = 'Dashboard',
) => getStudioApi().client.delete(`${path(nId, kind)}/${encodeURIComponent(id)}`)
export function dashboardScreenUrl(
  id: string,
  key: string,
  kind: ScreenResourceKind = 'Dashboard',
): string {
  return new URL(
    `${import.meta.env.BASE_URL}screen/${kind === 'Playlist' ? 'playlists' : 'dashboards'}/${encodeURIComponent(id)}#key=${encodeURIComponent(key)}`,
    window.location.origin,
  ).href
}

/** 公共大屏完全不发送登录Cookie/令牌，也不经过登录重定向拦截器。 */
export async function loadDashboardScreen(
  id: string,
  key: string,
  signal: AbortSignal,
  instanceIds?: string[],
  knownRevisionNId?: string,
  navigation?: Json[],
): Promise<
  DashboardRuntime & { parameters: Json; expiresOn: string | null; followPublished: boolean }
> {
  return readScreen(id, key, signal, instanceIds, knownRevisionNId, navigation)
}

export function loadPlaylistScreen(
  id: string,
  key: string,
  signal: AbortSignal,
  playlistItemId?: string,
  instanceIds?: string[],
  knownRevisionNId?: string,
  navigation?: Json[],
): Promise<Json> {
  return readScreen(id, key, signal, instanceIds, knownRevisionNId, navigation, playlistItemId)
}

async function readScreen<T>(
  id: string,
  key: string,
  signal: AbortSignal,
  instanceIds?: string[],
  knownRevisionNId?: string,
  navigation?: Json[],
  playlistItemId?: string,
): Promise<T> {
  const base = (import.meta.env.VITE_API_BASE_URL || '').replace(/\/$/, '')
  const query = new URLSearchParams({
    ...(instanceIds ? { instanceIds: JSON.stringify(instanceIds) } : {}),
    ...(knownRevisionNId ? { knownRevisionNId } : {}),
    ...(navigation?.length ? { navigation: JSON.stringify(navigation) } : {}),
    ...(playlistItemId ? { playlistItemId } : {}),
  })
  const response = await fetch(
    `${base}/api/v1/studio/screens/${encodeURIComponent(id)}/runtime${query.size ? `?${query}` : ''}`,
    {
      headers: { 'X-Dashboard-Screen-Key': key },
      credentials: 'omit',
      cache: 'no-store',
      redirect: 'error',
      signal,
    },
  )
  const envelope = await response.json().catch(() => null)
  if (!response.ok || envelope?.success !== true || !envelope.data)
    throw Object.assign(new Error(envelope?.message || '大屏暂不可用，请检查链接或联系发布者。'), {
      status: response.status ?? 400,
    })
  return envelope.data
}
