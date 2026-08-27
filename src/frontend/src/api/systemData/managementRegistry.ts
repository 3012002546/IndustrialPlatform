import type { SystemDataManagementApi } from './managementTypes'

let managementApi: SystemDataManagementApi | null = null

export function registerSystemDataManagementApi(api: SystemDataManagementApi): void {
  managementApi = api
}

export function getSystemDataManagementApi(): SystemDataManagementApi | null {
  return managementApi
}
