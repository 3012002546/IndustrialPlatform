import type { SystemDataOperationsApi } from './systemDataOperationsTypes'

let systemDataOperationsApi: SystemDataOperationsApi | null = null

export function registerSystemDataOperationsApi(api: SystemDataOperationsApi): void {
  systemDataOperationsApi = api
}

export function getSystemDataOperationsApi(): SystemDataOperationsApi | null {
  return systemDataOperationsApi
}
