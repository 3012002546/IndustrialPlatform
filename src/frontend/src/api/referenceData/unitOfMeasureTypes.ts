import type { PublicationStatus, ReferenceScope, VersionRequest } from './types'

export type UnitConversionKind = 'Ratio' | 'AbsoluteTemperature'
export type UnitRoundingMode = 'ToEven' | 'AwayFromZero'

export interface UnitDefinitionWrite {
  nId: string
  name: string
  symbol: string
  factorToBase: string
  offsetToBase: string
  decimalPlaces: number
  roundingMode: UnitRoundingMode
  enabled: boolean
  sort: number
}

export interface UnitDefinition extends UnitDefinitionWrite {
  id: string
  isFrozen: boolean
  isLocked: boolean
}

export type RuntimeUnitDefinition = UnitDefinitionWrite

export interface UnitDimensionSummary {
  id: string
  nId: string
  name: string
  description: string | null
  scopeType: ReferenceScope
  tenantNId: string | null
  revision: number
  status: PublicationStatus
  sourceRevision: number | null
  isSystemDefined: boolean
  conversionKind: UnitConversionKind
  baseUnitNId: string
  unitCount: number
  optimisticVersion: number
  concurrencyVersion: string
  lastUpdatedOn: string
  publishedOn: string | null
  publishedBy: string | null
  isFrozen: boolean
  isLocked: boolean
}

export interface UnitDimension extends Omit<UnitDimensionSummary, 'unitCount'> {
  units: UnitDefinition[]
}

export interface AvailableUnitDimension {
  nId: string
  name: string
  sourceScope: ReferenceScope
  sourceTenantNId: string | null
  revision: number
  publishedOn: string
  isSystemDefined: boolean
  conversionKind: UnitConversionKind
  baseUnitNId: string
  unitCount: number
}

export interface CreateUnitDimension {
  scopeType: ReferenceScope
  scopeId: null
  nId: string
  name: string
  description: string | null
  conversionKind: UnitConversionKind
  baseUnitNId: string
  units: UnitDefinitionWrite[]
}

export interface UpdateUnitDimension extends VersionRequest {
  name: string
  description: string | null
  conversionKind: UnitConversionKind
  baseUnitNId: string
  units: UnitDefinitionWrite[]
}

export interface RuntimeUnitDimension {
  nId: string
  name: string
  description: string | null
  sourceScope: ReferenceScope
  sourceTenantNId: string | null
  revision: number
  status: PublicationStatus
  sourceRevision: number | null
  publishedOn: string | null
  isSystemDefined: boolean
  conversionKind: UnitConversionKind
  baseUnitNId: string
  units: RuntimeUnitDefinition[]
}

export interface UnitConversionRequest {
  sourceScope: ReferenceScope
  sourceTenantNId: string | null
  unitDimensionNId: string
  unitRevision: number
  fromUnitNId: string
  toUnitNId: string
  value: string
}

export interface UnitConversionSnapshot {
  sourceFactorToBase: string
  sourceOffsetToBase: string
  targetFactorToBase: string
  targetOffsetToBase: string
  decimalPlaces: number
  roundingMode: UnitRoundingMode
}

export interface UnitConversionResult {
  unitDimensionNId: string
  unitRevision: number
  sourceScope: ReferenceScope
  sourceTenantNId: string | null
  fromUnitNId: string
  toUnitNId: string
  inputValue: string
  resultValue: string
  wasRounded: boolean
  conversionSnapshot: UnitConversionSnapshot
}
