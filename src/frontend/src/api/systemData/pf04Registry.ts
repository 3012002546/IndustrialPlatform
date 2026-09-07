import type { Pf04Api } from './pf04Types'

let pf04Api: Pf04Api | null = null

export function registerPf04Api(api: Pf04Api): void {
  pf04Api = api
}

export function getPf04Api(): Pf04Api | null {
  return pf04Api
}
