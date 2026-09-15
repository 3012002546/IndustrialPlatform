import {
  CapabilityState,
  SignalLedger,
  SLOT_ORDER,
  createLayout,
  mapHighLayout,
  ownerSlot,
  serializeCandidate,
  serializeDescription,
} from './protocol.mjs'

const params = new URLSearchParams(location.search)
const role = params.get('role') === 'high' ? 'high' : 'low'
const room = params.get('room') || 'pf06-default'
const peerId = crypto.randomUUID()
const bus = new BroadcastChannel(`pf06-poc:${room}`)
const capability = new CapabilityState()
const ledger = new SignalLedger()
const layout = createLayout(role)

const ui = {
  identity: document.querySelector('#identity'),
  log: document.querySelector('#log'),
  stats: document.querySelector('#stats'),
  audio: document.querySelector('#remoteAudio'),
  video: document.querySelector('#remoteVideo'),
}

let pc
let makingOffer = false
let ignoreOffer = false
let isSettingRemoteAnswerPending = false
let sequence = 0
let outgoingSequence = 0
let remoteDirections = []
let localStreams = { audio: null, screen: null }
let statsTimer
const trace = { startedAt: Date.now(), events: [], stats: [] }

ui.identity.textContent = `${role.toUpperCase()} · room=${room} · open a second tab with the other role`

function log(message, detail = '') {
  const line = `${new Date().toISOString()} ${message}${detail ? ` ${detail}` : ''}`
  ui.log.textContent = `${line}\n${ui.log.textContent}`.slice(0, 12000)
  trace.events.push({ at: new Date().toISOString(), elapsedMs: Date.now() - trace.startedAt, message, detail })
}

function send(message) {
  bus.postMessage({ ...message, from: peerId, role })
}

function transceivers() {
  if (!pc || pc.getTransceivers().length !== SLOT_ORDER.length) return []
  return pc.getTransceivers().slice(0, SLOT_ORDER.length)
}

function slotIndex(slot) {
  return SLOT_ORDER.indexOf(slot)
}

function remoteDirectionsFromSdp(sdp) {
  return sdp
    .split(/\r?\nm=/)
    .slice(1)
    .map((section) => {
      const direction = ['sendrecv', 'sendonly', 'recvonly', 'inactive'].find((value) =>
        new RegExp(`(?:^|\\r?\\n)a=${value}(?:\\r?\\n|$)`).test(section),
      )
      return direction || 'inactive'
    })
}

function applyDirections() {
  const list = transceivers()
  if (list.length !== SLOT_ORDER.length) return
  list[slotIndex('audio')].direction = 'sendrecv'
  for (const slot of ['screenLow', 'screenHigh']) {
    const index = slotIndex(slot)
    const hasLocalTrack = Boolean(list[index].sender.track)
    const remoteSends = remoteDirections[index] === 'sendonly' || remoteDirections[index] === 'sendrecv'
    list[index].direction = hasLocalTrack ? 'sendonly' : remoteSends ? 'recvonly' : 'inactive'
  }
}

async function ensurePeer() {
  if (pc) return
  pc = new RTCPeerConnection({ iceServers: [] })
  pc.onicecandidate = ({ candidate }) => candidate && send({ type: 'ice', candidate: serializeCandidate(candidate) })
  pc.ontrack = ({ track, streams, transceiver }) => {
    const index = Number(transceiver.mid)
    const slot = SLOT_ORDER[index]
    if (slot === 'audio') ui.audio.srcObject = streams[0] || new MediaStream([track])
    if (slot === 'screenLow' || slot === 'screenHigh') ui.video.srcObject = streams[0] || new MediaStream([track])
    log('remote track', `${slot} readyState=${track.readyState}`)
  }
  pc.onconnectionstatechange = () => log('connection', pc.connectionState)
  pc.onnegotiationneeded = async () => {
    await makeOffer()
  }

  async function makeOffer() {
    try {
      if (!pc || pc.signalingState !== 'stable') return
      makingOffer = true
      await pc.setLocalDescription(await pc.createOffer())
      send({ type: 'description', description: serializeDescription(pc.localDescription), sequence: ++outgoingSequence })
      log('offer sent', `role=${role}`)
    } catch (error) {
      log('negotiation error', error.message)
    } finally {
      makingOffer = false
    }
  }

  pc.makeOffer = makeOffer

  if (role === 'low') {
    for (const slot of SLOT_ORDER) pc.addTransceiver(slot === 'audio' ? 'audio' : 'video', { direction: 'inactive' })
    applyDirections()
  }
  startStats()
  log('peer created', role === 'low' ? 'low created audio/screenLow/screenHigh' : 'high waits for offer')
}

