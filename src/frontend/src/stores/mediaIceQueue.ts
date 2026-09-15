export type QueuedIceCandidate = {
  candidate: RTCIceCandidateInit
  expiresAt: number
}

export class MediaIceQueue {
  private static readonly maxCandidates = 128
  private static readonly lifetimeMs = 10_000
  private readonly queues = new Map<string, QueuedIceCandidate[]>()
  private readonly timers = new Map<string, ReturnType<typeof setTimeout>>()

  constructor(private readonly onExpired: (negotiationNId: string) => void, private readonly now = () => Date.now()) {}

  start(negotiationNId: string): void {
    if (!this.timers.has(negotiationNId)) this.schedule(negotiationNId, this.queues.get(negotiationNId)?.at(0)?.expiresAt ?? this.now() + MediaIceQueue.lifetimeMs)
  }

  add(negotiationNId: string, candidate: RTCIceCandidateInit): boolean {
    this.prune()
    const queue = this.queues.get(negotiationNId) ?? []
    const total = [...this.queues.values()].reduce((count, items) => count + items.length, 0)
    if (queue.length >= MediaIceQueue.maxCandidates || total >= MediaIceQueue.maxCandidates) return false

    queue.push({ candidate, expiresAt: this.now() + MediaIceQueue.lifetimeMs })
    this.queues.set(negotiationNId, queue)
    this.start(negotiationNId)
    return true
  }

  take(negotiationNId: string): QueuedIceCandidate[] {
    const queue = this.queues.get(negotiationNId) ?? []
    this.queues.delete(negotiationNId)
    this.clearTimer(negotiationNId)
    return queue.filter((item) => item.expiresAt > this.now())
  }

  cancel(negotiationNId: string): void {
    this.queues.delete(negotiationNId)
    this.clearTimer(negotiationNId)
  }

  discardExcept(currentNegotiationNId: string): void {
    const negotiationIds = new Set([...this.queues.keys(), ...this.timers.keys()])
    for (const negotiationNId of negotiationIds) {
      if (negotiationNId === currentNegotiationNId) continue
      this.queues.delete(negotiationNId)
      this.clearTimer(negotiationNId)
    }
  }

  clear(): void {
    this.queues.clear()
    for (const negotiationNId of this.timers.keys()) this.clearTimer(negotiationNId)
  }

  private prune(): void {
    const now = this.now()
    for (const [negotiationNId, queue] of this.queues) {
      const valid = queue.filter((item) => item.expiresAt > now)
      if (valid.length === 0) {
        this.queues.delete(negotiationNId)
        this.clearTimer(negotiationNId)
      } else {
        this.queues.set(negotiationNId, valid)
      }
    }
  }

  private schedule(negotiationNId: string, expiresAt: number): void {
    this.timers.set(negotiationNId, setTimeout(() => this.expire(negotiationNId), Math.max(0, expiresAt - this.now())))
  }

  private expire(negotiationNId: string): void {
    this.clearTimer(negotiationNId)
    const queue = this.queues.get(negotiationNId) ?? []
    const valid = queue.filter((item) => item.expiresAt > this.now())
    if (valid.length > 0) {
      this.queues.set(negotiationNId, valid)
      this.schedule(negotiationNId, valid[0]!.expiresAt)
      return
    }
    this.queues.delete(negotiationNId)
    this.onExpired(negotiationNId)
  }

  private clearTimer(negotiationNId: string): void {
    const timer = this.timers.get(negotiationNId)
    if (timer === undefined) return
    clearTimeout(timer)
    this.timers.delete(negotiationNId)
  }
}
