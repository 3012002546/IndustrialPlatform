export type MediaLong = number | string

export interface MediaError {
  code: string
  messageKey: string
}

export interface MediaResult<T> {
  ok: boolean
  data: T | null
  error: MediaError | null
  traceId: string
}

export interface ScreenDto {
  sessionNId: string
  conversationNId: string
  direction: 'ShareMine' | 'RequestPeer' | string
  initiatorUserNId: string
  inviteeUserNId: string
  sharerUserNId: string
  viewerUserNId: string
  state: string
  answer: string | null
  version: MediaLong
  deadlineOn: string
  startedOn: string | null
  endedOn: string | null
  endReason: string | null
  initiatorStoppedReported: boolean
  inviteeStoppedReported: boolean
}

export interface VoiceDto {
  callNId: string
  conversationNId: string
  callerUserNId: string
  calleeUserNId: string
  state: string
  answer: string | null
  version: MediaLong
  deadlineOn: string
  startedOn: string | null
  endedOn: string | null
  endReason: string | null
  callerStoppedReported: boolean
  calleeStoppedReported: boolean
}

export interface ConversationMediaDto {
  screen: ScreenDto | null
  voice: VoiceDto | null
  myEndpointSelected: boolean
  mediaContextNId: string | null
}

export interface ActiveMediaPageDto {
  items: Array<ConversationMediaDto & { conversationNId: string }>
  nextCursor: string | null
}

export interface MediaDescription {
  type: 'offer' | 'answer' | string
  sdp: string
}

export interface MediaCandidate {
  candidate: string
  sdpMid: string | null
  sdpMLineIndex: number | null
  usernameFragment: string | null
}

export interface MediaSignalRequest {
  mediaContextNId: string
  contextRevision: MediaLong
  negotiationNId: string
  sequence: MediaLong
  kind: 'Offer' | 'Answer' | 'IceCandidate' | string
  description?: MediaDescription
  candidate?: MediaCandidate
}

export interface MediaBindingDto {
  mediaContextNId: string
  contextRevision: MediaLong
  endpointRole: 'Low' | 'High' | string
  polite: boolean
  screen: ScreenDto | null
  voice: VoiceDto | null
  icePolicy: 'All' | 'RelayOnly' | string
  iceServers: Array<{ urls: string[]; username?: string; credential?: string }>
  confirm: {
    screen: { sessionNId: string; state: string; version: MediaLong; validForMs: number; allowed: boolean; errorCode: string | null } | null
    voice: { sessionNId: string; state: string; version: MediaLong; validForMs: number; allowed: boolean; errorCode: string | null } | null
  }
}

export interface KeepAliveMediaDto {
  contextRevision: MediaLong
  screen: { sessionNId: string; state: string; version: MediaLong; validForMs: number; allowed: boolean; errorCode: string | null } | null
  voice: { sessionNId: string; state: string; version: MediaLong; validForMs: number; allowed: boolean; errorCode: string | null } | null
}

export interface VoiceMutedDto {
  callNId: string
  userNId: string
  muted: boolean
  sequence: MediaLong
}

export interface EndAllMediaDto {
  screen: ScreenDto | null
  voice: VoiceDto | null
}

export interface MediaContextChangedDto {
  mediaContextNId: string
  contextRevision: MediaLong
  screenSessionNId: string | null
  voiceCallNId: string | null
}

export function mediaLong(value: MediaLong | undefined | null): number {
  const parsed = typeof value === 'string' ? Number(value) : value ?? 0
  return Number.isFinite(parsed) ? parsed : 0
}