async function handleDescription(description) {
  await ensurePeer()
  const offerCollision = description.type === 'offer' && (makingOffer || pc.signalingState !== 'stable')
  ignoreOffer = !isPolite() && offerCollision
  if (ignoreOffer) {
    log('ignored colliding offer', `polite=${isPolite()}`)
    return
  }
  if (offerCollision) await pc.setLocalDescription({ type: 'rollback' })
  isSettingRemoteAnswerPending = description.type === 'answer'
  await pc.setRemoteDescription(description)
  isSettingRemoteAnswerPending = false

  for (const candidate of ledger.drainCandidates()) await pc.addIceCandidate(candidate)

  if (role === 'high' && !layout.mapped) {
    const mapped = mapHighLayout(layout, pc.getTransceivers().slice(0, 3).map((item) => ({ kind: item.receiver.track.kind, mid: item.mid })))
    layout.mapped = mapped.mapped
    layout.transceivers = mapped.transceivers
    log('high mapped remote slots', layout.transceivers.map(({ slot, mid }) => `${slot}:${mid}`).join(', '))
  }
  if (description.type === 'offer') {
    remoteDirections = remoteDirectionsFromSdp(description.sdp)
    applyDirections()
    await pc.setLocalDescription(await pc.createAnswer())
    send({ type: 'description', description: serializeDescription(pc.localDescription), sequence: ++outgoingSequence })
    log('answer sent')
  }
}

function isPolite() {
  return role === 'low'
}

async function startVoice() {
  await ensurePeer()
  const generation = capability.startVoice()
  const stream = await navigator.mediaDevices.getUserMedia({ audio: true, video: false })
  if (!capability.canApply('voice', generation)) return stream.getTracks().forEach((track) => track.stop())
  localStreams.audio = stream
  const audio = transceivers()[slotIndex('audio')]
  await audio.sender.replaceTrack(stream.getAudioTracks()[0])
  applyDirections()
  log('local microphone attached', 'audio slot only')
}

async function startScreen() {
  await ensurePeer()
  if (capability.screen.active) return log('screen already active')
  const generation = capability.startScreen(role)
  const stream = await navigator.mediaDevices.getDisplayMedia({ video: true, audio: false })
  if (!capability.canApply('screen', generation)) return stream.getTracks().forEach((track) => track.stop())
  localStreams.screen = stream
  const slot = ownerSlot(role)
  const transceiver = transceivers()[slotIndex(slot)]
  await transceiver.sender.replaceTrack(stream.getVideoTracks()[0])
  stream.getVideoTracks()[0].addEventListener('ended', () => stopScreen())
  applyDirections()
  log('local screen attached', `${slot}; audio=false`)
}

async function stopVoice() {
  capability.stopVoice()
  const audio = transceivers()[slotIndex('audio')]
  if (audio) await audio.sender.replaceTrack(null).catch(() => {})
  localStreams.audio?.getTracks().forEach((track) => track.stop())
  localStreams.audio = null
  log('voice stopped', 'screen slot and peer connection retained')
}

