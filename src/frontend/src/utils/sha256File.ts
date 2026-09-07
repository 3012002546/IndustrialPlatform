const K = new Uint32Array([
  0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5,
  0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
  0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
  0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
  0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
  0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
  0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
  0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2,
])

const initial = [0x6a09e667, 0xbb67ae85, 0x3c6ef372, 0xa54ff53a, 0x510e527f, 0x9b05688c, 0x1f83d9ab, 0x5be0cd19]

function rotr(value: number, amount: number): number {
  return (value >>> amount) | (value << (32 - amount))
}

class Sha256 {
  private readonly state = new Uint32Array(initial)
  private readonly block = new Uint8Array(64)
  private blockLength = 0
  private bytes = 0

  update(data: Uint8Array): void {
    this.bytes += data.length
    let offset = 0
    while (offset < data.length) {
      const copied = Math.min(64 - this.blockLength, data.length - offset)
      this.block.set(data.subarray(offset, offset + copied), this.blockLength)
      this.blockLength += copied
      offset += copied
      if (this.blockLength === 64) {
        this.compress(this.block)
        this.blockLength = 0
      }
    }
  }

  digest(): Uint8Array {
    const bitLength = this.bytes * 8
    this.block[this.blockLength++] = 0x80
    if (this.blockLength > 56) {
      this.block.fill(0, this.blockLength)
      this.compress(this.block)
      this.blockLength = 0
    }
    this.block.fill(0, this.blockLength, 56)
    const view = new DataView(this.block.buffer)
    view.setUint32(56, Math.floor(bitLength / 0x100000000), false)
    view.setUint32(60, bitLength >>> 0, false)
    this.compress(this.block)

    const result = new Uint8Array(32)
    const output = new DataView(result.buffer)
    this.state.forEach((value, index) => output.setUint32(index * 4, value, false))
    return result
  }

  private compress(block: Uint8Array): void {
    const words = new Uint32Array(64)
    const view = new DataView(block.buffer, block.byteOffset, block.byteLength)
    for (let i = 0; i < 16; i++) words[i] = view.getUint32(i * 4, false)
    for (let i = 16; i < 64; i++) {
      const value = words[i - 15]!
      const gamma0 = rotr(value, 7) ^ rotr(value, 18) ^ (value >>> 3)
      const previous = words[i - 2]!
      const gamma1 = rotr(previous, 17) ^ rotr(previous, 19) ^ (previous >>> 10)
      words[i] = (words[i - 16]! + gamma0 + words[i - 7]! + gamma1) >>> 0
    }

    let a = this.state[0]!
    let b = this.state[1]!
    let c = this.state[2]!
    let d = this.state[3]!
    let e = this.state[4]!
    let f = this.state[5]!
    let g = this.state[6]!
    let h = this.state[7]!
    for (let i = 0; i < 64; i++) {
      const sigma1 = rotr(e, 6) ^ rotr(e, 11) ^ rotr(e, 25)
      const choice = (e & f) ^ (~e & g)
      const temp1 = (h + sigma1 + choice + K[i]! + words[i]!) >>> 0
      const sigma0 = rotr(a, 2) ^ rotr(a, 13) ^ rotr(a, 22)
      const majority = (a & b) ^ (a & c) ^ (b & c)
      const temp2 = (sigma0 + majority) >>> 0
      h = g; g = f; f = e; e = (d + temp1) >>> 0; d = c; c = b; b = a; a = (temp1 + temp2) >>> 0
    }
    this.state[0] = (this.state[0]! + a) >>> 0
    this.state[1] = (this.state[1]! + b) >>> 0
    this.state[2] = (this.state[2]! + c) >>> 0
    this.state[3] = (this.state[3]! + d) >>> 0
    this.state[4] = (this.state[4]! + e) >>> 0
    this.state[5] = (this.state[5]! + f) >>> 0
    this.state[6] = (this.state[6]! + g) >>> 0
    this.state[7] = (this.state[7]! + h) >>> 0
  }
}

export async function sha256File(file: File): Promise<string> {
  const hash = new Sha256()
  const reader = file.stream().getReader()
  try {
    while (true) {
      const chunk = await reader.read()
      if (chunk.done) break
      hash.update(chunk.value)
    }
  } finally {
    reader.releaseLock()
  }
  return Array.from(hash.digest(), (value) => value.toString(16).padStart(2, '0')).join('')
}

export function sha256Bytes(data: Uint8Array): string {
  const hash = new Sha256()
  hash.update(data)
  return Array.from(hash.digest(), (value) => value.toString(16).padStart(2, '0')).join('')
}

function writeInt64(value: number): Uint8Array {
  const result = new Uint8Array(8)
  const view = new DataView(result.buffer)
  view.setUint32(0, Math.floor(value / 0x100000000), false)
  view.setUint32(4, value >>> 0, false)
  return result
}

function writeWindow(content: Uint8Array, offset: number): Uint8Array {
  const header = new Uint8Array(12)
  const view = new DataView(header.buffer)
  view.setUint32(0, Math.floor(offset / 0x100000000), false)
  view.setUint32(4, offset >>> 0, false)
  view.setUint32(8, content.length, false)
  return Uint8Array.from([...header, ...content])
}

/** Matches FileUploadRules.ComputeSampleFingerprint(sample-v1). */
export async function sampleFingerprint(file: File, windowBytes = 65_536): Promise<string> {
  const windows: Uint8Array[] = []
  if (file.size <= windowBytes * 3) {
    windows.push(writeWindow(new Uint8Array(await file.slice(0, file.size).arrayBuffer()), 0))
  } else {
    const middleOffset = Math.floor((file.size - windowBytes) / 2)
    windows.push(writeWindow(new Uint8Array(await file.slice(0, windowBytes).arrayBuffer()), 0))
    windows.push(writeWindow(new Uint8Array(await file.slice(middleOffset, middleOffset + windowBytes).arrayBuffer()), middleOffset))
    windows.push(writeWindow(new Uint8Array(await file.slice(file.size - windowBytes).arrayBuffer()), file.size - windowBytes))
  }
  const prefix = new TextEncoder().encode('IPF:sample-v1\n')
  const bytes = Uint8Array.from([...prefix, ...writeInt64(file.size), ...windows.flatMap((window) => [...window])])
  return `sample-v1:${sha256Bytes(bytes)}`
}
