/** Keep one idempotency key across failed attempts of the same command until acknowledgement. */
export function pendingOperation() {
  let current: { fingerprint: string; key: string } | undefined
  return {
    key(input: unknown) {
      const fingerprint = JSON.stringify(input)
      if (current?.fingerprint !== fingerprint) current = { fingerprint, key: crypto.randomUUID() }
      return current.key
    },
    clear() {
      current = undefined
    },
  }
}