async function stopScreen() {
  if (!capability.screen.active && !localStreams.screen) return
  capability.stopScreen()
  const transceiver = transceivers()[slotIndex(ownerSlot(role))]
  if (transceiver) await transceiver.sender.replaceTrack(null).catch(() => {})
  localStreams.screen?.getTracks().forEach((track) => track.stop())
  localStreams.screen = null
  ui.video.srcObject = null
  log('screen stopped', 'audio slot and peer connection retained')
}

async function disposeAll() {
  await stopScreen()
  await stopVoice()
  pc?.close()
  pc = null
  clearInterval(statsTimer)
  ui.audio.srcObject = null
  log('disposed all', 'chat/signaling channel is intentionally not represented by this PoC')
}

async function toggleMute() {
  const stream = localStreams.audio
  const track = stream?.getAudioTracks()[0]
  if (!track) return log('mute ignored', 'no local microphone')
  track.enabled = !track.enabled
  log(track.enabled ? 'microphone unmuted' : 'microphone muted', 'track retained')
}

async function onMessage({ data }) {
  if (data.from === peerId) return
  try {
    if (data.type === 'hello' && role === 'low') {
      await ensurePeer()
      return pc.makeOffer()
    }
    if (data.type === 'description') return await handleDescription(data.description)
    if (data.type === 'ice') {
      await ensurePeer()
      if (!ledger.acceptSequence(data.sequence ?? ++sequence)) return
      if (!pc.remoteDescription) {
        if (ledger.queueCandidate(data.candidate)) log('queued early ICE')
        return
      }
      await pc.addIceCandidate(data.candidate).catch((error) => {
        if (!ignoreOffer) throw error
      })
    }
  } catch (error) {
    log('signal error', error.message)
  }
}

async function startStats() {
  clearInterval(statsTimer)
  statsTimer = setInterval(async () => {
    if (!pc) return
    const report = await pc.getStats()
    const values = []
    report.forEach((item) => {
      if (item.type === 'inbound-rtp' || item.type === 'outbound-rtp') {
        values.push(`${item.type} ${item.kind || item.mediaType || '?'} bytes=${item.bytesReceived ?? item.bytesSent ?? 0} frames=${item.framesDecoded ?? item.framesSent ?? 0}`)
      }
    })
    ui.stats.textContent = values.join('\n') || 'No RTP stats yet'
    trace.stats.push({ at: new Date().toISOString(), values })
    if (trace.stats.length > 600) trace.stats.shift()
  }, 1000)
}

async function exportTrace() {
  if (pc) await startStats()
  const blob = new Blob([JSON.stringify({ role, room, ...trace }, null, 2)], { type: 'application/json' })
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = `pf06-media-poc-${role}-${Date.now()}.json`
  link.click()
  URL.revokeObjectURL(url)
  log('trace exported', 'only timestamps, fixed event labels, and RTP byte/frame counters')
}

bus.addEventListener('message', onMessage)
document.querySelector('#connect').addEventListener('click', async () => {
  await ensurePeer()
  send({ type: 'hello' })
  if (role === 'low') await pc.makeOffer()
})
document.querySelector('#voice').addEventListener('click', () => startVoice().catch((error) => log('microphone failed', error.message)))
document.querySelector('#mute').addEventListener('click', () => toggleMute())
document.querySelector('#stopVoice').addEventListener('click', () => stopVoice().catch((error) => log('stop voice failed', error.message)))
document.querySelector('#screen').addEventListener('click', () => startScreen().catch((error) => log('screen failed', error.message)))
document.querySelector('#stopScreen').addEventListener('click', () => stopScreen().catch((error) => log('stop screen failed', error.message)))
document.querySelector('#reset').addEventListener('click', () => disposeAll().catch((error) => log('dispose failed', error.message)))
document.querySelector('#export').addEventListener('click', () => exportTrace().catch((error) => log('export failed', error.message)))
window.addEventListener('beforeunload', () => bus.close())

ensurePeer().catch((error) => log('peer init failed', error.message))
