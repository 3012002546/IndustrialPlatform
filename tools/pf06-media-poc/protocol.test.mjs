import assert from 'node:assert/strict'
import test from 'node:test'
import {
  CapabilityState,
  SignalLedger,
  SLOT_KIND,
  SLOT_ORDER,
  createLayout,
  mapHighLayout,
  ownerSlot,
  serializeCandidate,
  serializeDescription,
} from './protocol.mjs'

test('low creates the final three slots once and high maps the remote offer', () => {
  const low = createLayout('low')
  assert.deepEqual(low.transceivers.map(({ slot }) => slot), SLOT_ORDER)
  assert.deepEqual(low.transceivers.map(({ kind }) => kind), ['audio', 'video', 'video'])

  const high = createLayout('high')
  const mapped = mapHighLayout(
    high,
    low.transceivers.map(({ kind }, index) => ({ kind, mid: String(index) })),
  )
  assert.deepEqual(mapped.transceivers.map(({ slot }) => slot), SLOT_ORDER)
  assert.throws(() => mapHighLayout(mapped, []), /already mapped/)
})

test('screen ownership maps to one fixed video slot', () => {
  assert.equal(ownerSlot('low'), 'screenLow')
  assert.equal(ownerSlot('high'), 'screenHigh')
  assert.equal(SLOT_KIND[ownerSlot('low')], 'video')
  assert.equal(SLOT_KIND[ownerSlot('high')], 'video')
})

test('voice and screen state are independent and late work is rejected', () => {
  const state = new CapabilityState()
  const voiceGeneration = state.startVoice()
  const screenGeneration = state.startScreen('low')
  assert.equal(state.voice.active, true)
  assert.equal(state.screen.active, true)

  state.stopScreen()
  assert.equal(state.voice.active, true)
  assert.equal(state.canApply('screen', screenGeneration), false)
  assert.equal(state.canApply('voice', voiceGeneration), true)

  state.stopVoice()
  assert.equal(state.voice.active, false)
})

test('signaling rejects stale revisions and duplicate sequences with bounded queues', () => {
  const ledger = new SignalLedger({ candidateQueueMax: 2, negotiationMax: 2 })
  assert.equal(ledger.acceptRevision(2), true)
  assert.equal(ledger.acceptRevision(1), false)
  assert.equal(ledger.acceptSequence(1), true)
  assert.equal(ledger.acceptSequence(1), false)
  assert.equal(ledger.openNegotiation('a'), true)
  assert.equal(ledger.openNegotiation('b'), true)
  assert.equal(ledger.openNegotiation('c'), false)
  assert.equal(ledger.queueCandidate('a'), true)
  assert.equal(ledger.queueCandidate('b'), true)
  assert.equal(ledger.queueCandidate('c'), false)
  ledger.closeNegotiation('a')
  assert.equal(ledger.openNegotiation('c'), true)
})

test('signaling payloads are plain structured-cloneable records', () => {
  const description = serializeDescription({ type: 'offer', sdp: 'v=0' })
  const candidate = serializeCandidate({
    candidate: 'candidate:1 1 udp 1 127.0.0.1 9 typ host',
    sdpMid: '0',
    sdpMLineIndex: 0,
    usernameFragment: 'ufrag',
  })
  assert.deepEqual(structuredClone(description), description)
  assert.deepEqual(structuredClone(candidate), candidate)
})
