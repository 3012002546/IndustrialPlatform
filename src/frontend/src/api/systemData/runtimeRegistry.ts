import type { SystemDataRuntimeApi } from './runtimeApi'

let runtimeApi: SystemDataRuntimeApi | null = null

export function registerSystemDataRuntimeApi(api: SystemDataRuntimeApi): void {
  runtimeApi = api
}

export function getSystemDataRuntimeApi(): SystemDataRuntimeApi | null {
  return runtimeApi
}
