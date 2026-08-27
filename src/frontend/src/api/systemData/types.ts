/** SystemData v1 runtime DTOs. These mirror the backend public response contracts. */

export interface NavigationRuntimeNodeDto {
  nodeNId: string
  kind: string
  label: string
  resourceNId: string | null
  routeName: string | null
  requiredPermissionNId: string | null
  featureNId: string | null
  iconKey: string | null
  displayOrder: number
  children: NavigationRuntimeNodeDto[]
}

export interface NavigationRuntimeDto {
  revision: number
  degraded: boolean
  nodes: NavigationRuntimeNodeDto[]
}

export interface FeatureRuntimeItemDto {
  featureNId: string
  enabled: boolean
}

export interface FeatureRuntimeDto {
  revision: number
  degraded: boolean
  items: FeatureRuntimeItemDto[]
}

export interface ThemePolicyDto {
  policyRevision: number
  degraded: boolean
  allowedPalettes: string[]
  allowedModes: string[]
  allowedPcDensities: string[]
  defaultPalette: string
  defaultMode: string
  defaultPcDensity: string
}

export type RuntimeSnapshotResult<T> =
  | { kind: 'updated'; data: T; etag: string | undefined }
  | { kind: 'not-modified'; etag: string | undefined }
