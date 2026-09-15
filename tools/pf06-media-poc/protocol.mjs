export const SLOT_ORDER = Object.freeze(['audio', 'screenLow', 'screenHigh'])

export const SLOT_KIND = Object.freeze({
  audio: 'audio',
  screenLow: 'video',
  screenHigh: 'video',
})

export function createLayout(role) {
  if (role !== 'low' && role !== 'high') throw new Error('role must be low or high')
  return {
    role,
    mapped: role === 'low',
    transceivers:
      role === 'low'
        ? SLOT_ORDER.map((slot, index) => ({ slot, kind: SLOT_KIND[slot], index, mid: null }))
        : [],
  }
}

export function mapHighLayout(layout, remoteTransceivers) {
  if (layout.role !== 'high') throw new Error('only high maps a remote offer')
  if (layout.mapped) throw new Error('high layout is already mapped')
  if (remoteTransceivers.length !== SLOT_ORDER.length) throw new Error('expected exactly three transceivers')

  const transceivers = remoteTransceivers.map((remote, index) => {
    const slot = SLOT_ORDER[index]
    if (remote.kind !== SLOT_KIND[slot]) throw new Error(`slot ${slot} kind mismatch`)
    return { slot, kind: remote.kind, index, mid: remote.mid ?? null }
  })

  return { ...layout, mapped: true, transceivers }
}

export function ownerSlot(role) {
  return role === 'low' ? 'screenLow' : 'screenHigh'
}

export function serializeDescription(description) {
  return { type: description.type, sdp: description.sdp }
}

export function serializeCandidate(candidate) {
  return {
    candidate: candidate.candidate,
    sdpMid: candidate.sdpMid ?? null,
    sdpMLineIndex: candidate.sdpMLineIndex ?? null,
    usernameFragment: candidate.usernameFragment ?? null,
  }
}

export class CapabilityState {
  constructor() {
    this.voice = { active: false, generation: 0 }
    this.screen = { active: false, generation: 0, owner: null }
  }

  startVoice() {
    this.voice = { active: true, generation: this.voice.generation + 1 }
    return this.voice.generation
  }

  stopVoice() {
    this.voice = { active: false, generation: this.voice.generation + 1 }
    return this.voice.generation
  }

  startScreen(role) {
    if (this.screen.active) throw new Error('screen capability already active')
    this.screen = { active: true, generation: this.screen.generation + 1, owner: role }
    return this.screen.generation
  }

  stopScreen() {
    this.screen = { active: false, generation: this.screen.generation + 1, owner: null }
    return this.screen.generation
  }

  canApply(kind, generation) {
    return this[kind].generation === generation
  }
}

export class SignalLedger {
  constructor({ candidateQueueMax = 128, negotiationMax = 2 } = {}) {
    this.candidateQueueMax = candidateQueueMax
    this.negotiationMax = negotiationMax
    this.revision = 0
    this.lastSequence = 0
    this.negotiations = new Set()
    this.candidates = []
  }

  acceptRevision(revision) {
    if (revision < this.revision) return false
    this.revision = revision
    return true
  }

  acceptSequence(sequence) {
    if (!Number.isInteger(sequence) || sequence <= this.lastSequence) return false
    this.lastSequence = sequence
    return true
  }

  openNegotiation(id) {
    if (this.negotiations.size >= this.negotiationMax && !this.negotiations.has(id)) return false
    this.negotiations.add(id)
    return true
  }

  closeNegotiation(id) {
    this.negotiations.delete(id)
  }

  queueCandidate(candidate) {
    if (this.candidates.length >= this.candidateQueueMax) return false
    this.candidates.push(candidate)
    return true
  }

  drainCandidates() {
    const queued = this.candidates
    this.candidates = []
    return queued
  }
}
